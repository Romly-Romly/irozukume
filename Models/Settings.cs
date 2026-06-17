// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Text.Json.Serialization;

namespace Irozukume.Models;

// アプリの永続設定のルート。メインウィンドウの配置・コントラストマトリックスの配置・色編集状態・外観設定を持つ。設定項目が増えたらここへ足していく。
public sealed class Settings
{
	// 保存ファイルのスキーマ版。将来、互換性を保てない構造変更をしたときに、読み込み側でどの版のファイルかを判別して移行するための足場。保存時に常に現行版へ押印する。
	public const int CurrentSchemaVersion = 1;

	// 設定ファイルのスキーマ版。このキーを持たないファイルは 0 (版を導入する前のファイル) として読む。
	[JsonPropertyName("schema_version")]
	public int SchemaVersion { get; set; }

	[JsonPropertyName("window")]
	public WindowPlacement? Window { get; set; }

	// コントラストマトリックスの補助ウィンドウの配置。一度も開いていなければ null のままで、次に開くとき既定のサイズになる。
	[JsonPropertyName("matrix_window")]
	public WindowPlacement? MatrixWindow { get; set; }

	[JsonPropertyName("editor")]
	public EditorState? Editor { get; set; }

	[JsonPropertyName("appearance")]
	public AppearanceState? Appearance { get; set; }
}
