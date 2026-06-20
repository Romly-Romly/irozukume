// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Graphics.DirectX;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.ScreenPicker.Capture;
using Irozukume.ScreenPicker.Glass;
using Irozukume.ScreenPicker.Interop;

namespace Irozukume.ScreenPicker;

// カーソルに追従し中心に乗る美しいガラスレンズ。レンズ寸法のレイヤードウィンドウへ、毎フレーム per-pixel alpha のビットマップを UpdateLayeredWindow で「位置と中身を一括」反映する。
// 各フレームで「カーソル位置→所在モニタ→（必要なら）捕捉張り替え→背後（=カーソル周辺）の FP16 領域読み戻し→sRGB8 へ変換してガラスへ→中心レチクル上描き→中心画素の色を保持」を行う。
// 採色の正確さは FP16 経路（中心画素の scRGB を厳密変換）が担い、ガラスの見た目は sRGB8 へ落とした表示経路が担う。両経路を分離することで、ガラスは 8bit 前提の調律のまま崩れず動く。
// 描画・読み戻し・確定/中止のフックはすべてこの専用スレッドのメッセージループ上で行う。共有デバイスを複数スレッドから操作すると落ちるため。
// 左クリックで中心画素の色を確定し、Esc または右クリックで中止する。マウスホイールで拡大率を増減する。フックでこれらの入力を握り潰し、採色のための操作が裏のウィンドウへ漏れないようにする。
internal sealed class ScreenPickerLensWindow
{
	private static readonly UIntPtr RenderTimerId = (UIntPtr)1;

	// レンズ窓をカーソルからどれだけ離して置くか(物理ピクセル)。採色対象とレンズが重ならないようにする。
	private const int LensOffset = 28;

	// 拡大方式の切り替え閾値。bp(1ソース画素あたり物理px)がこれ以上なら自前の整数ブロック描画(画素精度・完全整列でレチクルが採色画素にちょうど一致)、未満ならビットマップ拡大(描画は軽いが整列が最大半ブロックずれる)。
	// カラーピッカーはレチクルと採色画素の一致が要件のため、ブロック方式を使う範囲を広めに取る。画素が見分けづらいごく低い倍率でのみビットマップ方式へ落とす。
	private const int HybridBpThreshold = 5;

	// マウスホイールで増減できる拡大率の範囲。実拡大率 bp(1ソース画素を何物理pxへ拡大するか)そのもので、下限5倍、上限はとりあえず32倍。DPI倍率に依らずこの範囲で増減できるよう、Magnify 単位ではなく bp で持つ。
	private const int WheelBpMin = 5;
	private const int WheelBpMax = 32;

	// Ctrl+ホイールで増減できる取得範囲の半径。採色点を中心とした正方形の半辺(ソース画素)で、辺は 2×半径+1 になる。0 で 1×1(単一画素)、上限はとりあえず 7(15×15)。
	private const int WheelSampleRadiusMax = 7;

	// レンズ窓のウィンドウクラス名をセッションごとに一意にするための連番。同名クラスの二重登録を避け、ピッカーを繰り返し開けるようにする。
	private static int _classSeq;

	private readonly GlassParams _p;
	private readonly DesktopCaptureSession _capture;
	private readonly GlassRenderer _renderer = new();
	private readonly CanvasDevice _device;
	private readonly Action<PickedColor?> _onCompleted;

	// 採色開始時の初期値(永続化された前回の最終値)。_initialBlockPx が 0 のときは設定の拡大率(Magnify)から初期 bp を導く。
	private readonly int _initialBlockPx;
	private readonly int _initialSampleRadius;

	// WndProc とフックのデリゲートを GC から守るために保持する。関数ポインタを Win32 へ渡したあとデリゲートが回収されると落ちるため。
	private PickerNativeMethods.WndProcDelegate? _wndProcKeepAlive;
	private PickerNativeMethods.HookProc? _mouseProcKeepAlive;
	private PickerNativeMethods.HookProc? _keyProcKeepAlive;

	private Thread? _thread;
	private IntPtr _hwnd;
	private IntPtr _hInstance;
	private string _className = string.Empty;
	private bool _classRegistered;
	private bool _timerSet;

	private IntPtr _mouseHook;
	private IntPtr _keyHook;

	// 低レベル入力フックを載せる専用スレッド。重い描画(キャプチャ読み戻し・Win2D 描画・モニタ切替の捕捉再構築)と同居させると、メッセージループが詰まってフックが LowLevelHooksTimeout を超え、OS がクリックを裏のアプリへ素通りさせる(さらに塞ぎが続くとフックごと外される)。フックはこのスレッドで素のメッセージループだけを回し、描画から切り離す。
	private Thread? _hookThread;
	private uint _hookThreadId;
	private readonly ManualResetEventSlim _hookReady = new(false);

	// 確定/中止が確定したら立てる。以後マウス入力を握り潰し、メッセージループを WM_CLOSE で畳む。
	private bool _closing;

	// 確定された色。null は中止(または採色前に閉じた)を表す。スレッド完了時に _onCompleted へ渡す。
	private PickedColor? _result;

	// 直近フレームで読んだ中心画素(=カーソル下)の sRGB8 を一語へ詰めた値。bit24 が採色可否、下位24bitが R(16-23)・G(8-15)・B(0-7)。描画スレッドが書き、別スレッドの入力フックが読むため Volatile で受け渡す。
	private int _centerPacked;

	// カーソル下が、自プロセスより高い整合性レベルの昇格ウィンドウ(=採色フックが OS に握られ採れない)かどうか。レンズに禁止マークを出すのに使う。直前に照会したウィンドウを覚え、ウィンドウが変わったときだけ照会して負荷を抑える。
	private IntPtr _lastProbedHwnd;
	private bool _blockedByElevation;

	private float _scale = 1f;
	private int _bodyPhysW;
	private int _bodyPhysH;

	// 現在の実拡大率 bp(1ソース画素あたりの物理px)。初期値は設定由来の _p.Magnify を現在のDPI倍率で割った値で、マウスホイールで増減する。フック専用スレッドが書き、描画スレッドが毎フレーム読むため Volatile で受け渡す。
	private int _blockPx;

