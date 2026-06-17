// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// 解釈に成功した色の元の書式。貼り付け時に書式へ合わせたタブ(と HSV/HSL の副モード)へ切り替える判断に使う。Unknown は未解釈(既定値)を表す。
public enum ColorSourceFormat
{
	Unknown,
	Hex,
	Rgb,
	Hsl,
	Hwb,
	Lch,
	Oklch,
	Lab,
	Oklab,
	Named,
	Transparent,
	TerminalTrueColor,
	TerminalIndexed,
}
