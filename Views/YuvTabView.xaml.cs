// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Controls;
using Irozukume.Models;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「YUV/YCbCr」タブの中身。色1(編集中)を YCbCr で編集する。上段は左の縦レール(輝度 Y)・中央の Cb-Cr 色差平面パッド・右の縦並び設定(YCbCr/YUV 切替・係数規格・量子化レンジ)で、パッドは色相環タブと同様にウィンドウへ追従して拡縮し、輝度レールの高さもそれに揃える。
// 下段に Cb・Cr のスライダーを置き、パッドとどちらからでも操作できる。色差平面は現在の輝度と符号化形式(規格・レンジ)に依って色が変わるため、それらが変わるたびにコードで画像を作って差し込む。
// 係数規格・量子化レンジ・YUV(符号付き)表記は切り替えられるが、いずれも色1の RGB は変えず数値の読み方とガモットの形だけを変える。RGB が真実の源で、他タブの編集にも追従する。編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class YuvTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// Cb-Cr 平面画像を生成した際の画素サイズ・輝度・符号化形式・色制限設定・色域外の見せ方。同じ条件での作り直しを避けるために覚えておく。輝度は未生成を表す NaN で初期化する。
	private int _planePixels = -1;
	private double _planeLuma = double.NaN;
	private YCbCrFormat _planeFormat;
	private SnapSettings _planeSnap;
	private GamutOutOfRangeStyle _planeStyle = (GamutOutOfRangeStyle)(-1);
	private bool _planeValid;

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)を色差平面パッドの一辺の算出に使う。
	private ScrollViewer? _scrollHost;

	// 色差平面パッドの一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinPadSide = 180.0;

	// 表示モード(YCbCr/YUV)のラジオを VM と同期する間、SelectionChanged が VM を上書きしないようにする。構築中の初期化を無視するため真で始める。
	private bool _modeSyncing = true;

	// Cb・Cr の数値入力欄をモデルに合わせて組み替えている最中か。組み替えに伴う NumberBox の値変化を利用者の入力と取り違えてモデルへ書き戻さないために立てる。
	private bool _chromaSyncing;


	public YuvTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 復元済みの表示モードをラジオに反映する。ここまでの SelectionChanged は _modeSyncing で無視し、以降の操作だけ VM へ伝える。
		ModeSelector.SelectedIndex = ViewModel.IsSignedMode ? 1 : 0;
		_modeSyncing = false;

		CbCrPad.SizeChanged += OnPadSizeChanged;

		// Cb-Cr パッドの操作は Cb・Cr を二次元でまとめて扱う(色域制限オン時の色域内への最近傍寄せ)ため、横・縦の個別束縛ではなく ValuesChanged からまとめて VM へ渡す。
		CbCrPad.ValuesChanged += OnCbCrPadValuesChanged;

		// パッドの一辺はスクロール領域の可視高さからスライダー群と輝度レール直下の数値欄の高さを引いて決めるため、それらの高さが変わったら算出し直す。可視高さの変化は読み込み後に見つけるスクロール領域の SizeChanged で、エリアの幅変化は PadArea の SizeChanged で拾う。
		SliderHost.SizeChanged += OnLayoutMetricChanged;
		LumaValueBox.SizeChanged += OnLayoutMetricChanged;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 表示倍率(DPI)の変化で色差平面を作り直せるよう XamlRoot の変化を購読する。表示先が変わっていれば購読を張り替える。
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// 輝度・符号化形式の変更で色差平面を塗り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
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
		SyncChromaBoxes();
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




	// Cb-Cr パッドを操作したら、Cb・Cr を二次元でまとめて VM へ渡す。色域制限オン時はカーソルに最も近い色域内の点へ寄せる処理が VM 側で行われる。
	private void OnCbCrPadValuesChanged(object? sender, EventArgs e)
	{
		ViewModel.SetYuvPad(CbCrPad.XValue, CbCrPad.YValue);
	}




	// 色差平面エリアの幅が変わったら、パッドの一辺を算出し直す。
	private void OnPadAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// 全体の高さ、スライダー群の高さ、輝度の数値欄の高さのいずれかが変わったら、パッドの一辺を算出し直す。
	private void OnLayoutMetricChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// 色差パッドを正方形のまま、エリアの幅と、下に並ぶ要素を除いた残りの可視高さの小さい方へ合わせ、輝度レールの高さもそれに揃える。上段の行を内容に収める高さにしているため、これでパッドは上端に詰まり、余白は下に集まる。可視高さはスクロール領域のビューポート高さから、下段のスライダー群・行間・輝度レール直下の数値欄(行間と入力欄)を引いて求める。輝度レール直下の数値欄は上段の行をパッドより縦に押し広げるため、その分も差し引く。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる(中身がビューポートを超え、スクロールバーが出る)。パッドの大きさが変わると、それを受けた SizeChanged で色差平面画像が作り直される。
	private void UpdatePadSize()
	{
		double available = _scrollHost?.ViewportHeight ?? LayoutRoot.ActualHeight;

		if (available <= 0.0)
		{
			return;
		}

		double lumaExtra = LumaPanel.Spacing + LumaValueBox.ActualHeight;
		double heightBudget = available - SliderHost.ActualHeight - LayoutRoot.RowSpacing - lumaExtra;
		double side = Math.Min(PadArea.ActualWidth, Math.Max(MinPadSide, heightBudget));

		if (side <= 0.0)
		{
			return;
		}

		if (CbCrPad.Width != side)
		{
			CbCrPad.Width = side;
			CbCrPad.Height = side;
			LumaSlider.Height = side;
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




	// YCbCr(0–255 中心 128)と YUV(符号付き 中心 0)の表示切替。色は変えず数値の読み方だけが変わる。構築中の初期化は無視する。
	private void OnModeChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_modeSyncing)
		{
			return;
		}

		ViewModel.IsSignedMode = ModeSelector.SelectedIndex == 1;
	}




	// 色1の輝度、符号化形式(規格・レンジ)、または色制限設定が変わったら色差平面を塗り直す。表示モード(YCbCr/YUV)の切替では平面は変わらず、数値の読み方だけが変わるため塗り直さない。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorEditorViewModel.Luma)
			|| e.PropertyName == nameof(ColorEditorViewModel.StandardIndex)
			|| e.PropertyName == nameof(ColorEditorViewModel.UseStudioRange)
			|| e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap)
			|| e.PropertyName == nameof(ColorEditorViewModel.OutOfRangeStyle))
		{
			RegeneratePlane();
		}

		// 色差の値そのものか、表記(YCbCr/符号付き YUV)が変わったら、Cb・Cr の数値入力欄を組み替える。係数規格・量子化レンジの変更でも色差の値は変わり、その通知(Cb・Cr)で拾われる。
		if (e.PropertyName == nameof(ColorEditorViewModel.Cb)
			|| e.PropertyName == nameof(ColorEditorViewModel.Cr)
			|| e.PropertyName == nameof(ColorEditorViewModel.IsSignedMode))
		{
			SyncChromaBoxes();
		}
	}




	// Cb-Cr 色差平面画像を、現在の輝度・符号化形式・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・輝度・形式・色制限設定なら作り直さない。
	private void RegeneratePlane()
	{
		double size = CbCrPad.ActualWidth;

		if (size <= 0.0 || CbCrPad.ActualHeight <= 0.0)
		{
			return;
		}

		// 色域外ハッチを LCH/Lab の平面と同じ太さ・間隔で見せるため、色差平面もそれらと同じく表示実寸と表示倍率に合わせた等倍で生成する。斜線のような高周波の細線は縮小・再拡大でぼけて周期も崩れるため、生成画素を頭打ちにせず実寸どおり描く。
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(size * scale);

		if (pixels <= 0)
		{
			return;
		}

		double luma = ViewModel.Luma;
		YCbCrFormat format = ViewModel.Format;
		SnapSettings snap = ViewModel.CurrentSnap;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;

		if (pixels == _planePixels && luma == _planeLuma && _planeValid && snap == _planeSnap && style == _planeStyle
			&& format.Standard == _planeFormat.Standard && format.FullRange == _planeFormat.FullRange)
		{
			return;
		}

		_planePixels = pixels;
		_planeLuma = luma;
		_planeFormat = format;
		_planeSnap = snap;
		_planeStyle = style;
		_planeValid = true;

		WriteableBitmap plane = CbCrPlane.Create(pixels, pixels, luma, format, snap, scale, style);
		PlaneImage.Source = plane;

		// ドラッグ中のレンズは、色域外のハッチや境界線など計算の込み入った色差平面を作り直さず、生成したこのビットマップをそのまま読んで映す。輝度はドラッグ中一定のため、平面は据え置きで足りる。
		CbCrPad.LensColorSampler = new BitmapFieldSampler(plane, CbCrPad).Sample;
	}




	// 青方向の色差スライダーのラベル。YUV 表記では U、YCbCr 表記では Cb。
	public string ChromaLabelB(bool signed)
	{
		return signed ? "U" : "Cb";
	}




	// 赤方向の色差スライダーのラベル。YUV 表記では V、YCbCr 表記では Cr。
	public string ChromaLabelR(bool signed)
	{
		return signed ? "V" : "Cr";
	}




	// Cb・Cr の数値入力欄を、現在の色差と表記に合わせて組み替える。YCbCr 表記では 0–255、符号付き YUV 表記では中心 128 を 0 とした符号付きの値で見せる。範囲を先に整えてから値を入れ、表記の切り替えで起きる丸めは組み替え中として書き戻さない。
	private void SyncChromaBoxes()
	{
		_chromaSyncing = true;

		bool signed = ViewModel.IsSignedMode;
		ConfigureChromaBox(CbValueBox, ViewModel.Cb01, signed);
		ConfigureChromaBox(CrValueBox, ViewModel.Cr01, signed);

		_chromaSyncing = false;
	}




	// 色差の数値入力欄ひとつを、束縛値(0–1)と表記から範囲・値を決めて設定する。スライダーと同じ 0–1 を源にし、0–255 へ直してから表記に応じて符号付きへ寄せる。
	private static void ConfigureChromaBox(NumberBox box, double value01, bool signed)
	{
		double code = value01 * 255.0;

		if (signed)
		{
			box.Minimum = -128.0;
			box.Maximum = 127.0;
			box.Value = Math.Round(code - 128.0);
		}
		else
		{
			box.Minimum = 0.0;
			box.Maximum = 255.0;
			box.Value = Math.Round(code);
		}
	}




	// 青方向の色差の数値入力欄が利用者の操作で変わったら、現在の表記の数値を 0–1 の束縛値へ直してモデルへ反映する。組み替え中の変化や空欄(NaN)は無視する。
	private void OnCbValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_chromaSyncing || double.IsNaN(sender.Value))
		{
			return;
		}

		ViewModel.Cb01 = ChromaCodeToValue01(sender.Value, ViewModel.IsSignedMode);
	}




	// 赤方向の色差の数値入力欄が利用者の操作で変わったら、現在の表記の数値を 0–1 の束縛値へ直してモデルへ反映する。組み替え中の変化や空欄(NaN)は無視する。
	private void OnCrValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_chromaSyncing || double.IsNaN(sender.Value))
		{
			return;
		}

		ViewModel.Cr01 = ChromaCodeToValue01(sender.Value, ViewModel.IsSignedMode);
	}




	// 数値入力欄の値(表記に応じた 0–255 または符号付き)を、束縛用の 0–1 へ直す。符号付き表記では中心の 128 を足してから割る。
	private static double ChromaCodeToValue01(double code, bool signed)
	{
		double code255 = signed ? code + 128.0 : code;
		return code255 / 255.0;
	}
}
