// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// CSS のカラー関数 (rgb()/hsl()/hwb() 等) をコピーするときに、アルファ値をどの表記で書き出すか。色や丸めには影響せず、コピー文字列のアルファ表記だけを変える。16進 (#RRGGBBAA) やパック (0xAARRGGBB) のアルファは対象外。
public enum WebAlphaUnit
{
	// 0.0–1.0 の数値。不透明は "1"。
	Number,

	// 0–100% のパーセント。不透明は "100%"。
	Percent,
}
