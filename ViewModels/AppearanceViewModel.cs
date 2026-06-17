// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Irozukume.Models;

namespace Irozukume.ViewModels;

// アプリの外観・表示設定を持つモデル。アプリのテーマ(配色)と表示言語を扱う。
// 設定ページのテーマ選択(0=ライト, 1=ダーク, 2=システム設定の使用)を介してテーマを切り替え、変更を購読側(ウィンドウ)へ通知して即時適用させる。
// 表示言語(0=日本語, 1=English, 2=システムに合わせる)は読み込み済み UI を動的に差し替えられないため、変更通知を受けた購読側が再起動を促す。保存済みの設定があれば、それで初期化する。
public sealed class AppearanceViewModel : INotifyPropertyChanged
{
	private ElementTheme _theme;

	// 表示言語の選択。"ja" / "en" / "system"。
	private string _language;




	// 既定ではテーマ・言語ともにシステムに従う。保存済みの設定があれば、それで初期化する。
	public AppearanceViewModel(AppearanceState? state = null)
	{
		_theme = ParseTheme(state?.Theme);
		_language = ParseLanguage(state?.Language);
	}




	// テーマが変わったときに発火する。ウィンドウ側がこれを受けてルート要素へ適用する。
	public event EventHandler? ThemeChanged;




	// 表示言語の選択が変わったときに発火する。ウィンドウ側がこれを受けて、選択を保存し再起動を促す。
	public event EventHandler? LanguageChanged;




	// 現在のテーマ。ウィンドウのルート要素の RequestedTheme へ適用する。Default はシステム設定に従う。
	public ElementTheme Theme => _theme;




	// テーマの選択(0=ライト, 1=ダーク, 2=システム設定の使用)。設定ページの ComboBox が束縛する。読み取りは現在のテーマに対応する番号を返し、書き込みは対応するテーマへ切り替えて購読側へ通知する。
	public int ThemeIndex
	{
		get => _theme switch
		{
			ElementTheme.Light => 0,
			ElementTheme.Dark => 1,
			_ => 2,
		};
		set
		{
			ElementTheme theme = value switch
			{
				0 => ElementTheme.Light,
				1 => ElementTheme.Dark,
				_ => ElementTheme.Default,
			};

			if (_theme == theme)
			{
				return;
			}

			_theme = theme;
			OnPropertyChanged(nameof(ThemeIndex));
			OnPropertyChanged(nameof(Theme));
			ThemeChanged?.Invoke(this, EventArgs.Empty);
		}
	}




	// 現在の表示言語。"ja" / "en" / "system"。起動時の言語適用や保存に使う。
	public string Language => _language;




	// 表示言語の選択(0=日本語, 1=English, 2=システムに合わせる)。設定ページの ComboBox が束縛する。読み取りは現在の言語に対応する番号を返し、書き込みは対応する言語へ切り替えて購読側へ通知する。実際の表示切替は読み込み済み UI には及ばないため、購読側で再起動を促す。
	public int LanguageIndex
	{
		get => _language switch
		{
			"ja" => 0,
			"en" => 1,
			_ => 2,
		};
		set
		{
			string language = value switch
			{
				0 => "ja",
				1 => "en",
				_ => "system",
			};

			if (_language == language)
			{
				return;
			}

			_language = language;
			OnPropertyChanged(nameof(LanguageIndex));
			OnPropertyChanged(nameof(Language));
			LanguageChanged?.Invoke(this, EventArgs.Empty);
		}
	}




	// 現在の外観・表示設定を取り出す。設定の保存に使う。
	public AppearanceState CaptureState()
	{
		return new AppearanceState { Theme = ThemeToString(_theme), Language = _language };
	}




	// 設定ファイルの文字列をテーマへ解釈する。未知・未指定はシステム設定に従う(Default)とする。
	private static ElementTheme ParseTheme(string? value)
	{
		return value switch
		{
			"light" => ElementTheme.Light,
			"dark" => ElementTheme.Dark,
			_ => ElementTheme.Default,
		};
	}




	// テーマを設定ファイル用の文字列にする。
	private static string ThemeToString(ElementTheme theme)
	{
		return theme switch
		{
			ElementTheme.Light => "light",
			ElementTheme.Dark => "dark",
			_ => "system",
		};
	}




	// 設定ファイルの文字列を表示言語へ解釈する。未知・未指定はシステムに合わせる(system)とする。
	private static string ParseLanguage(string? value)
	{
		return value switch
		{
			"ja" => "ja",
			"en" => "en",
			_ => "system",
		};
	}




	public event PropertyChangedEventHandler? PropertyChanged;




	private void OnPropertyChanged(string name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
