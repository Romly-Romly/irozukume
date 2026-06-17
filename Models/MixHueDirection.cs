// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// Mix タブの色相を持つ色空間(OKLCH・CIE LCH・HSL)で、複数の色相をどちら回りで混ぜるか。色相環は環状のため、混ぜる色相の間を結ぶ弧は2通りある。
// Shorter は最短の弧(色相ベクトルの合成方向)でつなぎ、素直で濁りにくい。多点でも各色の色相へ向かう自然な混ざりになる。
// Longer は色相環の 0 度の継ぎ目を跨がない側でつなぐ。最短側が継ぎ目を跨ぐ配色では反対回りになり、虹のような遠回りのグラデーションを作れる。継ぎ目を跨がない配色では Shorter と同じ。
// 色相を持たない色空間(OKLab・CIE Lab・Linear sRGB・sRGB)では効かない。
public enum MixHueDirection
{
	Shorter,
	Longer,
}
