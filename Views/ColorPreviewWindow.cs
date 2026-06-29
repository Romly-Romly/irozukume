// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.Interop;
using Irozukume.ScreenPicker.Interop;

namespace Irozukume.Views;

/// <summary>
/// 1色を見せる自前のレイヤードウィンドウ。
/// WinUI のウィンドウはクライアント領域を机に透かせない(不透明なリダイレクション面が残る)ため、Win32 のレイヤードウィンドウ(WS_EX_LAYERED)を Win2D で描き、UpdateLayeredWindow でピクセル単位のαを反映する。
/// 上端に不透明なバー(色コードを表示)、その下に色をαで塗った本体を置き、本体の背後に机が透ける。
/// バーも本体もドラッグで窓を移動でき、本体の左右・下端・下隅の縁ではサイズ変更する。
/// F でフルスクリーン(本体が画面を覆い、バーを隠す)、Esc で閉じる(隠す)。
/// 窓はセッション中1つだけを使い回し、別の色で開き直すと塗りとコードを差し替えて前面へ出す。色パネルの右クリックメニューから <see cref="ShowColor"/> で表示する。
/// </summary>
public sealed class ColorPreviewWindow
{
	/// <summary>
	/// タイトルバー帯の高さ(DIP)。
	/// </summary>
	private const int ToolbarHeightDip = 36;

	/// <summary>
	/// 枠付き表示の既定サイズ(DIP)。
	/// </summary>
	private const int DefaultWidthDip = 480;
	private const int DefaultHeightDip = 320;

	/// <summary>
	/// F キーの仮想キーコード。
	/// </summary>
	private const int VkF = 0x46;

	/// <summary>
	/// 本体の縁をドラッグしてサイズ変更できる縁の太さ(DIP)と、縮小の下限(DIP)。
	/// </summary>
	private const int ResizeBorderDip = 12;
	private const int MinWidthDip = 220;
	private const int MinHeightDip = 140;

	/// <summary>
	/// サイズ変更まわりのメッセージとヒットテスト結果。
	/// 本体の左右・下端・下隅をドラッグするとシステムがサイズ変更し、WM_SIZE で新寸法へ描き直す。各 HT* は縁・隅の位置に対応する。タイトルバー側(上)は伸縮させないため上端・上隅は持たない。
	/// </summary>
	private const uint WM_SIZE = 0x0005;
	private const int HTLEFT = 10;
	private const int HTRIGHT = 11;
	private const int HTBOTTOM = 15;
	private const int HTBOTTOMLEFT = 16;
	private const int HTBOTTOMRIGHT = 17;

	/// <summary>
	/// フルスクリーン切替に使う非クライアントダブルクリックメッセージと、クラスへ与えるダブルクリック有効化スタイル(これが無いと WM_NCLBUTTONDBLCLK が来ない)。
	/// バー帯も本体も HTCAPTION で持つため、ダブルクリックは非クライアント側で届く。
	/// </summary>
	private const uint CS_DBLCLKS = 0x0008;
	private const uint WM_NCLBUTTONDBLCLK = 0x00A3;

	/// <summary>
	/// ウィンドウクラス名を一意にするための連番。
	/// </summary>
	private static int _classSeq;

	/// <summary>
	/// WndProc のデリゲートを GC から守るために保持する。関数ポインタを Win32 へ渡したあと回収されると落ちるため。
	/// </summary>
	private PickerNativeMethods.WndProcDelegate? _wndProcKeepAlive;
	private IntPtr _hwnd;
	private IntPtr _hInstance;
	private string _className = string.Empty;

	/// <summary>
	/// 画像を描く Win2D デバイスと、UpdateLayeredWindow へ渡す GDI 資源。
	/// </summary>
	private readonly CanvasDevice _device = new();
	private IntPtr _memDC;
	private IntPtr _dibBitmap;
	private IntPtr _oldBitmap;
	private IntPtr _dibBits;
	private int _dibW;
	private int _dibH;
	private float _scale = 1f;

	/// <summary>
	/// 本体の色(αを含む)と、タイトルバーへ出すカラーコード。ShowColor で更新する。
	/// </summary>
	private Color _color = Color.FromArgb(0xFF, 0, 0, 0);
	private string _code = string.Empty;

	/// <summary>
	/// フルスクリーン中か。F・フルスクリーンで切り替える。
	/// </summary>
	private bool _isFullScreen;

