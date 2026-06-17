// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Irozukume.ViewModels;

// サイドバーの色パネルへ重ねる、1つの色覚種別での見え方の行の状態。
// 塗り (不透明・透過プレビュー)・キャプションの文字色・行の可視を持ち、所有者 (ColorEditorViewModel) がシミュレーション済みの色を流し込む。色パネル (ColorSwatchPanel) の各行がこれを束縛する。
public sealed class ColorVisionRowViewModel : INotifyPropertyChanged
{
	private Brush? _fillBrush;
	private Brush? _alphaLightBrush;
	private Brush? _alphaDarkBrush;
	private Brush? _foregroundBrush;
	private Visibility _rowVisibility = Visibility.Collapsed;




	// 行の塗り (シミュレーション済みの不透明色)。
	public Brush? FillBrush => _fillBrush;




	// 透過プレビューの市松の明色 (下地)。市松の明色へ現在の不透明度で色を合成した結果を、その色覚での見え方へ変換したもの。
	public Brush? AlphaLightBrush => _alphaLightBrush;




	// 透過プレビューの市松の暗色 (升)。市松の暗色へ現在の不透明度で色を合成した結果を、その色覚での見え方へ変換したもの。
	public Brush? AlphaDarkBrush => _alphaDarkBrush;




	// キャプションの文字色。行の塗りに対して読みやすい黒か白。
	public Brush? ForegroundBrush => _foregroundBrush;




	// 行の可視。対応する表示トグルがオンの間だけ見せる。
	public Visibility RowVisibility => _rowVisibility;




	// 表示物 (塗り・透過プレビューの市松2色・文字色) をまとめて入れ替えて行を見せる。所有者がシミュレーション済みの色から作ったブラシを流し込む。
	public void Show(Brush fillBrush, Brush alphaLightBrush, Brush alphaDarkBrush, Brush foregroundBrush)
	{
		_fillBrush = fillBrush;
		_alphaLightBrush = alphaLightBrush;
		_alphaDarkBrush = alphaDarkBrush;
		_foregroundBrush = foregroundBrush;
		OnPropertyChanged(nameof(FillBrush));
		OnPropertyChanged(nameof(AlphaLightBrush));
		OnPropertyChanged(nameof(AlphaDarkBrush));
		OnPropertyChanged(nameof(ForegroundBrush));
		SetVisibility(Visibility.Visible);
	}




	// 行を隠す。表示トグルがオフの間はブラシの更新もしないため、隠すだけで足りる。
	public void Hide()
	{
		SetVisibility(Visibility.Collapsed);
	}




	private void SetVisibility(Visibility visibility)
	{
		if (_rowVisibility == visibility)
		{
			return;
		}

		_rowVisibility = visibility;
		OnPropertyChanged(nameof(RowVisibility));
	}




	public event PropertyChangedEventHandler? PropertyChanged;




	private void OnPropertyChanged(string name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
