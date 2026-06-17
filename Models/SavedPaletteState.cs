// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Irozukume.Models;

// 保存済みのお気に入りパレット1件分。利用者が付けた表示名と、サイドバーの色をそのまま写した色の並び (各色は ColorEntryState の "#RRGGBB" + 不透明度) を持つ。色の並びを取っておくための器で、配色の作り方 (調和規則など) は持たない。
public sealed class SavedPaletteState
{
	// 一覧 (パレットの種類コンボ) に出す表示名。空や未指定のときは復元側で既定名を当てる。
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	// パレットを構成する色の並び。表示と同じ並び順で保存する。サイドバーの色と同じ形式 (素の RGB + 不透明度) で持つため、ColorEntryState を共有する。Mix タブのポッチ位置はお気に入りには持たせない。
	[JsonPropertyName("colors")]
	public List<ColorEntryState>? Colors { get; set; }
}