	// マウスホイールの回転量の端数。WHEEL_DELTA に満たない回転を取りこぼさないため貯める。フック専用スレッドだけが触れる。
	private int _wheelResidual;

	// 現在の取得範囲の半径(ソース画素)。0 で単一画素、n で (2n+1)² 画素をリニア平均して採る。Ctrl+ホイールで増減する。フック専用スレッドが書き、描画スレッドが毎フレーム読むため Volatile で受け渡す。
	private int _sampleRadius;

	// Ctrl+ホイールの回転量の端数。拡大率用(_wheelResidual)とは別に貯める。フック専用スレッドだけが触れる。
	private int _sampleWheelResidual;

	// レンズの画面上の左上位置(物理ピクセル)。毎フレーム、カーソル中心に追従して更新する。
	private int _lensX;
	private int _lensY;

	// レンズ中心が今いるモニタ。これが変わったらキャプチャ対象を切り替える。
	private IntPtr _currentMon;

	// 上端弧へ出すモニタ表記(例: "Monitor 1: LG HDR 4K")と、それを組んだモニタ。DisplayConfig の照会は重いため、モニタが変わったときだけ引き直してキャッシュする。
	private string _monitorLabel = string.Empty;
	private IntPtr _labeledMon;

	// レンズ画像を毎フレーム描く Win2D の中間ターゲットと、それを UpdateLayeredWindow へ渡すための GDI 資源。
	private CanvasRenderTarget? _rt;
	private IntPtr _memDC;
	private IntPtr _dibBitmap;
	private IntPtr _oldBitmap;
	private IntPtr _dibBits;

	// FP16→sRGB8 変換結果を貯める再利用バッファ。フレームごとの確保を避けるため領域サイズが変わった時だけ作り直す。
	private byte[] _bgra = Array.Empty<byte>();




	public ScreenPickerLensWindow(GlassParams p, DesktopCaptureSession capture, int initialBlockPx, int initialSampleRadius, Action<PickedColor?> onCompleted)
	{
		_p = p;
		_capture = capture;
		_device = capture.Device;
		_initialBlockPx = initialBlockPx;
		_initialSampleRadius = initialSampleRadius;
		_onCompleted = onCompleted;
	}




	// 採色中にホイールで最後に到達した実拡大率 bp。セッション終了後にサービスが読み、永続化する。
	public int CurrentBlockPx => Volatile.Read(ref _blockPx);

	// 採色中に Ctrl+ホイールで最後に到達した取得範囲の半径。セッション終了後にサービスが読み、永続化する。
	public int CurrentSampleRadius => Volatile.Read(ref _sampleRadius);




	public void Start()
	{
		_thread = new Thread(ThreadMain)
		{
			Name = "IrozukumeScreenPickerLens",
			IsBackground = true,
		};
		_thread.SetApartmentState(ApartmentState.STA);
		_thread.Start();
	}




	private void ThreadMain()
	{
		try
		{
			_scale = (float)(_capture.DpiX / 96.0);

			// 初期拡大率。永続化された前回の bp があればそれを使い、無ければ設定の拡大率(Magnify)から導く。取得範囲も前回値から始める。
			_blockPx = _initialBlockPx > 0
				? Math.Clamp(_initialBlockPx, WheelBpMin, WheelBpMax)
				: Math.Clamp((int)Math.Round(_p.Magnify / _scale), WheelBpMin, WheelBpMax);
			_sampleRadius = Math.Clamp(_initialSampleRadius, 0, WheelSampleRadiusMax);

			_renderer.Rebuild(_device, _p);

			GlassGeometry g = _renderer.Geometry;
			_bodyPhysW = (int)Math.Round(g.CardW * _scale);
			_bodyPhysH = (int)Math.Round(g.CardH * _scale);

			// 初期位置は捕捉中のモニタ中央。最初のカーソル取得で即座に追従へ移る。
			_lensX = _capture.MonitorLeft + (_capture.Width - _bodyPhysW) / 2;
			_lensY = _capture.MonitorTop + (_capture.Height - _bodyPhysH) / 2;
			_currentMon = _capture.HMonitor;

			EnsureRenderSurfaces();
			CreateNativeWindow();

			// 自己をキャプチャ対象から完全に除外する。これをしないとレンズの描画が捕捉へ映り込み自己フィードバックになる。
			PickerNativeMethods.SetWindowDisplayAffinity(_hwnd, PickerNativeMethods.WDA_EXCLUDEFROMCAPTURE);

			PickerNativeMethods.ShowWindow(_hwnd, PickerNativeMethods.SW_SHOWNOACTIVATE);

			StartHookThread();

			// 描画は WM_TIMER 駆動。約60fps で RenderFrame を回す。
			PickerNativeMethods.SetTimer(_hwnd, RenderTimerId, 16, IntPtr.Zero);
			_timerSet = true;

			RunMessageLoop();
		}
		catch
		{
			// 初期化や描画ループで起きた例外は、セッションを中止として畳む。
		}
		finally
		{
			Teardown();
			_onCompleted(_result);
		}
	}




	// レンズ寸法のレイヤードウィンドウを作る。最前面・非アクティブ化・タスクバー非表示・マウス素通り。中身は UpdateLayeredWindow が渡すアルファで決まる。
	private void CreateNativeWindow()
	{
		_hInstance = PickerNativeMethods.GetModuleHandle(null);
		_className = "IrozukumeScreenPickerLens_" + Interlocked.Increment(ref _classSeq);

		_wndProcKeepAlive = WndProc;

		var wc = new PickerNativeMethods.WNDCLASSEX
		{
			cbSize = Marshal.SizeOf<PickerNativeMethods.WNDCLASSEX>(),
			lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive),
			hInstance = _hInstance,
			lpszClassName = _className,
		};
		PickerNativeMethods.RegisterClassEx(ref wc);
		_classRegistered = true;

