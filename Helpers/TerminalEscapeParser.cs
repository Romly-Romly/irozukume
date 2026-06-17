// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using Irozukume.Models;

namespace Irozukume.Helpers;

// ANSI エスケープ列(ターミナルの色指定)を色として解釈する。トゥルーカラー(38;2;R;G;B / 48;2;…)・256色(38;5;n / 48;5;n)・16色/8色(SGR 30-37・90-97 等)に対応し、前景があれば前景、無ければ背景の色を採る。256色や基本色のインデックスは、与えた参照テーマで RGB へ引く。トゥルーカラーは参照テーマに依らず厳密。CSS 形式を扱う ColorStringParser で解釈できなかったときの二段目として使う。
public static class TerminalEscapeParser
{
	// 文字列を ANSI の色指定として解釈する。成功すれば true を返し color に結果を入れる。解釈できなければ false を返す。
	// ESC の表記(実体0x1B・\e・\x1b・\033・u001b・^[)はどれも色指定の前に '[' を伴うため、'[' から 'm' までの SGR 引数を直接走査して吸収する。引数の区切りは ';' と ':'(ISO 8613-6)の両方を扱い、複合属性(例: 1;38;5;208)からも色指定を拾う。
	public static bool TryParse(string? text, TerminalTheme theme, out ParsedColor color)
	{
		color = default;

		if (string.IsNullOrEmpty(text))
		{
			return false;
		}

		string s = text;
		(byte R, byte G, byte B)? fg = null;
		(byte R, byte G, byte B)? bg = null;

		// 採用した前景・背景がトゥルーカラー(38;2/48;2)由来か。インデックス(38;5/SGR 30-37 等)由来のときは偽。最後に当たった指定の種別を残し、貼り付け側がトゥルーカラーのときだけ RGB/CMYK タブへ切り替える判断に使う。
		bool fgTrueColor = false;
		bool bgTrueColor = false;

		int i = 0;

		while (i < s.Length)
		{
			if (s[i] != '[')
			{
				i++;
				continue;
			}

			int j = i + 1;

			while (j < s.Length && (char.IsDigit(s[j]) || s[j] == ';' || s[j] == ':'))
			{
				j++;
			}

			if (j < s.Length && s[j] == 'm' && j > i + 1)
			{
				ProcessSgr(s.Substring(i + 1, j - i - 1), theme, ref fg, ref bg, ref fgTrueColor, ref bgTrueColor);
				i = j + 1;
			}
			else
			{
				i++;
			}
		}

		if (fg is { } f)
		{
			color = new ParsedColor(f.R, f.G, f.B, 255, false, fgTrueColor ? ColorSourceFormat.TerminalTrueColor : ColorSourceFormat.TerminalIndexed);
			return true;
		}

		if (bg is { } b)
		{
			color = new ParsedColor(b.R, b.G, b.B, 255, false, bgTrueColor ? ColorSourceFormat.TerminalTrueColor : ColorSourceFormat.TerminalIndexed);
			return true;
		}

		return false;
	}




