// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// Mix タブの多点グラデーションで、色をどの色空間で補間するか。既定は知覚的に最も忠実な OKLCH。
// Oklch/Lch は明度・彩度・色相を分けて混ぜ、色相を弧でつなぐため鮮やかさを保ちやすい(Oklch は知覚均等、Lch は CIELAB 基準)。
// Oklab/Lab は明度と直交2軸を直線で混ぜ、補色どうしでは中間が灰へ寄る(Oklab は知覚均等、Lab は CIELAB 基準)。
// Hsl は Oklch と同じ円筒構成だが知覚均等ではない素朴な尺度。
// LinearRgb は光のまま(ガンマ解除した線形値)で各チャンネルを直線で混ぜ、中間が明るく濁りにくい。
// Rgb は各チャンネルを sRGB のまま直線で混ぜる最も素朴な方式。
public enum MixColorSpace
{
	Oklch,
	Oklab,
	Lch,
	Lab,
	Hsl,
	LinearRgb,
	Rgb,
}
