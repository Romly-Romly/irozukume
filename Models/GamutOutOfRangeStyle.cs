// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// 2次元スライダー(LCH の L-C 平面、Lab の a-b 平面)で sRGB 色域を外れた範囲の見せ方。色域内は常に実際の色で塗り、これは色域外の描き方だけを切り替える。各色の RGB やスライダーの値には影響しない。
public enum GamutOutOfRangeStyle
{
	// 明度・色相を保って彩度を境界へ詰めたクランプ色で塗り、色域の境界に黒白の輪郭線を引く。
	FillBoundary,

	// クランプ色塗りと境界線に加え、斜線ハッチを上に重ねる。
	FillBoundaryHatch,

	// 白で塗り、斜線ハッチだけで色域外を示す。実際の色も境界線も描かない。
	WhiteHatch,
}
