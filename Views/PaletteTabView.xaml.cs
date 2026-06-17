// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Irozukume.Helpers;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「Palette」タブの中身。
// コンボボックスでリストの種類(Web の名前付きカラー・ターミナルの配色・コピー／貼り付け履歴)を選び、クイック検索で色名・カラーコードから絞り込み、2カラムのリストから色を選ぶ。
// コピー／貼り付け履歴を選んだときは出所(すべて/貼り付け/コピー)での絞り込みと新しい順の並びを足す。リストの行をクリックすると色1(編集中)へ反映し、履歴の色が不透明度を持つならアルファも反映する。
// 各タブは色1・色2を束ねる共有モデルを外部から受け取り、選択や色制限の表示はそれを介して他タブと連動する。色制限が有効のとき、色は元のまま表示しつつそのモードで表せない色に警告の三角を出す。
public sealed partial class PaletteTabView : UserControl
{
	// 色1への反映と色制限モードを扱う共有モデル。警告の更新のため、表示の間だけ変更を購読する。
	private readonly ColorEditorViewModel _editor;

	// コピー／貼り付け履歴の共有ストア。履歴パレットの自動更新のため、表示の間だけ変更を購読する。
	private readonly ColorHistory _history;

	public PaletteViewModel ViewModel { get; }




