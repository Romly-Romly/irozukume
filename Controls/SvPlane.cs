// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls;

// 指定した色相における HSV の彩度・明度パッドの下地を、色を現在の色制限モードに従って丸めて段階的に描いた画像を生成する。横軸が彩度(左 0→右 1)、縦軸が明度(下 0→上 1)で、各画素を HSV→RGB へ変換してから丸める。丸めない滑らかな下地は XAML のグラデーションで賄うため、これは色制限が有効なときだけ使う。色相や色制限モードが変わるたびに作り直す想定。
public static class SvPlane
{
	// 指定した画素サイズ・色相・色制限設定で、彩度・明度パッドの下地を描いた WriteableBitmap を作る。全面を不透明で塗り、各画素の色を設定の制限へ丸める。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double hue, SnapSettings snap)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。画素数が多いとき(大きなパッド)に1スレッドでは塗りきれず色相ドラッグでカクつくため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			// 上端を明度 1、下端を明度 0 とし、パッドの縦方向(上ほど大)に合わせる。
			double value = 1.0 - ((y + 0.5) / pixelHeight);
			int rowBase = (y * pixelWidth) * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				// 左端を彩度 0、右端を彩度 1 とする。
				double saturation = (x + 0.5) / pixelWidth;
				(byte r, byte g, byte b) = ColorConversion.HsvToRgb(hue, saturation, value);
				(r, g, b) = ColorConversion.Snap(snap, r, g, b);

				int index = rowBase + (x * 4);

				// WriteableBitmap はアルファ乗算済みの BGRA を期待する。全面不透明のため色をそのまま並べる。
				pixels[index] = b;
				pixels[index + 1] = g;
				pixels[index + 2] = r;
				pixels[index + 3] = 0xFF;
			}
		});

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(pixels, 0, pixels.Length);
		}

		bitmap.Invalidate();
		return bitmap;
	}
}
