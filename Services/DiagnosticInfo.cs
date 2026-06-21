// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Irozukume.Helpers;

namespace Irozukume.Services;

// 不具合の報告に添えるための診断情報を一枚のテキストにまとめる。バージョン・OS・実行アーキテクチャ・ランタイム・表示言語・画面の拡大率・設定ファイルの所在と読み込み状態を集める。内容は開発者が読むための技術情報のため、利用者の表示言語に依らず一定の体裁にする (多言語化しない)。各項目の取得は個別に try で囲み、取れない項目は (unknown) で埋めて全体の生成は失敗させない。
public static class DiagnosticInfo
{
	// 診断情報のテキストを組み立てる。displayScale には現在の画面の拡大率 (XamlRoot.RasterizationScale, 1.0 で 100%) を渡すと一行加える。取得元 (XamlRoot) を持たない呼び出しでは null を渡し、その行を省く。
	public static string Build(double? displayScale = null)
	{
		var sb = new StringBuilder();

		sb.AppendLine($"Irozukume {AppVersion()}");
		sb.AppendLine($"OS: {Safe(() => RuntimeInformation.OSDescription)} ({Safe(() => RuntimeInformation.OSArchitecture.ToString())})");
		sb.AppendLine($"Process: {Safe(() => RuntimeInformation.ProcessArchitecture.ToString())}, elevated={Safe(() => ElevationHelper.IsElevated.ToString())}");
		sb.AppendLine($"Runtime: {Safe(() => RuntimeInformation.FrameworkDescription)}");
		sb.AppendLine($"UI culture: {Safe(() => CultureInfo.CurrentUICulture.Name)}, language override: {LanguageOverride()}");

		if (displayScale is double scale)
		{
			sb.AppendLine($"Display scale: {Math.Round(scale * 100)}%");
		}

		string path = SettingsStore.FilePath;
		sb.AppendLine($"Settings: {path}");
		sb.AppendLine($"  exists: {Safe(() => File.Exists(path).ToString())}");
		sb.AppendLine($"  last load repaired: {SettingsStore.LastLoadWasRepaired}");

		return sb.ToString().TrimEnd();
	}




	// 情報バージョン (AssemblyInformationalVersion) をそのまま返す。プレリリース表記 (-alpha 等) やビルドメタデータ (+ 以降のコミットハッシュ) も含め、不具合の特定に役立つ完全な版を残す。取得できなければ (unknown)。
	private static string AppVersion()
	{
		try
		{
			string? info = Assembly.GetExecutingAssembly()
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			return string.IsNullOrEmpty(info) ? "(unknown)" : info;
		}
		catch
		{
			return "(unknown)";
		}
	}




	// 適用中の表示言語の上書き値を返す。空 (システムの表示言語に従う) のときは (system)、取得に失敗したときは (unknown)。
	private static string LanguageOverride()
	{
		try
		{
			string ov = Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
			return string.IsNullOrEmpty(ov) ? "(system)" : ov;
		}
		catch
		{
			return "(unknown)";
		}
	}




	// 1項目の取得を例外から守る。取得に失敗した項目だけを (unknown) に倒し、診断情報全体の生成は続ける。
	private static string Safe(Func<string> get)
	{
		try
		{
			return get() ?? "(unknown)";
		}
		catch
		{
			return "(unknown)";
		}
	}
}
