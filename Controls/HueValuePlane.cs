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

// 指定した彩度における HSV の色相・明度パッドの下地を描いた画像を生成する。横軸が色相(左 0 度→右 360 度)、縦軸が明度(下 0→上 1)で、彩度は引数で与える一定値とする。各画素を HSV→RGB へ変換し、色制限が有効なら設定の制限へ丸める。色相が横一面に渡って変わり、彩度・明度の単純なグラデーションの重ねでは描けないため画像で賄う。彩度や色制限モードが変わるたびに作り直す想定。
public static class HueValuePlane
{
	// 指定した画素サイズ・彩度・色制限設定で、色相・明度パッドの下地を描いた WriteableBitmap を作る。全面を不透明で塗る。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double saturation, SnapSettings snap)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。画素数が多いとき(大きなパッド)に1スレッドでは塗りきれず彩度ドラッグでカクつくため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			// 上端を明度 1、下端を明度 0 とし、パッドの縦方向(上ほど大)に合わせる。
			double value = 1.0 - ((y + 0.5) / pixelHeight);
			int rowBase = (y * pixelWidth) * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				// 左端を色相 0 度、右端を色相 360 度とする。
				double hue = ((x + 0.5) / pixelWidth) * 360.0;
				(byte r, byte g, byte b) = ColorConversion.HsvToRgb(hue, saturation, value);

				if (snap.Mode != ColorLimitMode.None)
				{
					(r, g, b) = ColorConversion.Snap(snap, r, g, b);
				}

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
