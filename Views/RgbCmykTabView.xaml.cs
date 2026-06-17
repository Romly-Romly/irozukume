// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Globalization.NumberFormatting;
using Irozukume.Helpers;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「RGB/CMYK」タブの中身。色1(編集中)を R・G・B と C・M・Y・K の両系統のスライダーで編集する。CMYK は RGB から導出されるため、どちらを動かしても色1へ反映され、互いに追従する。
// 両群の下には独立した無彩色(灰)群としてスライダーを1本添え、つまむと R=G=B を同値へ揃えて脱色する。無彩色は3要素が連動するマクロのため、独立して動かせる R・G・B・C・M・Y・K の群とは分けて並べ、取り違えを避ける。
// 無彩色スライダーの値も 0–255 で、R・G・B と同じ単位コンボ(RgbUnitIndex)に従う。R・G・B のスライダーは常に 0–255 で扱い、R・G・B 共通の単位コンボボックスは数値入力欄の見せ方(0–255 / 00–FF / 0.0–1.0)だけを切り替える。
// 数値入力欄は選んだ単位に追従し、範囲・刻み・書式をコード側で組み替える。編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class RgbCmykTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// 数値入力欄をモデルに合わせて組み替えている最中か。組み替えに伴う NumberBox の値変化を、利用者の入力と取り違えてモデルへ書き戻さないために立てる。
	private bool _syncing;

	// 無彩色スライダーの値をモデルに合わせて流し込んでいる最中か。Gray は get(Rec.601 投影)と set(R=G=B 一括)が互いに逆関数でないため、R・G・B 変更に伴う Gray の再計算をスライダーへ流し込むと、双方向束縛なら書き戻されて脱色が起きる。これを立てて、流し込みに伴う ValueChanged を利用者の操作と取り違えてモデルへ書き戻さないようにする。
	private bool _graySliderSyncing;


	public RgbCmykTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// R・G・B の値と表示単位の変更で数値入力欄を組み替えるための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		SyncAllBoxes();
		SyncGraySlider();
	}




	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
	}




	// R・G・B のいずれかの値、または表示単位が変わったら数値入力欄を組み替える。単位の変更時は3つともまとめて組み替える。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(ColorEditorViewModel.R):
				SyncBox(RValueBox, ViewModel.R);
				break;

			case nameof(ColorEditorViewModel.G):
				SyncBox(GValueBox, ViewModel.G);
				break;

			case nameof(ColorEditorViewModel.B):
				SyncBox(BValueBox, ViewModel.B);
				break;

			case nameof(ColorEditorViewModel.Gray):
				SyncBox(GrayValueBox, ViewModel.Gray);
				SyncGraySlider();
				break;

			case nameof(ColorEditorViewModel.RgbUnitIndex):
				SyncAllBoxes();
				break;
		}
	}




	// R・G・B の数値入力欄を、現在の値と表示単位に合わせてまとめて組み替える。
	private void SyncAllBoxes()
	{
		SyncBox(RValueBox, ViewModel.R);
		SyncBox(GValueBox, ViewModel.G);
		SyncBox(BValueBox, ViewModel.B);
		SyncBox(GrayValueBox, ViewModel.Gray);
	}




	// 1つの数値入力欄を、現在の成分値(0–255)と表示単位に合わせて組み替える。単位ごとに範囲・刻み・書式を変え、値を単位の数値へ直して入れる。組み替え中は書き戻しを止め、範囲の切り替えで起きる値の丸めを利用者の入力と取り違えないようにする。
	private void SyncBox(NumberBox box, double channel)
	{
		_syncing = true;

		switch (ViewModel.RgbUnitIndex)
		{
			case 1:
				ConfigureBox(box, 0.0, 255.0, 1.0, 16.0, NumberFormatters.Hex, Math.Round(channel));
				break;
			case 2:
				ConfigureBox(box, 0.0, 1.0, 0.01, 0.1, NumberFormatters.TwoDecimal, channel / 255.0);
				break;
			default:
				ConfigureBox(box, 0.0, 255.0, 1.0, 16.0, NumberFormatters.Integer, Math.Round(channel));
				break;
		}

		_syncing = false;
	}




	// 数値入力欄の範囲・刻み・書式・値をまとめて設定する。範囲を先に整えてから値を入れることで、前の単位の値が新しい範囲へ丸められても最終的に正しい値で上書きする。
	private static void ConfigureBox(NumberBox box, double minimum, double maximum, double smallChange, double largeChange, INumberFormatter2 formatter, double value)
	{
		box.Minimum = minimum;
		box.Maximum = maximum;
		box.SmallChange = smallChange;
		box.LargeChange = largeChange;
		box.NumberFormatter = formatter;
		box.Value = value;
	}




	// 数値入力欄の値が利用者の操作で変わったら、現在の単位の数値を 0–255 の成分へ直してモデルへ反映する。どの成分かは Tag で判別する。組み替え中の変化や空欄(NaN)は無視する。
	private void OnRgbValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_syncing || double.IsNaN(sender.Value))
		{
			return;
		}

		double channel = ViewModel.RgbUnitIndex == 2 ? sender.Value * 255.0 : sender.Value;
		double rounded = Math.Round(channel);

		switch (sender.Tag as string)
		{
			case "R":
				ViewModel.R = rounded;
				break;

			case "G":
				ViewModel.G = rounded;
				break;

			case "B":
				ViewModel.B = rounded;
				break;

			case "Gray":
				ViewModel.Gray = rounded;
				break;
		}
	}




	// 無彩色スライダーのつまみを、現在の Gray(色1の Rec.601 投影)へ合わせる。流し込み中は書き戻しを止め、R・G・B の変更に伴う Gray の再計算が利用者の操作と取り違えられて脱色を起こすのを防ぐ。
	private void SyncGraySlider()
	{
		_graySliderSyncing = true;
		GraySlider.Value = ViewModel.Gray;
		_graySliderSyncing = false;
	}




	// 無彩色スライダーの値が利用者の操作で変わったら、その明るさへ R=G=B を一括で揃える(= 脱色)。流し込み中の変化や空欄(NaN)は無視し、R・G・B 変更に伴う追従では脱色しない。
	private void OnGraySliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		if (_graySliderSyncing || double.IsNaN(e.NewValue))
		{
			return;
		}

		ViewModel.Gray = Math.Round(e.NewValue);
	}
}
