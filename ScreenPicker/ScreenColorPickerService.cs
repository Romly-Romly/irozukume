// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Irozukume.Helpers;
using Irozukume.ScreenPicker.Capture;
using Irozukume.ScreenPicker.Glass;
using Irozukume.ScreenPicker.Interop;

namespace Irozukume.ScreenPicker;

// 確定された採色値。sRGB 8bit の RGB。
internal readonly record struct PickedColor(byte R, byte G, byte B);




// 画面カラーピッカーのセッションを取り回す。捕捉デバイスとセッションを起こし、ガラスレンズ窓を出して採色を待ち、確定色を返す。
// レンズは専用スレッドで動くため、確定/中止はレンズ窓のコールバックで通知され、ここで捕捉とデバイスを畳んで結果を確定する。多重起動は1セッションに制限する。
internal sealed class ScreenColorPickerService
{
	private bool _busy;




	// 画面ピッカーを開き、利用者が確定したら採色色を、中止したら null を返す。既にセッション中、または捕捉非対応の環境では null を返す。
	public async Task<PickedColor?> PickAsync()
	{
		if (_busy)
		{
			return null;
		}

		_busy = true;
		CanvasDevice? device = null;
		DesktopCaptureSession? capture = null;
		bool handedOff = false;

		try
		{
			if (!GraphicsCaptureSession.IsSupported())
			{
				return null;
			}

			device = new CanvasDevice();
			IntPtr hmon = PickStartMonitor();
			capture = new DesktopCaptureSession(device, hmon);
			capture.Start();

			GlassParams p = BuildParams();
			var tcs = new TaskCompletionSource<PickedColor?>();
			DesktopCaptureSession captureRef = capture;
			CanvasDevice deviceRef = device;

			var lens = new ScreenPickerLensWindow(p, capture, result =>
			{
				// レンズスレッドの後始末時に呼ばれる。捕捉とデバイスを畳んでから結果を確定する。
				try
				{
					captureRef.Dispose();
				}
				catch
				{
				}

				try
				{
					deviceRef.Dispose();
				}
				catch
				{
				}

				tcs.TrySetResult(result);
			});

			handedOff = true;
			lens.Start();

			return await tcs.Task;
		}
		catch
		{
			if (!handedOff)
			{
				capture?.Dispose();
				device?.Dispose();
			}

			return null;
		}
		finally
		{
			_busy = false;
		}
	}




	// 開始モニタを決める。カーソルのいるモニタを優先し、取得できなければプライマリにする。レンズは以後カーソルの移動でモニタを切り替える。
	private static IntPtr PickStartMonitor()
	{
		if (PickerNativeMethods.GetCursorPos(out PickerNativeMethods.POINT pt))
		{
			return PickerNativeMethods.MonitorFromPoint(pt, PickerNativeMethods.MONITOR_DEFAULTTONEAREST);
		}

		return PickerNativeMethods.MonitorFromPoint(new PickerNativeMethods.POINT { X = 0, Y = 0 }, PickerNativeMethods.MONITOR_DEFAULTTOPRIMARY);
	}




	// 設定(ScreenPickerTuning)から実効のガラスパラメータを組む。拡大率・レンズ径を写し、屈折の強さは既定値への倍率で掛ける。ガラス効果が無効なら屈折・ぼかし・彩度強調・白み・光沢・ハイライトを切り、縁と拡大だけの素のルーペにする。
	private static GlassParams BuildParams()
	{
		var p = new GlassParams
		{
			Magnify = ScreenPickerTuning.Magnify,
			CircleSize = ScreenPickerTuning.Diameter,
		};

		p.Scale *= ScreenPickerTuning.RefractionStrength;

		if (!ScreenPickerTuning.GlassEffect)
		{
			p.Displace = false;
			p.Blur = false;
			p.Enhance = false;
			p.Tint = false;
			p.Gloss = false;
			p.Light = false;
		}

		return p;
	}
}
