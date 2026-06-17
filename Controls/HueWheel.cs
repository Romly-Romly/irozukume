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

// 色相環(リングの帯)の画像を生成する。各画素の中心からの角度を色相に、半径で帯の内外を決め、帯の縁は不透明度を滑らかに落として縁取りをなじませる。角度の取り方は RingGeometry と揃えるため、つまみの位置と環の色がずれない。生成はサイズ変更時など必要なときだけ行う想定で、描画中の頻繁な再生成は呼び出し側が避ける。
public static class HueWheel
{
	// 縁取りをなじませる幅(画素)。帯の内外の境界でこの幅だけ不透明度を落とす。
	private const double EdgeSoftness = 1.0;


	// 指定した画素サイズで色相環を描いた WriteableBitmap を作る。innerRadius・outerRadius は画素単位の帯の内外半径で、リングの幾何(RingGeometry)から得た値に表示倍率を掛けたものを渡す。帯の外と内側の穴は透明にし、中央へ別のコントロールを透かせる。色制限が有効なら各画素の色をその制限へ丸めて段階的にし、None なら滑らかに描く。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double innerRadius, double outerRadius, SnapSettings snap)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];
		double centerX = pixelWidth / 2.0;
		double centerY = pixelHeight / 2.0;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。色制限の切替で大きな色相環を塗り直す際に1スレッドでは重いため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			for (int x = 0; x < pixelWidth; x++)
			{
				double dx = x + 0.5 - centerX;
				double dy = y + 0.5 - centerY;
				double radius = Math.Sqrt((dx * dx) + (dy * dy));
				double coverage = BandCoverage(radius, innerRadius, outerRadius);
				int index = ((y * pixelWidth) + x) * 4;

				if (coverage <= 0.0)
				{
					pixels[index] = 0;
					pixels[index + 1] = 0;
					pixels[index + 2] = 0;
					pixels[index + 3] = 0;
					continue;
				}

				double hue = RingGeometry.ValueFromPoint(dx, dy);
				(byte r, byte g, byte b) = ColorConversion.HsvToRgb(hue, 1.0, 1.0);

				if (snap.Mode != ColorLimitMode.None)
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




	// 半径が帯に属する度合い(0–1)を返す。帯の内側から外側までは 1、両縁の外側へ EdgeSoftness の幅で 0 まで落とし、それより外と内側の穴は 0 にする。
	private static double BandCoverage(double radius, double innerRadius, double outerRadius)
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
