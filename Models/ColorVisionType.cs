// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// 色覚シミュレーションの型。錐体のどれが弱まるかで分け、弱まりの強さ(severity)は別の値として与える。サイドバーの各色パネルへ重ねる見え方の行と、その表示トグルの単位になる。
public enum ColorVisionType
{
	// P型 (1型・赤錐体)。赤の感度が弱まり、赤と緑が混同しやすい。
	Protan,

	// D型 (2型・緑錐体)。緑の感度が弱まり、赤と緑が混同しやすい。有病率が最も高い。
	Deutan,

	// T型 (3型・青錐体)。青の感度が弱まり、青と黄が混同しやすい。
	Tritan,

	// 1色覚。色の区別が無く明暗だけの見え方。severity を持たない。
	Monochromacy,
}
