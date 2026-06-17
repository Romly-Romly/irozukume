// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.ViewModels;

// Palette タブの並び順。色相順・名前順に加え、コピー／貼り付け履歴パレットでは新しい順に並べる履歴順を持つ。
public enum PaletteSort
{
	Hue,
	Name,
	History,
}




// パレットの種類コンボに並べる項目。お気に入りと組み込みパレットの間へ区切り線を挟むため、表示名の項目と区切りの項目を同じ型で扱う。区切りは選択できない。
public sealed class PaletteComboItem
{
	public string Name { get; }

	public bool IsSeparator { get; }

	// 区切りは選択させない。ComboBoxItem の IsEnabled へ束縛する。
	public bool IsSelectable => !IsSeparator;




	public PaletteComboItem(string name, bool isSeparator)
	{
		Name = name;
		IsSeparator = isSeparator;
	}




	// 閉じたコンボの選択表示がテンプレートではなく文字列化で描かれた場合の保険。表示名を返す。
	public override string ToString()
	{
		return Name;
	}
}




// Palette タブの状態。
// 選べるパレット(コンボボックス)・現在のパレット・検索語・並び順を持ち、それらから表示するスウォッチの一覧を作る。
// コピー／貼り付け履歴も「自動で積まれるライブなパレット」の1つとして同じ仕組みで扱い、選択時は共有の履歴ストアを母集合にし、出所(貼り付け/コピー)での絞り込みと新しい順の並びを足す。
// 色を選ぶと色編集の共有モデルの色1へ反映し、履歴の色が不透明度を持つならアルファも反映する。色制限モードは共有モデルが持ち、その変化を受けて各スウォッチの警告を更新する。
public sealed class PaletteViewModel : INotifyPropertyChanged
{
	// 色1への反映と色制限モードの参照に使う、色編集の共有モデル。
	private readonly ColorEditorViewModel _editor;

	// コピー／貼り付け履歴の共有ストア。履歴パレットの母集合と、その自動更新の購読元になる。
	private readonly ColorHistory _history;

	// お気に入りパレットの共有ストア。「お気に入り」パレットの母集合 (1行=1お気に入りの行の一覧) と、保存・削除・名前変更・一括取得の操作元になる。
	private readonly FavoritePalettes _favorites;

	// 選べるパレットの一覧。組み込みパレット (名前付きカラー・ターミナル配色・フルパレット・履歴) に続けて「お気に入り」を1枠だけ並べる。お気に入りは履歴と同じく1枠で、中身 (各お気に入りの行) は選択時にストアから組む。
	private readonly List<ColorPalette> _palettes = new();

	// 現在のパレットの全色のスウォッチ。検索・出所フィルタで絞る前の母集合で、並べ替えと警告更新の対象になる。
	private readonly List<PaletteSwatch> _allSwatches = new();

	private int _selectedPaletteIndex;
	private string _searchText = "";
	private PaletteSort _sort = PaletteSort.Hue;
	private int _historyFilterIndex;

	// インデックス参照用の256色フルパレットの位置。このパレットだけは基本16色が参照テーマに追従するため、テーマ変更時に作り直す対象として覚えておく。
	private int _fullPaletteIndex;

	// コピー／貼り付け履歴パレットの位置。固定色を持たず、選択時に履歴ストアから母集合を都度組む。
	private int _historyPaletteIndex;

	// 「お気に入り」パレットの位置。固定色を持たず、選択時にお気に入りストアから行の一覧を都度組む。
	private int _favoritesPaletteIndex;

	// 現在の母集合を組んだときの参照テーマ。フルパレット表示中にテーマが変わったかの判定に使う。
	private TerminalTheme _loadedTheme;