	public PaletteTabView(ColorEditorViewModel editor, ColorHistory history, FavoritePalettes favorites)
	{
		_editor = editor;
		_history = history;
		ViewModel = new PaletteViewModel(editor, history, favorites);
		this.InitializeComponent();

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 色制限の切替で警告を塗り替えるための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		_editor.PropertyChanged -= OnEditorPropertyChanged;
		_editor.PropertyChanged += OnEditorPropertyChanged;

		// 並び順を VM 起点でラジオへ反映するための購読。履歴選択時の履歴順への自動切替などを拾う。
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		// 履歴の増減で履歴パレットを組み直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有ストアへ購読を残さない。
		_history.Entries.CollectionChanged -= OnHistoryChanged;
		_history.Entries.CollectionChanged += OnHistoryChanged;

		// 非表示の間に色制限が変わっていたり、貼り付け・コピーで履歴が増えていることがあるため、表示に戻った時点で現状へ合わせる。
		ViewModel.RefreshWarnings();
		ViewModel.ReloadHistory();
		SyncSortRadios();
	}




	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		_editor.PropertyChanged -= OnEditorPropertyChanged;
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_history.Entries.CollectionChanged -= OnHistoryChanged;
	}




	private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap))
		{
			ViewModel.RefreshWarnings();
		}
	}




	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(PaletteViewModel.Sort))
		{
			SyncSortRadios();
		}
	}




	private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		ViewModel.ReloadHistory();
	}




	// VM の並び順に合わせてラジオの選択を揃える。履歴選択時に履歴順へ自動で切り替わったときなどに、表示も追従させる。既に選ばれているラジオを再度立てても VM 側は同値で素通りするため、ループにはならない。
	private void SyncSortRadios()
	{
		switch (ViewModel.Sort)
		{
			case PaletteSort.Name:
				SortNameRadio.IsChecked = true;
				break;
			case PaletteSort.History:
				SortHistoryRadio.IsChecked = true;
				break;
			default:
				SortHueRadio.IsChecked = true;
				break;
		}
	}




	// 並び順のラジオが切り替わったら VM へ伝える。どれが選ばれたかは Tag で判別する。名前付きフィールドは初期化の途中で未割り当てのことがあるため参照しない。初期選択(色相順)を XAML で与えたときの Checked もここに来るが、その時点で VM は構築済みのため同じ並びを設定するだけで害はない。
	private void OnSortChanged(object sender, RoutedEventArgs e)
	{
		if (sender is RadioButton radio && radio.Tag is string tag)
		{
			ViewModel.Sort = tag switch
			{
				"name" => PaletteSort.Name,
				"history" => PaletteSort.History,
				_ => PaletteSort.Hue,
			};
		}
	}




	// クイック検索の入力を VM へ渡す。絞り込みは VM 側で行う。
	private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
	{
		ViewModel.SearchText = sender.Text;
	}




	// リストの色がクリックされたら、その色を色1(編集中)へ反映する。ItemsRepeater は行要素へ DataContext を流さないため、テンプレートで Tag に束縛したスウォッチから取り出す。
	private void OnSwatchClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement element && element.Tag is PaletteSwatch swatch)
		{
			ViewModel.Apply(swatch);
		}
	}




	// お気に入りの行が実体化・再束縛されるたびに、色の升の帯を色数ぶんの等分で組み直す。升は色数が可変のため XAML のテンプレートでは組めず、ここで行ごとに作る。再利用された行へも毎回呼ばれるため、作り直しで前の行の升が残らない。
	private void OnFavoriteContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
	{
		if (args.InRecycleQueue || args.Phase != 0)
		{
			return;
		}

		if (args.ItemContainer?.ContentTemplateRoot is not Grid rowGrid || rowGrid.Children.Count == 0 || rowGrid.Children[0] is not Grid strip)
		{
			return;
		}

		if (args.Item is not FavoriteRowViewModel row)
		{
			return;
		}

		BuildColorStrip(strip, row);
	}




	// 1つのお気に入りの色を、等分カラムの升として帯へ流し込む。各升はクリックでその色を編集中の色へつまみ食いする。升の縁は色の境目を見せるためのもので、テーマの枠色が引けないときは縁なしにする。
	private void BuildColorStrip(Grid strip, FavoriteRowViewModel row)
	{
		strip.Children.Clear();
		strip.ColumnDefinitions.Clear();

		Brush? stroke = Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out object? value) && value is Brush brush
			? brush
			: null;

		for (int i = 0; i < row.Cells.Count; i++)
		{
			FavoriteCellViewModel cell = row.Cells[i];
			strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var swatch = new Border
			{
				Background = cell.SwatchBrush,
				BorderBrush = stroke,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(4),
			};

			var button = new Button
			{
				Tag = cell,
				Content = swatch,
				Padding = new Thickness(0),
				MinWidth = 0,
				MinHeight = 0,
				Margin = new Thickness(0, 6, 0, 6),
				Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
				BorderThickness = new Thickness(0),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Stretch,
			};
			button.Click += OnFavoriteCellClick;
			ToolTipService.SetToolTip(button, cell.HexText);
			Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, cell.HexText);
			Grid.SetColumn(button, i);
			strip.Children.Add(button);
		}
	}




	// お気に入りの行の色の升がクリックされたら、その色を編集中の色へつまみ食いする。升の Tag に束縛したセルから色を取り出す。一色だけの反映のため、サイドバーの状態は終了時に保存され、ここでの即時保存は行わない。
	private void OnFavoriteCellClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement element && element.Tag is FavoriteCellViewModel cell)
		{
			ViewModel.PickColor(cell.R, cell.G, cell.B);
		}
	}




	// お気に入りの行がダブルクリックされたら、選択中のお気に入りでパレットを復元する。単クリックは行の選択にとどめ、選択→削除・名前変更の最中に色が変わらないようにする。復元はサイドバーを丸ごと置き換えるが、元に戻すで戻せるため確認は挟まない。サイドバーの状態は終了時に保存されるため、ここでの即時保存は行わない。
	private void OnFavoritesDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if (FavoritesList.SelectedItem is FavoriteRowViewModel row)
		{
			ViewModel.RestoreFavorite(row.Source);
		}
	}




	// お気に入りリストでのキー操作。選択中の行に対し、Enter でパレットを復元・DEL で削除(確認あり)・F2 で名前変更を行う。
	private async void OnFavoritesKeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (FavoritesList.SelectedItem is not FavoriteRowViewModel row)
		{
			return;
		}

		if (e.Key == Windows.System.VirtualKey.Enter)
		{
			e.Handled = true;
			ViewModel.RestoreFavorite(row.Source);
		}
		else if (e.Key == Windows.System.VirtualKey.Delete)
		{
			e.Handled = true;
			await DeleteFavoriteAsync(row);
		}
		else if (e.Key == Windows.System.VirtualKey.F2)
		{
			e.Handled = true;
			await RenameFavoriteAsync(row);
		}
	}




	// お気に入りの行が右クリックされたら、復元・名前変更・削除のメニューをその場に出す。行のテンプレートで Tag に束縛した行から対象のお気に入りを取り出す。
	private void OnFavoriteRowRightTapped(object sender, RightTappedRoutedEventArgs e)
	{
		if (sender is not FrameworkElement element || element.Tag is not FavoriteRowViewModel row)
		{
			return;
		}

		var flyout = new MenuFlyout();

		var restore = new MenuFlyoutItem { Text = Loc.Get("Favorite_MenuRestore"), KeyboardAcceleratorTextOverride = Loc.Get("Favorite_RestoreShortcut") };
		restore.Click += (_, _) => ViewModel.RestoreFavorite(row.Source);

		var rename = new MenuFlyoutItem { Text = Loc.Get("Favorite_MenuRename"), KeyboardAcceleratorTextOverride = "F2" };
		rename.Click += async (_, _) => await RenameFavoriteAsync(row);

		var delete = new MenuFlyoutItem { Text = Loc.Get("Favorite_MenuDelete"), KeyboardAcceleratorTextOverride = "Del" };
		delete.Click += async (_, _) => await DeleteFavoriteAsync(row);

		flyout.Items.Add(restore);
		flyout.Items.Add(new MenuFlyoutSeparator());
		flyout.Items.Add(rename);
		flyout.Items.Add(delete);

		flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
	}




	// お気に入りの名前を変える。現在の名前を初期値に出し、確定したら一覧を更新して設定ファイルへも書き出す。右クリックメニューと F2 の双方から呼ぶ。
	private async Task RenameFavoriteAsync(FavoriteRowViewModel row)
	{
		string? name = await PromptForNameAsync(Loc.Get("Favorite_RenameDialogTitle"), row.Name);

		if (name is not null)
		{
			ViewModel.RenameFavorite(row.Source, name);
			PersistFavorites();
		}
	}




	// お気に入りを削除する。元に戻せないため確認を取り、合意が得られたら消して設定ファイルへも書き出す。右クリックメニューと DEL の双方から呼ぶ。
	private async Task DeleteFavoriteAsync(FavoriteRowViewModel row)
	{
		if (await ConfirmDeleteAsync(row.Name))
		{
			ViewModel.DeleteFavorite(row.Source);
			PersistFavorites();
		}
	}




	// お気に入りの削除前に確認を取る。元に戻せないため、合意が得られたときだけ真を返す。
	private async Task<bool> ConfirmDeleteAsync(string name)
	{
		var dialog = new ContentDialog
		{
			XamlRoot = this.XamlRoot,
			Title = Loc.Get("Favorite_DeleteDialogTitle"),
			Content = Loc.Get("Favorite_DeleteConfirmFormat", name),
			PrimaryButtonText = Loc.Get("Favorite_DeleteConfirm"),
			CloseButtonText = Loc.Get("Favorite_DialogCancel"),
			DefaultButton = ContentDialogButton.Close,
		};

		return await dialog.ShowAsync() == ContentDialogResult.Primary;
	}




	// 名前を入力するダイアログを出し、確定された名前を返す。取り消したときや空白だけのときは null を返す。初期値は全選択した状態で出し、すぐ書き換えられるようにする。
	private async Task<string?> PromptForNameAsync(string title, string initialName)
	{
		var textBox = new TextBox
		{
			Text = initialName,
			SelectionStart = 0,
			SelectionLength = initialName.Length,
		};

		var dialog = new ContentDialog
		{
			XamlRoot = this.XamlRoot,
			Title = title,
			Content = textBox,
			PrimaryButtonText = Loc.Get("Favorite_DialogSave"),
			CloseButtonText = Loc.Get("Favorite_DialogCancel"),
			DefaultButton = ContentDialogButton.Primary,
		};

		if (await dialog.ShowAsync() != ContentDialogResult.Primary)
		{
			return null;
		}

		string name = textBox.Text.Trim();
		return name.Length == 0 ? null : name;
	}




	// お気に入りの変更を終了を待たず設定ファイルへ書き出す。お気に入りは利用者の明示操作で増減するため、不意の終了で失わないよう即時に保存する。
	private static void PersistFavorites()
	{
		(Application.Current as App)?.PersistSettings();
	}
}
