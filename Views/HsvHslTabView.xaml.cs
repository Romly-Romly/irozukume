// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「HSV/HSL」タブの中身。色1(編集中)を色相環(リング)と中央のパッドで編集する。
// 色相環は HSV・HSL で共通とし、モードで中央のパッド(HSV は彩度・明度の正方形、HSL は彩度・輝度の三角形)と数値表示だけを切り替える。
// 色相環の画像・三角形の画像・各パッドの大きさは、リングの実寸と表示倍率に合わせてコードで用意する。
// リングは正方形を前提とし(色相環画像をリング全面へ引き伸ばして貼るため)、本タブでは固定の正方サイズで配置する。編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class HsvHslTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// 色相環画像を生成した際の画素サイズ・帯の内外半径(画素)・色制限設定。同じ条件での作り直しを避けるために覚えておく。
	private int _wheelPixelWidth;
	private int _wheelPixelHeight;
	private double _wheelInnerRadius;
	private double _wheelOuterRadius;
	private SnapSettings _wheelSnap;

	// 三角形画像を生成した際の画素サイズ・色相・色制限設定・頂点の角丸半径(画素)。同じ条件での作り直しを避けるために覚えておく。色相と角丸半径は未生成を表す NaN で初期化する。
	private int _trianglePixels = -1;
	private double _triangleHue = double.NaN;
	private SnapSettings _triangleSnap;
	private double _triangleCornerRadius = double.NaN;

	// 彩度・明度パッドの段階化ビットマップを生成した際の画素サイズ・色相・色制限設定。色制限が有効なときだけ使う。同じ条件での作り直しを避けるために覚えておく。色相は未生成を表す NaN で初期化する。
	private int _svPixels = -1;
	private double _svHue = double.NaN;
	private SnapSettings _svSnap;

	// 白み・黒みパッドのビットマップを生成した際の画素サイズ・色相・色制限設定。同じ条件での作り直しを避けるために覚えておく。色相は未生成を表す NaN で初期化する。
	private int _hwbPixels = -1;
	private double _hwbHue = double.NaN;
	private SnapSettings _hwbSnap;

	// 中央のパッドの表示モード。既定は HSV(彩度・明度の正方形)。HSL は彩度・輝度の三角形、HWB は白み・黒みの正方形。
	private CenterMode _mode = CenterMode.Hsv;

	// 副モードのラジオを VM の復元値に合わせる間、SelectionChanged が VM を上書きしないようにする。構築中の初期化を無視するため真で始める。
	private bool _modeSyncing = true;

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)を色相環の一辺の算出に使う。
	private ScrollViewer? _scrollHost;

	// 色相環の一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinRingSide = 180.0;


	public HsvHslTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// ドラッグ中のレンズに映す色面の色を、各コントロールへサンプラーとして渡す。座標はコントロール局所(画素)で受ける。色制限が有効ならその丸めも掛け、表示と一致させる。各サンプラーは自分の色面に加えて、レンズが隣接領域を覗いたときに背後にあるものも映す(パッドのレンズは外側で色相環を、色相環のレンズは内側で中央パッドを)。互いの色面のみを参照する経路にしてあり、無限の相互参照は起きない。
		HueRing.LensColorSampler = SampleRingThroughLens;
		SvPad.LensColorSampler = SampleSvPad;
		SlPad.LensColorSampler = SampleSlPad;
		HwbPad.LensColorSampler = SampleHwbPad;

		// 復元済みの副モードをラジオに反映し、表示を整える。ここまでの SelectionChanged は _modeSyncing で無視し、以降の操作だけ VM へ伝える。
		ModeSelector.SelectedIndex = ViewModel.HsvSubModeIndex;
		_modeSyncing = false;
		ApplyMode();

		HueRing.SizeChanged += OnHueRingSizeChanged;

		// 色相環の一辺はスクロール領域の可視高さからスライダー群の高さを引いて決めるため、スライダー群の高さが変わったら算出し直す。可視高さの変化は読み込み後に見つけるスクロール領域の SizeChanged で、エリアの幅変化は RingArea の SizeChanged で拾う。
		SliderHost.SizeChanged += OnLayoutMetricChanged;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 表示倍率(DPI)の変化で色相環・三角形の画像を作り直せるよう XamlRoot の変化を購読する。表示先が変わっていれば購読を張り替える。
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// 色相・色制限モードの変更で中央パッドや色相環を塗り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		// タブの中身を包む祖先のスクロール領域を一度だけ見つけ、その可視高さの変化で色相環を算出し直せるようにする。
		if (_scrollHost is null)
		{
			_scrollHost = FindScrollHost();

			if (_scrollHost is not null)
			{
				_scrollHost.SizeChanged += OnLayoutMetricChanged;
			}
		}

		UpdateRingSize();
		UpdateRingVisuals();
		UpdateSvPadLayer();
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
		UpdateRingVisuals();
	}




	private void OnHueRingSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateRingVisuals();
	}




	// 色相環エリアの幅が変わったら、色相環の一辺を算出し直す。
	private void OnRingAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateRingSize();
	}




	// 全体の高さ、またはスライダー群の高さが変わったら、色相環の一辺を算出し直す。
	private void OnLayoutMetricChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateRingSize();
	}




	// 色相環を正方形のまま、エリアの幅と、スライダー群を除いた残りの可視高さの小さい方へ合わせる。上段の行を内容に収める高さにしているため、これで色相環は上端に詰まり、余白は下に集まる。可視高さはスクロール領域のビューポート高さからスライダー群の高さと行間を引いて求める。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる(中身がビューポートを超え、スクロールバーが出る)。色相環の大きさが変わると、それを受けた SizeChanged で色相環・三角形の画像と各パッドの寸法が作り直される。
	private void UpdateRingSize()
	{
		double available = _scrollHost?.ViewportHeight ?? LayoutRoot.ActualHeight;

		if (available <= 0.0)
		{
			return;
		}

		double heightBudget = available - SliderHost.ActualHeight - LayoutRoot.RowSpacing;
		double side = Math.Min(RingArea.ActualWidth, Math.Max(MinRingSide, heightBudget));

		if (side <= 0.0)
		{
			return;
		}

		if (HueRing.Width != side)
		{
			HueRing.Width = side;
			HueRing.Height = side;
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




	// 色1の色相、または色制限モードが変わったら中央パッドと色相環を塗り直す。色相が変わると、HSL モードでは三角形が、HSV モードでは(制限時のみ使う)彩度・明度ビットマップが色を変える。制限を切り替えると色相環・三角形・SV ビットマップの丸めが一斉に変わる。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorEditorViewModel.H))
		{
			RegenerateActiveCenter();
			return;
		}

		if (e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap))
		{
			UpdateRingVisuals();
			UpdateSvPadLayer();
		}
	}




	// HSV/HSL/HWB の切り替え。表示を入れ替え、選んだ副モードを保存用に VM へ伝える。構築中の初期化(_modeSyncing)は無視する。
	private void OnModeChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_modeSyncing)
		{
			return;
		}

		ApplyMode();
		ViewModel.HsvSubModeIndex = ModeSelector.SelectedIndex;
	}




	// ラジオの選択(0=HSV, 1=HSL, 2=HWB)に合わせて、中央のパッドとスライダー群の表示を入れ替え、選んだモードの中央画像を用意する。
	private void ApplyMode()
	{
		_mode = ModeSelector.SelectedIndex switch
		{
			1 => CenterMode.Hsl,
			2 => CenterMode.Hwb,
			_ => CenterMode.Hsv,
		};

		SvPad.Visibility = _mode == CenterMode.Hsv ? Visibility.Visible : Visibility.Collapsed;
		SlPad.Visibility = _mode == CenterMode.Hsl ? Visibility.Visible : Visibility.Collapsed;
		HwbPad.Visibility = _mode == CenterMode.Hwb ? Visibility.Visible : Visibility.Collapsed;
		HsvSliders.Visibility = _mode == CenterMode.Hsv ? Visibility.Visible : Visibility.Collapsed;
		HslSliders.Visibility = _mode == CenterMode.Hsl ? Visibility.Visible : Visibility.Collapsed;
		HwbSliders.Visibility = _mode == CenterMode.Hwb ? Visibility.Visible : Visibility.Collapsed;

		// 正規化トグルは HWB モード固有のため、そのときだけ見せる。
		NormalizeCheck.Visibility = _mode == CenterMode.Hwb ? Visibility.Visible : Visibility.Collapsed;

		// HSV は色制限時だけ下地ビットマップを使うため出し分けを更新し、HSL・HWB は常にビットマップを作り直す。
		if (_mode == CenterMode.Hsv)
		{
			UpdateSvPadLayer();
		}
		else
		{
			RegenerateActiveCenter();
		}
	}




	// 中央の副モードを HSL(彩度・輝度の三角形)へ切り替える。貼り付け連動から呼ぶ。ModeSelector の選択を変えることで、表示の切り替えと _mode の更新を OnModeChanged 経由でまとめて行う。既に HSL のときは選択が変わらず何も起きない。
	public void ShowHslMode()
	{
		ModeSelector.SelectedIndex = 1;
	}




	// 中央の副モードを HWB(白み・黒みの正方形)へ切り替える。貼り付け連動から呼ぶ。ModeSelector の選択を変えることで、表示の切り替えと _mode の更新を OnModeChanged 経由でまとめて行う。既に HWB のときは選択が変わらず何も起きない。
	public void ShowHwbMode()
	{
		ModeSelector.SelectedIndex = 2;
	}




	// 色相環画像と各パッドの大きさを、リングの実寸と表示倍率に合わせて更新する。彩度・明度の正方形パッドは回転しても内側の穴に収まるよう内円に内接する正方形(内径÷√2 の辺長)に、彩度・輝度の三角形パッドは内円に内接する三角形を収める正方形(内径)にする。色相環の画像は同じ条件なら作り直さない。
	private void UpdateRingVisuals()
	{
		double width = HueRing.ActualWidth;
		double height = HueRing.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		RingMetrics metrics = RingGeometry.Compute(width, height, HueRing.RingThickness, HueRing.ThumbDiameter);

		SvPad.Width = metrics.InnerRadius * Math.Sqrt(2.0);
		SvPad.Height = SvPad.Width;
		SlPad.Width = metrics.InnerRadius * 2.0;
		SlPad.Height = SlPad.Width;
		HwbPad.Width = metrics.InnerRadius * Math.Sqrt(2.0);
		HwbPad.Height = HwbPad.Width;

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixelWidth = (int)Math.Round(width * scale);
		int pixelHeight = (int)Math.Round(height * scale);
		double innerRadius = metrics.InnerRadius * scale;
		double outerRadius = metrics.OuterRadius * scale;

		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;

		// 画素サイズ・帯の内外半径・色制限設定が前回と同じなら作り直さない。リング太さやつまみ径が変わって帯が動いた場合も拾えるよう、半径も判定の鍵に含める。
		if (pixelWidth != _wheelPixelWidth || pixelHeight != _wheelPixelHeight || innerRadius != _wheelInnerRadius || outerRadius != _wheelOuterRadius || snap != _wheelSnap)
		{
			_wheelPixelWidth = pixelWidth;
			_wheelPixelHeight = pixelHeight;
			_wheelInnerRadius = innerRadius;
			_wheelOuterRadius = outerRadius;
			_wheelSnap = snap;

			WriteableBitmap wheel = HueWheel.Create(pixelWidth, pixelHeight, innerRadius, outerRadius, snap);
			HueRing.TrackBrush = new ImageBrush
			{
				ImageSource = wheel,
				Stretch = Stretch.Fill,
			};
		}

		RegenerateActiveCenter();
	}




	// 彩度・輝度の三角形画像を、現在の色相・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズと色相なら作り直さない。三角形パッドが非表示でも作っておけるよう、大きさはパッドの実寸ではなくリングの幾何から求める。
	private void RegenerateTriangle()
	{
		if (HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0)
		{
			return;
		}

		RingMetrics metrics = RingGeometry.Compute(HueRing.ActualWidth, HueRing.ActualHeight, HueRing.RingThickness, HueRing.ThumbDiameter);
		double box = metrics.InnerRadius * 2.0;
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(box * scale);

		if (pixels <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		double cornerRadius = SlPad.VertexCornerRadius * scale;

		if (pixels == _trianglePixels && ViewModel.H == _triangleHue && snap == _triangleSnap && cornerRadius == _triangleCornerRadius)
		{
			return;
		}

		_trianglePixels = pixels;
		_triangleHue = ViewModel.H;
		_triangleSnap = snap;
		_triangleCornerRadius = cornerRadius;
		TriangleImage.Source = HslTriangle.Create(pixels, pixels, ViewModel.H, snap, cornerRadius);
	}




	// 彩度・明度パッドの下地を、滑らかな XAML グラデーションと段階化ビットマップのどちらで見せるか切り替える。色制限が有効で HSV モードのときだけビットマップを使い、その際は画像を用意する。
	private void UpdateSvPadLayer()
	{
		bool useImage = ViewModel.LimitMode != ColorLimitMode.None && _mode == CenterMode.Hsv;
		SvImage.Visibility = useImage ? Visibility.Visible : Visibility.Collapsed;
		SvGradientLayer.Visibility = useImage ? Visibility.Collapsed : Visibility.Visible;

		if (useImage)
		{
			RegenerateSvPad();
		}
	}




	// 彩度・明度パッドの段階化ビットマップを、現在の色相・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。色制限なし、または HSL モードのときは使わないため何もしない。同じ画素サイズ・色相・設定なら作り直さない。三角形と同様、大きさはパッドの実寸ではなくリングの幾何から求める。
	private void RegenerateSvPad()
	{
		if (ViewModel.LimitMode == ColorLimitMode.None || _mode != CenterMode.Hsv)
		{
			return;
		}

		if (HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0)
		{
			return;
		}

		RingMetrics metrics = RingGeometry.Compute(HueRing.ActualWidth, HueRing.ActualHeight, HueRing.RingThickness, HueRing.ThumbDiameter);
		double box = metrics.InnerRadius * Math.Sqrt(2.0);
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(box * scale);

		if (pixels <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;

		if (pixels == _svPixels && ViewModel.H == _svHue && snap == _svSnap)
		{
			return;
		}

		_svPixels = pixels;
		_svHue = ViewModel.H;
		_svSnap = snap;
		SvImage.Source = SvPlane.Create(pixels, pixels, ViewModel.H, snap);
	}




	// 白み・黒みパッドのビットマップを、現在の色相・パッドの大きさ・表示倍率・色制限設定に合わせて作り直す。HWB は純色・白・黒の3頂点の加法混合のため、滑らかな下地もこのビットマップで賄い、HSV と違って色制限の有無に依らず常に作る。同じ画素サイズ・色相・設定なら作り直さない。大きさは正方形パッドの実寸ではなくリングの幾何(内円に内接する正方形)から求める。
	private void RegenerateHwbPlane()
	{
		if (HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0)
		{
			return;
		}

		RingMetrics metrics = RingGeometry.Compute(HueRing.ActualWidth, HueRing.ActualHeight, HueRing.RingThickness, HueRing.ThumbDiameter);
		double box = metrics.InnerRadius * Math.Sqrt(2.0);
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(box * scale);

		if (pixels <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;

		if (pixels == _hwbPixels && ViewModel.H == _hwbHue && snap == _hwbSnap)
		{
			return;
		}

		_hwbPixels = pixels;
		_hwbHue = ViewModel.H;
		_hwbSnap = snap;
		HwbImage.Source = HwbPlane.Create(pixels, pixels, ViewModel.H, snap);
	}




	// 現在のモードの中央パッドを塗り直す。HSL は彩度・輝度の三角形、HWB は白み・黒みの正方形、HSV は色制限時のみ使う彩度・明度ビットマップ。
	private void RegenerateActiveCenter()
	{
		switch (_mode)
		{
			case CenterMode.Hsl:
				RegenerateTriangle();
				break;

			case CenterMode.Hwb:
				RegenerateHwbPlane();
				break;

			default:
				RegenerateSvPad();
				break;
		}
	}




	// 色相環レンズのサンプラー。コントロール局所座標(画素)の点が帯の上にあれば、その角度の色相を全彩度・全明度で返す。帯の内側の穴・外側は透明。色制限が有効ならその丸めも掛ける。角度・帯の幾何は HueWheel.Create と同じ規約(RingGeometry)で求め、表示と一致させる。
	private Color SampleHueRing(double x, double y)
	{
		double width = HueRing.ActualWidth;
		double height = HueRing.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		RingMetrics metrics = RingGeometry.Compute(width, height, HueRing.RingThickness, HueRing.ThumbDiameter);
		double dx = x - metrics.Center.X;
		double dy = y - metrics.Center.Y;
		double radius = Math.Sqrt((dx * dx) + (dy * dy));

		if (radius < metrics.InnerRadius || radius > metrics.OuterRadius)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double hue = RingGeometry.ValueFromPoint(dx, dy);
		(byte r, byte g, byte b) = ColorConversion.HsvToRgb(hue, 1.0, 1.0);
		SnapSettings snap = ViewModel.CurrentSnap;

		if (snap.Mode != ColorLimitMode.None)
		{
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
		}

		return Color.FromArgb(0xFF, r, g, b);
	}




	// パッドの外側をレンズで覗いたとき、その点の背後にあるリング(色相環)の色を返す。パッド局所座標をリング局所座標へ写し、色相環のサンプラーで帯の色を引き、その上に静止中のつまみを重ねる。リングの帯の外は透明。パッドはリングと同心・同倍率のため、ガラスレンズの拡大・歪曲がパッドの色面とリング(つまみ含む)へ一様に掛かり、境目で不自然に途切れない。
	private Color SampleRingBehindPad(double padWidth, double padHeight, double padRotation, double x, double y)
	{
		if (HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		Point ring = RingGeometry.PadPointToRing(padWidth, padHeight, HueRing.ActualWidth, HueRing.ActualHeight, padRotation, x, y);
		Color band = SampleHueRing(ring.X, ring.Y);
		return HueRing.SampleThumbOverlay(band, ring.X, ring.Y);
	}




	// 色相環のレンズのサンプラー(合成)。重ね順は奥から「帯 → 中央パッドの色面 → そのつまみ」。まず帯(または帯の外は透明)を求め、その上に中央の現在のパッドの色面とつまみを重ねる。つまみは色面の縁から帯側へはみ出すため、つまみを最前面にして帯に隠れないようにする。色相環そのものをドラッグ中はつまみがレンズへ置き換わるため、ここでは色相環自身のつまみは描かない。参照するのはパッドの色面のみ(SampleRingBehindPad とは逆向き)で、相互参照のループにはならない。
	private Color SampleRingThroughLens(double x, double y)
	{
		Color result = SampleHueRing(x, y);
		return OverlayActivePad(result, x, y);
	}




	// 帯の色 under の上に、現在のモードの中央パッドの色面とつまみを重ねる。
	private Color OverlayActivePad(Color under, double x, double y)
	{
		return _mode switch
		{
			CenterMode.Hsl => OverlayPad(under, SlPad, SlPad.PadRotation, SampleSlField, SlPad.SampleThumbOverlay, x, y),
			CenterMode.Hwb => OverlayPad(under, HwbPad, HwbPad.PadRotation, SampleHwbField, HwbPad.SampleThumbOverlay, x, y),
			_ => OverlayPad(under, SvPad, SvPad.PadRotation, SampleSvField, SvPad.SampleThumbOverlay, x, y),
		};
	}




	// 指定のパッドの色面とつまみを、帯の色 under の上へ重ねる。リング局所座標をパッド局所座標へ写し、色面サンプラー field(パッド内のみ)で色面を引いて(あれば帯の上へ重ね)、その上にパッドのつまみ描き込み thumbOverlay を掛ける。色面は帯の内側にあり帯とは重ならないが、つまみは色面の縁から帯側へはみ出すため、最前面に置いて帯に隠れないようにする。
	private Color OverlayPad(Color under, FrameworkElement pad, double padRotation, Func<double, double, Color> field, Func<Color, double, double, Color> thumbOverlay, double x, double y)
	{
		if (pad.ActualWidth <= 0.0 || pad.ActualHeight <= 0.0 || HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0)
		{
			return under;
		}

		Point padPoint = RingGeometry.RingPointToPad(pad.ActualWidth, pad.ActualHeight, HueRing.ActualWidth, HueRing.ActualHeight, padRotation, x, y);
		Color value = field(padPoint.X, padPoint.Y);
		Color baseColor = value.A != 0 ? value : under;
		return thumbOverlay(baseColor, padPoint.X, padPoint.Y);
	}




	// 彩度・明度パッドの色面サンプラー(パッド内のみ)。コントロール局所座標(画素)を彩度(横: 左0→右1)・明度(縦: 下0→上1)へ写し、現在の色相での HSV 色を返す。色制限が有効ならその丸めも掛ける。パッドの外([0,1] の外)は透明を返す。SvPlane.Create と同じ写し方で、表示と一致させる。色相環のレンズが内側を覗くときの色面参照にも使うため、リングへのフォールバックを持たない。
	private Color SampleSvField(double x, double y)
	{
		double width = SvPad.ActualWidth;
		double height = SvPad.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double saturation = x / width;
		double value = 1.0 - (y / height);

		if (saturation < 0.0 || saturation > 1.0 || value < 0.0 || value > 1.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		(byte r, byte g, byte b) = ColorConversion.HsvToRgb(ViewModel.H, saturation, value);
		SnapSettings snap = ViewModel.CurrentSnap;

		if (snap.Mode != ColorLimitMode.None)
		{
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
		}

		return Color.FromArgb(0xFF, r, g, b);
	}




	// 彩度・明度パッドのレンズのサンプラー。パッド内は色面、外はその点の背後にあるリング(帯＋つまみ)を返す。レンズの拡大・歪曲が色面とリングへ一様に掛かり、境目で不自然に途切れない。
	private Color SampleSvPad(double x, double y)
	{
		Color field = SampleSvField(x, y);

		if (field.A != 0)
		{
			return field;
		}

		return SampleRingBehindPad(SvPad.ActualWidth, SvPad.ActualHeight, SvPad.PadRotation, x, y);
	}




	// 彩度・輝度の三角形パッドの色面サンプラー(三角形内のみ)。コントロール局所座標(画素)を三角形の重心座標へ写し、内側なら彩度・輝度を求めて現在の色相での HSL 色を返す。表示の角丸三角形と同じ判定で、丸めた頂点の外は透明を返す。色制限が有効ならその丸めも掛ける。HslTriangle.Create と同じ写し方(TriangleGeometry)で、表示と一致させる。色相環のレンズが内側を覗くときの色面参照にも使うため、リングへのフォールバックを持たない。
	private Color SampleSlField(double x, double y)
	{
		double width = SlPad.ActualWidth;
		double height = SlPad.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		TriangleVertices vertices = TriangleGeometry.ComputeVertices(width, height);
		double radius = SlPad.VertexCornerRadius;
		TriangleVertices insetVertices = TriangleGeometry.InsetVertices(vertices, radius);
		var point = new Point(x, y);

		if (TriangleGeometry.SignedDistanceToTriangle(point, insetVertices) > radius)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		(double wHue, double wBlack, double wWhite) = TriangleGeometry.PointToBarycentric(point, vertices);
		(wHue, wBlack, wWhite) = TriangleGeometry.ClampBarycentric(wHue, wBlack, wWhite);
		(double saturation, double lightness) = TriangleGeometry.BarycentricToSl(wHue, wBlack, wWhite);
		(byte r, byte g, byte b) = ColorConversion.HslToRgb(ViewModel.H, saturation, lightness);
		SnapSettings snap = ViewModel.CurrentSnap;

		if (snap.Mode != ColorLimitMode.None)
		{
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
		}

		return Color.FromArgb(0xFF, r, g, b);
	}




	// 彩度・輝度の三角形パッドのレンズのサンプラー。三角形内は色面、外はその点の背後にあるリング(帯＋つまみ)を返す(三角形と内円の隙間はリングが透明を返し背景が透ける)。
	private Color SampleSlPad(double x, double y)
	{
		Color field = SampleSlField(x, y);

		if (field.A != 0)
		{
			return field;
		}

		return SampleRingBehindPad(SlPad.ActualWidth, SlPad.ActualHeight, SlPad.PadRotation, x, y);
	}




	// 白み・黒みパッドの色面サンプラー(パッド内のみ)。コントロール局所座標(画素)を白み(横: 左0→右1)・黒み(縦: 上0→下1)へ写し、現在の色相での HWB 色を返す。色制限が有効ならその丸めも掛ける。パッドの外は透明を返す。HwbPlane.Create と同じ写し方で、表示と一致させる。色相環のレンズが内側を覗くときの色面参照にも使うため、リングへのフォールバックを持たない。
	private Color SampleHwbField(double x, double y)
	{
		double width = HwbPad.ActualWidth;
		double height = HwbPad.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double whiteness = x / width;
		double blackness = y / height;

		if (whiteness < 0.0 || whiteness > 1.0 || blackness < 0.0 || blackness > 1.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		(byte r, byte g, byte b) = ColorConversion.HwbToRgb(ViewModel.H, whiteness, blackness);
		SnapSettings snap = ViewModel.CurrentSnap;

		if (snap.Mode != ColorLimitMode.None)
		{
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
		}

		return Color.FromArgb(0xFF, r, g, b);
	}




	// 白み・黒みパッドのレンズのサンプラー。パッド内は色面、外はその点の背後にあるリング(帯＋つまみ)を返す。
	private Color SampleHwbPad(double x, double y)
	{
		Color field = SampleHwbField(x, y);

		if (field.A != 0)
		{
			return field;
		}

		return SampleRingBehindPad(HwbPad.ActualWidth, HwbPad.ActualHeight, HwbPad.PadRotation, x, y);
	}




	// 中央のパッドの表示モード。HSV は彩度・明度の正方形、HSL は彩度・輝度の三角形、HWB は白み・黒みの正方形。
	private enum CenterMode
	{
		Hsv,
		Hsl,
		Hwb,
	}
}
