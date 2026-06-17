// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Windows.UI;

namespace Irozukume.Models;

// 名前付きカラー1色。色名とその色を持つ。パレット(名前付きカラーの一覧)を構成する最小単位で、リスト表示と検索の素材になる。
public sealed class NamedColor
{
	public string Name { get; }

	public Color Color { get; }




	public NamedColor(string name, byte r, byte g, byte b)
	{
		Name = name;
		Color = Color.FromArgb(0xFF, r, g, b);
	}
}