	/// <summary>
	/// 描画中の再入を抑える札。UpdateLayeredWindow が同期で WM_SIZE を呼び戻しても二重に描かない。
	/// </summary>
	private bool _rendering;

	/// <summary>
	/// 枠付き(windowed)時の左上位置と寸法(物理ピクセル)。フルスクリーンから戻る位置の保持に使う。
	/// </summary>
	private int _winX;
	private int _winY;
	private int _physW = DefaultWidthDip;
	private int _physH = DefaultHeightDip;




	/// <summary>
	/// 指定の色を表示し、前面へ出す。色コードはタイトルバーへ、色はαごと本体へ反映する。隠した後の再表示にも使い、別の色で呼ぶと中身を差し替える。
	/// </summary>
	public void ShowColor(Color color, byte alpha)
	{
		SetColor(color, alpha);

		EnsureWindow();

		// 枠付き表示中ならドラッグで動いた現在位置を取り込んでから描く。フルスクリーン中の再表示は枠付きへ戻す。
		if (_isFullScreen)
		{
			_isFullScreen = false;
		}
		else
		{
			SyncWindowedPosition();
		}

		Render();
		PickerNativeMethods.ShowWindow(_hwnd, PickerNativeMethods.SW_SHOW);
		NativeMethods.SetForegroundWindow(_hwnd);
	}




	/// <summary>
	/// 表示中のプレビューの色だけを差し替える。
	/// アクティブな色の編集へ追従させるために使い、前面の奪取はしない(編集の手を止めないため)。窓が無い・隠れている間は何もしない。
	/// 枠付き表示中はドラッグで動いた現在位置を取り込んでから描く。これをしないと UpdateLayeredWindow が古い記録位置へ窓を引き戻し、移動が無かったことになる(タイトルバーのドラッグ移動では WM_SIZE が飛ばず記録位置が更新されないため)。
	/// </summary>
	public void UpdateColor(Color color, byte alpha)
	{
		if (!IsVisible)
		{
			return;
		}

		SetColor(color, alpha);

		if (!_isFullScreen)
		{
			SyncWindowedPosition();
		}

		Render();
	}




	/// <summary>
	/// 窓が生成済みで画面に出ているか。アクティブ色への追従を表示中だけに絞るために見る。
	/// </summary>
	public bool IsVisible => _hwnd != IntPtr.Zero && PickerNativeMethods.IsWindowVisible(_hwnd);




	/// <summary>
	/// 本体の色(αを含む)とタイトルバーへ出すカラーコードを現在値へ揃える。表示と追従の両方が通る。
	/// </summary>
	private void SetColor(Color color, byte alpha)
	{
		_color = Color.FromArgb(alpha, color.R, color.G, color.B);
		_code = alpha >= 0xFF
			? ColorStringFormatter.HexRgb(color.R, color.G, color.B)
			: ColorStringFormatter.HexRgba(color.R, color.G, color.B, alpha);
	}




	/// <summary>
	/// ウィンドウを隠す。トレイ退避時や Esc・閉じるで呼ばれる。破棄せず使い回す。
	/// </summary>
	public void HideWindow()
	{
		if (_hwnd != IntPtr.Zero)
		{
			PickerNativeMethods.ShowWindow(_hwnd, PickerNativeMethods.SW_HIDE);
		}
	}




	/// <summary>
	/// 初回だけネイティブのレイヤードウィンドウを作り、DPI を読んで寸法と初期位置を決める。
	/// </summary>
	private void EnsureWindow()
	{
		if (_hwnd != IntPtr.Zero)
		{
			return;
		}

		CreateNativeWindow();

		uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
		_scale = dpi == 0 ? 1f : dpi / 96f;
		_physW = (int)Math.Round(DefaultWidthDip * _scale);
		_physH = (int)Math.Round(DefaultHeightDip * _scale);

		CenterOnPrimaryMonitor();
	}




	private void CreateNativeWindow()
	{
		_hInstance = PickerNativeMethods.GetModuleHandle(null);
		_className = "IrozukumeColorPreview_" + Interlocked.Increment(ref _classSeq);
		_wndProcKeepAlive = WndProc;

		var wc = new PickerNativeMethods.WNDCLASSEX
		{
			cbSize = Marshal.SizeOf<PickerNativeMethods.WNDCLASSEX>(),
			style = CS_DBLCLKS,
			lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive),
			hInstance = _hInstance,
			lpszClassName = _className,
			// 既定の矢印カーソル(IDC_ARROW=32512)。これを与えないと、伸縮の縁から本体へ入ってもカーソルが両矢印のまま残る。
			// HTCLIENT のとき DefWindowProc がクラスカーソルを当てる仕組みのため、空だと直前のカーソルが居座る。
			hCursor = PickerNativeMethods.LoadCursor(IntPtr.Zero, (IntPtr)32512),
		};
		PickerNativeMethods.RegisterClassEx(ref wc);

