// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// ターミナルの基本16色(インデックス 0-15)の参照配色。256色の 0-15、および16色・8色の実RGBは端末の配色テーマで変わるため、主要ターミナルの既知の配色をプリセットとして選ぶ。キューブ(16-231)とグレー(232-255)は xterm 標準で固定のため、この選択の影響を受けない。
public enum TerminalTheme
{
	// Windows Terminal 既定の配色。
	Campbell,

	// 標準 VGA テキストモードの16色配色。
	Vga,

	// xterm 既定の16色。
	Xterm,
}
