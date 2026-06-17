// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Irozukume.Models;

namespace Irozukume.Helpers;

// 色を ANSI エスケープ列(ターミナルの色指定)へ整形する。トゥルーカラー・256色・16色・8色の各形式と、前景・背景を扱う。256/16/8 は TerminalPalette の最近傍探索で最も近いインデックスを選び、距離計算と参照テーマはコピー時の設定を使う。先頭の ESC をどの表記で書き出すか、末尾にリセットを付けるかは設定で決める。CSS 系を扱う ColorStringFormatter とは別に持つ。
public static class TerminalColorFormatter
{
	// コピーする形式を識別子から整形する。識別子は前景が _fg、背景が _bg で終わる。整形できない識別子は空文字を返す。
	public static string Format(string key, byte r, byte g, byte b, TerminalEscStyle esc, bool resetSuffix, SnapMetric metric, TerminalTheme theme)
	{
		bool background = key.EndsWith("_bg");
		string prefix = EscPrefix(esc);

		string body = key switch
		{
			"term_tc_fg" or "term_tc_bg" => TrueColorBody(background, r, g, b),
			"term_256_fg" or "term_256_bg" => Indexed256Body(background, r, g, b, metric, theme),
			"term_16_fg" or "term_16_bg" => SgrBody(background, r, g, b, ColorLimitMode.Term16, metric, theme),
			"term_8_fg" or "term_8_bg" => SgrBody(background, r, g, b, ColorLimitMode.Term8, metric, theme),
			_ => "",
		};

		if (body.Length == 0)
		{
			return "";
		}

		string result = prefix + body;

		if (resetSuffix)
		{
			result += prefix + "[0m";
		}

		return result;
	}




	// トゥルーカラーの本体。前景は 38、背景は 48 に続けて 2;R;G;B を並べる。
	private static string TrueColorBody(bool background, byte r, byte g, byte b)
	{
		int lead = background ? 48 : 38;
		return $"[{lead};2;{r};{g};{b}m";
	}




	// 256色の本体。最も近いインデックスを選び、前景は 38、背景は 48 に続けて 5;インデックス を並べる。
	private static string Indexed256Body(bool background, byte r, byte g, byte b, SnapMetric metric, TerminalTheme theme)
	{
		int index = TerminalPalette.NearestIndex(r, g, b, ColorLimitMode.Term256, metric, theme);
		int lead = background ? 48 : 38;
		return $"[{lead};5;{index}m";
	}




	// 16色・8色の本体。最も近いインデックスを選び、前景・背景それぞれの SGR 番号を並べる。
	private static string SgrBody(bool background, byte r, byte g, byte b, ColorLimitMode mode, SnapMetric metric, TerminalTheme theme)
	{
		int index = TerminalPalette.NearestIndex(r, g, b, mode, metric, theme);
		int sgr = background ? TerminalPalette.BackgroundSgr(index) : TerminalPalette.ForegroundSgr(index);
		return $"[{sgr}m";
	}




	// 先頭の ESC(0x1B)を、設定で選んだ表記の文字列にする。Unicode 表記はソース上に \u の並びを置けないため、バックスラッシュと u001b を分けて組み立てる。
	private static string EscPrefix(TerminalEscStyle style)
	{
		return style switch
		{
			TerminalEscStyle.Literal => "\x1b",
			TerminalEscStyle.BackslashE => "\\e",
			TerminalEscStyle.Octal => "\\033",
			TerminalEscStyle.Unicode => "\\" + "u001b",
			TerminalEscStyle.Caret => "^[",
			_ => "\\x1b",
		};
	}
}
