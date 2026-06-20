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

// 角度=色相・中心からの半径=彩度の円盤(HSV ホイール)の下地画像を生成する。各画素の中心からの角度を色相に、半径を彩度(中心 0→縁 1)に写し、明度は引数で与える一定値とする。角度の取り方は RingGeometry と揃えるため、つまみの位置と円盤の色がずれない。円の縁は不透明度を滑らかに落として背景になじませ、円の外は透明にする。色相は円盤の全方位に現れるため色相を引数に取らず、明度または色制限が変わったときだけ作り直す想定。
public static class HueSatDisk
{
	// 縁取りをなじませる幅(画素)。円の外周でこの幅だけ不透明度を落とす。
	private const double EdgeSoftness = 1.0;


	// 指定した画素サイズ・明度・色制限設定で、色相・彩度の円盤を描いた WriteableBitmap を作る。円の半径は画素サイズの短辺の半分。各画素を HSV→RGB へ変換し、色制限が有効なら設定の制限へ丸める。円の外は透明にし、中央に別のコントロールを重ねられるようにはしないが、背景は透ける。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double value, SnapSettings snap)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];
		double centerX = pixelWidth / 2.0;
		double centerY = pixelHeight / 2.0;
		double maxRadius = Math.Min(pixelWidth, pixelHeight) / 2.0;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。色制限の切替や明度ドラッグで大きな円盤を塗り直す際に1スレッドでは重いため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			for (int x = 0; x < pixelWidth; x++)
			{
				double dx = x + 0.5 - centerX;
				double dy = y + 0.5 - centerY;
				double radius = Math.Sqrt((dx * dx) + (dy * dy));
				double coverage = DiskCoverage(radius, maxRadius);
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
				double saturation = Math.Clamp(radius / maxRadius, 0.0, 1.0);
				(byte r, byte g, byte b) = ColorConversion.HsvToRgb(hue, saturation, value);

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




	// 半径が円盤に属する度合い(0–1)を返す。中心から maxRadius までは 1、そこから外へ EdgeSoftness の幅で 0 まで落とし、それより外は 0 にする。
	private static double DiskCoverage(double radius, double maxRadius)
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
}
