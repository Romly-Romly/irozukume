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

// 指定した黒みにおける HWB の色相・白みパッドの下地を描いた画像を生成する。横軸が色相(左 0 度→右 360 度)、縦軸が白み(下 0→上 1)で、黒みは引数で与える一定値とする。各画素を HWB→RGB へ変換し、色制限が有効なら設定の制限へ丸める。色相が横一面に渡って変わるため画像で賄う。白み+黒みが 1 を超える上側(黒みが大きいとき)は無彩色へ退化するが、HwbToRgb がその灰を返す。黒みや色制限モードが変わるたびに作り直す想定。HueLightnessPlane の HSL を HWB へ替え、縦軸を輝度から白みにした対の描画。
public static class HueWhitenessPlane
{
	// 指定した画素サイズ・黒み・色制限設定で、色相・白みパッドの下地を描いた WriteableBitmap を作る。全面を不透明で塗る。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double blackness, SnapSettings snap)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。画素数が多いとき(大きなパッド)に1スレッドでは塗りきれず黒みドラッグでカクつくため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			// 上端を白み 1、下端を白み 0 とし、パッドの縦方向(上ほど大)に合わせる。
			double whiteness = 1.0 - ((y + 0.5) / pixelHeight);
			int rowBase = (y * pixelWidth) * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				// 左端を色相 0 度、右端を色相 360 度とする。
				double hue = ((x + 0.5) / pixelWidth) * 360.0;
				(byte r, byte g, byte b) = ColorConversion.HwbToRgb(hue, whiteness, blackness);

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
