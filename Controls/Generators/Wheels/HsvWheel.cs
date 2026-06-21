// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Wheels;

/// <summary>
/// 配色タブの「HSV ディスク」の下地を描いた画像を生成する。
/// 中心からの角度を色相、中心からの距離を彩度(S)に対応させ、明度(V)は 1 で塗る。
/// 中心(S=0)が白、縁(S=1)がその色相の純色になる、ふつうの色相環。
/// 明度(V)は各色のスライダーが受け持つため下地には含めない。
/// 色制限が有効なら塗る色をその制限へ丸める。
/// </summary>
/// <remarks>下地は編集対象の色に依存しないため、画素サイズ・色制限が変わるときだけ作り直す。円形の切り抜きは利用側(HarmonyDisc)が行う。</remarks>
public static class HsvWheel
{
	// 指定した画素サイズ・色制限で、HSV の色相環の下地を描いた WriteableBitmap を作る。各画素は中心からの角度で色相、距離で彩度を決め、明度 1 の HSV から sRGB を作る。四隅(円盤の外)は彩度を 1 に詰め、円形の切り抜きは利用側に委ねる。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) =>
		{
			// 上端を +1・下端を −1、左端を −1・右端を +1 とした正規化座標。中心からの距離が円盤の半径(1)を表す。
			double aNorm = (2.0 * u) - 1.0;
			double bNorm = 1.0 - (2.0 * v);
			double saturation = Math.Min(Math.Sqrt((aNorm * aNorm) + (bNorm * bNorm)), 1.0);

			// 色相 0(赤)を上(北)に置き、時計回りに増やす。画面の角度(東基準・反時計回り)から 90 度回し符号を反転して色相へ写す。
			double hue = 90.0 - (Math.Atan2(bNorm, aNorm) * 180.0 / Math.PI);
			hue = ((hue % 360.0) + 360.0) % 360.0;
			return ColorConversion.HsvToRgb(hue, saturation, 1.0);
		});
	}
}
