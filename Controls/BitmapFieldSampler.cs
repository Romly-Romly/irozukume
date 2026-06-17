// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Irozukume.Controls;

// 生成済みの色面ビットマップ(WriteableBitmap)を、レンズの「座標→色」サンプラーとして読み出す補助。色域減光やハッチなど計算が込み入った色面(CbCr 平面・LCH の L-C 平面・色相環)では、色を作り直すより表示中のビットマップをそのまま読む方が確実で表示と必ず一致する。コントロール局所座標(DIP)を、ビットマップが引き伸ばされて貼られている前提で画素位置へ写し、その色を返す。ビットマップの外は透明を返し、端でレンズが外側を透かして見せる。生成のたびに作り直して利用側がサンプラーを差し替える。
internal sealed class BitmapFieldSampler
{
	// 取り込んだ時点のビットマップ画素(乗算済みアルファの BGRA)と寸法。生成のたびに本オブジェクトごと作り直すため、内容は固定とみなす。
	private readonly byte[] _pixels;
	private readonly int _width;
	private readonly int _height;

	// 貼り付け先のコントロール。ビットマップはこれへ引き伸ばして表示されるため、呼び出し時の実寸(DIP)で座標を画素へ写す。
	private readonly FrameworkElement _control;




	public BitmapFieldSampler(WriteableBitmap bitmap, FrameworkElement control)
	{
		_width = bitmap.PixelWidth;
		_height = bitmap.PixelHeight;
		_pixels = new byte[_width * _height * 4];

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.ReadExactly(_pixels, 0, _pixels.Length);
		}

		_control = control;
	}




	// コントロール局所座標(DIP)の点の色を返す。ビットマップはコントロール全面へ引き伸ばされている前提で画素位置へ写す。範囲外は透明。乗算済みアルファを素のアルファへ戻して返す(レンズ側で再度乗算するため)。
	public Color Sample(double x, double y)
	{
		double width = _control.ActualWidth;
		double height = _control.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		int px = (int)Math.Floor((x / width) * _width);
		int py = (int)Math.Floor((y / height) * _height);

		if (px < 0 || px >= _width || py < 0 || py >= _height)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		int i = ((py * _width) + px) * 4;
		byte a = _pixels[i + 3];

		if (a == 0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		if (a == 255)
		{
			return Color.FromArgb(255, _pixels[i + 2], _pixels[i + 1], _pixels[i]);
		}

		// 乗算済み BGRA を素のアルファへ戻す。
		byte Straight(byte channel) => (byte)Math.Min(255, (channel * 255) / a);
		return Color.FromArgb(a, Straight(_pixels[i + 2]), Straight(_pixels[i + 1]), Straight(_pixels[i]));
	}
}
