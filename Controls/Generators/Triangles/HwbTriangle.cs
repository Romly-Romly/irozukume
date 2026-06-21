// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls.Generators.Triangles;

/// <summary>
/// HWB の白み・黒みを表す三角形の画像を生成する。
/// 三角形は純色・白・黒を頂点に取り、各画素の重心座標から白み(白の重み)・黒み(黒の重み)を求め、与えた色相とあわせて HWB→RGB で塗る。HWB は色 = 純色·(1−W−B) + 白·W + 黒·B の線形補間のため、三角形の内側(W+B≤1)は退化しない。
/// 三角形の形・縁のなじませ・角丸・色制限の扱いは TriangleRenderer に委ね、本クラスは重心の重みから HWB の色を作る式だけを与える。HslTriangle と同じ三角形の形を使い、塗りだけ HSL の双錐断面から HWB の線形補間へ替えた対の描画。
/// </summary>
/// <remarks>色相が変わるたびに作り直す想定。三角形は未回転(純色の頂点が真上)で描き、回転は表示側(TrianglePad)で行う。</remarks>
public static class HwbTriangle
{
	// 指定した画素サイズで、与えた色相の白み・黒み三角形を描いた WriteableBitmap を作る。三角形の外は透明にし、中央へ別のコントロールやリングを透かせる。cornerRadius(画素)を与えると三角形の3頂点を丸める。色制限が有効なら各画素の色をその制限へ丸めて段階的にし、None なら滑らかに描く。fillBox が真のときは外接円に内接させず箱を縦横いっぱいに埋める頂点取りにする(独立三角形用)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double hue, SnapSettings snap, double cornerRadius, bool fillBox = false)
	{
		return TriangleRenderer.Create(pixelWidth, pixelHeight, snap, cornerRadius, fillBox, (wHue, wBlack, wWhite) =>
		{
			(double whiteness, double blackness) = TriangleGeometry.BarycentricToWb(wHue, wBlack, wWhite);
			return ColorConversion.HwbToRgb(hue, whiteness, blackness);
		});
	}
}