		// レイヤード窓。タスクバーへ出すため WS_EX_APPWINDOW を併せる。中身と位置・寸法は UpdateLayeredWindow が一括で決めるため、生成時の寸法は仮で構わない。
		uint exStyle = (uint)(PickerNativeMethods.WS_EX_LAYERED | PickerNativeMethods.WS_EX_APPWINDOW);
		_hwnd = PickerNativeMethods.CreateWindowEx(exStyle, _className, Loc.Get("PreviewWindowTitle"), PickerNativeMethods.WS_POPUP, 100, 100, DefaultWidthDip, DefaultHeightDip, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

		if (_hwnd == IntPtr.Zero)
		{
			throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
		}
	}




	/// <summary>
	/// 初期位置をプライマリモニタの作業領域の中央に置く。
	/// </summary>
	private void CenterOnPrimaryMonitor()
	{
		IntPtr mon = PickerNativeMethods.MonitorFromPoint(new PickerNativeMethods.POINT { X = 0, Y = 0 }, PickerNativeMethods.MONITOR_DEFAULTTOPRIMARY);
		var mi = new PickerNativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<PickerNativeMethods.MONITORINFOEX>() };

		if (PickerNativeMethods.GetMonitorInfo(mon, ref mi))
		{
			_winX = mi.rcWork.Left + (mi.rcWork.Width - _physW) / 2;
			_winY = mi.rcWork.Top + (mi.rcWork.Height - _physH) / 2;
		}
		else
		{
			_winX = 100;
			_winY = 100;
		}
	}




	/// <summary>
	/// ドラッグで動いた現在のウィンドウ位置を枠付き位置として取り込む。フルスクリーンへ移る前と、枠付きでの再表示前に呼ぶ。
	/// </summary>
	private void SyncWindowedPosition()
	{
		if (PickerNativeMethods.GetWindowRect(_hwnd, out PickerNativeMethods.RECT r))
		{
			_winX = r.Left;
			_winY = r.Top;
		}
	}




	/// <summary>
	/// 枠付きとフルスクリーンを行き来する。フルスクリーンへ移る前に枠付き位置を控え、戻るときその位置へ復す。
	/// </summary>
	private void ToggleFullScreen()
	{
		if (_isFullScreen)
		{
			_isFullScreen = false;
		}
		else
		{
			SyncWindowedPosition();
			_isFullScreen = true;
		}

		Render();
	}




	/// <summary>
	/// 描画。UpdateLayeredWindow が同期で WM_SIZE を呼び戻しても二重に描かないよう、再入を札で抑える。
	/// </summary>
	private void Render()
	{
		if (_rendering)
		{
			return;
		}

		_rendering = true;
		try
		{
			RenderCore();
		}
		finally
		{
			_rendering = false;
		}
	}




	/// <summary>
	/// 現在の状態(枠付き/フルスクリーン)に合わせて画像を描き、UpdateLayeredWindow で位置・寸法・中身を一括反映する。
	/// 枠付きは上バー＋本体、フルスクリーンはバーを隠して本体だけをモニタ全面へ。
	/// </summary>
	private void RenderCore()
	{
		int x;
		int y;
		int w;
		int h;
		bool showToolbar;

		if (_isFullScreen)
		{
			IntPtr mon = PickerNativeMethods.MonitorFromWindow(_hwnd, PickerNativeMethods.MONITOR_DEFAULTTONEAREST);
			var mi = new PickerNativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<PickerNativeMethods.MONITORINFOEX>() };
			PickerNativeMethods.GetMonitorInfo(mon, ref mi);
			x = mi.rcMonitor.Left;
			y = mi.rcMonitor.Top;
			w = mi.rcMonitor.Width;
			h = mi.rcMonitor.Height;
			showToolbar = false;
		}
		else
		{
			x = _winX;
			y = _winY;
			w = _physW;
			h = _physH;
			showToolbar = true;
		}

		EnsureDib(w, h);

		using (var rt = new CanvasRenderTarget(_device, w, h, 96f, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied))
		{
			using (CanvasDrawingSession ds = rt.CreateDrawingSession())
			{
				ds.Clear(Color.FromArgb(0, 0, 0, 0));

				int toolbarH = showToolbar ? (int)Math.Round(ToolbarHeightDip * _scale) : 0;

				// 本体。色のαで塗るため、レイヤード窓の per-pixel alpha で背後の机が透ける。
				ds.FillRectangle(0, toolbarH, w, h - toolbarH, _color);

				if (showToolbar)
				{
					// 不透明なタイトルバー帯。どんな本体色の上でも読めるよう濃灰に白寄りの文字・アイコンを置く。
					ds.FillRectangle(0, 0, w, toolbarH, Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B));

					int btnW = toolbarH;
					int fsX = w - 2 * btnW;
					int closeX = w - btnW;
					Color fg = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0);

					// 左寄せのカラーコード。右のボタンに被らないよう幅を切る。
					using (var fmt = new CanvasTextFormat
					{
						FontFamily = "Consolas",
						FontSize = 14f * _scale,
						HorizontalAlignment = CanvasHorizontalAlignment.Left,
						VerticalAlignment = CanvasVerticalAlignment.Center,
					})
					{
						float pad = 10f * _scale;
						ds.DrawText(_code, new Rect(pad, 0, Math.Max(0, fsX - pad * 2), toolbarH), fg, fmt);
					}

					// 右にフルスクリーン(四隅の括弧)と閉じる(×)のボタン。当たり判定は HitButton と同じ並び。
					DrawFullScreenIcon(ds, fsX + btnW / 2f, toolbarH / 2f, toolbarH * 0.4f, fg);
					DrawCloseIcon(ds, closeX + btnW / 2f, toolbarH / 2f, toolbarH * 0.34f, fg);
				}

				if (_isFullScreen)
				{
					DrawFullScreenHint(ds, w, h);
				}
			}

			byte[] pixels = rt.GetPixelBytes();
			Marshal.Copy(pixels, 0, _dibBits, pixels.Length);
		}

		var ptDst = new PickerNativeMethods.POINT { X = x, Y = y };
		var size = new PickerNativeMethods.SIZE { cx = w, cy = h };
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




	/// <summary>
	/// UpdateLayeredWindow へ渡すトップダウン 32bit DIB を、寸法が変わったとき(枠付き⇔フルスクリーン)だけ作り直す。
	/// </summary>
	private void EnsureDib(int w, int h)
	{
		if (_memDC == IntPtr.Zero)
		{
			IntPtr screenDC = PickerNativeMethods.GetDC(IntPtr.Zero);
			_memDC = PickerNativeMethods.CreateCompatibleDC(screenDC);
			PickerNativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
		}

		if (_dibBitmap != IntPtr.Zero && _dibW == w && _dibH == h)
		{
			return;
		}

		if (_dibBitmap != IntPtr.Zero)
		{
			PickerNativeMethods.SelectObject(_memDC, _oldBitmap);
			PickerNativeMethods.DeleteObject(_dibBitmap);
			_dibBitmap = IntPtr.Zero;
		}

		var bmi = new PickerNativeMethods.BITMAPINFOHEADER
		{
			biSize = Marshal.SizeOf<PickerNativeMethods.BITMAPINFOHEADER>(),
			biWidth = w,
			biHeight = -h,
			biPlanes = 1,
			biBitCount = 32,
			biCompression = PickerNativeMethods.BI_RGB,
			biSizeImage = (uint)(w * h * 4),
		};

		_dibBitmap = PickerNativeMethods.CreateDIBSection(_memDC, ref bmi, PickerNativeMethods.DIB_RGB_COLORS, out _dibBits, IntPtr.Zero, 0);
		_oldBitmap = PickerNativeMethods.SelectObject(_memDC, _dibBitmap);
		_dibW = w;
		_dibH = h;
	}




	private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == PickerNativeMethods.WM_NCHITTEST)
		{
			return HitTest(lParam);
		}

		if (msg == WM_SIZE)
		{
			OnSize();
			return IntPtr.Zero;
		}

		if (msg == NativeMethods.WM_GETMINMAXINFO)
		{
			ClampMinSize(lParam);
			return IntPtr.Zero;
		}

		if (msg == PickerNativeMethods.WM_KEYDOWN)
		{
			int vk = (int)wParam;

			if (vk == (int)PickerNativeMethods.VK_ESCAPE)
			{
				HideWindow();
				return IntPtr.Zero;
			}

			if (vk == VkF)
			{
				ToggleFullScreen();
				return IntPtr.Zero;
			}
		}

		if (msg == PickerNativeMethods.WM_LBUTTONDOWN)
		{
			int lp = (int)(lParam.ToInt64() & 0xFFFFFFFFL);
			int clientX = (short)(lp & 0xFFFF);
			int clientY = (short)((lp >> 16) & 0xFFFF);
			int toolbarH = (int)Math.Round(ToolbarHeightDip * _scale);
			int btn = !_isFullScreen && clientY >= 0 && clientY < toolbarH ? HitButton(clientX) : -1;

			if (btn == 0)
			{
				ToggleFullScreen();
			}
			else if (btn == 1)
			{
				HideWindow();
			}

			return IntPtr.Zero;
		}

		if (msg == WM_NCLBUTTONDBLCLK && (int)wParam == PickerNativeMethods.HTCAPTION)
		{
			// バー帯と本体のダブルクリック(どちらも HTCAPTION)でフルスクリーンを切り替える。既定の最大化は抑える。
			ToggleFullScreen();
			return IntPtr.Zero;
		}

		if (msg == PickerNativeMethods.WM_CLOSE)
		{
			HideWindow();
			return IntPtr.Zero;
		}

		return PickerNativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
	}




	/// <summary>
	/// タイトルバーでは伸縮しない。
	/// バー内のボタンはクリック(HTCLIENT)、それ以外の帯はドラッグ移動(HTCAPTION)。本体は左右・下端・下隅の縁で伸縮、それ以外はドラッグ移動(HTCAPTION)。フルスクリーン中は全面 HTCLIENT。
	/// </summary>
	private IntPtr HitTest(IntPtr lParam)
	{
		if (_isFullScreen)
		{
			return (IntPtr)PickerNativeMethods.HTCLIENT;
		}

		int packed = (int)(lParam.ToInt64() & 0xFFFFFFFFL);
		int screenX = (short)(packed & 0xFFFF);
		int screenY = (short)((packed >> 16) & 0xFFFF);

		if (!PickerNativeMethods.GetWindowRect(_hwnd, out PickerNativeMethods.RECT r))
		{
			return (IntPtr)PickerNativeMethods.HTCLIENT;
		}

		int clientX = screenX - r.Left;
		int clientY = screenY - r.Top;
		int toolbarH = (int)Math.Round(ToolbarHeightDip * _scale);

		// バー帯は伸縮させない。ボタンの上はクリック、それ以外の帯はドラッグ移動。
		if (clientY < toolbarH)
		{
			return HitButton(clientX) >= 0 ? (IntPtr)PickerNativeMethods.HTCLIENT : (IntPtr)PickerNativeMethods.HTCAPTION;
		}

		// 本体は左右・下端・下隅の縁で伸縮、それ以外はドラッグ移動(HTCAPTION)。
		int border = (int)Math.Round(ResizeBorderDip * _scale);
		int resize = ResizeHitCode(clientX, clientY, r.Width, r.Height, border);
		return resize >= 0 ? (IntPtr)resize : (IntPtr)PickerNativeMethods.HTCAPTION;
	}




	/// <summary>
	/// 本体の縁(border 太さ)のうち、左右・下端・下隅に居れば伸縮用の HT* を、それ以外は -1 を返す。タイトルバー側(上)は伸縮させないため上端・上隅は持たない。隅(2辺)を辺より先に判定する。
	/// </summary>
	private int ResizeHitCode(int clientX, int clientY, int w, int h, int border)
	{
		bool bottom = clientY >= h - border;
		bool left = clientX < border;
		bool right = clientX >= w - border;

		return (bottom, left, right) switch
		{
			(true, true, _) => HTBOTTOMLEFT,
			(true, _, true) => HTBOTTOMRIGHT,
			(true, _, _) => HTBOTTOM,
			(_, true, _) => HTLEFT,
			(_, _, true) => HTRIGHT,
			_ => -1,
		};
	}




	/// <summary>
	/// 端ドラッグでサイズが変わったら、実寸と位置を取り込んで描き直す。
	/// </summary>
	private void OnSize()
	{
		if (_isFullScreen)
		{
			return;
		}

		if (PickerNativeMethods.GetWindowRect(_hwnd, out PickerNativeMethods.RECT r))
		{
			_winX = r.Left;
			_winY = r.Top;
			_physW = r.Width;
			_physH = r.Height;
		}

		Render();
	}




	/// <summary>
	/// ドラッグ縮小の下限を、バーとボタンが潰れないサイズに留める。
	/// </summary>
	private void ClampMinSize(IntPtr lParam)
	{
		var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
		mmi.ptMinTrackSize.X = (int)Math.Round(MinWidthDip * _scale);
		mmi.ptMinTrackSize.Y = (int)Math.Round(MinHeightDip * _scale);
		Marshal.StructureToPtr(mmi, lParam, false);
	}




	/// <summary>
	/// バー内の x 座標(クライアント)がどのボタンの上か。0=フルスクリーン、1=閉じる、-1=ボタン以外。並びは <see cref="Render"/> のアイコン配置と揃える。
	/// </summary>
	private int HitButton(int clientX)
	{
		int btnW = (int)Math.Round(ToolbarHeightDip * _scale);
		int closeX = _physW - btnW;
		int fsX = _physW - 2 * btnW;

		if (clientX >= closeX && clientX < _physW)
		{
			return 1;
		}

		if (clientX >= fsX && clientX < closeX)
		{
			return 0;
		}

		return -1;
	}




	/// <summary>
	/// フルスクリーン中に画面中央へ出す解除ヒントの札。本体の色が透明に近いと画面が素の机に見え、操作できないと誤解されるため、本体のαに左右されない不透明な下地へカラーコードと解除方法を載せ、何かが被さっていると一目で分かるようにする。
	/// </summary>
	private void DrawFullScreenHint(CanvasDrawingSession ds, int w, int h)
	{
		string text = _code + "\n" + Loc.Get("PreviewFullScreenHint");

		using var fmt = new CanvasTextFormat
		{
			FontFamily = "Segoe UI",
			FontSize = 18f * _scale,
			HorizontalAlignment = CanvasHorizontalAlignment.Center,
			VerticalAlignment = CanvasVerticalAlignment.Center,
			WordWrapping = CanvasWordWrapping.NoWrap,
		};

		// 画面いっぱいの版面で中央寄せに組み、実際に文字が乗る範囲(DrawBounds)を採って下地の大きさを決める。
		using var layout = new CanvasTextLayout(ds, text, fmt, w, h);
		Rect ink = layout.DrawBounds;
		float padX = 28f * _scale;
		float padY = 18f * _scale;
		var pill = new Rect(ink.X - padX, ink.Y - padY, ink.Width + padX * 2, ink.Height + padY * 2);

		ds.FillRoundedRectangle(pill, 12f * _scale, 12f * _scale, Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B));
		ds.DrawTextLayout(layout, 0, 0, Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0));
	}




	/// <summary>
	/// フルスクリーンを表す四隅の括弧アイコン。中心(cx,cy)、一辺 size の正方形の四隅へ短いL字を描く。
	/// </summary>
	private static void DrawFullScreenIcon(CanvasDrawingSession ds, float cx, float cy, float size, Color color)
	{
		float half = size / 2f;
		float corner = size * 0.34f;
		float t = Math.Max(1f, size * 0.12f);
		float l = cx - half;
		float r = cx + half;
		float top = cy - half;
		float bottom = cy + half;

		ds.DrawLine(l, top, l + corner, top, color, t);
		ds.DrawLine(l, top, l, top + corner, color, t);
		ds.DrawLine(r, top, r - corner, top, color, t);
		ds.DrawLine(r, top, r, top + corner, color, t);
		ds.DrawLine(l, bottom, l + corner, bottom, color, t);
		ds.DrawLine(l, bottom, l, bottom - corner, color, t);
		ds.DrawLine(r, bottom, r - corner, bottom, color, t);
		ds.DrawLine(r, bottom, r, bottom - corner, color, t);
	}




	/// <summary>
	/// 閉じるを表す × アイコン。中心(cx,cy)、対角線の長さ size。
	/// </summary>
	private static void DrawCloseIcon(CanvasDrawingSession ds, float cx, float cy, float size, Color color)
	{
		float half = size / 2f;
		float t = Math.Max(1f, size * 0.12f);
		ds.DrawLine(cx - half, cy - half, cx + half, cy + half, color, t);
		ds.DrawLine(cx - half, cy + half, cx + half, cy - half, color, t);
	}
}
