// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Text.Json;

using Irozukume.Models;

namespace Irozukume.Services;

// settings.json の読み書き。実行ファイルと同じディレクトリに置くことで、ZIP 配布版でも書き込み権限のある場所に保存され、ポータブル運用 (USB メモリ等) でもそのまま動く。シリアライズは snake_case + インデント整形で、人が直接編集してもよい体裁にする。
public static class SettingsStore
{
	private static readonly JsonSerializerOptions _options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
	};




	private static string SettingsPath()
	{
		// AppContext.BaseDirectory は単一ファイル発行でも実行ファイルのあるディレクトリを返す。
		var dir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
		return Path.Combine(dir, "settings.json");
	}




	// 設定を読み込む。ファイルが無い・壊れている・読めないときは既定値で起動する。
	public static Settings Load()
	{
		var path = SettingsPath();

		try
		{
			if (!File.Exists(path))
			{
				return new Settings();
			}

			var text = File.ReadAllText(path);
			var settings = JsonSerializer.Deserialize<Settings>(text, _options);
			return settings ?? new Settings();
		}
		catch (Exception ex)
		{
			// 既存ファイルの読み込み・解析に失敗したときは、既定値で起動しつつ原因を追えるよう内容を残し、壊れたファイルを上書きで失う前に .bak へ退避する。手編集の誤りで設定が無言で消えるのを防ぐ。退避自体の失敗は無視する。
			System.Diagnostics.Debug.WriteLine($"[Irozukume] settings.json の読み込みに失敗したため既定値で起動します: {ex}");

			try
			{
				File.Copy(path, path + ".bak", overwrite: true);
			}
			catch
			{
			}

			return new Settings();
		}
	}




	// 設定を保存する。一時ファイルへ書き切ってから本体へ原子的に差し替える。本体を直接上書きすると、書き込み途中の異常終了で settings.json が壊れた JSON のまま残り、次回起動の Load が既定値へ倒れて設定が失われる。一時ファイル方式なら本体は常に「直前の完全な内容」か「新しい完全な内容」のいずれかになる。保存失敗は致命とせず黙って見送る。
	public static void Save(Settings settings)
	{
		var path = SettingsPath();
		var tmpPath = path + ".tmp";

		try
		{
			// 書き出すファイルには常に現行のスキーマ版を刻む。版を持たない古いファイルを読み込んだ場合 (SchemaVersion は 0) も、保存を経た時点で現行版へ更新される。
			settings.SchemaVersion = Settings.CurrentSchemaVersion;

			var text = JsonSerializer.Serialize(settings, _options);

			// 一時ファイルへ書いた内容をディスクへ確実に書き戻してから差し替える。フラッシュを挟まないと、リネームのメタデータだけが先に永続化され、その直後の OS クラッシュや電源断で中身が未書き戻し (ゼロ長等) のまま本体が壊れ得る。
			var bytes = System.Text.Encoding.UTF8.GetBytes(text);
			using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				stream.Write(bytes, 0, bytes.Length);
				stream.Flush(flushToDisk: true);
			}

			File.Move(tmpPath, path, overwrite: true);
		}
		catch
		{
			// 差し替え前に失敗した場合は書きかけの一時ファイルが残る (本体は無傷) ので片付ける。後始末の失敗も無視し、次回保存で上書きされるに任せる。
			try
			{
				if (File.Exists(tmpPath))
				{
					File.Delete(tmpPath);
				}
			}
			catch
			{
			}
		}
	}
}
