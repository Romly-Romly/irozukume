// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls.Generators.Wheels;

/// <summary>
/// LCH(OKLCH・CIE LCH)の色相環の画像を生成する。
/// 各画素の中心からの角度を LCH の色相とし、固定の明度のもとでその色相が sRGB 色域に収まる最大の彩度で塗る。
/// 色相を等角度で配るため、HSV の色相環と違って知覚的に均等な色相環になり、明度を一定にすることで一周の明るさもそろう。
/// 角度の取り方は <see cref="RingGeometry"/> と揃えるため、つまみの位置と環の色がずれない。
/// 彩度は色相ごとに色域いっぱいへ寄せるため、鮮やかさは色相で変わる(その色相がどこまで鮮やかにできるかを表す)。
/// </summary>
/// <remarks>生成はサイズ・副モード・色制限・表示倍率の変化時など必要なときだけ行う想定。</remarks>
public static class LchHueWheel
{
	// 色相環を塗る明度。各表色系の明度上限に対する割合で与え、一周を通して一定に保つ。鮮やかさが乗りやすく、暗すぎず明るすぎない中庸の明度にする。
	private const double LightnessFraction = 0.70;


	// 指定した画素サイズ・副モード・色制限設定で LCH の色相環を描いた WriteableBitmap を作る。innerRadius・outerRadius は画素単位の帯の内外半径で、リングの幾何(RingGeometry)から得た値に表示倍率を掛けたものを渡す。帯の外と内側の穴は透明にし、中央へ別のコントロールを透かせる。各画素はその色相で色域に収まる最大彩度の色で塗り、色制限が有効なら設定の制限へ丸める。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double innerRadius, double outerRadius, LchSpace space, SnapSettings snap)
	{
		double lightness = LchColor.LMax(space) * LightnessFraction;
		return RadialRenderer.Create(pixelWidth, pixelHeight, snap,
			radius => RadialRenderer.BandCoverage(radius, innerRadius, outerRadius),
			(dx, dy, radius) =>
			{
				double hue = RingGeometry.ValueFromPoint(dx, dy);
				Color color = LchColor.ToRgb(space, lightness, LchColor.MaxChroma(space, lightness, hue), hue);
				return (color.R, color.G, color.B);
			});
	}
}
