// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;
using Irozukume.Helpers;

namespace Irozukume.Models;

// Palette タブに並べるターミナル(ANSI)の配色。配色テーマ(Campbell/VGA/xterm)ごとの基本16色を固定で3つ、加えてインデックス参照用の256色フルパレットを1つ提供する。基本16色は端末の配色テーマで実RGBが変わるため、フルパレットの 0-15 は呼び出し時の参照テーマで引く。色の値は TerminalPalette を一点として引く。
public static class TerminalColorPalettes
{
	// 基本16色(0-15)の ANSI 色名。明色は Bright を冠する。言語非依存のため多言語化せず直接書く。
	private static readonly string[] AnsiNames =
	{
		"Black", "Red", "Green", "Yellow", "Blue", "Magenta", "Cyan", "White",
		"Bright Black", "Bright Red", "Bright Green", "Bright Yellow", "Bright Blue", "Bright Magenta", "Bright Cyan", "Bright White",
	};

	// 配色テーマごとの基本16色パレット(Campbell/VGA/xterm)。設定の参照テーマに依らず、それぞれの配色を固定で見せる。
	public static IReadOnlyList<ColorPalette> ThemePalettes { get; } = new[]
	{
		ThemePalette(TerminalTheme.Campbell, "Campbell"),
		ThemePalette(TerminalTheme.Vga, "VGA"),
		ThemePalette(TerminalTheme.Xterm, "xterm"),
	};




	// 256色フルパレットのコンボボックス表示名。0-15 が参照テーマに追従するため、表示名にはテーマを含めない。
	public static string FullPaletteDisplayName => Loc.Get("Palette_Terminal256");




	// 256色フルパレットの色一覧を、指定の参照テーマで作る。0-15 はテーマの基本16色で「0 Black」等の名前、16-231 はキューブ、232-255 はグレーで、いずれも番号のみの名前にする。インデックスから色を引く参照に使う。
	public static IReadOnlyList<NamedColor> FullPaletteColors(TerminalTheme theme)
	{
		var colors = new List<NamedColor>(256);

		for (int i = 0; i < 256; i++)
		{
			(byte r, byte g, byte b) = TerminalPalette.IndexToRgb(i, theme);
			string name = i < 16 ? $"{i} {AnsiNames[i]}" : i.ToString();
			colors.Add(new NamedColor(name, r, g, b));
		}

		return colors;
	}




	// 1つの配色テーマの基本16色パレットを作る。各色は「0 Black」のように番号と ANSI 色名を併記する。
	private static ColorPalette ThemePalette(TerminalTheme theme, string label)
	{
		var colors = new List<NamedColor>(16);

		for (int i = 0; i < 16; i++)
		{
			(byte r, byte g, byte b) = TerminalPalette.BaseColor(theme, i);
			colors.Add(new NamedColor($"{i} {AnsiNames[i]}", r, g, b));
		}

		return new ColorPalette(Loc.Get("Palette_Terminal16", label), colors);
	}
}
