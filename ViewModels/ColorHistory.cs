// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Irozukume.Models;

namespace Irozukume.ViewModels;

// 色の履歴を一手に持つ共有ストア。貼り付けで色を取り込むと新しいものを先頭へ積み、同じ色が既にあれば取り除いてから先頭へ入れ直し、最新の表記と位置にまとめる。
// 件数は上限で頭打ちにする。履歴タブの表示と、メインウィンドウからの追加が同じこのストアを参照する。永続設定から復元し、保存用に取り出せる。
public sealed class ColorHistory
{
	// 履歴の保持上限。これを超えた古い履歴は捨てる。
	private const int MaxEntries = 50;




	// 保存済みの履歴があればそれで初期化する。新しいものが先頭の並びをそのまま受け取る。壊れた項目や上限超過分は取り込まない。
	public ColorHistory(IReadOnlyList<ColorHistoryItemState>? saved)
	{
		if (saved is null)
		{
			return;
		}

		foreach (ColorHistoryItemState item in saved)
		{
			if (Entries.Count >= MaxEntries)
			{
				break;
			}

			if (TryRestore(item, out ColorHistoryEntry? entry))
			{
				Entries.Add(entry!);
			}
		}
	}




	// 履歴の一覧。新しいものが先頭。履歴タブのリストがこれに追従し、貼り付けでの追加もここへ反映する。
	public ObservableCollection<ColorHistoryEntry> Entries { get; } = new();




	// 貼り付けで取り込んだ色を履歴へ積む。
	public void AddPaste(byte r, byte g, byte b, byte a, bool hasAlpha, string source)
	{
		Add(HistoryKind.Paste, r, g, b, a, hasAlpha, source);
	}




	// 形式を選んでコピーした色を履歴へ積む。
	public void AddCopy(byte r, byte g, byte b, byte a, bool hasAlpha, string source)
	{
		Add(HistoryKind.Copy, r, g, b, a, hasAlpha, source);
	}




	// 画面カラーピッカーで拾った色を履歴へ積む。画面ピックの出所は色そのものではなく取得手段のため、保存用の文字列は持たせず、表示名は表示時に現在の言語で解決する。
	public void AddPick(byte r, byte g, byte b, byte a, bool hasAlpha)
	{
		Add(HistoryKind.Pick, r, g, b, a, hasAlpha, "");
	}




	// 取り込んだ色を履歴へ積む。不透明度を持たない色は不透明 (255) として保つ。同じ出所・同じ色が既にあれば取り除いてから先頭へ入れ直し、上限を超えた末尾を捨てる。
	private void Add(HistoryKind kind, byte r, byte g, byte b, byte a, bool hasAlpha, string source)
	{
		byte alpha = hasAlpha ? a : (byte)255;
		var entry = new ColorHistoryEntry(r, g, b, alpha, hasAlpha, kind, source);

		for (int i = 0; i < Entries.Count; i++)
		{
			if (Entries[i].SameColor(entry))
			{
				Entries.RemoveAt(i);
				break;
			}
		}

		Entries.Insert(0, entry);

		while (Entries.Count > MaxEntries)
		{
			Entries.RemoveAt(Entries.Count - 1);
		}
	}




	// 永続設定用に履歴を取り出す。表示と同じ新しい順で並べ、色は "#RRGGBB"、不透明度は別キーで書き出す。
	public List<ColorHistoryItemState> Capture()
	{
		var list = new List<ColorHistoryItemState>(Entries.Count);

		foreach (ColorHistoryEntry entry in Entries)
		{
			list.Add(new ColorHistoryItemState
			{
				Color = $"#{entry.R:X2}{entry.G:X2}{entry.B:X2}",
				Alpha = entry.A,
				HasAlpha = entry.HasAlpha,
				Kind = KindToString(entry.Kind),
				Source = entry.Source,
			});
		}

		return list;
	}




	// 保存済み1件を履歴へ復元する。色の16進が壊れていれば取り込まない。
	private static bool TryRestore(ColorHistoryItemState item, out ColorHistoryEntry? entry)
	{
		entry = null;

		if (!TryParseHex(item.Color, out byte r, out byte g, out byte b))
		{
			return false;
		}

		// 不透明度を持たない履歴は不透明 (255) に揃える。AddPaste と同じ正規化を復元側でも行い、「HasAlpha が偽なら A は 255」という不変条件を保って重複排除を一貫させる。
		byte a = item.HasAlpha ? (byte)Math.Clamp(item.Alpha, 0, 255) : (byte)255;

		// 画面ピックは出所の文字列を持たない (表示名は表示時に解決する)。保存値に取得手段のラベルが残っていても取り込まず空に揃え、「画面ピックの Source は空」という不変条件を保つ。
		HistoryKind kind = ParseKind(item.Kind);
		string source = kind == HistoryKind.Pick ? "" : (item.Source ?? "");
		entry = new ColorHistoryEntry(r, g, b, a, item.HasAlpha, kind, source);
		return true;
	}




	// "#RRGGBB" を R・G・B バイトへ解釈する。先頭の # は省略可。解釈できなければ false を返す。
	private static bool TryParseHex(string? hex, out byte r, out byte g, out byte b)
	{
		r = g = b = 0;

		if (string.IsNullOrWhiteSpace(hex))
		{
			return false;
		}

		string s = hex.TrimStart('#');

		if (s.Length != 6)
		{
			return false;
		}

		return byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
			&& byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
			&& byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
	}




	// 設定ファイルの文字列を出所へ解釈する。未知・未指定は貼り付けとして扱う。
	private static HistoryKind ParseKind(string? value)
	{
		return value switch
		{
			"copy" => HistoryKind.Copy,
			"pick" => HistoryKind.Pick,
			_ => HistoryKind.Paste,
		};
	}




	// 出所を設定ファイル用の文字列にする。
	private static string KindToString(HistoryKind kind)
	{
		return kind switch
		{
			HistoryKind.Copy => "copy",
			HistoryKind.Pick => "pick",
			_ => "paste",
		};
	}
}
