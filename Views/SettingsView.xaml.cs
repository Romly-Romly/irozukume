// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Irozukume.Helpers;
using Irozukume.Services;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 設定ページの中身。ウィンドウ内で本体と切り替えて表示する。左の NavigationView で大分類を選び、選んだ分類の中身だけを見せる。各分類の中身は名前付きの StackPanel として持ち、選択(Tag)に応じて表示を切り替える。編集対象の状態は色リストを束ねる共有モデルを外部から受け取り、メニューのショートカットと同じ値を双方向で参照する。
public sealed partial class SettingsView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// アプリの外観設定(テーマ)。外観セクションのテーマ選択が束縛する。
	public AppearanceViewModel Appearance { get; }




	public SettingsView(ColorEditorViewModel viewModel, AppearanceViewModel appearance)
	{
		ViewModel = viewModel;
		Appearance = appearance;
		this.InitializeComponent();

		// 初期表示は先頭の分類(全般)。選択を入れると SelectionChanged 経由で対応ページが出る。
		SettingsNav.SelectedItem = SettingsNav.MenuItems[0];
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




	// NavigationView の選択が変わったら、選ばれた分類(Tag)に対応するページだけを見せる。
	private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
	{
		if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
		{
			ShowPage(tag);
		}
	}




	// 指定した分類のページだけを表示し、他を畳む。
	private void ShowPage(string tag)
	{
		PageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
		PageDisplay.Visibility = tag == "display" ? Visibility.Visible : Visibility.Collapsed;
		PageScreenPicker.Visibility = tag == "screenpicker" ? Visibility.Visible : Visibility.Collapsed;
		PageLens.Visibility = tag == "lens" ? Visibility.Visible : Visibility.Collapsed;
		PageMaintenance.Visibility = tag == "maintenance" ? Visibility.Visible : Visibility.Collapsed;
		PageAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;

		// このアプリについてを開いたとき、診断情報を今の実行環境で組み直して流し込む。画面の拡大率は表示中のルート(XamlRoot)から取り、取得元が無ければその行は省かれる。
		if (tag == "about")
		{
			DiagnosticsTextBlock.Text = DiagnosticInfo.Build(this.XamlRoot?.RasterizationScale);
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




	// 設定の管理: 設定ファイルのある実行フォルダをエクスプローラーで開く。バックアップ等はそこから手動で行ってもらう。
	private void OnOpenSettingsFolderClick(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).OpenSettingsFolder();
	}




	// 設定の管理: 全設定を消して既定の状態で再起動する。取り返しがつかないため確認を挟み、承諾されたときだけ実行する。既定では取り消し側を選んでおき、誤って確定しないようにする。
	private async void OnResetSettingsClick(object sender, RoutedEventArgs e)
	{
		var dialog = new ContentDialog
		{
			XamlRoot = this.XamlRoot,
			Title = Loc.Get("SettingsResetDialogTitle"),
			Content = Loc.Get("SettingsResetDialogBody", Loc.Get("AppName")),
			PrimaryButtonText = Loc.Get("SettingsResetDialogConfirm"),
			CloseButtonText = Loc.Get("CommonCancel"),
			DefaultButton = ContentDialogButton.Close,
		};
		MarkDestructivePrimaryButton(dialog);

		if (await dialog.ShowAsync() == ContentDialogResult.Primary)
		{
			((App)Application.Current).ResetSettingsAndRestart();
		}
	}




	// 設定の管理: 設定ファイルを削除してアプリを終了する。アンインストール前の後始末用。取り返しがつかないため確認を挟み、承諾されたときだけ削除して終了する。既定では取り消し側を選んでおき、誤って確定しないようにする。
	private async void OnDeleteSettingsClick(object sender, RoutedEventArgs e)
	{
		var dialog = new ContentDialog
		{
			XamlRoot = this.XamlRoot,
			Title = Loc.Get("SettingsDeleteDialogTitle"),
			Content = Loc.Get("SettingsDeleteDialogBody"),
			PrimaryButtonText = Loc.Get("SettingsDeleteDialogConfirm"),
			CloseButtonText = Loc.Get("CommonCancel"),
			DefaultButton = ContentDialogButton.Close,
		};
		MarkDestructivePrimaryButton(dialog);

		if (await dialog.ShowAsync() == ContentDialogResult.Primary)
		{
			((App)Application.Current).DeleteSettingsAndExit();
		}
	}




	// 破壊的操作の確認ダイアログで、確定ボタンの文字色をシステムの危険色(赤)にして「元に戻せない操作」だと警告する。既定ボタンに割り当てたキャンセルはアクセント色のまま残し、安全側を強調する。赤はテーマ追従の SystemFillColorCriticalBrush を引き、ライト/ダーク/ハイコントラストで適切な赤になる。差し替えるのはボタンの前景リソース(通常・ホバー・押下)で、ダイアログのリソース辞書へ置くことで既定スタイルの確定ボタンだけに効く。キャンセルは AccentButtonStyle がテンプレート内の別リソースから前景を取るため影響を受けない。
	private static void MarkDestructivePrimaryButton(ContentDialog dialog)
	{
		var criticalBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
		dialog.Resources["ButtonForeground"] = criticalBrush;
		dialog.Resources["ButtonForegroundPointerOver"] = criticalBrush;
		dialog.Resources["ButtonForegroundPressed"] = criticalBrush;
	}




	// このアプリについての「診断情報」コピー。展開部に表示中の内容をそのままクリップボードへ写す。押下が伝わるよう、コピー後しばらくボタンの表示を「コピーしました」に変えて無効化し、その後元へ戻す。クリップボードへ書けない環境では何もしない。
	private async void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
	{
		try
		{
			var package = new DataPackage();
			package.SetText(DiagnosticsTextBlock.Text);
			Clipboard.SetContent(package);
		}
		catch
		{
			// クリップボードへ書き込めないときは何もしない。
			return;
		}

		if (sender is Button button)
		{
			object? original = button.Content;
			button.Content = Loc.Get("DiagnosticsCopied");
			button.IsEnabled = false;

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(1.5));
			}
			finally
			{
				button.Content = original;
				button.IsEnabled = true;
			}
		}
	}
}
