// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「Lab」タブの中身。色1(編集中)を Lab の直交軸で編集する。
// 上段は左の縦レール(明度 L)・中央の a-b 平面パッド・右の縦並び設定(OKLab/CIE Lab 切替・色域制限トグル)で、パッドは色相環タブと同様にウィンドウへ追従して拡縮し、明度レールの高さもそれに揃える。
// 下段に a・b のスライダーを置き、パッドとどちらからでも操作できる。a-b 平面は sRGB 色域に収まる部分だけを実色で塗り、色域外はハッチで透かして可視化する。
// 平面は現在の明度と副モードに依って色と色域の形が変わるため、それらが変わるたびにコードで画像を作って差し込む。編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class LabTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// a-b 平面画像を生成した際の画素サイズ・明度・色制限設定・副モード・色域外の見せ方。同じ条件での作り直しを避けるために覚えておく。明度は未生成を表す NaN で初期化する。
	private int _planePixels = -1;
	private double _planeLightness = double.NaN;
	private SnapSettings _planeSnap;
	private int _planeSpaceIndex = -1;
	private GamutOutOfRangeStyle _planeStyle = (GamutOutOfRangeStyle)(-1);

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)をパッドの一辺の算出に使う。
	private ScrollViewer? _scrollHost;

	// パッドの一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinPadSide = 180.0;

	// 副モードのラジオを VM の復元値に合わせる間、SelectionChanged が VM を上書きしないようにする。構築中の初期化を無視するため真で始める。
	private bool _spaceSyncing = true;


	public LabTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 復元済みの副モードをラジオに反映する。ここまでの SelectionChanged は _spaceSyncing で無視し、以降の操作だけ VM へ伝える。
		SpaceSelector.SelectedIndex = ViewModel.LabSpaceIndex;
		_spaceSyncing = false;

		// a-b パッドの操作は両軸を二次元でまとめて扱う(色域内への半径方向の寄せ)ため、横・縦の個別束縛ではなく ValuesChanged からまとめて VM へ渡す。
		AbPad.ValuesChanged += OnAbPadValuesChanged;

		AbPad.SizeChanged += OnPadSizeChanged;

		// パッドの一辺はスクロール領域の可視高さからスライダー群と明度レール直下の数値欄の高さを引いて決めるため、それらの高さが変わったら算出し直す。可視高さの変化は読み込み後に見つけるスクロール領域の SizeChanged で、エリアの幅変化は PadArea の SizeChanged で拾う。
		SliderHost.SizeChanged += OnLayoutMetricChanged;
		LightnessValueBox.SizeChanged += OnLayoutMetricChanged;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 表示倍率(DPI)の変化で a-b 平面を作り直せるよう XamlRoot の変化を購読する。表示先が変わっていれば購読を張り替える。
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// 明度・副モード・色制限モードの変更で a-b 平面を塗り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		// タブの中身を包む祖先のスクロール領域を一度だけ見つけ、その可視高さの変化でパッドを算出し直せるようにする。
		if (_scrollHost is null)
		{
			_scrollHost = FindScrollHost();

			if (_scrollHost is not null)
			{
				_scrollHost.SizeChanged += OnLayoutMetricChanged;
			}
		}

		UpdatePadSize();
		RegeneratePlane();
	}




	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		if (_subscribedRoot is not null)
		{
			_subscribedRoot.Changed -= OnXamlRootChanged;
			_subscribedRoot = null;
		}

		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
	}




	private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
	{
		RegeneratePlane();
	}




	private void OnPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegeneratePlane();
	}




	// a-b 平面エリアの幅が変わったら、パッドの一辺を算出し直す。
	private void OnPadAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// 全体の高さ、スライダー群の高さ、明度の数値欄の高さのいずれかが変わったら、パッドの一辺を算出し直す。
	private void OnLayoutMetricChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// a-b パッドを正方形のまま、エリアの幅と、下に並ぶ要素を除いた残りの可視高さの小さい方へ合わせ、明度レールの高さもそれに揃える。上段の行を内容に収める高さにしているため、これでパッドは上端に詰まり、余白は下に集まる。可視高さはスクロール領域のビューポート高さから、下段のスライダー群・行間・明度レール直下の数値欄(行間と入力欄)を引いて求める。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる。パッドの大きさが変わると、それを受けた SizeChanged で平面画像が作り直される。
	private void UpdatePadSize()
	{
		double available = _scrollHost?.ViewportHeight ?? LayoutRoot.ActualHeight;

		if (available <= 0.0)
		{
			return;
		}

		double lightnessExtra = LightnessPanel.Spacing + LightnessValueBox.ActualHeight;
		double heightBudget = available - SliderHost.ActualHeight - LayoutRoot.RowSpacing - lightnessExtra;
		double side = Math.Min(PadArea.ActualWidth, Math.Max(MinPadSide, heightBudget));

		if (side <= 0.0)
		{
			return;
		}

		if (AbPad.Width != side)
		{
			AbPad.Width = side;
			AbPad.Height = side;
			LightnessSlider.Height = side;
		}
	}




	// 視覚ツリーを上へ辿って、最初に見つかる祖先のスクロール領域を返す。タブの中身が包まれているスクロール領域を、束縛先を直接知らずに取得するために使う。
	private ScrollViewer? FindScrollHost()
	{
		DependencyObject? node = VisualTreeHelper.GetParent(this);

		while (node is not null)
		{
			if (node is ScrollViewer scroller)
			{
				return scroller;
			}

			node = VisualTreeHelper.GetParent(node);
		}

		return null;
	}




	// a-b パッドを操作したら、両軸を二次元でまとめて VM へ渡す。色域制限オン時はカーソル位置を色相(a:b の比)を保って色域の縁へ寄せる処理が VM 側で行われる。
	private void OnAbPadValuesChanged(object? sender, EventArgs e)
	{
		ViewModel.SetLabPad(AbPad.XValue, AbPad.YValue);
	}




	// OKLab / CIE Lab の切り替え。選んだ副モードを VM へ伝える。構築中の初期化(_spaceSyncing)は無視する。
	private void OnSpaceChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_spaceSyncing)
		{
			return;
		}

		ViewModel.LabSpaceIndex = SpaceSelector.SelectedIndex;
	}




	// 副モードを OKLab へ切り替える。貼り付け連動から呼ぶ。SpaceSelector の選択を変えることで表示の切り替えを行う。既に OKLab のときは選択が変わらず何も起きない。
	public void ShowOklabMode()
	{
		SpaceSelector.SelectedIndex = 0;
	}




	// 副モードを CIE Lab へ切り替える。貼り付け連動から呼ぶ。SpaceSelector の選択を変えることで表示の切り替えを行う。既に CIE Lab のときは選択が変わらず何も起きない。
	public void ShowLabMode()
	{
		SpaceSelector.SelectedIndex = 1;
	}




	// 色1の明度・副モード・色制限設定が変わったら a-b 平面を塗り直す。a・b の値だけが変わったときは平面は変わらず、つまみ位置の束縛だけが動く。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorEditorViewModel.LabL)
			|| e.PropertyName == nameof(ColorEditorViewModel.LabSpaceIndex)
			|| e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap)
			|| e.PropertyName == nameof(ColorEditorViewModel.OutOfRangeStyle))
		{
			RegeneratePlane();
		}
	}




	// a-b 平面画像を、現在の明度・副モード・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・明度・設定・副モードなら作り直さない。色域外のハッチを等間隔に保つため、L-C 平面と同じく表示倍率そのままの実画素解像度で生成して等倍で表示する(縮小生成からの引き伸ばしは細いハッチ線をにじませて間隔を不均一に見せる)。
	private void RegeneratePlane()
	{
		double size = AbPad.ActualWidth;

		if (size <= 0.0 || AbPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(size * scale);

		if (pixels <= 0)
		{
			return;
		}

		double lightness = ViewModel.LabL;
		SnapSettings snap = ViewModel.CurrentSnap;
		int spaceIndex = ViewModel.LabSpaceIndex;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;

		if (pixels == _planePixels && lightness == _planeLightness && snap == _planeSnap && spaceIndex == _planeSpaceIndex && style == _planeStyle)
		{
			return;
		}

		_planePixels = pixels;
		_planeLightness = lightness;
		_planeSnap = snap;
		_planeSpaceIndex = spaceIndex;
		_planeStyle = style;

		LchSpace space = spaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;

		// 平面の生成は素の明度(表示尺度 0–100 ではなく表色系の尺度)で行う。表示尺度から素の尺度へ戻して渡す。
		double nativeLightness = lightness / 100.0 * LabColor.LMax(space);

		WriteableBitmap plane = AbPlane.Create(pixels, pixels, space, nativeLightness, snap, scale, style);
		PlaneImage.Source = plane;

		// ドラッグ中のレンズは、色域ハッチなど計算の込み入った平面を作り直さず、生成したこのビットマップをそのまま読んで映す。明度はパッドのドラッグ中一定のため、平面は据え置きで足りる。
		AbPad.LensColorSampler = new BitmapFieldSampler(plane, AbPad).Sample;
	}
}
