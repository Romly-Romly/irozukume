// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// Mix タブの平面を、各ポッチの色からどう塗り広げるか(空間補間の重み付け)。既定は逆距離加重。
public enum MixInterpolation
{
	// 逆距離加重(Shepard) 各ポッチからの距離の逆数2乗で混ぜ、にじむように柔らかくつながる。
	InverseDistance,

	// 距離をガウス関数で重み付けし、ポッチの近くをより強く効かせる。
	Gaussian,

	// 各画素を一番近いポッチの色一色にし、境界が直線のモザイク(ボロノイ図)になる。
	Nearest,
}
