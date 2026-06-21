// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls.Generators;

/// <summary>
/// 純色・白・黒を頂点に取る三角形の下地を描く共通の生成器。
/// 各画素の重心座標(純色・黒・白の重み)を求めてクランプし、その重みから作る色を colorAt へ委ね、色制限が有効ならその制限へ丸めて、アルファ乗算済み BGRA の WriteableBitmap を返す。
/// 三角形の縁は不透明度を滑らかに落として縁取りをなじませ、外は透明にする。cornerRadius を与えると3頂点を丸める。
/// 行を全コアへ分散して塗る。
/// 頂点の取り方と重心の対応は TriangleGeometry に従うため、つまみの位置と三角形の色がずれない。三角形は未回転(純色の頂点が真上)で描き、回転は表示側で行う。
/// </summary>
/// <remarks>同じ三角形の形に対し、重心の重みから色を作る式だけを差し替えて各表色系の三角形を得る。</remarks>
public static class TriangleRenderer
{
	// 指定した画素サイズ・色制限設定で、colorAt が返す色を塗った三角形の下地 WriteableBitmap を作る。colorAt はクランプ済みの重心座標 (wHue, wBlack, wWhite)(純色・黒・白の頂点の重み)を受け取り当該画素の色を返す。cornerRadius(画素)で3頂点を丸め、fillBox が真なら外接円に内接させず箱を縦横いっぱいに埋める頂点取りにする。色制限が有効なら各画素の色をその制限へ丸める。三角形の外は透明にする。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, SnapSettings snap, double cornerRadius, bool fillBox, Func<double, double, double, (byte R, byte G, byte B)> colorAt)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];
		TriangleVertices vertices = fillBox
			? TriangleGeometry.ComputeFillVertices(pixelWidth, pixelHeight)
			: TriangleGeometry.ComputeVertices(pixelWidth, pixelHeight);

		// 角丸三角形は、辺を内側へ寄せた三角形を cornerRadius ぶん膨らませた形(=その符号付き距離が cornerRadius 以内)として描く。膨張で頂点が半径ぶん丸まり、辺は元の位置へ戻る。被覆は境界の前後 1 画素でなじませる。
		TriangleVertices insetVertices = TriangleGeometry.InsetVertices(vertices, cornerRadius);
		const double softness = 1.0;

		bool limited = snap.Mode != ColorLimitMode.None;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		if (limited)
		{
			ColorConversion.Snap(snap, 0, 0, 0);
		}

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
				(byte r, byte g, byte b) = colorAt(clampedHue, clampedBlack, clampedWhite);

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
}
