// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Microsoft.Windows.ApplicationModel.Resources;

namespace Irozukume.Helpers;

// コードで組み立てる文字列(トレイメニュー・履歴の絞り込み名・各種メッセージ等)を resw から取得するための薄いラッパー。XAML 上の文字列は x:Uid で resw を直接参照するため、ここはコードから引く分だけを扱う。ResourceLoader は生成にコストがあるため一度だけ作って使い回す。非パッケージアプリでは WinAppSDK 版の ResourceLoader を使う。
public static class Loc
{
	private static readonly ResourceLoader _loader = new();




	// 指定したキーの文字列を返す。
	public static string Get(string key)
	{
		return _loader.GetString(key);
	}




	// 指定したキーの書式文字列に引数を差し込んで返す。プレースホルダ({0} 等)を含むメッセージに使う。
	public static string Get(string key, params object[] args)
	{
		return string.Format(_loader.GetString(key), args);
	}
}