	public PaletteViewModel(ColorEditorViewModel editor, ColorHistory history, FavoritePalettes favorites)
	{
		_editor = editor;
		_history = history;
		_favorites = favorites;

		RebuildPaletteList();

		// お気に入りの増減でコンボの一覧を組み直すための購読。寿命の長い共有ストアだが、VM とお気に入りストアは同じ寿命のため購読は解かない。
		_favorites.Items.CollectionChanged += OnFavoritesChanged;

		// 既定では先頭のお気に入り(空のことが多い)ではなく、その次の Web 名前付きカラーを開く。お気に入りはコンボの先頭に置くが、初期表示は従来どおり名前付きカラーにする。
		_selectedPaletteIndex = Math.Min(_favoritesPaletteIndex + 1, _palettes.Count - 1);
		LoadPalette(_selectedPaletteIndex);
	}




	// 選べるパレットの一覧を組む。先頭に「お気に入り」を置き、続けて Web の名前付きカラー・ターミナルの配色テーマ別16色(固定)・インデックス参照用の256色フルパレット(0-15 は参照テーマ追従)・コピー／貼り付け履歴を並べる。お気に入り・フルパレット・履歴は表示名だけ持たせ、中身は読み込み時に都度組む。お気に入りだけは1色1行のスウォッチではなく、1行=1お気に入り(色の帯+名前)の専用リストで見せる。コンボでは先頭のお気に入りと残りの組み込みパレットの間へ区切り線を挟む(ComboItems)。
	private void RebuildPaletteList()
	{
		_palettes.Clear();
		_favoritesPaletteIndex = _palettes.Count;
		_palettes.Add(new ColorPalette(Loc.Get("Palette_Favorites"), Array.Empty<NamedColor>()));
		_palettes.AddRange(NamedColorPalettes.All);
		_palettes.AddRange(TerminalColorPalettes.ThemePalettes);
		_fullPaletteIndex = _palettes.Count;
		_palettes.Add(new ColorPalette(TerminalColorPalettes.FullPaletteDisplayName, Array.Empty<NamedColor>()));
		_historyPaletteIndex = _palettes.Count;
		_palettes.Add(new ColorPalette(Loc.Get("Palette_History"), Array.Empty<NamedColor>()));
	}




	// 選べるパレットの一覧。コンボボックスの表示名はここから引く。
	public IReadOnlyList<ColorPalette> Palettes => _palettes;




	// コンボボックスに並べる項目。先頭にお気に入り、続けて区切り、その後に残りの組み込みパレットを並べる。区切りは選択できない見せ物の項目で、母集合(_palettes)には含めない。
	public IReadOnlyList<PaletteComboItem> ComboItems
	{
		get
		{
			var items = new List<PaletteComboItem>(_palettes.Count + 1)
			{
				new PaletteComboItem(_palettes[_favoritesPaletteIndex].DisplayName, false),
				new PaletteComboItem("", true),
			};

			for (int i = 0; i < _palettes.Count; i++)
			{
				if (i == _favoritesPaletteIndex)
				{
					continue;
				}

				items.Add(new PaletteComboItem(_palettes[i].DisplayName, false));
			}

			return items;
		}
	}




	// コンボの選択位置(区切りを1項目ぶん挟んだ位置)と、母集合の位置(_palettes の添字)を相互に変換する束縛用。コンボは [お気に入り, 区切り, 残り…] の並びで、お気に入りは添字0、区切りはコンボ位置1、残りはコンボ位置 = 母集合の添字 + 1。区切りが選ばれたとき(本来は選択不可)は無視する。
	public int SelectedComboIndex
	{
		get => _selectedPaletteIndex == _favoritesPaletteIndex ? 0 : _selectedPaletteIndex + 1;
		set
		{
			if (value <= 0)
			{
				SelectedPaletteIndex = _favoritesPaletteIndex;
				return;
			}

			if (value == 1)
			{
				return;
			}

			SelectedPaletteIndex = value - 1;
		}
	}




	// 履歴パレットの出所での絞り込みの選択肢。「すべての履歴」はすべて、「貼り付け履歴」は貼り付けと画面ピック、「コピー履歴」はコピーだけを見せる。
	public IReadOnlyList<string> HistoryFilterNames { get; } = new[] { Loc.Get("History_Filter_All"), Loc.Get("History_Filter_Paste"), Loc.Get("History_Filter_Copy") };




