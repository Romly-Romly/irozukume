// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// 色を「最も近いパレット色」へ丸めるときに使う距離の測り方。色制限(WebSafe・RGB565 等の格子モード、Term256/16/8 のターミナルモード)の最近傍探索で共通に使う。格子モードは Rgb のときチャンネル独立丸めの速い経路を通り、Lab・Redmean のときはチャンネル独立丸めの近傍を当該距離で再評価する。
public enum SnapMetric
{
	// CIELAB 上のユークリッド距離(CIE76)。sRGB を Lab へ変換して測る。見た目に最も近い色を選ぶ。
	Lab,

	// 赤成分の平均で重み付けした RGB 距離(redmean)。Lab には及ばないが単純 RGB より知覚に近く、計算は軽い。
	Redmean,

	// RGB 空間のユークリッド距離。最も単純。格子モードではチャンネル独立丸めと一致する。
	Rgb,
}
