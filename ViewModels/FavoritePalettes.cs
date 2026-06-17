// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.ViewModels;

// お気に入りパレットの1色。素の RGB と不透明度 (0–255) を持つ。サイドバーの色をそのまま写したもので、表示・一括取得の双方で使う。
public readonly struct FavoriteColor
{
	public byte R { get; }

	public byte G { get; }

	public byte B { get; }

	public byte A { get; }




	public FavoriteColor(byte r, byte g, byte b, byte a)
	{
		R = r;
		G = g;
		B = b;
		A = a;
	}
}




// お気に入りパレット1件。表示名と色の並びを持つ。名前は変更できるよう可変にし、色の並びは登録時に固定する。
public sealed class FavoritePalette
{
	public string Name { get; set; }

	public IReadOnlyList<FavoriteColor> Colors { get; }




	public FavoritePalette(string name, IReadOnlyList<FavoriteColor> colors)
	{
		Name = name;
		Colors = colors;
	}
}




// お気に入りパレットを一手に持つ共有ストア。サイドバーの色を名前付きで取っておき、登録順に並べる。Palette タブの一覧 (パレットの種類コンボ) と、保存・一括取得の操作が同じこのストアを参照する。永続設定から復元し、保存用に取り出せる。
public sealed class FavoritePalettes
{
	// 保存済みのお気に入りがあればそれで初期化する。登録順の並びをそのまま受け取り、色を1つも解釈できないパレットは取り込まない。
	public FavoritePalettes(IReadOnlyList<SavedPaletteState>? saved)
	{
		if (saved is null)
		{
			return;
		}

		int autoNumber = 1;

		foreach (SavedPaletteState item in saved)
		{
			var colors = ParseColors(item.Colors);

			if (colors.Count == 0)
			{
				continue;
			}

			string name = string.IsNullOrWhiteSpace(item.Name)
				? Loc.Get("Favorite_DefaultNameFormat", autoNumber)
				: item.Name!;
			Items.Add(new FavoritePalette(name, colors));
			autoNumber++;
		}
	}




	// お気に入りの一覧。登録順。Palette タブのコンボがこれに追従し、保存・削除もここへ反映する。
	public ObservableCollection<FavoritePalette> Items { get; } = new();




	// 現在のサイドバーの色を名前付きで取っておく。末尾へ加え、加えたパレットを返す。色が空のときは何もせず null を返す。
	public FavoritePalette? Add(string name, IReadOnlyList<FavoriteColor> colors)
	{
		if (colors is null || colors.Count == 0)
		{
			return null;
		}

		var palette = new FavoritePalette(name, new List<FavoriteColor>(colors));
		Items.Add(palette);
		return palette;
	}




	// お気に入りを取り除く。一覧に無ければ何もしない。
	public void Remove(FavoritePalette palette)
	{
		Items.Remove(palette);
	}




	// お気に入りの名前を変える。空白だけの名前は無視する。
	public void Rename(FavoritePalette palette, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		palette.Name = name;
	}




	// 永続設定用にお気に入りを取り出す。表示と同じ登録順で並べ、色は "#RRGGBB"、不透明度は別キーで書き出す。
	public List<SavedPaletteState> Capture()
	{
		var list = new List<SavedPaletteState>(Items.Count);

		foreach (FavoritePalette palette in Items)
		{
			var colors = new List<ColorEntryState>(palette.Colors.Count);

			foreach (FavoriteColor color in palette.Colors)
			{
				colors.Add(new ColorEntryState
				{
					Rgb = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
					Alpha = color.A,
				});
			}

			list.Add(new SavedPaletteState
			{
				Name = palette.Name,
				Colors = colors,
			});
		}

		return list;
	}




	// 保存済みの色一覧をお気に入りの色へ復元する。色の16進が壊れた項目は読み飛ばし、不透明度は 0–255 へ収める。不透明度のキーを持たない色は不透明 (255) として扱う。
	private static List<FavoriteColor> ParseColors(IReadOnlyList<ColorEntryState>? saved)
	{
		var colors = new List<FavoriteColor>();

		if (saved is null)
		{
			return colors;
		}

		foreach (ColorEntryState entry in saved)
		{
			if (TryParseHex(entry.Rgb, out byte r, out byte g, out byte b))
			{
				byte a = (byte)Math.Clamp(entry.Alpha, 0, 255);
				colors.Add(new FavoriteColor(r, g, b, a));
			}
		}

		return colors;
	}




	// "#RRGGBB" を R・G・B バイトへ解釈する。先頭の # は省略可。解釈できなければ false を返す。
	private static bool TryParseHex(string? hex, out byte r, out byte g, out byte b)
	{
		r = g = b = 0;

		if (string.IsNullOrWhiteSpace(hex))
		{
			return false;
		}

		string s = hex.TrimStart('#');

		if (s.Length != 6)
		{
			return false;
		}

		return byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
			&& byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
			&& byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
	}
}
