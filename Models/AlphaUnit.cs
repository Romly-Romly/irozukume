// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// アルファ値(不透明度)を数値として見せるときの単位。値そのものは常に 0–255 のバイトで扱い、これは表示の見せ方だけを表す。
public enum AlphaUnit
{
	// 0–255 のバイト表記。
	Byte,

	// 00–FF の16進2桁表記。
	Hex,

	// 0–100% のパーセント表記。
	Percent,

	// 0.0–1.0 の正規化表記。
	Normalized,
}
