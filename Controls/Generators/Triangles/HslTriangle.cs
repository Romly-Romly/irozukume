// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls.Generators.Triangles;

/// <summary>
/// HSL の彩度・輝度を表す三角形の画像を生成する。
/// 三角形は HSL 双錐の色相断面で、純色・白・黒を頂点に取る。各画素の重心座標から彩度・輝度を求め、与えた色相とあわせて HSL→RGB で塗る。
/// 三角形の形・縁のなじませ・角丸・色制限の扱いは TriangleRenderer に委ね、本クラスは重心の重みから HSL の色を作る式だけを与える。
/// </summary>
/// <remarks>色相が変わるたびに作り直す想定。三角形は未回転(純色の頂点が真上)で描き、回転は表示側(TrianglePad)で行う。</remarks>
public static class HslTriangle
{
	// 指定した画素サイズで、与えた色相の彩度・輝度三角形を描いた WriteableBitmap を作る。三角形の外は透明にし、中央へ別のコントロールやリングを透かせる。cornerRadius(画素)を与えると三角形の3頂点を丸める。色制限が有効なら各画素の色をその制限へ丸めて段階的にし、None なら滑らかに描く。fillBox が真のときは外接円に内接させず箱を縦横いっぱいに埋める頂点取りにする(独立三角形用)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double hue, SnapSettings snap, double cornerRadius, bool fillBox = false)
	{
		return TriangleRenderer.Create(pixelWidth, pixelHeight, snap, cornerRadius, fillBox, (wHue, wBlack, wWhite) =>
		{
			(double saturation, double lightness) = TriangleGeometry.BarycentricToSl(wHue, wBlack, wWhite);
			return ColorConversion.HslToRgb(hue, saturation, lightness);
		});
	}
}