	// 現在表示中のスウォッチ。出所フィルタと検索で絞り、選んだ並び順に並べた結果で、リストが束縛する。お気に入りを選んでいるときは空にし、代わりに FavoriteRows を見せる。
	public ObservableCollection<PaletteSwatch> Items { get; } = new();




	// お気に入りを選んでいるときに見せる行の一覧。1行=1お気に入り(色の帯+名前)で、専用のリストが束縛する。検索語があれば名前で絞る。
	public ObservableCollection<FavoriteRowViewModel> FavoriteRows { get; } = new();




	// 選択中のパレット。変えると母集合を作り直し、現在の検索・並び順で一覧を組み直す。
	public int SelectedPaletteIndex
	{
		get => _selectedPaletteIndex;
		set
		{
			if (_selectedPaletteIndex == value || value < 0 || value >= Palettes.Count)
			{
				return;
			}

			_selectedPaletteIndex = value;
			OnPropertyChanged(nameof(SelectedPaletteIndex));
			OnPropertyChanged(nameof(SelectedComboIndex));
			OnPropertyChanged(nameof(IsHistorySelected));
			OnPropertyChanged(nameof(HistoryControlsVisibility));
			NotifyFavoritesSelectionChanged();
			LoadPalette(value);
		}
	}




	// クイック検索の語。前後の空白は落として持つ。変えると一覧を絞り直す。
	public string SearchText
	{
		get => _searchText;
		set
		{
			string normalized = (value ?? "").Trim();

			if (_searchText == normalized)
			{
				return;
			}

			_searchText = normalized;
			OnPropertyChanged(nameof(SearchText));

			if (IsFavoritesSelected)
			{
				RebuildFavoriteRows();
			}
			else
			{
				RebuildItems();
			}
		}
	}




	// 並び順。色相順・名前順・履歴順。変えると一覧を並べ直す。履歴順は履歴パレットでのみ意味を持つ。
	public PaletteSort Sort
	{
		get => _sort;
		set
		{
			if (_sort == value)
			{
				return;
			}

			_sort = value;
			OnPropertyChanged(nameof(Sort));
			RebuildItems();
		}
	}




	// 履歴パレットの出所での絞り込み。変えると一覧を絞り直す。履歴パレット以外では効かない。
	public int HistoryFilterIndex
	{
		get => _historyFilterIndex;
		set
		{
			if (_historyFilterIndex == value || value < 0 || value >= HistoryFilterNames.Count)
			{
				return;
			}

			_historyFilterIndex = value;
			OnPropertyChanged(nameof(HistoryFilterIndex));
			RebuildItems();
		}
	}




	// 現在の選択がコピー／貼り付け履歴パレットか。出所フィルタと履歴順の出し入れ、履歴ストアの自動更新の要否、空案内の表示判定に使う。
	public bool IsHistorySelected => _selectedPaletteIndex == _historyPaletteIndex;




	// 履歴パレット専用の操作 (出所フィルタ・履歴順) の表示・非表示。
	public Visibility HistoryControlsVisibility => IsHistorySelected ? Visibility.Visible : Visibility.Collapsed;




	// 履歴パレットが空のときに出す案内の表示・非表示。固定パレットでは検索で 0 件でも案内は出さない。一覧の組み直しで変わりうる。
	public Visibility EmptyHintVisibility => IsHistorySelected && Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;




	// 現在の選択が「お気に入り」パレットか。お気に入り専用のリスト・空案内の出し入れと、並び順コントロールの抑制に使う。
	public bool IsFavoritesSelected => _selectedPaletteIndex == _favoritesPaletteIndex;




	// お気に入りの行リストの表示・非表示。お気に入りを選んでいるときだけ見せる。
	public Visibility FavoritesListVisibility => IsFavoritesSelected ? Visibility.Visible : Visibility.Collapsed;




