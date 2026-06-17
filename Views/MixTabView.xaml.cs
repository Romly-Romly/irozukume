// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「Mix」タブの中身。サイドバーの色を任意の位置に置いた多点グラデーションを2次元平面で見せ、各色のポッチをドラッグして配置を変えられる。
// 右の縦並びで、混ぜる色空間(既定は知覚的に忠実な OKLCH)と平面の塗り広げ方(空間補間。既定は逆距離加重)を選び、自動アレンジでポッチを四隅・正多角形・縦一列・横一列へ整列させる。
// 平面のグラデーション画像は、平面の実寸と表示倍率に合わせてコードで用意する。編集対象の状態は色リストを束ねる共有モデルを外部から受け取る。
public sealed partial class MixTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// グラデーション画像を生成した際の画素サイズ(幅・高さ)・色空間・補間方式・色制限設定。同じ条件での作り直しを避けるために覚えておく。色そのものの変化は ColorsChanged で別に拾い、その都度作り直す。平面は非正方形に広がるため幅と高さを別々に覚える。
	private int _fieldWidthPx = -1;
	private int _fieldHeightPx = -1;
	private MixColorSpace _fieldSpace = (MixColorSpace)(-1);
	private MixInterpolation _fieldMethod = (MixInterpolation)(-1);
	private SnapSettings _fieldSnap;

	// 平面の一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinPadSide = 180.0;

	// グラデーション画像の画素数の上限。平面は滑らかな下地で、引き伸ばしても粗が目立たないため、大きな平面でも画素数を抑えてドラッグ中の塗り直しを軽くする。
	private const int MaxFieldPixels = 360;

	// コンボボックスを VM の値へ合わせる間、SelectionChanged が VM を上書きしないようにする。構築中の初期化を無視するため真で始める。
	private bool _comboSyncing = true;

	// つまみをドラッグ中か。ドラッグの間はグラデーションを固定して塗り直さず、編集色の反映が平面へ戻って色がドリフトする自己参照を断つ。
	private bool _thumbDragging;

	// つまみドラッグ開始時に固定したポッチ列(位置＋色)。ドラッグ中のサンプルはこの固定値から採り、編集色を変えても参照する平面は動かさない。
	private List<MixField.Stop>? _frozenStops;

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)を平面の一辺の算出に使う。
	private ScrollViewer? _scrollHost;


	public MixTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 復元済みの色空間・色相の回り方・補間方式・シャープネスを各操作子へ反映する。ここまでの SelectionChanged / ValueChanged は _comboSyncing で無視し、以降の操作だけ VM へ伝える。
		SpaceCombo.SelectedIndex = ViewModel.MixSpaceIndex;
		HueDirCombo.SelectedIndex = ViewModel.MixHueDirIndex;
		MethodCombo.SelectedIndex = ViewModel.MixMethodIndex;
		SharpnessSlider.Value = ViewModel.MixSharpness;
		_comboSyncing = false;

		Pad.PipMoved += OnPipMoved;
		Pad.ThumbMoved += OnThumbMoved;
		Pad.ThumbDragStarted += OnThumbDragStarted;
		Pad.ThumbDragEnded += OnThumbDragEnded;
		Pad.SizeChanged += OnPadSizeChanged;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// 色の値・並び・件数・色制限の変更で平面とポッチを作り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.ColorListChanged -= OnColorListChanged;
		ViewModel.ColorListChanged += OnColorListChanged;
		ViewModel.ColorsChanged -= OnColorsChanged;
		ViewModel.ColorsChanged += OnColorsChanged;
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		if (_scrollHost is null)
		{
			_scrollHost = FindScrollHost();

			if (_scrollHost is not null)
			{
				_scrollHost.SizeChanged += OnLayoutMetricChanged;
			}
		}

		UpdatePadSize();
		RebuildPips();
		Pad.SetThumb(ViewModel.MixThumbX, ViewModel.MixThumbY);
		RegenerateField();
	}




	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		if (_subscribedRoot is not null)
		{
			_subscribedRoot.Changed -= OnXamlRootChanged;
			_subscribedRoot = null;
		}

		ViewModel.ColorListChanged -= OnColorListChanged;
		ViewModel.ColorsChanged -= OnColorsChanged;
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
	}




	private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
	{
		RegenerateField(force: false);
	}




	// 色の並び・件数・アクティブが変わったら、ポッチを作り直して(番号・色・アクティブ強調)平面も塗り直す。
	private void OnColorListChanged(object? sender, EventArgs e)
	{
		RebuildPips();
		RegenerateField();
	}




	// いずれかの色の値が変わったら平面を塗り直し、ポッチの色も追従させる。ただしつまみドラッグ中は、編集色の反映でグラデーションが動いて自己参照のドリフトが起きるのを避けるため、塗り直さず固定したままにする(ドラッグ終了でまとめて塗り直す)。
	private void OnColorsChanged(object? sender, EventArgs e)
	{
		if (_thumbDragging)
		{
			return;
		}

		RebuildPips();
		RegenerateField();
	}




	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// 色制限(表示レンズ)が変わると平面の丸めが変わる。色空間・補間方式の変更は VM が ColorsChanged を流すため、ここでは扱わない。
		if (e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap))
		{
			RegenerateField();
		}
	}




	private void OnPadAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	private void OnLayoutMetricChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	private void OnPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateField(force: false);
	}




	// 平面を正方形に縛らず、領域いっぱい(エリアの幅最大・残りの可視高さ)へ広げる。可視高さはスクロール領域のビューポート高さを使う。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる。スクロール領域の中では縦が伸び放題になるため、高さは明示して与える。
	private void UpdatePadSize()
	{
		double available = _scrollHost?.ViewportHeight ?? LayoutRoot.ActualHeight;

		if (available <= 0.0)
		{
			return;
		}

		double padWidth = PadArea.ActualWidth;
		double padHeight = Math.Max(MinPadSide, available - 12.0);

		if (padWidth <= 0.0)
		{
			return;
		}

		if (Pad.Width != padWidth)
		{
			Pad.Width = padWidth;
		}

		if (Pad.Height != padHeight)
		{
			Pad.Height = padHeight;
		}
	}




	// サイドバーの色からポッチ群を作り直す。位置が未設定の色は正多角形へ配り、各ポッチの番号(1 始まり)・色・アクティブ強調を反映する。
	private void RebuildPips()
	{
		ViewModel.EnsureMixPositions(Pad.ActualWidth, Pad.ActualHeight);

		IReadOnlyList<SidebarColorViewModel> colors = ViewModel.Colors;
		var pips = new List<MixPad.Pip>(colors.Count);

		for (int i = 0; i < colors.Count; i++)
		{
			SidebarColorViewModel color = colors[i];
			pips.Add(new MixPad.Pip(color.MixX, color.MixY, color.Rgb, i + 1, i == ViewModel.ActiveColorIndex));
		}

		Pad.SetPips(pips);
	}




	// 現在の色・ポッチ位置・色空間・補間方式・色制限・平面の大きさ・表示倍率に合わせてグラデーション画像を作り直す。force が偽のとき(サイズ・表示倍率の変化だけ)は、画素サイズ・色空間・補間方式・色制限が前回と同じなら作り直さない。色やポッチ位置の変化では force を真にして必ず塗り直す。平面は非正方形に広がるため、画像も平面の縦横比に合わせて作る(長辺を上限画素数で頭打ちにする)。
	private void RegenerateField(bool force = true)
	{
		if (Pad.ActualWidth <= 0.0 || Pad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		double rawWidth = Pad.ActualWidth * scale;
		double rawHeight = Pad.ActualHeight * scale;
		double longer = Math.Max(rawWidth, rawHeight);

		if (longer <= 0.0)
		{
			return;
		}

		// 長辺が上限画素数を超えるときは、縦横比を保ったまま両辺を縮める。
		double cap = longer > MaxFieldPixels ? MaxFieldPixels / longer : 1.0;
		int pixelWidth = Math.Max(1, (int)Math.Round(rawWidth * cap));
		int pixelHeight = Math.Max(1, (int)Math.Round(rawHeight * cap));

		MixColorSpace fieldSpace = ViewModel.MixSpace;
		MixInterpolation fieldMethod = ViewModel.MixMethod;
		SnapSettings fieldSnap = ViewModel.CurrentSnap;

		if (!force && pixelWidth == _fieldWidthPx && pixelHeight == _fieldHeightPx && fieldSpace == _fieldSpace && fieldMethod == _fieldMethod && fieldSnap == _fieldSnap)
		{
			return;
		}

		List<MixField.Stop> stops = BuildStops();
		Pad.FieldImage = MixField.Create(pixelWidth, pixelHeight, stops, fieldMethod, fieldSpace, ViewModel.MixHueDirection, ViewModel.MixSharpness, fieldSnap);

		_fieldWidthPx = pixelWidth;
		_fieldHeightPx = pixelHeight;
		_fieldSpace = fieldSpace;
		_fieldMethod = fieldMethod;
		_fieldSnap = fieldSnap;
	}




	// ポッチがドラッグされたら、その色の位置を更新して平面だけ塗り直す。ポッチ自身の表示位置はコントロール側で動いているため、作り直さない。ポッチを掴んでも編集対象の色は変えない(編集する色はサイドバーで選ぶ)。
	private void OnPipMoved(int index, double x, double y)
	{
		ViewModel.SetMixPosition(index, x, y);
		RegenerateField();
	}




	// つまみのドラッグ開始でグラデーションを固定する。以後のサンプルはこの固定値から採り、編集色を変えても参照する平面は動かさない。
	private void OnThumbDragStarted()
	{
		_thumbDragging = true;
		_frozenStops = BuildStops();
	}




	// つまみのドラッグ終了で固定を解き、最新の色でポッチと平面を塗り直す。
	private void OnThumbDragEnded()
	{
		_thumbDragging = false;
		_frozenStops = null;
		RebuildPips();
		RegenerateField();
	}




	// つまみが動いたら、固定したグラデーションからその点の混色を採り、編集中の色へ反映してつまみ位置を覚える。固定値から採ることで、編集色の反映が平面へ戻って色がドリフトする自己参照を断つ。固定が無い場合(理論上ドラッグ外)は現在の色から採る。
	private void OnThumbMoved(double u, double v)
	{
		IReadOnlyList<MixField.Stop> stops = _frozenStops ?? BuildStops();
		Color sampled = MixField.Sample(stops, u, v, ViewModel.MixMethod, ViewModel.MixSpace, ViewModel.MixHueDirection, ViewModel.MixSharpness, Pad.ActualWidth, Pad.ActualHeight);
		ViewModel.SetActiveRgbFromMix(sampled.R, sampled.G, sampled.B);
		ViewModel.SetMixThumb(u, v);
	}




	// サイドバーの各色から、現在のポッチ位置と色でポッチ列(MixField の入力)を組む。位置が未設定の色は正多角形へ配ってから読む。
	private List<MixField.Stop> BuildStops()
	{
		ViewModel.EnsureMixPositions(Pad.ActualWidth, Pad.ActualHeight);

		IReadOnlyList<SidebarColorViewModel> colors = ViewModel.Colors;
		var stops = new List<MixField.Stop>(colors.Count);

		foreach (SidebarColorViewModel color in colors)
		{
			stops.Add(new MixField.Stop(color.MixX, color.MixY, color.Rgb));
		}

		return stops;
	}




	// 色空間の選択を VM へ伝える。構築中の初期化(_comboSyncing)は無視する。VM 側の変更通知(ColorsChanged)が平面の塗り直しを促す。
	private void OnSpaceChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_comboSyncing)
		{
			return;
		}

		ViewModel.MixSpaceIndex = SpaceCombo.SelectedIndex;
	}




	// 色相の回り方の選択を VM へ伝える。VM 側の変更通知(ColorsChanged)が平面の塗り直しを促す。
	private void OnHueDirChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_comboSyncing)
		{
			return;
		}

		ViewModel.MixHueDirIndex = HueDirCombo.SelectedIndex;
	}




	// 補間方式の選択を VM へ伝える。
	private void OnMethodChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_comboSyncing)
		{
			return;
		}

		ViewModel.MixMethodIndex = MethodCombo.SelectedIndex;
	}




	// シャープネスの値を VM へ伝える。VM 側の変更通知(ColorsChanged)が平面の塗り直しを促す。
	private void OnSharpnessChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		if (_comboSyncing)
		{
			return;
		}

		ViewModel.MixSharpness = SharpnessSlider.Value;
	}




	// 自動アレンジのメニュー項目を選んだら、全ポッチを指定の整列形へ並べ直し、ポッチと平面を作り直す。
	private void OnArrangeClick(object sender, RoutedEventArgs e)
	{
		if (sender is not MenuFlyoutItem item || item.Tag is not string tag)
		{
			return;
		}

		MixArrange preset = tag switch
		{
			"corners" => MixArrange.Corners,
			"column" => MixArrange.Column,
			"row" => MixArrange.Row,
			_ => MixArrange.Polygon,
		};

		ViewModel.AutoArrangeMix(preset, Pad.ActualWidth, Pad.ActualHeight);
		RebuildPips();
		RegenerateField();
	}




	// 視覚ツリーを上へ辿って、最初に見つかる祖先のスクロール領域を返す。タブの中身が包まれているスクロール領域を、束縛先を直接知らずに取得するために使う。
	private ScrollViewer? FindScrollHost()
	{
		DependencyObject? node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this);

		while (node is not null)
		{
			if (node is ScrollViewer scroller)
			{
				return scroller;
			}

			node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
		}

		return null;
	}
}
