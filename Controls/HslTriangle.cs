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

// HSL の彩度・輝度を表す三角形の画像を生成する。三角形は HSL 双錐の色相断面で、純色・白・黒を頂点に取る。各画素の重心座標から彩度・輝度を求めて色を塗り、三角形の縁は不透明度を滑らかに落として縁取りをなじませる。頂点の取り方と重心の対応は TriangleGeometry と揃えるため、つまみの位置と三角形の色がずれない。三角形は未回転(純色の頂点が真上)で描き、回転は表示側(TrianglePad)で行う。色相が変わるたびに作り直す想定。
public static class HslTriangle
{
	// 指定した画素サイズで、与えた色相の彩度・輝度三角形を描いた WriteableBitmap を作る。三角形の外は透明にし、中央へ別のコントロールやリングを透かせる。cornerRadius(画素)を与えると三角形の3頂点を丸める。色制限が有効なら各画素の色をその制限へ丸めて段階的にし、None なら滑らかに描く。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double hue, SnapSettings snap, double cornerRadius)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];
		TriangleVertices vertices = TriangleGeometry.ComputeVertices(pixelWidth, pixelHeight);

		// 角丸三角形は、辺を内側へ寄せた三角形を cornerRadius ぶん膨らませた形(=その符号付き距離が cornerRadius 以内)として描く。膨張で頂点が半径ぶん丸まり、辺は元の位置へ戻る。被覆は境界の前後 1 画素でなじませる。
		TriangleVertices insetVertices = TriangleGeometry.InsetVertices(vertices, cornerRadius);
		const double softness = 1.0;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。画素数が多いとき1スレッドでは塗りきれず色相変更でカクつくため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			for (int x = 0; x < pixelWidth; x++)
			{
				var point = new Windows.Foundation.Point(x + 0.5, y + 0.5);
				double distance = TriangleGeometry.SignedDistanceToTriangle(point, insetVertices);
				double coverage = Math.Clamp(((cornerRadius - distance) / softness) + 0.5, 0.0, 1.0);
				int index = ((y * pixelWidth) + x) * 4;

				if (coverage <= 0.0)
				{
					pixels[index] = 0;
					pixels[index + 1] = 0;
					pixels[index + 2] = 0;
					pixels[index + 3] = 0;
					continue;
				}

				// 色は元の三角形の重心座標から。角丸で被覆する範囲は元の三角形の内側に収まるため、重みは負にならないが、縁のなじませ画素のために収めておく。
				(double wHue, double wBlack, double wWhite) = TriangleGeometry.PointToBarycentric(point, vertices);
				(double clampedHue, double clampedBlack, double clampedWhite) = TriangleGeometry.ClampBarycentric(wHue, wBlack, wWhite);
				(double saturation, double lightness) = TriangleGeometry.BarycentricToSl(clampedHue, clampedBlack, clampedWhite);
				(byte r, byte g, byte b) = ColorConversion.HslToRgb(hue, saturation, lightness);

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
}
