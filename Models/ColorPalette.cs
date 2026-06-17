// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;

namespace Irozukume.Models;

// 名前付きカラーの一覧。コンボボックスで選ぶ「リストの種類」1件に対応する。コンボボックスに出す表示名と、その並びの色を持つ。
public sealed class ColorPalette
{
	public string DisplayName { get; }

	public IReadOnlyList<NamedColor> Colors { get; }




	public ColorPalette(string displayName, IReadOnlyList<NamedColor> colors)
	{
		DisplayName = displayName;
		Colors = colors;
	}
}