		// レンズはカーソルに追従するだけで掴ませない。WS_EX_TRANSPARENT でマウスを素通りさせ、下のウィンドウ操作を妨げない。確定/中止のクリックは別途フックで拾う。
		uint exStyle = (uint)(PickerNativeMethods.WS_EX_LAYERED | PickerNativeMethods.WS_EX_TRANSPARENT | PickerNativeMethods.WS_EX_TOPMOST | PickerNativeMethods.WS_EX_TOOLWINDOW | PickerNativeMethods.WS_EX_NOACTIVATE);

		_hwnd = PickerNativeMethods.CreateWindowEx(exStyle, _className, "Irozukume Screen Picker Lens", PickerNativeMethods.WS_POPUP, _lensX, _lensY, _bodyPhysW, _bodyPhysH, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

		if (_hwnd == IntPtr.Zero)
		{
			int err = Marshal.GetLastWin32Error();
			throw new InvalidOperationException($"CreateWindowEx failed: {err}");
		}
	}




	// 入力フック専用スレッドを起こす。フックが据わるまで(または失敗が確定するまで)待ってから戻る。据わる前にレンズを見せて最初のクリックを取りこぼさないようにする。
	private void StartHookThread()
	{
		_hookThread = new Thread(HookThreadMain)
		{
			Name = "IrozukumeScreenPickerHook",
			IsBackground = true,
		};
		_hookThread.Start();
		_hookReady.Wait(2000);
	}




