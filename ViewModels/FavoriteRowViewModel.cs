// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Irozukume.ViewModels;

// お気に入りのリストの1行分。1行が1つのお気に入りパレットに対応し、名前と色の升 (2〜5個) を並べる。
// 名前のクリックで一括取得、升のクリックで一色つまみ食い、右クリックで管理操作の宛先になるよう、元のお気に入りも保持する。
public sealed class FavoriteRowViewModel
{
	// この行が表すお気に入り。一括取得・名前変更・削除の対象になる。
	public FavoritePalette Source { get; }

	public string Name => Source.Name;

	// 行に並べる色の升。お気に入りの色の並びと同じ順。
	public IReadOnlyList<FavoriteCellViewModel> Cells { get; }




	public FavoriteRowViewModel(FavoritePalette source)
	{
		Source = source;

		var cells = new List<FavoriteCellViewModel>(source.Colors.Count);

		foreach (FavoriteColor color in source.Colors)
		{
			cells.Add(new FavoriteCellViewModel(color));
		}

		Cells = cells;
	}
}




// お気に入りの行に並ぶ色の升1つ分。塗りのブラシと、つまみ食いで編集中の色へ渡す素の RGB を持つ。表示は不透明で、不透明度は一括取得のときに復元する。
public sealed class FavoriteCellViewModel
{
	public byte R { get; }

	public byte G { get; }

	public byte B { get; }

	public Brush SwatchBrush { get; }

	// 読み上げ・ツールチップ用のカラーコード。
	public string HexText { get; }




	public FavoriteCellViewModel(FavoriteColor color)
	{
		R = color.R;
		G = color.G;
		B = color.B;
		SwatchBrush = new SolidColorBrush(Color.FromArgb(0xFF, color.R, color.G, color.B));
		HexText = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
	}
}
