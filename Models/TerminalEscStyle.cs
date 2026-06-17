// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// ターミナルのコピーで、エスケープ列の先頭の ESC(0x1B)をどの表記で書き出すか。貼り付け先(端末への直貼り、シェルの printf、ソースの文字列リテラル等)で受け付ける表記が異なるため、設定で選ぶ。
public enum TerminalEscStyle
{
	// 実体の ESC 文字(0x1B)をそのまま書き出す。
	Literal,

	// バックスラッシュ + e の表記で書き出す。
	BackslashE,

	// バックスラッシュ + x1b の16進表記で書き出す。
	Hex,

	// バックスラッシュ + 033 の8進表記で書き出す。
	Octal,

	// バックスラッシュ + u001b の Unicode コードポイント表記で書き出す。
	Unicode,

	// キャレット記法(^[)で書き出す。
	Caret,
}