	// フック専用スレッドの本体。低レベル入力フックを据え、確定/中止を拾うためだけの素のメッセージループを回す。重い描画は一切載せないので、フレームやモニタ切替の遅延でフックがタイムアウトすることがない。
	// 先に PeekMessage でこのスレッドのメッセージキューを確実に生成し、スレッドIDを公開してから据える。これ以降は後始末時の PostThreadMessage(WM_QUIT) が確実に届く。
	private void HookThreadMain()
	{
		try
		{
			PickerNativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, PickerNativeMethods.PM_NOREMOVE);
			_hookThreadId = PickerNativeMethods.GetCurrentThreadId();

			InstallHooks();
			_hookReady.Set();

			while (PickerNativeMethods.GetMessage(out PickerNativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
			{
				PickerNativeMethods.TranslateMessage(ref msg);
				PickerNativeMethods.DispatchMessage(ref msg);
			}
		}
		catch
		{
			// フック据え付けやループで起きた例外はセッションを中止扱いにし、待機側を解放する。
		}
		finally
		{
			UninstallHooks();
			_hookReady.Set();
		}
	}




	// 確定(左クリック)と中止(右クリック・Esc)を拾う低レベル入力フックを、フック専用スレッドのメッセージループへ据える。
	private void InstallHooks()
	{
		_mouseProcKeepAlive = MouseHook;
		_keyProcKeepAlive = KeyHook;

		_mouseHook = PickerNativeMethods.SetWindowsHookEx(PickerNativeMethods.WH_MOUSE_LL, _mouseProcKeepAlive, _hInstance, 0);
		_keyHook = PickerNativeMethods.SetWindowsHookEx(PickerNativeMethods.WH_KEYBOARD_LL, _keyProcKeepAlive, _hInstance, 0);
	}




	// フックを解除する。据えたフック専用スレッド上(HookThreadMain の finally)で呼ぶ。
	private void UninstallHooks()
	{
		if (_mouseHook != IntPtr.Zero)
		{
			PickerNativeMethods.UnhookWindowsHookEx(_mouseHook);
			_mouseHook = IntPtr.Zero;
		}

		if (_keyHook != IntPtr.Zero)
		{
			PickerNativeMethods.UnhookWindowsHookEx(_keyHook);
			_keyHook = IntPtr.Zero;
		}
	}




	// フック専用スレッドのメッセージループへ WM_QUIT を投げ、スレッドの終了(フック解除を含む)を待つ。描画スレッドの後始末から呼ぶ。
	private void StopHookThread()
	{
		if (_hookThread is null)
		{
			return;
		}

		_hookReady.Wait(2000);

		if (_hookThreadId != 0)
		{
			PickerNativeMethods.PostThreadMessage(_hookThreadId, PickerNativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
		}

		_hookThread.Join(2000);
		_hookThread = null;
		_hookReady.Dispose();
	}




	private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
	{
		if (nCode == PickerNativeMethods.HC_ACTION)
		{
			// 確定/中止が決まった後は、後続のマウス入力(離す動作を含む)を全て握り潰して裏のアプリへ漏らさない。
			if (_closing)
			{
				return (IntPtr)1;
			}

			uint msg = (uint)wParam;

			if (msg == PickerNativeMethods.WM_LBUTTONDOWN)
			{
				int packed = Volatile.Read(ref _centerPacked);
				if ((packed & (1 << 24)) != 0)
				{
					_result = new PickedColor((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
					BeginClose();
				}

				return (IntPtr)1;
			}

			if (msg == PickerNativeMethods.WM_RBUTTONDOWN)
			{
				_result = null;
				BeginClose();
				return (IntPtr)1;
			}

			if (msg == PickerNativeMethods.WM_MOUSEWHEEL)
			{
				var ms = Marshal.PtrToStructure<PickerNativeMethods.MSLLHOOKSTRUCT>(lParam);
				short delta = (short)(ms.mouseData >> 16);

				// Ctrl 押下中は取得範囲、それ以外は拡大率を増減する。Ctrl の状態は MSLLHOOKSTRUCT に入らないため、この瞬間の物理キー状態を照会する。
				if ((PickerNativeMethods.GetAsyncKeyState(PickerNativeMethods.VK_CONTROL) & 0x8000) != 0)
				{
					AdjustSampleRadius(delta);
				}
				else
				{
					AdjustMagnify(delta);
				}

				return (IntPtr)1;
			}
		}

		return PickerNativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
	}




	// マウスホイールの回転量から実拡大率 bp を増減し、範囲内へ収める。1ノッチ(WHEEL_DELTA)あたり bp を1段動かし、満たない回転は端数として貯めて取りこぼさない。描画スレッドが次フレームで読む _blockPx へ Volatile で書く。フック専用スレッドから呼ぶ。
	private void AdjustMagnify(int wheelDelta)
	{
		_wheelResidual += wheelDelta;
		int notches = _wheelResidual / PickerNativeMethods.WHEEL_DELTA;
		if (notches == 0)
		{
			return;
		}

		_wheelResidual -= notches * PickerNativeMethods.WHEEL_DELTA;
		int next = Math.Clamp(Volatile.Read(ref _blockPx) + notches, WheelBpMin, WheelBpMax);
		Volatile.Write(ref _blockPx, next);
	}




	// Ctrl+ホイールの回転量から取得範囲の半径を増減し、範囲内へ収める。1ノッチ(WHEEL_DELTA)あたり半径を1段動かし、満たない回転は端数として貯めて取りこぼさない。描画スレッドが次フレームで読む _sampleRadius へ Volatile で書く。フック専用スレッドから呼ぶ。
	private void AdjustSampleRadius(int wheelDelta)
	{
		_sampleWheelResidual += wheelDelta;
		int notches = _sampleWheelResidual / PickerNativeMethods.WHEEL_DELTA;
		if (notches == 0)
		{
			return;
		}

		_sampleWheelResidual -= notches * PickerNativeMethods.WHEEL_DELTA;
		int next = Math.Clamp(Volatile.Read(ref _sampleRadius) + notches, 0, WheelSampleRadiusMax);
		Volatile.Write(ref _sampleRadius, next);
	}




	private IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
	{
		if (nCode == PickerNativeMethods.HC_ACTION)
		{
			// 確定/中止が決まった後は、後続のキー入力を握り潰して裏のアプリへ漏らさない。確定済みの色を後続の Esc で取り消さないためでもある。
			if (_closing)
			{
				return (IntPtr)1;
			}

			uint msg = (uint)wParam;

			if (msg == PickerNativeMethods.WM_KEYDOWN || msg == PickerNativeMethods.WM_SYSKEYDOWN)
			{
				var kb = Marshal.PtrToStructure<PickerNativeMethods.KBDLLHOOKSTRUCT>(lParam);
				if (kb.vkCode == PickerNativeMethods.VK_ESCAPE)
				{
					_result = null;
					BeginClose();
					return (IntPtr)1;
				}

				// 矢印キーでカーソルを1物理ピクセルずつ動かし、採色点を微調整する。握り潰して裏のアプリへ漏らさない。
				if (TryNudgeCursor(kb.vkCode))
				{
					return (IntPtr)1;
				}
			}
		}

		return PickerNativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
	}




	// 矢印キーなら現在のカーソル位置を1物理ピクセルだけ動かす。プロセスは Per-Monitor V2 のため SetCursorPos は物理ピクセル座標で効き、1移動が採色対象の1ソース画素ぶんに正確に対応する。描画スレッドは次フレームでこの新位置を読んで追従する。矢印キーを処理したら true を返す。フック専用スレッドから呼ぶ。
	private static bool TryNudgeCursor(uint vkCode)
	{
		int dx = 0;
		int dy = 0;
		switch (vkCode)
		{
			case PickerNativeMethods.VK_LEFT:
				dx = -1;
				break;
			case PickerNativeMethods.VK_RIGHT:
				dx = 1;
				break;
			case PickerNativeMethods.VK_UP:
				dy = -1;
				break;
			case PickerNativeMethods.VK_DOWN:
				dy = 1;
				break;
			default:
				return false;
		}

		if (PickerNativeMethods.GetCursorPos(out PickerNativeMethods.POINT cur))
		{
			PickerNativeMethods.SetCursorPos(cur.X + dx, cur.Y + dy);
		}

		return true;
	}




	// 確定/中止が決まったらメッセージループを畳むよう促す。WM_CLOSE を投げ、WndProc がタイマを止めてループを抜けさせる。
	private void BeginClose()
	{
		if (_closing)
		{
			return;
		}

		_closing = true;
		PickerNativeMethods.PostMessage(_hwnd, PickerNativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
	}




	// 現在のジオメトリに合わせて、レンズ画像の中間ターゲットと、UpdateLayeredWindow へ渡すトップダウン 32bit DIB を(再)生成する。形状変更で本体寸法が変わったときも呼ぶ。
	private void EnsureRenderSurfaces()
	{
		GlassGeometry g = _renderer.Geometry;

		// 中間ターゲット。透明合成のため Premultiplied、フォーマットは B8G8R8A8。論理寸法は本体 DIP、DPI を倍率に合わせて物理バッファを本体物理ピクセルにする。
		_rt?.Dispose();
		_rt = new CanvasRenderTarget(_device, g.CardW, g.CardH, _scale * 96f, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);

		if (_memDC == IntPtr.Zero)
		{
			IntPtr screenDC = PickerNativeMethods.GetDC(IntPtr.Zero);
			_memDC = PickerNativeMethods.CreateCompatibleDC(screenDC);
			PickerNativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
		}
		else if (_dibBitmap != IntPtr.Zero)
		{
			// 旧ビットマップを DC から外して破棄してから作り直す。
			PickerNativeMethods.SelectObject(_memDC, _oldBitmap);
			PickerNativeMethods.DeleteObject(_dibBitmap);
			_dibBitmap = IntPtr.Zero;
		}

		var bmi = new PickerNativeMethods.BITMAPINFOHEADER
		{
			biSize = Marshal.SizeOf<PickerNativeMethods.BITMAPINFOHEADER>(),
			biWidth = _bodyPhysW,
			biHeight = -_bodyPhysH,
			biPlanes = 1,
			biBitCount = 32,
			biCompression = PickerNativeMethods.BI_RGB,
			biSizeImage = (uint)(_bodyPhysW * _bodyPhysH * 4),
		};

		_dibBitmap = PickerNativeMethods.CreateDIBSection(_memDC, ref bmi, PickerNativeMethods.DIB_RGB_COLORS, out _dibBits, IntPtr.Zero, 0);
		_oldBitmap = PickerNativeMethods.SelectObject(_memDC, _dibBitmap);
	}




	// 採色点を中心とした (2r+1)² 画素を scRGB(リニア)で平均する。r=0 なら中心画素そのもの。読み戻し領域(n×n)の縁で範囲がはみ出すぶんは、取り込める画素だけで平均する。
	private static ScRgbColor AverageRegion(ScRgbColor[] px, int n, int halfSpan, int r)
	{
		if (r <= 0)
		{
			return px[halfSpan * n + halfSpan];
		}

		int x0 = Math.Max(0, halfSpan - r);
		int x1 = Math.Min(n - 1, halfSpan + r);
		int y0 = Math.Max(0, halfSpan - r);
		int y1 = Math.Min(n - 1, halfSpan + r);

		double sumR = 0;
		double sumG = 0;
		double sumB = 0;
		int count = 0;
		for (int y = y0; y <= y1; y++)
		{
			int row = y * n;
			for (int x = x0; x <= x1; x++)
			{
				ScRgbColor c = px[row + x];
				sumR += c.R;
				sumG += c.G;
				sumB += c.B;
				count++;
			}
		}

		return new ScRgbColor((float)(sumR / count), (float)(sumG / count), (float)(sumB / count), 1f);
	}




	// レンズ画面位置の背後デスクトップを FP16 で読み戻し、sRGB8 へ変換して屈折描画し、中心レチクルを重ねて per-pixel alpha のビットマップへ焼き、UpdateLayeredWindow で位置ごと反映する。
	// 同じ読み戻しから中心画素(=カーソル下)の sRGB8 を厳密変換で取り出し、確定用に保持する。
	private void RenderFrame()
	{
		if (_rt == null)
		{
			return;
		}

		if (!PickerNativeMethods.GetCursorPos(out PickerNativeMethods.POINT cur))
		{
			return;
		}

		// カーソルのいるモニタが変わったら捕捉対象をそのモニタへ切り替える。別 DPI なら倍率と描画面も作り直し、このフレームは描かずに次へ回す(最新フレーム未着のため)。
		IntPtr mon = PickerNativeMethods.MonitorFromPoint(cur, PickerNativeMethods.MONITOR_DEFAULTTONEAREST);
		if (mon != _currentMon)
		{
			_currentMon = mon;
			_capture.SwitchMonitor(mon);
			_scale = (float)(_capture.DpiX / 96.0);
			RebuildBodySizeIfNeeded();
			return;
		}

		GlassGeometry g = _renderer.Geometry;
		int monL = _capture.MonitorLeft;
		int monT = _capture.MonitorTop;

		// 拡大は自前の整数ブロック描画で行う。Win2D の変換に潜む内部丸めを経由しないので位相を完全に支配でき、採色画素を本体中心へ正確に置ける。
		// bp は 1 ソース画素を何物理ピクセルへ拡大するか(整数)。blockDip はその DIP 値。FillRectangle は DIP を素直に ×scale して物理化するため、ブロックもレチクルも同じ系で揃う。
		int bp = Volatile.Read(ref _blockPx);
		double blockDip = bp / (double)_scale;

		// 必要なソース画素だけ読む。本体中心から地図の縁(margin+card/2)までを覆う範囲＋余裕。これで読み戻し領域が大きく縮む。
		int halfSpanX = (int)Math.Ceiling((g.CardW / 2.0 + g.Margin) * _scale / bp) + 2;
		int halfSpanY = (int)Math.Ceiling((g.CardH / 2.0 + g.Margin) * _scale / bp) + 2;
		int halfSpan = Math.Max(halfSpanX, halfSpanY);
		int n = halfSpan * 2 + 1;

		int rbX = cur.X - monL - halfSpan;
		int rbY = cur.Y - monT - halfSpan;

		// レンズ窓はカーソルの右下へずらして置き、対象を隠さない。右端・下端で画面外へ出るぶんは水平／垂直の反対側へ避ける。
		PositionLensWindow(cur, monL, monT);

		// カーソル下が昇格ウィンドウ(採色フックが効かない)かを判定する。レンズはカーソルからずらして置くので、カーソル位置の真下は対象ウィンドウであってレンズ自身ではない。
		UpdateElevationBlock(cur);

		if (!_capture.TryReadRegion(rbX, rbY, n, n, out ScRgbColor[] px))
		{
			return;
		}

		// 採色値。取得範囲が単一画素なら中心画素(=カーソル下)そのもの、広げていれば採色点を中心とした (2r+1)² 画素をリニア(scRGB)で平均する。ガンマ空間で平均すると濁るため、リニアのまま平均してから sRGB8 へ落とす。
		int sampleRadius = Volatile.Read(ref _sampleRadius);
		ScRgbColor center = AverageRegion(px, n, halfSpan, sampleRadius);
		Color centerSdr = ScRgbColorMath.ScRgbToSrgb8(center, _capture.SdrWhiteScale);
		Volatile.Write(ref _centerPacked, (1 << 24) | (centerSdr.R << 16) | (centerSdr.G << 8) | centerSdr.B);

		int need = n * n * 4;
		if (_bgra.Length != need)
		{
			_bgra = new byte[need];
		}
		for (int i = 0; i < px.Length; i++)
		{
			ScRgbColorMath.ScRgbToSrgbBgra(px[i], _capture.SdrWhiteScale, _bgra, i * 4);
		}

		// 拡大方式を bp で切り替える。両経路とも 1 ソース画素 → bp 物理px の最近傍拡大で、レチクル枠は blockDip。違いは整列の厳密さ(ブロック=完全/ビットマップ=最大半画素ズレ)と速度。
		bool useBlocks = bp >= HybridBpThreshold;

		CanvasCommandList? cl = null;
		CanvasBitmap? bmp = null;
		ICanvasImage localBg;

		if (useBlocks)
		{
			// 各ソース画素を整数ブロックとして地図ローカル DIP 上へ明示的に置く。採色画素(中心)を本体中心へ正確に乗せる。Win2D の内部丸めを経由しないので枠と完全整列する。
			double bodyCxLocal = g.Margin + g.CardW / 2.0;
			double bodyCyLocal = g.Margin + g.CardH / 2.0;
			cl = new CanvasCommandList(_device);
			using (CanvasDrawingSession bds = cl.CreateDrawingSession())
			{
				bds.Antialiasing = CanvasAntialiasing.Aliased;
				for (int dy = -halfSpan; dy <= halfSpan; dy++)
				{
					double top = bodyCyLocal + dy * blockDip - blockDip / 2.0;
					if (top + blockDip < 0 || top > g.MapH)
					{
						continue;
					}
					int row = halfSpan + dy;
					for (int dx = -halfSpan; dx <= halfSpan; dx++)
					{
						double left = bodyCxLocal + dx * blockDip - blockDip / 2.0;
						if (left + blockDip < 0 || left > g.MapW)
						{
							continue;
						}
						int o = (row * n + (halfSpan + dx)) * 4;
						Color col = Color.FromArgb(255, _bgra[o + 2], _bgra[o + 1], _bgra[o + 0]);
						bds.FillRectangle((float)left, (float)top, (float)blockDip, (float)blockDip, col);
					}
				}
			}
			localBg = cl;
		}
		else
		{
			// 低倍率はビットマップを最近傍で 1 回拡大して渡す(矩形を多数積まないぶん軽い)。採色画素を本体中心へ寄せるが、位相は Win2D 内部丸め依存で最大半画素ズレる(小さい画素なので目立たない)。
			bmp = CanvasBitmap.CreateFromBytes(_device, _bgra, n, n, DirectXPixelFormat.B8G8R8A8UIntNormalized);
			double a = 1.0 / bp;
			double pCenterX = (g.CardW / 2.0 + g.Margin) * _scale;
			double pCenterY = (g.CardH / 2.0 + g.Margin) * _scale;
			double cxRb = halfSpan + 0.5 - pCenterX * a;
			double cyRb = halfSpan + 0.5 - pCenterY * a;
			Matrix3x2 m = Matrix3x2.CreateScale(bp) * Matrix3x2.CreateTranslation((float)(-cxRb * bp), (float)(-cyRb * bp));
			localBg = GlassRenderer.TransformBackground(bmp, m);
		}

		(double highlightRot, double highlightElev) = ComputeHighlightTransform(cur, monL, monT);

		// モニタが変わったときだけモニタ表記を引き直す(DisplayConfig 照会は重いため)。
		if (_currentMon != _labeledMon)
		{
			_monitorLabel = MonitorNaming.GetLabel(_currentMon);
			_labeledMon = _currentMon;
		}

		// 上端弧へ出す補助情報。カーソルの絶対座標・モニタ表記・現在モニタ内の相対座標・モニタのDPIを並べる。
		string topText = $"{cur.X}, {cur.Y} - {_monitorLabel} ({cur.X - monL}, {cur.Y - monT}) - DPI:{_capture.DpiX}";

		// 下端弧へ出す情報。拡大率・採色した色・状態タグを並べる。色が SDR 白より明るい(HDR)か sRGB 色域外(WCG)なら、#rrggbb はクランプされた近似値のため頭に ~ を冠し、該当タグを添える。
		bool hdr = ScRgbColorMath.IsBrighterThanSdrWhite(center, _capture.SdrWhiteScale);
		bool wcg = ScRgbColorMath.IsOutsideSrgbGamut(center);
		string hex = $"#{centerSdr.R:x2}{centerSdr.G:x2}{centerSdr.B:x2}";
		if (hdr || wcg)
		{
			hex = "~" + hex;
		}

		string tags = hdr ? "[HDR]" : string.Empty;
		if (wcg)
		{
			tags += tags.Length > 0 ? " [WCG]" : "[WCG]";
		}

		// 取得範囲を広げているときは、色の前に範囲の寸法(N×N)を出す。単一画素のときは既定の採色のため出さない。
		int sampleSide = 2 * sampleRadius + 1;
		string sample = sampleSide > 1 ? $"{sampleSide}×{sampleSide} - " : string.Empty;

		// カラーコードの前に採色色の見本(■)を置く。■ は GlassRenderer 側で採色色そのもので塗られる。
		string bottomText = tags.Length > 0 ? $"×{bp} - {sample}■ {hex} - {tags}" : $"×{bp} - {sample}■ {hex}";

		using (CanvasDrawingSession ds = _rt.CreateDrawingSession())
		{
			ds.Clear(Color.FromArgb(0, 0, 0, 0));
			_renderer.Draw(ds, _device, _p, localBg, -g.Margin, -g.Margin, bottomText, topText, centerSdr, highlightRot, highlightElev);

			// レチクルと禁止マークはガラス本体の上に重ねる装飾のため、本体と同じ角丸(円)形でクリップして描く。取得範囲を広げ拡大率も高いとレチクルの開口や腕が本体外へはみ出すので、円の内側へ収める。
			using (CanvasGeometry bodyClip = CanvasGeometry.CreateRoundedRectangle(_device, 0, 0, g.CardW, g.CardH, (float)g.Radius, (float)g.Radius))
			using (ds.CreateLayer(1f, bodyClip))
			{
				// レチクル枠の開口を取得範囲((2r+1)ブロック)に一致させる。範囲が単一画素なら拡大ブロック1個(blockDip)、広げていればそのぶん開口も広がり、平均に取り込む画素が枠で見える。
				DrawReticle(ds, g.CardW / 2.0, g.CardH / 2.0, (2 * sampleRadius + 1) * blockDip, _scale);

				// 昇格ウィンドウの上では採色フックが効かないため、禁止マークを重ねて採れないことを示す。
				if (_blockedByElevation)
				{
					DrawBlockedIcon(ds, g.CardW / 2.0, g.CardH / 2.0, g.CardW);
				}
			}
		}

		cl?.Dispose();
		bmp?.Dispose();

		byte[] pixels = _rt.GetPixelBytes();
		Marshal.Copy(pixels, 0, _dibBits, pixels.Length);

		var ptDst = new PickerNativeMethods.POINT { X = _lensX, Y = _lensY };
		var size = new PickerNativeMethods.SIZE { cx = _bodyPhysW, cy = _bodyPhysH };
		var ptSrc = new PickerNativeMethods.POINT { X = 0, Y = 0 };
		var blend = new PickerNativeMethods.BLENDFUNCTION
		{
			BlendOp = PickerNativeMethods.AC_SRC_OVER,
			BlendFlags = 0,
			SourceConstantAlpha = 255,
			AlphaFormat = PickerNativeMethods.AC_SRC_ALPHA,
		};

		PickerNativeMethods.UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref ptDst, ref size, _memDC, ref ptSrc, 0, ref blend, PickerNativeMethods.ULW_ALPHA);
	}




	// ハイライトを焼いたときの設計上の向き(方位角)。左上から照らした想定で、追従時はこの向きを基準に光源方向との差ぶんだけハイライトを回す。
	private const double HighlightDesignAzimDeg = 225.0;

	// 仮想光源を画面のどこに置くか。モニタ左上を原点とした幅・高さの比率で、(0.1, 0.1) は左上から1割内側。
	private const double LightSourceFracX = 0.1;
	private const double LightSourceFracY = 0.1;

	// 距離による仰角追従の帯(度)。全ての灯の仰角へ加えるオフセットで、光源直上(距離0)で Near、モニタ対角線ぶん離れたとき Far。Near>0 は仰角を上げて艶を中央寄りへ、Far<0 は下げてリム寄りへ寄せる。採色点を覆わないよう中央へ寄り切らない控えめな帯にする。
	private const double ElevOffsetNear = 18.0;
	private const double ElevOffsetFar = -28.0;




	// 光源追従が有効なとき、焼いたハイライトへ与える回転(度)と仰角オフセット(度)を求める。仮想光源は現在モニタの左上寄りに固定する。
	// 回転は採色点(カーソル)から光源へ向かう方位角と設計向きとの差。カーソルが中央・光源が左上なら方位は設計と一致し回転はゼロになる。
	// 仰角オフセットは光源との距離をモニタ対角線で正規化し、近いほど Near(仰角を上げ中央寄り)・遠いほど Far(仰角を下げリム寄り)へ補間する。これは焼き直しで本物の仰角として反映される。
	private (double rotDeg, double elevOffsetDeg) ComputeHighlightTransform(PickerNativeMethods.POINT cur, int monL, int monT)
	{
		double lightX = monL + _capture.Width * LightSourceFracX;
		double lightY = monT + _capture.Height * LightSourceFracY;
		double dx = lightX - cur.X;
		double dy = lightY - cur.Y;

		double rotDeg = 0;
		if (_p.LightFollow)
		{
			rotDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI - HighlightDesignAzimDeg;
		}

		double elevOffsetDeg = 0;
		if (_p.LightFollowDist)
		{
			double diag = Math.Sqrt((double)_capture.Width * _capture.Width + (double)_capture.Height * _capture.Height);
			double t = diag > 0 ? Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy) / diag) : 0;
			elevOffsetDeg = ElevOffsetNear + (ElevOffsetFar - ElevOffsetNear) * t;
		}

		return (rotDeg, elevOffsetDeg);
	}




	// レンズ窓をカーソルの右下へ置く。右端・下端からはみ出すぶんは反対側(左上)へ反転し、最後にカーソルのいるモニタ内へクランプする。
	private void PositionLensWindow(PickerNativeMethods.POINT cur, int monL, int monT)
	{
		int monR = monL + _capture.Width;
		int monB = monT + _capture.Height;

		int x = cur.X + LensOffset;
		int y = cur.Y + LensOffset;

		if (x + _bodyPhysW > monR)
		{
			x = cur.X - LensOffset - _bodyPhysW;
		}
		if (y + _bodyPhysH > monB)
		{
			y = cur.Y - LensOffset - _bodyPhysH;
		}

		_lensX = Math.Clamp(x, monL, Math.Max(monL, monR - _bodyPhysW));
		_lensY = Math.Clamp(y, monT, Math.Max(monT, monB - _bodyPhysH));
	}




	// ガラス本体寸法を現在の倍率・ジオメトリへ合わせ、変わっていれば描画面を作り直す。
	private void RebuildBodySizeIfNeeded()
	{
		GlassGeometry g = _renderer.Geometry;
		int newW = (int)Math.Round(g.CardW * _scale);
		int newH = (int)Math.Round(g.CardH * _scale);
		if (newW != _bodyPhysW || newH != _bodyPhysH)
		{
			_bodyPhysW = newW;
			_bodyPhysH = newH;
			EnsureRenderSurfaces();
		}
	}




	// 採色点を示す素のレチクル。中心の枠の内側の開口を「拡大表示された 1 ソース画素(ブロック)」ぴったりに合わせ、枠はその画素を外側から縁取り、四方へ腕を伸ばす。
	// cellDip は拡大ブロックの論理(DIP)サイズ(=blockDip)。ブロックも枠も同じ FillRectangle/DIP 系で描くので、本体中心に置いたブロックと枠が一致する。scale は DPI 倍率で、白縁を物理ピクセル単位で一定にするのに使う。
	// 枠・腕の内縁は画素境界(±half)に固定し外側へだけ広げる。白を先・赤を後に重ねると、白は画素の内側へ食い込まず外周だけの縁取りとして残り、枠の開口が拡大ドットにぴたり一致する。
	private static void DrawReticle(CanvasDrawingSession ds, double cx, double cy, double cellDip, float scale)
	{
		ds.Antialiasing = CanvasAntialiasing.Aliased;

		float cxf = (float)cx;
		float cyf = (float)cy;
		float half = (float)(cellDip / 2.0);   // 拡大ドットの半径。枠の内縁をここに一致させる
		float rt = 1f;                          // 赤線の太さ(枠・腕で共通)
		float we = 1f / scale;                  // 赤の外側へ出す白縁の太さ(物理1ピクセル)
		float reach = 96f;                      // 枠の外縁から外へ伸ばす腕の長さ

		Color red = Color.FromArgb(255, 255, 40, 40);
		Color white = Color.FromArgb(255, 255, 255, 255);

		// 中心の枠を、内縁を画素境界(±half)に固定した正方形リングで描く。thick ぶん外側へ広げるので、内側の開口は常に拡大ドットと一致する。
		void Ring(float thick, Color c)
		{
			float o = half + thick;
			ds.FillRectangle(cxf - o, cyf - o, o * 2, thick, c);            // 上辺
			ds.FillRectangle(cxf - o, cyf + half, o * 2, thick, c);        // 下辺
			ds.FillRectangle(cxf - o, cyf - half, thick, half * 2, c);     // 左辺
			ds.FillRectangle(cxf + half, cyf - half, thick, half * 2, c);  // 右辺
		}

		// 四方の腕。内端を画素境界(±half)に固定し、外側・側方だけ e ぶん広げる。内端は枠の赤と重なって繋がり、画素の内側へは入らない。
		void Arms(float e, Color c)
		{
			ds.FillRectangle(cxf - rt / 2 - e, cyf - half - reach - e, rt + e * 2, reach + e, c);   // 上
			ds.FillRectangle(cxf - rt / 2 - e, cyf + half, rt + e * 2, reach + e, c);               // 下
			ds.FillRectangle(cxf - half - reach - e, cyf - rt / 2 - e, reach + e, rt + e * 2, c);   // 左
			ds.FillRectangle(cxf + half, cyf - rt / 2 - e, reach + e, rt + e * 2, c);               // 右
		}

		// 白を先に全部敷き(外側へ we 広げる)、赤を後に重ねる。
		Arms(we, white);
		Ring(rt + we, white);
		Arms(0f, red);
		Ring(rt, red);
	}




	// カーソル下のウィンドウを照会し、自プロセスより高い整合性レベル(昇格ウィンドウ)なら採れない印を立てる。直前と同じウィンドウなら照会を省き、トークン照会のコストを移動の継ぎ目だけに抑える。
	private void UpdateElevationBlock(PickerNativeMethods.POINT cur)
	{
		IntPtr w = PickerNativeMethods.WindowFromPoint(cur);

		// レンズはカーソルからずらして置くので通常ここに自窓は来ないが、念のため自窓・無効値のときは前回の判定を保つ。
		if (w == _hwnd || w == IntPtr.Zero)
		{
			return;
		}

		if (w == _lastProbedHwnd)
		{
			return;
		}

		_lastProbedHwnd = w;
		_blockedByElevation = ElevationHelper.IsWindowAboveOurIntegrity(w);
	}




	// 採色できない昇格ウィンドウ上で出す禁止マーク(○に斜線)。背景の明暗に依らず立つよう、暗い下地・白縁・赤の順で重ねる。cx/cy は本体中心の DIP 座標、cardW は本体の DIP 幅で大きさの基準にする。
	private static void DrawBlockedIcon(CanvasDrawingSession ds, double cx, double cy, double cardW)
	{
		ds.Antialiasing = CanvasAntialiasing.Antialiased;

		var center = new Vector2((float)cx, (float)cy);
		float r = (float)(cardW * 0.30);
		float stroke = (float)(cardW * 0.085);

		Color red = Color.FromArgb(0xFF, 0xE5, 0x3A, 0x35);
		Color white = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

		// 下地。背景がどんな色でもマークが沈まないよう、半透明の暗い円を敷く。
		ds.FillCircle(center, r + stroke, Color.FromArgb(0x78, 0, 0, 0));

		// 斜線の端点(左上→右下、45度)。円の内側へ収める。
		double a = Math.PI / 4.0;
		float dx = (float)(Math.Cos(a) * r);
		float dy = (float)(Math.Sin(a) * r);
		var p0 = new Vector2((float)cx - dx, (float)cy - dy);
		var p1 = new Vector2((float)cx + dx, (float)cy + dy);

		// 白を先に太く敷いて縁取りにし、赤を細く重ねる。
		ds.DrawCircle(center, r, white, stroke + 2f);
		ds.DrawLine(p0, p1, white, stroke + 2f);
		ds.DrawCircle(center, r, red, stroke);
		ds.DrawLine(p0, p1, red, stroke);
	}




	private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == PickerNativeMethods.WM_TIMER)
		{
			try
			{
				RenderFrame();
			}
			catch
			{
				// 一過性の描画失敗でピッカーごと落とさないための安全網。次フレームで回復する。
			}

			return IntPtr.Zero;
		}

		if (msg == PickerNativeMethods.WM_CLOSE)
		{
			if (_timerSet)
			{
				PickerNativeMethods.KillTimer(_hwnd, RenderTimerId);
				_timerSet = false;
			}

			PickerNativeMethods.PostQuitMessage(0);
			return IntPtr.Zero;
		}

		return PickerNativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
	}




