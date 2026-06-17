// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;

namespace Irozukume.Models;

// Palette タブのコンボボックスに並べる、選べるパレットの一覧。現状は Web の名前付きカラーのみ。リストの種類が増えたらここへ加える。
public static class NamedColorPalettes
{
	public static IReadOnlyList<ColorPalette> All { get; } = new[] { WebNamedColors.Palette };
}
