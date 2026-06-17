// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.UI;
using Irozukume.ScreenPicker.Interop;

namespace Irozukume.ScreenPicker.Capture;

// scRGB（リニア・Rec.709）の浮動小数色。WGC を R16G16B16A16Float で捕捉した際の画素値そのもの。
// scRGB では (1,1,1) が D65 白 80nit に相当し、HDR ハイライトでは 1.0 を超える値を取りうる。
internal readonly struct ScRgbColor
{
	public readonly float R;
	public readonly float G;
	public readonly float B;
	public readonly float A;

	public ScRgbColor(float r, float g, float b, float a)
	{
		R = r;
		G = g;
		B = b;
		A = a;
	}
}

// 1モニタに対する Windows.Graphics.Capture セッション。HDR を取りこぼさないため捕捉形式を R16G16B16A16Float（FP16 scRGB）とし、カラーピッカーとして正確な色を読む。
// カーソル像は捕捉に含めない（IsCursorCaptureEnabled=false）。これによりカーソル下の実ピクセルが隠れず読める。
// 共有デバイスを複数スレッドから操作すると落ちるため、デバイス操作はすべて描画スレッド（TryReadRegion / SwitchMonitor）に寄せ、FrameArrived（バックグラウンド）は最新フレームを保持するだけにする。
internal sealed class DesktopCaptureSession : IDisposable
{
	private const DirectXPixelFormat CaptureFormat = DirectXPixelFormat.R16G16B16A16Float;
	private const int LockTimeoutMs = 1000;

	private readonly object _gate = new();
	private readonly CanvasDevice _device;
	private IntPtr _hMonitor;

	private GraphicsCaptureItem? _item;
	private Direct3D11CaptureFramePool? _framePool;
	private GraphicsCaptureSession? _session;
	private Direct3D11CaptureFrame? _latestFrame;
	private CanvasRenderTarget? _scratch;
	private int _scratchW;
	private int _scratchH;
	private long _frameCount;
	private TimeSpan _lastFrameTime;
	private double _lastIntervalMs;

	// 新フレーム到達をバックグラウンドスレッドで通知する。
	public event Action? FrameReady;

	public CanvasDevice Device => _device;
	public long FrameCount => Interlocked.Read(ref _frameCount);
	public double LastIntervalMs => _lastIntervalMs;
	public IntPtr HMonitor => _hMonitor;
	public int MonitorLeft { get; private set; }
	public int MonitorTop { get; private set; }
	public string DeviceName { get; private set; } = string.Empty;
	public uint DpiX { get; private set; } = 96;
	public uint DpiY { get; private set; } = 96;
	public int Width { get; private set; }
	public int Height { get; private set; }
	public double SdrWhiteScale { get; private set; } = 1.0;
	public int ScalePercent => (int)Math.Round(DpiX * 100.0 / 96.0);




	public DesktopCaptureSession(CanvasDevice device, IntPtr hMonitor)
	{
		_device = device;
		_hMonitor = hMonitor;
	}




	public void Start()
	{
		QueryMonitorMetrics();

		_item = CaptureInterop.CreateItemForMonitor(_hMonitor);
		Width = _item.Size.Width;
		Height = _item.Size.Height;

		_framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, CaptureFormat, 2, _item.Size);
		_session = _framePool.CreateCaptureSession(_item);

		// カーソル像を捕捉に含めない。カーソル下の実ピクセルを採色するため必須。古いOSでは未対応なので例外を許容する。
		try
		{
			_session.IsCursorCaptureEnabled = false;
		}
		catch
		{
		}