	private void RunMessageLoop()
	{
		while (PickerNativeMethods.GetMessage(out PickerNativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
		{
			PickerNativeMethods.TranslateMessage(ref msg);
			PickerNativeMethods.DispatchMessage(ref msg);
		}
	}




	// メッセージループを抜けたあとの後始末。タイマ・フック・ウィンドウ・GDI 資源・焼いたマップを解放し、ウィンドウクラスの登録も解く。繰り返しピッカーを開いても資源が積み残らないようにする。
	private void Teardown()
	{
		if (_timerSet && _hwnd != IntPtr.Zero)
		{
			PickerNativeMethods.KillTimer(_hwnd, RenderTimerId);
			_timerSet = false;
		}

		StopHookThread();

		if (_hwnd != IntPtr.Zero)
		{
			PickerNativeMethods.DestroyWindow(_hwnd);
			_hwnd = IntPtr.Zero;
		}

		FreeRenderSurfaces();
		_renderer.Dispose();

		if (_classRegistered)
		{
			PickerNativeMethods.UnregisterClass(_className, _hInstance);
			_classRegistered = false;
		}
	}




	private void FreeRenderSurfaces()
	{
		_rt?.Dispose();
		_rt = null;

		if (_memDC != IntPtr.Zero)
		{
			if (_dibBitmap != IntPtr.Zero)
			{
				PickerNativeMethods.SelectObject(_memDC, _oldBitmap);
				PickerNativeMethods.DeleteObject(_dibBitmap);
				_dibBitmap = IntPtr.Zero;
			}

			PickerNativeMethods.DeleteDC(_memDC);
			_memDC = IntPtr.Zero;
		}
	}
}
