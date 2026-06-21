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
/// 円盤や色相環など、中心からの極座標で塗る下地を描く共通の生成器。
/// 各画素の中心からのずれ (dx, dy) と半径を求め、被覆度 coverageOf と色 colorAt を呼び、アルファ乗算済み BGRA の WriteableBitmap を返す。
/// 行を全コアへ分散して塗り、被覆度 0 の画素は透明、縁では被覆度に応じて不透明度を落として背景になじませる。
/// 色制限が有効なら各画素の色をその制限へ丸める。
/// </summary>
internal static class RadialRenderer
{
	// 縁取りをなじませる幅(画素)。円・帯の境界でこの幅だけ不透明度を落とす。
	private const double EdgeSoftness = 1.0;


	// 指定した画素サイズ・色制限設定で、coverageOf と colorAt に従って極座標の下地を描いた WriteableBitmap を作る。coverageOf は中心からの半径を受けて被覆度(0–1)を返し、colorAt は中心からのずれ (dx, dy) と半径を受けてその画素の色を返す。被覆度が 0 の画素は colorAt を呼ばず透明にする。色制限が有効なら各画素の色をその制限へ丸める。
	internal static WriteableBitmap Create(int pixelWidth, int pixelHeight, SnapSettings snap, Func<double, double> coverageOf, Func<double, double, double, (byte R, byte G, byte B)> colorAt)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];
		double centerX = pixelWidth / 2.0;
		double centerY = pixelHeight / 2.0;

		bool limited = snap.Mode != ColorLimitMode.None;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		if (limited)
		{
			ColorConversion.Snap(snap, 0, 0, 0);
		}

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。色制限の切替やドラッグで大きな円盤・環を塗り直す際に1スレッドでは重いため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			for (int x = 0; x < pixelWidth; x++)
			{
				double dx = x + 0.5 - centerX;
				double dy = y + 0.5 - centerY;
				double radius = Math.Sqrt((dx * dx) + (dy * dy));
				double coverage = coverageOf(radius);
				int index = ((y * pixelWidth) + x) * 4;

				if (coverage <= 0.0)
				{
					pixels[index] = 0;
					pixels[index + 1] = 0;
					pixels[index + 2] = 0;
					pixels[index + 3] = 0;
					continue;
				}

				(byte r, byte g, byte b) = colorAt(dx, dy, radius);

				if (limited)
				{
					(r, g, b) = ColorConversion.Snap(snap, r, g, b);
				}

				byte alpha = (byte)Math.Round(coverage * 255.0);

				// WriteableBitmap はアルファ乗算済みの BGRA を期待するため、各色にアルファを掛けて格納する。縁取りの半透明部分を背景の上で正しく見せる。
				pixels[index] = (byte)(b * alpha / 255);
				pixels[index + 1] = (byte)(g * alpha / 255);
				pixels[index + 2] = (byte)(r * alpha / 255);
				pixels[index + 3] = alpha;
			}
		});

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(pixels, 0, pixels.Length);
		}

		bitmap.Invalidate();
		return bitmap;
	}




	// 半径が円盤に属する度合い(0–1)を返す。中心から maxRadius までは 1、そこから外へ EdgeSoftness の幅で 0 まで落とし、それより外は 0 にする。
	internal static double DiskCoverage(double radius, double maxRadius)
	{
		if (radius > maxRadius + EdgeSoftness)
		{
			return 0.0;
		}

		if (radius <= maxRadius)
		{
			return 1.0;
		}

		return Math.Clamp((maxRadius + EdgeSoftness - radius) / EdgeSoftness, 0.0, 1.0);
	}




	// 半径が帯に属する度合い(0–1)を返す。帯の内側から外側までは 1、両縁の外側へ EdgeSoftness の幅で 0 まで落とし、それより外と内側の穴は 0 にする。
	internal static double BandCoverage(double radius, double innerRadius, double outerRadius)
	{
		if (radius < innerRadius - EdgeSoftness || radius > outerRadius + EdgeSoftness)
		{
			return 0.0;
		}

		double coverage = 1.0;

		if (radius < innerRadius)
		{
			coverage = Math.Min(coverage, (radius - (innerRadius - EdgeSoftness)) / EdgeSoftness);
		}

		if (radius > outerRadius)
		{
			coverage = Math.Min(coverage, ((outerRadius + EdgeSoftness) - radius) / EdgeSoftness);
		}

		return Math.Clamp(coverage, 0.0, 1.0);
	}
}
