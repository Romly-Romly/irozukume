// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Text.Json.Serialization;

namespace Irozukume.Models;

// 保存済みの色履歴1件。色は "#RRGGBB"、不透明度は別キー、その不透明度が元の文字列にあったか、出所、元の文字列を持つ。サイドバーの各色と同じく色とアルファを混ぜず、別々のキーで保存する。
public sealed class ColorHistoryItemState
{
	[JsonPropertyName("color")]
	public string? Color { get; set; }

	// 不透明度 (0–255)。HasAlpha が偽の履歴では 255 を入れておく。
	[JsonPropertyName("alpha")]
	public int Alpha { get; set; } = 255;

	// 元の文字列が不透明度を伴っていたか。偽なら色1へ戻すとき不透明度に触れない。
	[JsonPropertyName("has_alpha")]
	public bool HasAlpha { get; set; }

	// 出所。"paste" など。未知・未指定は貼り付けとして扱う。
	[JsonPropertyName("kind")]
	public string? Kind { get; set; }

	// 取り込んだ元の文字列。リストの表示に使う。
	[JsonPropertyName("source")]
	public string? Source { get; set; }
}
