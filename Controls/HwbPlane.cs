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

// 指定した色相における HWB の白み・黒みパッドの下地を描いた画像を生成する。横軸が白み(左 0→右 1)、縦軸が黒み(上 0→下 1)で、純色を左上・白を右上・黒を左下・灰を右下に取る。各画素を HWB→RGB へ変換し、白み+黒みが 1 を超える右下の三角形は色相を失った灰へ退化する。HWB は純色・白・黒の3頂点を線形に混ぜる加法的な配色のため、HSV のような重ねたグラデーションでは正しく描けない。よって滑らかな下地も含めて常にこの画像で賄い、色相や色制限モードが変わるたびに作り直す想定。色制限が有効なら各画素の色をその制限へ丸めて段階的にする。
public static class HwbPlane
{
	// 指定した画素サイズ・色相・色制限設定で、白み・黒みパッドの下地を描いた WriteableBitmap を作る。全面を不透明で塗り、各画素の色を設定の制限へ丸める。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double hue, SnapSettings snap)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。画素数が多いとき1スレッドでは塗りきれず色相ドラッグでカクつくため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			// 上端を黒み 0、下端を黒み 1 とし、パッドの縦方向(下ほど黒み大)に合わせる。
			double blackness = (y + 0.5) / pixelHeight;
			int rowBase = (y * pixelWidth) * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				// 左端を白み 0、右端を白み 1 とする。
				double whiteness = (x + 0.5) / pixelWidth;
				(byte r, byte g, byte b) = ColorConversion.HwbToRgb(hue, whiteness, blackness);
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