	// 1色1行のスウォッチリストの表示・非表示。お気に入りを選んでいるときは隠し、代わりにお気に入りの行リストを見せる。
	public Visibility SwatchListVisibility => IsFavoritesSelected ? Visibility.Collapsed : Visibility.Visible;




	// お気に入りが空のときに出す案内の表示・非表示。一覧の組み直しで変わりうる。
	public Visibility FavoritesEmptyHintVisibility => IsFavoritesSelected && FavoriteRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;




	// 並び順コントロールの表示・非表示。お気に入りは登録順で見せ、並び順は効かないため隠す。
	public Visibility SortVisibility => IsFavoritesSelected ? Visibility.Collapsed : Visibility.Visible;




	// 指定のお気に入りでサイドバーのパレットを復元する。サイドバーの色は丸ごと置き換わり、1段としてまとめて元に戻せる。
	public void RestoreFavorite(FavoritePalette favorite)
	{
		var colors = new List<(byte, byte, byte, byte)>(favorite.Colors.Count);

		foreach (FavoriteColor color in favorite.Colors)
		{
			colors.Add((color.R, color.G, color.B, color.A));
		}

		_editor.LoadColorsIntoSidebar(colors);
	}




	// 1色を色1(編集中)へ反映する。お気に入りの行の色の升をクリックしたときの、一色つまみ食いに使う。不透明度は現在の値を保つ。
	public void PickColor(byte r, byte g, byte b)
	{
		_editor.ApplyColor(r, g, b, null);
	}




	// 指定のお気に入りの名前を変える。行の表示名だけが変わり、色は変わらない。名前が空白だけのときは何もしない。
	public void RenameFavorite(FavoritePalette favorite, string name)
	{
		_favorites.Rename(favorite, name);
		RebuildFavoriteRows();
	}




	// 指定のお気に入りを削除する。元に戻せないため、呼び出し側で確認してから呼ぶ。削除は行リストの組み直しで反映する。
	public void DeleteFavorite(FavoritePalette favorite)
	{
		_favorites.Remove(favorite);
	}