		_framePool.FrameArrived += OnFrameArrived;
		_session.StartCapture();
	}




	// 捕捉対象のモニタを切り替える。レンズが別モニタへ移ったときに呼ぶ。古いセッションとプールを畳み、新しいモニタで捕捉し直す。
	// デバイス操作を含むので描画スレッドから呼ぶこと。バックグラウンドの FrameArrived がこの破棄中に古いプールへ触れて落ちるのを防ぐため、破棄一式を _gate ロック下で行う。FrameArrived 側はロック取得後に現行プールと一致するか確かめてからフレームを取るので、破棄とフレーム取得が同時に走らない。
	public void SwitchMonitor(IntPtr hMonitor)
	{
		if (hMonitor == _hMonitor)
		{
			return;
		}

		lock (_gate)
		{
			if (_framePool is not null)
			{
				_framePool.FrameArrived -= OnFrameArrived;
			}

			_session?.Dispose();
			_framePool?.Dispose();
			_session = null;
			_framePool = null;

			_latestFrame?.Dispose();
			_latestFrame = null;

			_hMonitor = hMonitor;
		}

		Start();
	}




	private void QueryMonitorMetrics()
	{
		var mi = new PickerNativeMethods.MONITORINFOEX
		{
			cbSize = Marshal.SizeOf<PickerNativeMethods.MONITORINFOEX>(),
		};

		if (PickerNativeMethods.GetMonitorInfo(_hMonitor, ref mi))
		{
			MonitorLeft = mi.rcMonitor.Left;
			MonitorTop = mi.rcMonitor.Top;
			DeviceName = mi.szDevice;
		}

		if (PickerNativeMethods.GetDpiForMonitor(_hMonitor, PickerNativeMethods.MDT_EFFECTIVE_DPI, out uint dx, out uint dy) == 0)
		{
			DpiX = dx;
			DpiY = dy;
		}

		SdrWhiteScale = DisplayInfo.GetSdrWhiteScale(DeviceName);
	}




	// バックグラウンドスレッドで発火する。デバイス操作は一切せず、最新フレームを保持するだけにとどめる(共有デバイスをこのスレッドから触らない)。
	// フレーム取得はロック下で、かつ sender が現行プールと一致するときだけ行う。モニタ切り替えで破棄された古いプールへ触れないようにするため。
	// バックグラウンドスレッドでの未処理例外はプロセスを終了させるので、本体全体を try/catch で囲み、切り替え中の競合などで例外が出てもアプリを落とさない。
	private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
	{
		bool stored = false;

		try
		{
			bool taken = false;

			try
			{
				Monitor.TryEnter(_gate, LockTimeoutMs, ref taken);
				if (!taken)
				{
					return;
				}

				if (!ReferenceEquals(sender, _framePool))
				{
					return;
				}

				Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
				if (frame is null)
				{
					return;
				}

				_latestFrame?.Dispose();
				_latestFrame = frame;

				long count = Interlocked.Increment(ref _frameCount);
				TimeSpan now = frame.SystemRelativeTime;
				if (count > 1)
				{
					_lastIntervalMs = (now - _lastFrameTime).TotalMilliseconds;
				}
				_lastFrameTime = now;
				stored = true;
			}
			finally
			{
				if (taken)
				{
					Monitor.Exit(_gate);
				}
			}
		}
		catch
		{
			return;
		}

		if (stored)
		{
			FrameReady?.Invoke();
		}
	}




	// 描画スレッドから呼ぶ。最新フレームから要求矩形をスクラッチへ複写し、scRGB 浮動小数で CPU へ読み戻す。
	// 範囲外は黒(0,0,0,1)で埋め、配列の (0,0) が常に要求した (left,top) に対応する。原点はクランプしないため、中心画素を一定の位置から取れる。フレーム未着なら false。
	public bool TryReadRegion(int left, int top, int w, int h, out ScRgbColor[] pixels)
	{
		pixels = Array.Empty<ScRgbColor>();
		if (w <= 0 || h <= 0)
		{
			return false;
		}

		bool taken = false;

		try
		{
			Monitor.TryEnter(_gate, LockTimeoutMs, ref taken);
			if (!taken)
			{
				return false;
			}

			if (_latestFrame is null)
			{
				return false;
			}

			if (_scratch is null || _scratchW != w || _scratchH != h)
			{
				_scratch?.Dispose();
				_scratch = new CanvasRenderTarget(_device, w, h, 96f, CaptureFormat, CanvasAlphaMode.Ignore);
				_scratchW = w;
				_scratchH = h;
			}

			using CanvasBitmap whole = CanvasBitmap.CreateFromDirect3D11Surface(_device, _latestFrame.Surface, 96f, CanvasAlphaMode.Ignore);

			using (CanvasDrawingSession ds = _scratch.CreateDrawingSession())
			{
				ds.Clear(Color.FromArgb(255, 0, 0, 0));
				ds.DrawImage(whole, new Vector2(-left, -top));
			}

			byte[] raw = _scratch.GetPixelBytes(0, 0, w, h);
			pixels = ToScRgb(raw, w * h);
			return true;
		}
		finally
		{
			if (taken)
			{
				Monitor.Exit(_gate);
			}
		}
	}




	private static ScRgbColor[] ToScRgb(byte[] raw, int count)
	{
		var px = new ScRgbColor[count];
		for (int i = 0; i < count; i++)
		{
			int o = i * 8;
			px[i] = new ScRgbColor(
				(float)BitConverter.ToHalf(raw, o),
				(float)BitConverter.ToHalf(raw, o + 2),
				(float)BitConverter.ToHalf(raw, o + 4),
				(float)BitConverter.ToHalf(raw, o + 6));
		}
		return px;
	}




	public void Dispose()
	{
		try
		{
			// 破棄一式を _gate 下で行い、バックグラウンドの FrameArrived が破棄中のプールへ触れないようにする。FrameArrived 側は現行プールと一致するときだけフレームを取る。
			lock (_gate)
			{
				if (_framePool is not null)
				{
					_framePool.FrameArrived -= OnFrameArrived;
				}

				_session?.Dispose();
				_framePool?.Dispose();
				_session = null;
				_framePool = null;

				_scratch?.Dispose();
				_latestFrame?.Dispose();
				_scratch = null;
				_latestFrame = null;
			}
		}
		catch
		{
		}
	}
}
