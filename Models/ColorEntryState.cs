// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Text.Json.Serialization;

namespace Irozukume.Models;

// 保存済みのサイドバーの色1件分。色は "#RRGGBB" 形式で持ち、不透明度 (アルファ, 0–255) はアルファを色へ混ぜないよう独立したキーで持つ。
public sealed class ColorEntryState
{
	[JsonPropertyName("rgb")]
	public string? Rgb { get; set; }

	// 不透明度 (0–255)。既定は不透明 (255)。このキーを持たない設定でも不透明として扱う。
	[JsonPropertyName("alpha")]
	public int Alpha { get; set; } = 255;

	// Mix タブでのポッチの位置(正規化 0–1・左上原点)。利用者が動かしていない色ではキー自体を持たず、Mix タブ側が初回に正多角形へ配る。
	[JsonPropertyName("mix_x")]
	public double? MixX { get; set; }

	[JsonPropertyName("mix_y")]
	public double? MixY { get; set; }
}
