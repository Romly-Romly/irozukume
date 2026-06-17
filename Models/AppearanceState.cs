// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Text.Json.Serialization;

namespace Irozukume.Models;

// アプリの外観・表示に関する永続設定。アプリのテーマ(配色)と表示言語を持つ。設定項目が増えたらここへ足していく。
public sealed class AppearanceState
{
	// アプリのテーマ。"light" / "dark" / "system"。未指定や未知の値はシステム設定に従う(system)として扱う。
	[JsonPropertyName("theme")]
	public string? Theme { get; set; }

	// アプリの表示言語。"ja" / "en" / "system"。未指定や未知の値はシステムに合わせる(system)として扱う。
	[JsonPropertyName("language")]
	public string? Language { get; set; }
}
