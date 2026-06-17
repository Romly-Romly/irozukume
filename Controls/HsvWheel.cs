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

// 配色タブの「HSV ディスク」の下地を描いた画像を生成する。中心からの角度を色相、中心からの距離を彩度(S)に対応させ、明度(V)は 1 で塗る。中心(S=0)が白、縁(S=1)がその色相の純色になる、ふつうの色相環。明度(V)は各色のスライダーが受け持つため下地には含めない。色制限が有効なら塗る色をその制限へ丸める。下地は編集対象の色に依存しないため、画素サイズ・色制限が変わるときだけ作り直す。円形の切り抜きは利用側(HarmonyDisc)が行う。
public static class HsvWheel
{
	// 指定した画素サイズ・色制限で、HSV の色相環の下地を描いた WriteableBitmap を作る。各画素は中心からの角度で色相、距離で彩度を決め、明度 1 の HSV から sRGB を作る。四隅(円盤の外)は彩度を 1 に詰め、円形の切り抜きは利用側に委ねる。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, SnapSettings snap)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		// 最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。
		Parallel.For(0, pixelHeight, y =>
		{
			// 上端を +1、下端を −1とした正規化座標。中心からの距離が円盤の半径(1)を表す。
			double bNorm = 1.0 - (2.0 * (y + 0.5) / pixelHeight);
			int rowBase = y * pixelWidth * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				double aNorm = (2.0 * (x + 0.5) / pixelWidth) - 1.0;
				int index = rowBase + (x * 4);

				double saturation = Math.Min(Math.Sqrt((aNorm * aNorm) + (bNorm * bNorm)), 1.0);

				// 色相 0(赤)を上(北)に置き、時計回りに増やす。画面の角度(東基準・反時計回り)から 90 度回し符号を反転して色相へ写す。
				double hue = 90.0 - (Math.Atan2(bNorm, aNorm) * 180.0 / Math.PI);
				hue = ((hue % 360.0) + 360.0) % 360.0;

				(byte r, byte g, byte b) = ColorConversion.HsvToRgb(hue, saturation, 1.0);
				(r, g, b) = ColorConversion.Snap(snap, r, g, b);

				// WriteableBitmap はアルファ乗算済みの BGRA を期待する。下地は全面不透明のため色をそのまま並べる。
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
