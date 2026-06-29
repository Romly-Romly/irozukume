// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.ViewModels;

/// <summary>
/// 「画像から抽出」タブの状態。
/// 読み込んだ画像の画素標本から、選んだアルゴリズムと色数で代表色のパレットを起こし、スウォッチの一覧として見せる。
/// 各色はクリックで色1(編集中)へ反映でき、一覧をまとめてお気に入りへ保存できる。色制限が有効のときは各スウォッチへその警告を出す(色そのものは変えない)。
/// 画像の復号と抽出の実行(重い計算のバックグラウンド実行)は表示側が担い、ここは束縛する状態と結果のスウォッチの組み立て・お気に入りへの保存を持つ。
/// </summary>
public sealed class ImagePaletteViewModel : INotifyPropertyChanged
{
	/// <summary>
	/// 取り出す色数の下限・上限。サイドバーは5色までだが、お気に入りはより多くの色を持てるため、配色として扱いやすい範囲を広めに採る。
	/// </summary>
	public const int MinColors = 2;
	public const int MaxColors = 16;

	/// <summary>
	/// 色1への反映に使う、色編集の共有モデル。色制限(警告)の参照元でもある。
	/// </summary>
	private readonly ColorEditorViewModel _editor;

	/// <summary>
	/// お気に入りパレットの共有ストア。抽出した一覧をまとめて保存する先。
	/// </summary>
	private readonly FavoritePalettes _favorites;

	private string _algorithmKey = "kmeans";
	/// <summary>
	/// 既定の色数はサイドバー(色カード)の最大色数に揃える。抽出結果をそのままサイドバーへ移しやすい数を初期値にするため。
	/// </summary>
	private int _colorCount = ColorEditorViewModel.MaxColors;
	private bool _isExtracting;
	private bool _hasImage;




	public ImagePaletteViewModel(ColorEditorViewModel editor, FavoritePalettes favorites)
	{
		_editor = editor;
		_favorites = favorites;
	}




	/// <summary>
	/// 抽出結果のスウォッチ一覧。割り当て画素の割合の降順(主要色が先頭)で並ぶ。
	/// </summary>
	public ObservableCollection<PaletteSwatch> Items { get; } = new();




	/// <summary>
	/// 抽出アルゴリズムの識別子(コンボの Tag)。"median_cut" / "kmeans" / "octree" / "sa"。表示側のコンボが SelectedValue で双方向に束縛する。
	/// </summary>
	public string AlgorithmKey
	{
		get => _algorithmKey;
		set
		{
			string normalized = value ?? "kmeans";

			if (_algorithmKey == normalized)
			{
				return;
			}

			_algorithmKey = normalized;
			OnPropertyChanged(nameof(AlgorithmKey));
			OnPropertyChanged(nameof(SaSettingsVisibility));
		}
	}




	/// <summary>
	/// 取り出す色数。下限・上限へ収める。表示側のスライダーが書き込む。
	/// </summary>
	public int ColorCount
	{
		get => _colorCount;
		set
		{
			int clamped = Math.Clamp(value, MinColors, MaxColors);

			if (_colorCount == clamped)
			{
				return;
			}

			_colorCount = clamped;
			OnPropertyChanged(nameof(ColorCount));
		}
	}




	/// <summary>
	/// 抽出中か。重い計算の最中は操作子を抑え、進捗(スピナー)を見せる。
	/// </summary>
	public bool IsExtracting
	{
		get => _isExtracting;
		set
		{
			if (_isExtracting == value)
			{
				return;
			}

			_isExtracting = value;
			OnPropertyChanged(nameof(IsExtracting));
			OnPropertyChanged(nameof(IsIdle));
			OnPropertyChanged(nameof(CanExtract));
			OnPropertyChanged(nameof(CanSave));
			OnPropertyChanged(nameof(EmptyHintVisibility));
			OnPropertyChanged(nameof(ExtractingVisibility));
		}
	}




	/// <summary>
	/// 抽出していない(操作を受け付けられる)か。操作子の有効・無効に束縛する。
	/// </summary>
	public bool IsIdle => !_isExtracting;




	/// <summary>
	/// 抽出を実行できるか。画像を読み込んでいて、かつ抽出中でないとき。抽出ボタンの可否に束縛する。
	/// </summary>
	public bool CanExtract => _hasImage && !_isExtracting;




	/// <summary>
	/// 抽出結果があるか。空案内の出し分けと保存ボタンの可否に使う。
	/// </summary>
	public bool HasResult => Items.Count > 0;




	/// <summary>
	/// お気に入りへ保存できるか。結果があり、かつ抽出中でないとき。
	/// </summary>
	public bool CanSave => HasResult && !_isExtracting;




	/// <summary>
	/// スウォッチ一覧の表示・非表示。結果があるときだけ見せる。
	/// </summary>
	public Visibility SwatchListVisibility => HasResult ? Visibility.Visible : Visibility.Collapsed;




	/// <summary>
	/// 結果が無く抽出もしていないときに出す案内の表示・非表示。
	/// </summary>
	public Visibility EmptyHintVisibility => !HasResult && !_isExtracting ? Visibility.Visible : Visibility.Collapsed;




	/// <summary>
	/// 抽出中の進捗(スピナー)の表示・非表示。
	/// </summary>
	public Visibility ExtractingVisibility => _isExtracting ? Visibility.Visible : Visibility.Collapsed;




