// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Irozukume.Models;
using Romly.WinUI.Common.Windowing;

namespace Irozukume.Services;

// settings.json の読み書き。実行ファイルと同じディレクトリに置くことで、ZIP 配布版でも書き込み権限のある場所に保存され、ポータブル運用 (USB メモリ等) でもそのまま動く。シリアライズは snake_case + インデント整形で、人が直接編集してもよい体裁にする。
public static class SettingsStore
{
	private static readonly JsonSerializerOptions _options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,

		// 人が手で編集した settings.json にありがちな綴りの揺れを、読み込み時はできるだけ許容する。数値の文字列表記・末尾カンマ・コメントを受け入れる。いずれも書き出しの体裁には影響しない。
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
	};




	// 直近の Load で、壊れた節を初期値で補う救済を行ったか。起動時に「設定の一部を読み込めなかった」旨を一度だけ知らせる判断に使う。
	public static bool LastLoadWasRepaired { get; private set; }




	private static string SettingsPath()
	{
		// AppContext.BaseDirectory は単一ファイル発行でも実行ファイルのあるディレクトリを返す。
		var dir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
		return Path.Combine(dir, "settings.json");
	}




	// 設定ファイルの絶対パス。診断情報の表示など、外部から所在を知りたいときに使う。
	public static string FilePath => SettingsPath();




	// 設定を読み込む。ファイルが無い・壊れている・読めないときは既定値で起動する。全体としては解釈できない壊れ方でも、トップレベルの節 (window・editor・appearance 等) ごとに救えるところは救う。1か所の綴り誤りで全設定を失わないため。
	public static Settings Load()
	{
		LastLoadWasRepaired = false;

		var path = SettingsPath();

		if (!File.Exists(path))
		{
			return new Settings();
		}

		string text;
		try
		{
			text = File.ReadAllText(path);
		}
		catch (Exception ex)
		{
			// ファイル自体が読めない (ロック・権限など)。既定値で起動しつつ原因を crash.log へ残す。
			CrashLog.Write(ex);
			return new Settings();
		}

		try
		{
			var settings = JsonSerializer.Deserialize<Settings>(text, _options);

			// 中身が "null" などで実体が無いときは既定で起動する。
			return settings ?? new Settings();
		}
		catch (Exception ex)
		{
			// 全体の解釈に失敗した。壊れた内容を上書きで失う前に .bak へ退避し、原因を crash.log へ残したうえで、節ごとに救えるところだけ救う。救済した旨は起動時の通知のために覚えておく。
			LastLoadWasRepaired = true;
			CrashLog.Write(ex);
			BackupCorruptFile(path);
			return LoadSalvaging(text);
		}
	}




	// 全体の解釈に失敗したとき、トップレベルの節ごとに個別に解釈し直し、壊れた節だけを捨てて読めた節は活かす。
	private static Settings LoadSalvaging(string text)
	{
		var settings = new Settings();

		JsonNode? root;
		try
		{
			root = JsonNode.Parse(text);
		}
		catch (Exception ex)
		{
			// JSON として全く読めない (括弧の不整合など)。全既定で起動する。
			CrashLog.Write(ex);
			return settings;
		}

		if (root is not JsonObject obj)
		{
			return settings;
		}

		settings.SchemaVersion = SalvageSchemaVersion(obj);
		settings.Window = SalvageSection<WindowPlacement>(obj, "window");
		settings.MatrixWindow = SalvageSection<WindowPlacement>(obj, "matrix_window");
		settings.Editor = SalvageSection<EditorState>(obj, "editor");
		settings.Appearance = SalvageSection<AppearanceState>(obj, "appearance");

		return settings;
	}




	// 指定キーの節を単独で解釈する。読めなければ null を返してその節だけ既定に倒し、原因を crash.log へ残す。
	private static T? SalvageSection<T>(JsonObject obj, string key) where T : class
	{
		if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node is null)
		{
			return null;
		}

		try
		{
			return node.Deserialize<T>(_options);
		}
		catch (Exception ex)
		{
			CrashLog.Write(ex);
			return null;
		}
	}




	// schema_version を単独で読む。読めなければ 0 (版を持たない設定として扱う) を返す。
	private static int SalvageSchemaVersion(JsonObject obj)
	{
		if (!obj.TryGetPropertyValue("schema_version", out JsonNode? node) || node is null)
		{
			return 0;
		}

		try
		{
			return node.GetValue<int>();
		}
		catch
		{
			return 0;
		}
	}




	// 壊れた settings.json を上書きで失う前に .bak へ退避する。手編集の誤りで設定が無言で消えるのを防ぐ。退避自体の失敗は無視する。
	private static void BackupCorruptFile(string path)
	{
		try
		{
			File.Copy(path, path + ".bak", overwrite: true);
		}
		catch
		{
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




	// 設定ファイルとその退避・一時ファイルを削除する。設定のリセットやアンインストール前の後始末で使う。存在しないファイルやアクセス不能は無視する。
	public static void Delete()
	{
		var path = SettingsPath();

		foreach (var target in new[] { path, path + ".bak", path + ".tmp" })
		{
			try
			{
				if (File.Exists(target))
				{
					File.Delete(target);
				}
			}
			catch
			{
			}
		}
	}
}
