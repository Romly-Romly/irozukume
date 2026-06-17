// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// R・G・B の各成分を数値として見せるときの単位。値そのものは常に 0–255 で扱い、これは表示と入力解釈の見せ方だけを表す。
public enum RgbUnit
{
	// 0–255 の10進表記。
	Byte,

	// 00–FF の16進2桁表記。
	Hex,

	// 0.0–1.0 の正規化表記。
	Normalized,
}
