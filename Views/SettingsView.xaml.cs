// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Irozukume.Helpers;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 設定ページの中身。ウィンドウ内で本体と切り替えて表示する。設定セクション群とアプリ情報を、ウィンドウ幅に応じて横並び/縦積みに切り替えるレスポンシブ配置にする。
// 編集対象の状態は色リストを束ねる共有モデルを外部から受け取り、メニューのショートカットと同じ値を双方向で参照する。
public sealed partial class SettingsView : UserControl
{
	// アプリ情報を右の側帯に置くか、設定の下へ回すかの境目(DIP)。これより狭いと縦積みにする。
	private const double NarrowThreshold = 960.0;

	// 直近で適用したレイアウトが狭い側か。未適用は null。同じ判定が続く間の無駄な再配置を避ける。
	private bool? _isNarrow;

	public ColorEditorViewModel ViewModel { get; }

	// アプリの外観設定(テーマ)。外観セクションのテーマ選択が束縛する。
	public AppearanceViewModel Appearance { get; }




	public SettingsView(ColorEditorViewModel viewModel, AppearanceViewModel appearance)
	{
		ViewModel = viewModel;
		Appearance = appearance;
		this.InitializeComponent();
	}




	// アプリ情報に表示する「アプリ名 バージョン」の文字列。バージョンは情報バージョン(AssemblyInformationalVersion)から組み立て、プレリリース表記(-alpha 等)も含める。数値のみの AssemblyVersion では接尾辞が落ちるため、文字列を丸ごと持つこちらを使う。
	public string AppNameVersionText
	{
		get
		{
			string appName = Loc.Get("AppName");
			string? informational = Assembly.GetExecutingAssembly()
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

			if (string.IsNullOrEmpty(informational))
			{
				return appName;
			}

			// ビルドメタデータ(+ 以降に付くコミットハッシュ等)は表示に不要なため落とす。
			int plus = informational.IndexOf('+');
			string version = plus >= 0 ? informational.Substring(0, plus) : informational;
			return $"{appName} {version}";
		}
	}




	// 上級者向け設定の「デフォルトに戻す」。スライダーつまみレンズの調整を既定値へ戻す。束縛中のトグル・スライダーも更新される。
	private void OnResetLensClick(object sender, RoutedEventArgs e)
	{
		ViewModel.ResetLensTuning();
	}




	// 上級者向け設定の「デフォルトに戻す」。画面カラーピッカーのレンズの調整を既定値へ戻す。束縛中のトグル・スライダーも更新される。
	private void OnResetScreenPickerClick(object sender, RoutedEventArgs e)
	{
		ViewModel.ResetScreenPickerTuning();
	}




	// 横幅に応じてアプリ情報の位置を切り替える。VisualState と AdaptiveTrigger による宣言的なリフローは閾値跨ぎで XAML 層の例外を招きやすいため、コードビハインドで明示的に再配置する。
	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		bool narrow = e.NewSize.Width < NarrowThreshold;

		if (_isNarrow == narrow)
		{
			return;
		}

		_isNarrow = narrow;

		if (narrow)
		{
			// 狭いとき: 右列を畳んで横幅を設定へ明け渡し、アプリ情報を設定の下(2行目の左列)へ回す。列間の余白も消す。
			AboutColumn.Width = new GridLength(0);
			LayoutGrid.ColumnSpacing = 0;
			Grid.SetRow(AboutPanel, 1);
			Grid.SetColumn(AboutPanel, 0);
		}
		else
		{
			// 広いとき: アプリ情報を右の側帯(1行目の右列)に置く。
			AboutColumn.Width = new GridLength(320);
			LayoutGrid.ColumnSpacing = 32;
			Grid.SetRow(AboutPanel, 0);
			Grid.SetColumn(AboutPanel, 1);
		}
	}
}
