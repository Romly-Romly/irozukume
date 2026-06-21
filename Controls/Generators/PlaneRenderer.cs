// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators;

/// <summary>
/// 色域外を持たない表色系の2次元パッドの下地を描く共通の生成器。
/// 横軸・縦軸を正規化座標で掃き、各画素の色を colorAt へ委ね、色制限が有効ならその制限へ丸めて、アルファ乗算済み BGRA の WriteableBitmap を返す。
/// 行を全コアへ分散して塗る。
/// 各表色系の Plane はこの骨格へ「正規化座標から色を返す関数」を渡すだけで下地を得る。
/// </summary>
/// <remarks>色域外(sRGB に収まらない組み合わせ)を持つ表色系には使えない。</remarks>
public static class PlaneRenderer
{
	// 指定した画素サイズ・色制限設定で、colorAt が返す色を塗った下地の WriteableBitmap を作る。colorAt は正規化座標 (u, v) を受け取り当該画素の色を返す。u は横軸で左端 0→右端 1、v は縦軸で上端 0→下端 1。縦軸の向き(上端を最大とするか、黒みのように上端を 0 とするか)は colorAt 側で決める。色制限が有効なら各画素の色をその制限へ丸める。全面を不透明で塗る。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, SnapSettings snap, Func<double, double, (byte R, byte G, byte B)> colorAt)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		bool limited = snap.Mode != ColorLimitMode.None;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		if (limited)
		{
			ColorConversion.Snap(snap, 0, 0, 0);
		}

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。画素数が多いとき(大きなパッド)に1スレッドでは塗りきれずドラッグでカクつくため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			// 縦軸の正規化座標。上端を 0、下端を 1 とする。縦軸の意味付けは colorAt に委ねる。
			double v = (y + 0.5) / pixelHeight;
			int rowBase = (y * pixelWidth) * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				// 横軸の正規化座標。左端を 0、右端を 1 とする。
				double u = (x + 0.5) / pixelWidth;
				(byte r, byte g, byte b) = colorAt(u, v);

				if (limited)
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
