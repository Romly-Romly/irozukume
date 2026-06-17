// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Text;

namespace Irozukume.Services;

// 未処理例外を実行ファイル隣の crash.log へ追記する。アルファ版でテスターが踏んだ不具合を、配布フォルダに残るログから後で診断できるようにする。保存先は SettingsStore と同じ実行ファイルのディレクトリで、ZIP 配布・ポータブル運用でも書き込める。ログ書き込み自体の失敗は握り潰し、クラッシュ処理の最中に更なる例外で状況を悪化させない。
internal static class CrashLog
{
	public static void Write(Exception? exception)
	{
		try
		{
			var dir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
			var path = Path.Combine(dir, "crash.log");

			var entry = $"==== {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ===={Environment.NewLine}{exception?.ToString() ?? "(例外情報なし)"}{Environment.NewLine}{Environment.NewLine}";
			File.AppendAllText(path, entry, Encoding.UTF8);
		}
		catch
		{
			// ログの書き込みに失敗しても何もしない。クラッシュ処理中に更なる例外を投げて状況を悪化させないため。
		}
	}
}
