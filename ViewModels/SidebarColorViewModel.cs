// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Irozukume.ViewModels;

// サイドバーの色1件分の状態。
// 素の RGB (フルカラー) と不透明度を真実の値として持ち、表示用のブラシ・16進表記は所有者 (ColorEditorViewModel) が色制限の丸めを掛けて流し込む。
// 色パネル (ColorSwatchPanel) がこれを束縛する。
public sealed class SidebarColorViewModel : INotifyPropertyChanged
{
	private bool _isActive;
	private Brush? _fillBrush;
	private Brush? _alphaFillBrush;
	private Brush? _foregroundBrush;
	private string _hexText = string.Empty;
	private bool _canDelete;
	private bool _canAdd;




	// 素の RGB。表示の丸めを焼き込まない値で、保存・スナップショット・入れ替えはこれを使う。表示は所有者が UpdateDisplay で流し込むため、変更通知は持たない。
	public Color Rgb { get; set; }




	// 不透明度 (0–255)。色と一緒に持ち、入れ替えや並べ替えでも色に付いて動く。
	public double Alpha { get; set; } = 255.0;




	// Mix タブでのポッチの位置(正規化 0–1・左上原点)。色に付いて動き、入れ替えや並べ替えでも保たれる。NaN は未設定で、Mix タブ側が初回に正多角形へ配る。
	public double MixX { get; set; } = double.NaN;
	public double MixY { get; set; } = double.NaN;




	// P型 (1型) での見え方の行。表示トグルがオンの間、パネルの下部に重ねる。
	public ColorVisionRowViewModel Protan { get; } = new();




	// D型 (2型) での見え方の行。
	public ColorVisionRowViewModel Deutan { get; } = new();




	// T型 (3型) での見え方の行。
	public ColorVisionRowViewModel Tritan { get; } = new();




	// 1色覚での見え方の行。
	public ColorVisionRowViewModel Monochromacy { get; } = new();




	// アクティブ (編集対象) か。アクティブなパネルはサイドバーで他の2倍の高さになる。
	public bool IsActive
	{
		get => _isActive;
		set
		{
			if (_isActive == value)
			{
				return;
			}

			_isActive = value;
			OnPropertyChanged(nameof(IsActive));
			OnPropertyChanged(nameof(ActiveRingVisibility));
		}
	}




	// アクティブを示すアクセント枠の可視。コントラストの役選択や Mix タブのポッチと同じく、編集対象の色をアクセント色の枠で示すために使う。
	public Visibility ActiveRingVisibility => _isActive ? Visibility.Visible : Visibility.Collapsed;




	// スウォッチの背景 (色制限の丸めを反映した不透明色)。
	public Brush? FillBrush => _fillBrush;




	// 透過プレビューの塗り (丸めを反映した色に現在の不透明度を載せたもの)。市松模様の上に重ねる。
	public Brush? AlphaFillBrush => _alphaFillBrush;




	// 16進表記の文字色。16進表記は透過プレビューの上に重なるため、市松と現在のアルファの色を合成した見え方に対して読みやすい黒か白。
	public Brush? ForegroundBrush => _foregroundBrush;




	// 16進表記 (丸めを反映した表示色)。
	public string HexText => _hexText;




	// 削除ボタンを押せるか。色が1つしか無い間は押せない。所有者が件数の変化に合わせて更新する。
	public bool CanDelete
	{
		get => _canDelete;
		set
		{
			if (_canDelete == value)
			{
				return;
			}

			_canDelete = value;
			OnPropertyChanged(nameof(CanDelete));
		}
	}




	// 追加ボタンを押せるか。色が上限に達している間は押せない。所有者が件数の変化に合わせて更新する。
	public bool CanAdd
	{
		get => _canAdd;
		set
		{
			if (_canAdd == value)
			{
				return;
			}

			_canAdd = value;
			OnPropertyChanged(nameof(CanAdd));
		}
	}




	// 表示物 (スウォッチ背景・透過プレビュー・文字色・16進表記) をまとめて入れ替えて通知する。所有者が色制限の丸めを掛けた結果を流し込む。
	public void UpdateDisplay(Brush fillBrush, Brush alphaFillBrush, Brush foregroundBrush, string hexText)
	{
		_fillBrush = fillBrush;
		_alphaFillBrush = alphaFillBrush;
		_foregroundBrush = foregroundBrush;
		_hexText = hexText;
		OnPropertyChanged(nameof(FillBrush));
		OnPropertyChanged(nameof(AlphaFillBrush));
		OnPropertyChanged(nameof(ForegroundBrush));
		OnPropertyChanged(nameof(HexText));
	}




	public event PropertyChangedEventHandler? PropertyChanged;




	private void OnPropertyChanged(string name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
