// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Irozukume.Models;

namespace Irozukume.Helpers;

// 配色の拘束の種類。Offset は固定の色相オフセットを持つ角度系で、点数はオフセットの並びの長さで決まる。SharedHue は色相を全色で揃えトーンを色ごとに変える(モノクロマティック)。SharedTone はトーン(明度+彩度)を全色で揃え色相を色ごとに変える(ドミナントトーン・トーナル)。Achromatic は無彩色だけで明度の階調を作る(モノトーン)。色相も彩度も持たず、円盤ではなく明度の縦軸を持つ横並びのパッドで扱う。拘束系(SharedHue・SharedTone・Achromatic)は固定の点数を持たず、現在の色数に追従する。
public enum HarmonySchemeKind
{
	Offset,
	SharedHue,
	SharedTone,
	Achromatic,
}




// 角度系の配色の幾何を一手に扱う。各配色が基準色相から各色をどれだけずらすか(色相のオフセット)と、配色の線を多角形として閉じるかを返す。明度・彩度には関与せず、色相の角度関係だけを定める。配色タブが、基準色の色相にこのオフセットを足して各色の色相を決め、線の描き方を選ぶのに使う。拘束系(SharedHue・SharedTone)は固定のオフセットを持たないため、配色タブが点数と配置を色数から組み立てる。
public static class ColorHarmony
{
	// 配色の拘束の種類を返す。Monochromatic は色相を揃える SharedHue、DominantTone・Tonal はトーンを揃える SharedTone、Monotone は無彩色の Achromatic、それ以外は固定オフセットの Offset。配色タブが点数・拘束・スライダーの出し方を切り替えるのに使う。
	public static HarmonySchemeKind Kind(ColorHarmonyScheme scheme)
	{
		return scheme switch
		{
			ColorHarmonyScheme.Monochromatic => HarmonySchemeKind.SharedHue,
			ColorHarmonyScheme.DominantTone or ColorHarmonyScheme.Tonal => HarmonySchemeKind.SharedTone,
			ColorHarmonyScheme.Monotone => HarmonySchemeKind.Achromatic,
			_ => HarmonySchemeKind.Offset,
		};
	}




	// 共通トーンをくすんだ中間色域に収める配色か。Tonal のときだけ真。配色タブが共通トーンの明度・彩度を中間色域へ制限するのに使う。
	public static bool IsTonal(ColorHarmonyScheme scheme)
	{
		return scheme == ColorHarmonyScheme.Tonal;
	}

	// 各配色の、基準色相からの色相オフセット(度)の並び。先頭(0)が基準色で、続く要素が配色上の各色のずれを表す。配色の最大色数はこの並びの長さで、サイドバーの色数がこれより多いぶんは配色に組み込まず自由に置ける。
	public static double[] HueOffsets(ColorHarmonyScheme scheme)
	{
		return scheme switch
		{
			ColorHarmonyScheme.Complementary => new[] { 0.0, 180.0 },
			ColorHarmonyScheme.Diad => new[] { 0.0, 60.0 },
			ColorHarmonyScheme.Analogous => new[] { 0.0, 30.0, 60.0, 90.0, 120.0 },
			ColorHarmonyScheme.Triadic => new[] { 0.0, 120.0, 240.0 },
			ColorHarmonyScheme.SplitComplementary => new[] { 0.0, 150.0, 210.0 },
			ColorHarmonyScheme.Tetradic => new[] { 0.0, 60.0, 180.0, 240.0 },
			ColorHarmonyScheme.Square => new[] { 0.0, 90.0, 180.0, 270.0 },
			ColorHarmonyScheme.Pentad => new[] { 0.0, 72.0, 144.0, 216.0, 288.0 },
			_ => new[] { 0.0 },
		};
	}




	// 配色の線を多角形として閉じるか。3点以上で面を成す Triadic・SplitComplementary・Tetradic・Square・Pentad は末尾から先頭へ戻して閉じ、2点の Complementary・Diad と片側へ並ぶ Analogous は開いた線にする。
	public static bool IsClosed(ColorHarmonyScheme scheme)
	{
		return scheme is ColorHarmonyScheme.Triadic
			or ColorHarmonyScheme.SplitComplementary
			or ColorHarmonyScheme.Tetradic
			or ColorHarmonyScheme.Square
			or ColorHarmonyScheme.Pentad;
	}




	// 逆周り(色相オフセットを左右反転した鏡像)が元と別物になる非対称な配色か。片側へ寄る Diad・Analogous と、矩形の狭辺が左右どちらにも向ける Tetradic は鏡像が別形のため真。色相環を等分する配色や反対色を挟んで対称な配色は、鏡像が同じ集合になるため偽。反転トグルの有効・無効と、反転が実際に効くかの判定に使う。
	public static bool IsDirectional(ColorHarmonyScheme scheme)
	{
		return scheme is ColorHarmonyScheme.Diad
			or ColorHarmonyScheme.Analogous
			or ColorHarmonyScheme.Tetradic;
	}
}
