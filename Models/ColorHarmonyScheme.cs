// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// 配色タブの色の関係づけ。前半は基準色からの色相の角度関係でまとまりを作る角度系の配色。後半は固定の角度を持たず、色相かトーン(明度+彩度)のどちらかを全色で揃える拘束系の配色で、点数は固定でなく現在の色数に追従する。
// Complementary は基準と反対(180°)の2色。Diad は基準から少し離れた2色(約60°)。Analogous は基準から同じ向きへ少しずつ離れた隣り合う色。Triadic は色相環を三等分した3色。SplitComplementary は反対色の手前で左右に割った3色。Tetradic は基準と近接色の対を反対側にも置いた矩形の4色。Square は色相環を四等分した4色。Pentad は色相環を五等分した5色。
// Monochromatic は色相を1つに固定し、色ごとに明度・彩度を変える(1本のスポーク上に色数ぶん並ぶ)。DominantTone はトーン(明度+彩度)を全色で揃え、色相だけを自由に変える(同じ半径の輪の上に並ぶ)。Tonal はドミナントトーンの一種で、共通トーンをくすんだ中間色域に収める。Monotone は無彩色(白黒グレー)だけで明度の階調を作る。色相も彩度も持たず、各色は明度だけが違う。
public enum ColorHarmonyScheme
{
	Complementary,
	Diad,
	Analogous,
	Triadic,
	SplitComplementary,
	Tetradic,
	Square,
	Pentad,
	Monochromatic,
	DominantTone,
	Tonal,
	Monotone,
}