	/// <summary>
	/// 画像を読み込み済みか。プレビューと案内、×(閉じる)ボタンの出し分けに使う。表示側が読み込み・クリアで書き込む。
	/// </summary>
	public bool HasImage
	{
		get => _hasImage;
		set
		{
			if (_hasImage == value)
			{
				return;
			}

			_hasImage = value;
			OnPropertyChanged(nameof(HasImage));
			OnPropertyChanged(nameof(CanExtract));
			OnPropertyChanged(nameof(CloseButtonVisibility));
		}
	}




	/// <summary>
	/// ×(画像を閉じる)ボタンの表示・非表示。画像を読み込んでいるときだけ出す。
	/// </summary>
	public Visibility CloseButtonVisibility => _hasImage ? Visibility.Visible : Visibility.Collapsed;




	/// <summary>
	/// 現在選ばれているアルゴリズム。識別子から列挙へ読み替える。未知の識別子は k-means とみなす。
	/// </summary>
	public ImagePaletteAlgorithm CurrentAlgorithm => _algorithmKey switch
	{
		"median_cut" => ImagePaletteAlgorithm.MedianCut,
		"octree" => ImagePaletteAlgorithm.Octree,
		"sa" => ImagePaletteAlgorithm.SimulatedAnnealing,
		_ => ImagePaletteAlgorithm.KMeans,
	};




	/// <summary>
	/// 焼きなまし法の調整値の参照元。画像タブの詳細設定の各操作子がこの共有モデルのプロパティを直に束縛し、変更は settings.json へ保存される。
	/// </summary>
	public ColorEditorViewModel Editor => _editor;




	/// <summary>
	/// 焼きなまし法の詳細設定パネルの表示・非表示。焼きなまし法を選んでいるときだけ見せる。
	/// </summary>
	public Visibility SaSettingsVisibility => CurrentAlgorithm == ImagePaletteAlgorithm.SimulatedAnnealing ? Visibility.Visible : Visibility.Collapsed;




	/// <summary>
	/// お気に入りへ保存する際の既定の名前。連番で、既存のお気に入りの数の次を当てる。
	/// </summary>
	public string DefaultFavoriteName => Loc.Get("Favorite_DefaultNameFormat", _favorites.Items.Count + 1);




	/// <summary>
	/// スウォッチの色を色1(編集中)へ反映する。抽出色は常に不透明のため、不透明度は現在の値を保つ。
	/// </summary>
	public void Apply(PaletteSwatch swatch)
	{
		_editor.ApplyColor(swatch.Color.R, swatch.Color.G, swatch.Color.B, null);
	}




	/// <summary>
	/// 抽出結果からスウォッチの一覧を組み直す。
	/// 各色の表示名にはその色が占める割合(百分率)を当て、リストの右列に主要さが見えるようにする。色制限の警告は与えられた設定で当てる。
	/// </summary>
	public void PopulateFromExtraction(IReadOnlyList<ImagePaletteExtractor.ExtractedSwatch> result, SnapSettings snap)
	{
		Items.Clear();

		foreach (ImagePaletteExtractor.ExtractedSwatch swatch in result)
		{
			string label = (swatch.Weight * 100.0).ToString("0.#", CultureInfo.CurrentCulture) + "%";
			Items.Add(new PaletteSwatch(new NamedColor(label, swatch.Color.R, swatch.Color.G, swatch.Color.B), snap));
		}

		NotifyResultChanged();
	}




	/// <summary>
	/// 読み込んだ画像と結果を捨てて、最初の状態へ戻す。×(画像を閉じる)で呼ぶ。
	/// </summary>
	public void Reset()
	{
		Items.Clear();
		HasImage = false;
		NotifyResultChanged();
	}




	/// <summary>
	/// 色制限と参照テーマの変化に合わせて各スウォッチの警告を塗り替える。色は変えない。共有モデルでの切替時と、タブを表示に戻した時点で呼ぶ。
	/// </summary>
	public void RefreshWarnings(SnapSettings snap)
	{
		foreach (PaletteSwatch swatch in Items)
		{
			swatch.ApplyLimit(snap);
		}
	}




	/// <summary>
	/// 現在の抽出結果を1つのお気に入りパレットとして保存する。
	/// お気に入りに登録できるのはリストの先頭から最大 <see cref="ColorEditorViewModel.MaxColors"/> 色までで(復元先のサイドバーがその色数までのため)、それを超える分は保存しない。
	/// 抽出色はすべて不透明として収め、加えたパレットを返す。結果が空のときは何もせず null を返す。
	/// </summary>
	public FavoritePalette? SaveFavorite(string name)
	{
		if (Items.Count == 0)
		{
			return null;
		}

		int max = ColorEditorViewModel.MaxColors;
		var colors = new List<FavoriteColor>(Math.Min(Items.Count, max));

		for (int i = 0; i < Items.Count && i < max; i++)
		{
			PaletteSwatch swatch = Items[i];
			colors.Add(new FavoriteColor(swatch.Color.R, swatch.Color.G, swatch.Color.B, 0xFF));
		}

		return _favorites.Add(name, colors);
	}




	/// <summary>
	/// 結果の有無に依存する表示物の通知をまとめる。一覧を組み直したときに呼ぶ。
	/// </summary>
	private void NotifyResultChanged()
	{
		OnPropertyChanged(nameof(HasResult));
		OnPropertyChanged(nameof(CanSave));
		OnPropertyChanged(nameof(SwatchListVisibility));
		OnPropertyChanged(nameof(EmptyHintVisibility));
	}




	public event PropertyChangedEventHandler? PropertyChanged;




	private void OnPropertyChanged(string name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
