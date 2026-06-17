// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Irozukume.Helpers;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「アルファ」タブの中身。色1の不透明度を 0–255 の単一スライダーで編集する。
// スライダーの値は常に 0–255 で、単位コンボボックスは数値入力欄の見せ方(0–255 / 00–FF / 0–100% / 0.0–1.0)だけを切り替える。
// 数値入力欄は選んだ単位に追従し、範囲・刻み・書式・右に添える単位記号をコード側で組み替える。編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class AlphaTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// 数値入力欄をモデルに合わせて組み替えている最中か。組み替えに伴う NumberBox の値変化を、利用者の入力と取り違えてモデルへ書き戻さないために立てる。
	private bool _syncing;


	public AlphaTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 不透明度の値と表示単位の変更で数値入力欄を組み替えるための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		SyncAlphaBox();
	}




	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
	}




	// 不透明度そのもの、または表示単位が変わったら数値入力欄を組み替える。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorEditorViewModel.Alpha)
			|| e.PropertyName == nameof(ColorEditorViewModel.AlphaUnitIndex))
		{
			SyncAlphaBox();
		}
	}




	// 数値入力欄を、現在の不透明度(0–255)と表示単位に合わせて組み替える。単位ごとに範囲・刻み・書式・右の単位記号を変え、値を単位の数値へ直して入れる。組み替え中は書き戻しを止め、範囲の切り替えで起きる値の丸めを利用者の入力と取り違えないようにする。
	private void SyncAlphaBox()
	{
		_syncing = true;

		double alpha = ViewModel.Alpha;

		switch (ViewModel.AlphaUnitIndex)
		{
			case 1:
				ConfigureBox(0.0, 255.0, 1.0, 16.0, NumberFormatters.Hex, Math.Round(alpha), string.Empty);
				break;
			case 2:
				ConfigureBox(0.0, 100.0, 1.0, 10.0, NumberFormatters.Integer, Math.Round(alpha / 255.0 * 100.0), "%");
				break;
			case 3:
				ConfigureBox(0.0, 1.0, 0.01, 0.1, NumberFormatters.TwoDecimal, alpha / 255.0, string.Empty);
				break;
			default:
				ConfigureBox(0.0, 255.0, 1.0, 10.0, NumberFormatters.Integer, Math.Round(alpha), string.Empty);
				break;
		}

		_syncing = false;
	}




	// 数値入力欄の範囲・刻み・書式・値・単位記号をまとめて設定する。範囲を先に整えてから値を入れることで、前の単位の値が新しい範囲へ丸められても最終的に正しい値で上書きする。書式は10進(DecimalFormatter)と16進(HexByteNumberFormatter)の双方を受けられるよう INumberFormatter2 で受ける。
	private void ConfigureBox(double minimum, double maximum, double smallChange, double largeChange, Windows.Globalization.NumberFormatting.INumberFormatter2 formatter, double value, string suffix)
	{
		AlphaValueBox.Minimum = minimum;
		AlphaValueBox.Maximum = maximum;
		AlphaValueBox.SmallChange = smallChange;
		AlphaValueBox.LargeChange = largeChange;
		AlphaValueBox.NumberFormatter = formatter;
		AlphaValueBox.Value = value;
		AlphaUnitSuffix.Text = suffix;
	}




	// 数値入力欄の値が利用者の操作で変わったら、現在の単位の数値を 0–255 の不透明度へ直してモデルへ反映する。組み替え中の変化や空欄(NaN)は無視する。
	private void OnAlphaValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_syncing || double.IsNaN(sender.Value))
		{
			return;
		}

		double alpha = ViewModel.AlphaUnitIndex switch
		{
			2 => sender.Value / 100.0 * 255.0,
			3 => sender.Value * 255.0,
			_ => sender.Value,
		};

		ViewModel.Alpha = Math.Round(alpha);
	}
}