	// お気に入りストアが増減したとき、お気に入りを表示中なら行リストを組み直す。表示していないときは次に開いたとき LoadPalette が組むため、空案内の更新だけ行う。
	private void OnFavoritesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		if (IsFavoritesSelected)
		{
			RebuildFavoriteRows();
		}
	}




	// お気に入りの行リストを、ストアの登録順で組み直す。検索語があれば名前で絞る。
	private void RebuildFavoriteRows()
	{
		FavoriteRows.Clear();

		foreach (FavoritePalette favorite in _favorites.Items)
		{
			if (_searchText.Length > 0 && !favorite.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			FavoriteRows.Add(new FavoriteRowViewModel(favorite));
		}

		OnPropertyChanged(nameof(FavoritesEmptyHintVisibility));
	}




	// 指定のパレットを選び、一覧を組み直して表示する。SelectedPaletteIndex の setter と違い、同じ位置でも読み直す。保存直後にお気に入りへ切り替えて見せるために使う。
	private void SelectPalette(int index)
	{
		index = Math.Clamp(index, 0, _palettes.Count - 1);
		_selectedPaletteIndex = index;
		OnPropertyChanged(nameof(SelectedPaletteIndex));
		OnPropertyChanged(nameof(SelectedComboIndex));
		OnPropertyChanged(nameof(IsHistorySelected));
		OnPropertyChanged(nameof(HistoryControlsVisibility));
		NotifyFavoritesSelectionChanged();
		LoadPalette(index);
	}




	// 「お気に入り」を選んでいるかに依存する表示物の通知をまとめる。選択の切り替え時に呼ぶ。
	private void NotifyFavoritesSelectionChanged()
	{
		OnPropertyChanged(nameof(IsFavoritesSelected));
		OnPropertyChanged(nameof(FavoritesListVisibility));
		OnPropertyChanged(nameof(SwatchListVisibility));
		OnPropertyChanged(nameof(FavoritesEmptyHintVisibility));
		OnPropertyChanged(nameof(SortVisibility));
	}




	// パレットの色を色1(編集中)へ反映する。履歴の色が不透明度を持つならアルファも反映し、持たない色は現在のアルファを保つ。各タブのスライダーや他の表色系の表示もこれに追従する。
	public void Apply(PaletteSwatch swatch)
	{
		_editor.ApplyColor(swatch.Color.R, swatch.Color.G, swatch.Color.B, swatch.HasAlpha ? swatch.Alpha : (byte?)null);
	}




	// 色制限と参照テーマの変化に合わせて表示を更新する。共有モデルでの切替時と、タブを表示に戻した時点で呼ぶ。フルパレットを表示中に参照テーマが変わったときは 0-15 の色が変わるため母集合から作り直し、それ以外は色は変わらないため各スウォッチの警告だけを更新する。
	public void RefreshWarnings()
	{
		if (_selectedPaletteIndex == _fullPaletteIndex && _editor.CurrentSnap.Theme != _loadedTheme)
		{
			LoadPalette(_selectedPaletteIndex);
			return;
		}

		SnapSettings snap = _editor.CurrentSnap;

		foreach (PaletteSwatch swatch in _allSwatches)
		{
			swatch.ApplyLimit(snap);
		}
	}




	// 履歴ストアが増減したとき、履歴パレットを表示中なら母集合を組み直す。タブを表示に戻した時点でも、非表示の間の増減へ追いつくために呼ぶ。
	public void ReloadHistory()
	{
		if (IsHistorySelected)
		{
			LoadPalette(_historyPaletteIndex);
		}
	}




	// 指定のパレットの全色からスウォッチの母集合を作り直し、現在の検索・並び順で一覧を組み直す。フルパレットは現在の参照テーマで都度組み、履歴パレットは共有の履歴ストアから新しい順で組む。お気に入りは1色1行のスウォッチではなく専用の行リストで見せるため、母集合は空にして行リストを組む。
	private void LoadPalette(int index)
	{
		_allSwatches.Clear();
		SnapSettings snap = _editor.CurrentSnap;
		_loadedTheme = snap.Theme;

		// お気に入りはスウォッチの母集合を使わず、専用の行リストを組む。スウォッチ一覧は空のままにする。
		if (index == _favoritesPaletteIndex)
		{
			RebuildFavoriteRows();
			RebuildItems();
			return;
		}

		if (index == _historyPaletteIndex)
		{
			int order = 0;

			foreach (ColorHistoryEntry entry in _history.Entries)
			{
				_allSwatches.Add(new PaletteSwatch(entry, order, snap));
				order++;
			}
		}
		else
		{
			IReadOnlyList<NamedColor> colors = index == _fullPaletteIndex
				? TerminalColorPalettes.FullPaletteColors(snap.Theme)
				: Palettes[index].Colors;

			foreach (NamedColor color in colors)
			{
				_allSwatches.Add(new PaletteSwatch(color, snap));
			}
		}

		// 履歴パレットは新しい順 (履歴順) を既定にし、固定パレットへ移ったときは履歴専用の履歴順を色相順へ戻す。色相順・名前順はどちらのパレットでも保つ。
		PaletteSort desired = index == _historyPaletteIndex
			? PaletteSort.History
			: (_sort == PaletteSort.History ? PaletteSort.Hue : _sort);

		if (_sort != desired)
		{
			_sort = desired;
			OnPropertyChanged(nameof(Sort));
		}

		RebuildItems();
	}




	// 母集合を選んだ並び順に並べ、履歴パレットでは出所フィルタで、いずれのパレットでも検索語で絞って表示用の一覧に詰め直す。
	private void RebuildItems()
	{
		Items.Clear();

		foreach (PaletteSwatch swatch in Sorted())
		{
			if (IsHistorySelected && !MatchesHistoryFilter(swatch))
			{
				continue;
			}

			if (Matches(swatch, _searchText))
			{
				Items.Add(swatch);
			}
		}

		OnPropertyChanged(nameof(EmptyHintVisibility));
	}




	// 履歴パレットの出所フィルタに合うか。「すべての履歴」はすべて、「貼り付け履歴」は貼り付けと画面ピック、「コピー履歴」はコピーだけを通す。出所フィルタは取り込み(貼り付け)と書き出し(コピー)の2区分で、画面ピックは取り込み側として貼り付けと同じ括りに入れる。リストの左アイコンが貼り付けと画面ピックを見分ける。
	private bool MatchesHistoryFilter(PaletteSwatch swatch)
	{
		return _historyFilterIndex switch
		{
			1 => swatch.Kind == HistoryKind.Paste || swatch.Kind == HistoryKind.Pick,
			2 => swatch.Kind == HistoryKind.Copy,
			_ => true,
		};
	}




	// 母集合を現在の並び順で並べる。履歴順は新しい順 (Order 昇順)。名前順は自然順(数字の塊を数値として比べる昇順)で、番号始まりのターミナルパレットはインデックス順に、CSS の色名は綴りの昇順になる。色相順は有彩色を色相→輝度で並べ、無彩色(灰・黒・白)を末尾へ輝度の暗い順から明るい順でまとめる。
	private IEnumerable<PaletteSwatch> Sorted()
	{
		if (_sort == PaletteSort.History)
		{
			return _allSwatches.OrderBy(s => s.Order);
		}

		if (_sort == PaletteSort.Name)
		{
			return _allSwatches.OrderBy(s => s.Name, NaturalStringComparer.Ordinal);
		}

		return _allSwatches
			.OrderBy(s => s.IsAchromatic)
			.ThenBy(s => s.IsAchromatic ? 0.0 : s.Hue)
			.ThenBy(s => s.Lightness);
	}




	// 検索語に当てはまるか。名前は部分一致または文字を飛ばし読みする曖昧一致、カラーコードは # を外した16進の部分一致。語が空ならすべて当てる。
	private static bool Matches(PaletteSwatch swatch, string query)
	{
		if (query.Length == 0)
		{
			return true;
		}

		// 名前は部分一致を常に当て、文字を飛ばし読みする曖昧一致は3文字以上のときだけ当てる。1–2文字では曖昧一致がほぼ全色に通ってしまい絞り込みにならないため。
		if (swatch.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| (query.Length >= 3 && IsSubsequence(swatch.Name, query)))
		{
			return true;
		}

		bool hadHash = query.StartsWith('#');
		string stripped = hadHash ? query.Substring(1) : query;

		// # だけの入力など16進の中身が無いときはカラーコード検索とみなさない。空文字の部分一致はすべての色に通ってしまうため。
		if (stripped.Length == 0)
		{
			return false;
		}

		// # 付き、または2文字以上の16進並びのときだけカラーコード検索とみなす。短い英字を16進と取り違えて拾いすぎないようにする。
		if (hadHash || (stripped.Length >= 2 && LooksLikeHex(stripped)))
		{
			string hex = swatch.HexText.TrimStart('#');

			if (hex.Contains(stripped, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}




	// query の各文字が text にこの順で現れるか。間に他の文字が挟まってもよい飛ばし読みの一致。大文字小文字は区別しない。
	private static bool IsSubsequence(string text, string query)
	{
		int qi = 0;

		foreach (char c in text)
		{
			if (qi >= query.Length)
			{
				break;
			}

			if (char.ToLowerInvariant(c) == char.ToLowerInvariant(query[qi]))
			{
				qi++;
			}
		}

		return qi == query.Length;
	}




	// 文字列が16進数字(0–9・a–f)だけで構成されているか。空文字は偽とする。
	private static bool LooksLikeHex(string s)
	{
		if (s.Length == 0)
		{
			return false;
		}

		foreach (char c in s)
		{
			bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

			if (!isHex)
			{
				return false;
			}
		}

		return true;
	}




	public event PropertyChangedEventHandler? PropertyChanged;




	private void OnPropertyChanged(string name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
