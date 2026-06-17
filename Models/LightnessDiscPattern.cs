// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// 配色タブの円盤の種類。半径(中心からの距離)に何の軸を割り当てるかを決める。Hsv 以外は OKLCH の明度ディスクで、半径=明度・スライダー=彩度。FullRange は中心が白・縁が黒で明度の全域を覆い、FullRangeReversed はその逆向きで中心が黒・縁が白。どちらも等半径が等明度になり、最も鮮やかな色は中ほどの輪に並ぶ。WhiteToCusp は中心が白・縁をその色相が最も鮮やかになる明度(cusp)に、BlackToCusp は中心が黒・縁を cusp に取る。cusp の明度は色相で変わるため、cusp を使う型は半径と明度の対応が色相ごとに伸縮する。Hsv は HSV の色相環で、半径=彩度(S)・スライダー=明度(V)。中心が白・縁がその色相の純色で、色相は HSV 色相を使うため伝統的な RGB の色相環に沿った配色になる。
public enum LightnessDiscPattern
{
	FullRange,
	FullRangeReversed,
	WhiteToCusp,
	BlackToCusp,
	Hsv,
}