	// '[' と 'm' の間の SGR 引数列を解釈し、見つかった前景色・背景色を更新する。引数は ';' で区切られ、各区切りが ':' を含むときは ISO 8613-6 のコロン形式の色指定として単独で扱う。fgTrue・bgTrue は更新した色がトゥルーカラー由来かを併せて持ち帰る。
	private static void ProcessSgr(string paramsText, TerminalTheme theme, ref (byte R, byte G, byte B)? fg, ref (byte R, byte G, byte B)? bg, ref bool fgTrue, ref bool bgTrue)
	{
		string[] fields = paramsText.Split(';');
		int k = 0;

		while (k < fields.Length)
		{
			string field = fields[k];

			if (field.Contains(':'))
			{
				ApplyColonColor(field, theme, ref fg, ref bg, ref fgTrue, ref bgTrue);
				k++;
				continue;
			}

			if (!int.TryParse(field, out int code))
			{
				k++;
				continue;
			}

			if (code == 38 || code == 48)
			{
				bool background = code == 48;

				if (k + 1 < fields.Length && int.TryParse(fields[k + 1], out int sub))
				{
					if (sub == 5 && k + 2 < fields.Length && int.TryParse(fields[k + 2], out int idx))
					{
						AssignIndex(background, idx, theme, ref fg, ref bg, ref fgTrue, ref bgTrue);
						k += 3;
						continue;
					}

					if (sub == 2 && k + 4 < fields.Length
						&& int.TryParse(fields[k + 2], out int rr)
						&& int.TryParse(fields[k + 3], out int gg)
						&& int.TryParse(fields[k + 4], out int bb))
					{
						AssignRgb(background, rr, gg, bb, ref fg, ref bg, ref fgTrue, ref bgTrue);
						k += 5;
						continue;
					}
				}

				k++;
				continue;
			}

			if (code >= 30 && code <= 37)
			{
				AssignIndex(false, code - 30, theme, ref fg, ref bg, ref fgTrue, ref bgTrue);
			}
			else if (code >= 90 && code <= 97)
			{
				AssignIndex(false, code - 90 + 8, theme, ref fg, ref bg, ref fgTrue, ref bgTrue);
			}
			else if (code >= 40 && code <= 47)
			{
				AssignIndex(true, code - 40, theme, ref fg, ref bg, ref fgTrue, ref bgTrue);
			}
			else if (code >= 100 && code <= 107)
			{
				AssignIndex(true, code - 100 + 8, theme, ref fg, ref bg, ref fgTrue, ref bgTrue);
			}

			k++;
		}
	}




	// コロン形式の色指定(38:5:n / 48:5:n / 38:2:[colorspace]:R:G:B 等)を解釈する。トゥルーカラーは末尾3つの数値を R・G・B とし、colorspace の有無や空フィールドの違いを吸収する。
	private static void ApplyColonColor(string field, TerminalTheme theme, ref (byte R, byte G, byte B)? fg, ref (byte R, byte G, byte B)? bg, ref bool fgTrue, ref bool bgTrue)
	{
		string[] parts = field.Split(':');

		if (parts.Length < 2 || !int.TryParse(parts[0], out int lead) || (lead != 38 && lead != 48))
		{
			return;
		}

		bool background = lead == 48;

		if (!int.TryParse(parts[1], out int type))
		{
			return;
		}

		if (type == 5)
		{
			if (parts.Length >= 3 && int.TryParse(parts[2], out int idx))
			{
				AssignIndex(background, idx, theme, ref fg, ref bg, ref fgTrue, ref bgTrue);
			}

			return;
		}

		if (type == 2)
		{
			var nums = new List<int>();

			for (int p = 2; p < parts.Length; p++)
			{
				if (int.TryParse(parts[p], out int v))
				{
					nums.Add(v);
				}
			}

			if (nums.Count >= 3)
			{
				AssignRgb(background, nums[nums.Count - 3], nums[nums.Count - 2], nums[nums.Count - 1], ref fg, ref bg, ref fgTrue, ref bgTrue);
			}
		}
	}




	// パレットインデックス(0-255)から RGB を引いて前景か背景へ入れる。範囲外は無視する。入れた側のトゥルーカラー印は、インデックス由来のため偽にする。
	private static void AssignIndex(bool background, int index, TerminalTheme theme, ref (byte R, byte G, byte B)? fg, ref (byte R, byte G, byte B)? bg, ref bool fgTrue, ref bool bgTrue)
	{
		if (index < 0 || index > 255)
		{
			return;
		}

		(byte R, byte G, byte B) rgb = TerminalPalette.IndexToRgb(index, theme);

		if (background)
		{
			bg = rgb;
			bgTrue = false;
		}
		else
		{
			fg = rgb;
			fgTrue = false;
		}
	}




	// トゥルーカラーの R・G・B を 0-255 へ丸めて前景か背景へ入れる。入れた側のトゥルーカラー印を真にする。
	private static void AssignRgb(bool background, int r, int g, int b, ref (byte R, byte G, byte B)? fg, ref (byte R, byte G, byte B)? bg, ref bool fgTrue, ref bool bgTrue)
	{
		(byte R, byte G, byte B) rgb = ((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));

		if (background)
		{
			bg = rgb;
			bgTrue = true;
		}
		else
		{
			fg = rgb;
			fgTrue = true;
		}
	}
}
