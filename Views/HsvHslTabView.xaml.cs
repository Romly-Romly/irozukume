// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;
using Irozukume.Controls.Geometry;
using Irozukume.Controls.Generators.Planes;
using Irozukume.Controls.Generators.Disks;
using Irozukume.Controls.Generators.Wheels;
using Irozukume.Controls.Generators.Triangles;

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

	// 円盤画像(色相環の角度=色相・半径=彩度 or 明度)を生成した際の画素サイズ・固定成分の値・色制限設定。同じ条件での作り直しを避けるために覚えておく。固定成分は半径に取らない側(半径=彩度なら明度、半径=明度なら彩度)で、未生成を表す NaN で初期化する。色相は円盤の全方位に現れるため鍵に含めない。
	private int _diskPixels = -1;
	private double _diskFixed = double.NaN;
	private SnapSettings _diskSnap;

	// 円盤の半径に明度を取るか(true)、彩度を取るか(false)。レイアウトに応じて切り替える。半径に取らない側が固定成分(縦スライダー)になる。
	private bool _diskRadiusIsValue;

	// 直交パッドの下地画像を生成した際の画素サイズ(縦横独立)・固定成分の値・色制限設定。同じ条件での作り直しを避けるために覚えておく。固定成分はパッドの2軸に取らない側で、未生成を表す NaN で初期化する。パッドの2軸はパッド全面に現れるため鍵に含めない。
	private int _cartPixelWidth = -1;
	private int _cartPixelHeight = -1;
	private double _cartFixed = double.NaN;
	private SnapSettings _cartSnap;

	// 直交パッドの横軸・縦軸に取る成分。レイアウトに応じて切り替える。2軸に取らない残り1成分が固定成分(縦スライダー)になる。
	private Channel _cartX = Channel.Hue;
	private Channel _cartY = Channel.Value;

	// 円盤をドラッグ中かどうか。ポインタを捕捉している間だけ真にする。
	private bool _wheelDragging;

	// 円盤のドラッグ中につまみをガラスレンズへ膨らませる管理役。置き場(WheelLens)へ載せ、座標は円盤局所(DIP)で渡す。
	private LensController? _wheelLens;

	// 円盤のつまみのガラスレンズの効き。2次元パッドと同じ効きにそろえる。各項目の意味と単位は GlassLensParams を参照。
	private static readonly GlassLensParams WheelLensParams = new()
	{
		Diameter = 50.0,
		Magnify = 1.4,
		EdgeAmount = -24.0,
		Chroma = true,
		ChromaSpread = 0.4,
		BevelFraction = 0.4,
	};

	// 中央のパッドの表示モード。既定は HSV(彩度・明度の正方形)。HSL は彩度・輝度の三角形、HWB は白み・黒みの正方形。
	private CenterMode _mode = CenterMode.Hsv;

	// 色相環エリアの見せ方。RingSquare は色相リング+パッド、HueSatWheel は角度=色相・半径=彩度の円盤+明度の縦スライダー。円盤は HSV モードのときだけ有効で、HSL・HWB では常に RingSquare として扱う。VM の HsvLayoutIndex を反映する。
	private DiskLayout _layout = DiskLayout.RingSquare;

	// HSL モードの見せ方。VM の HslLayoutIndex を反映する。既定は HSL らしい色相リング+三角形。HSL モードのときだけ効く。
	private HslLayout _hslLayout = HslLayout.RingTriangle;

	// HWB モードの見せ方。VM の HwbLayoutIndex を反映する。既定は現行の色相リング+正方形。HWB モードのときだけ効く。
	private HwbLayout _hwbLayout = HwbLayout.RingSquare;

	// 現在のレイアウトが円盤ホスト(WheelDisk)・直交パッドホスト(HvPad)・独立三角形ホスト(TriangleBarPad)のいずれを使うか。HSV・HSL の両モードで同じ判定に使うため、レイアウト列挙ではなくこの真偽をホストの出し分け・寸法・画像作り直しの分岐に使う。いずれも偽なら色相リング+中央パッド(三角形/正方形)のホスト。三角形ホストは HSL のみ。
	private bool _diskHost;
	private bool _cartHost;
	private bool _triangleHost;

	// HSL の彩度・輝度の正方形パッド(リング内)のビットマップを生成した際の画素サイズ・色相・色制限設定。同じ条件での作り直しを避けるために覚えておく。色相は未生成を表す NaN で初期化する。
	private int _slSquarePixels = -1;
	private double _slSquareHue = double.NaN;
	private SnapSettings _slSquareSnap;

	// 独立三角形(色相は縦スライダー)の三角形画像を生成した際の画素サイズ(縦横独立。箱を埋める正三角形のため幅と高さの比は約 2:√3)・色相・色制限設定・頂点の角丸半径(画素)。同じ条件での作り直しを避けるために覚えておく。色相と角丸半径は未生成を表す NaN で初期化する。
	private int _triBarPixelW = -1;
	private int _triBarPixelH = -1;
	private double _triBarHue = double.NaN;
	private SnapSettings _triBarSnap;
	private double _triBarCorner = double.NaN;

	// HWB の白み・黒みの三角形画像(リング内)を生成した際の画素サイズ・色相・色制限設定・頂点の角丸半径(画素)。同じ条件での作り直しを避けるために覚えておく。色相と角丸半径は未生成を表す NaN で初期化する。
	private int _hwbTriPixels = -1;
	private double _hwbTriHue = double.NaN;
	private SnapSettings _hwbTriSnap;
	private double _hwbTriCorner = double.NaN;

	// HWB の独立三角形の三角形画像を生成した際の画素サイズ(縦横独立)・色相・色制限設定・頂点の角丸半径(画素)。同じ条件での作り直しを避けるために覚えておく。色相と角丸半径は未生成を表す NaN で初期化する。
	private int _hwbTriBarPixelW = -1;
	private int _hwbTriBarPixelH = -1;
	private double _hwbTriBarHue = double.NaN;
	private SnapSettings _hwbTriBarSnap;
	private double _hwbTriBarCorner = double.NaN;

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
		SlSquarePad.LensColorSampler = SampleSlSquarePad;
		HwbPad.LensColorSampler = SampleHwbPad;
		HwbTrianglePad.LensColorSampler = SampleHwbTriRingPad;

		// 円盤(色相・彩度)のレンズ管理役。置き場は円盤と同座標の Canvas(WheelLens)。
		_wheelLens = new LensController(WheelDisk, WheelLens, WheelLensParams);

		// 直交パッドは PlanarPad を流用するため、つまみ・レンズ・矢印操作はそのまま得られる。レンズに映す色面のサンプラーだけ与え、大きさ変化で下地画像を作り直す。軸の束縛はレイアウトに応じて ApplyLayout が差し替える。
		HvPad.LensColorSampler = SampleCartField;
		HvPad.SizeChanged += OnHvPadSizeChanged;

		// 独立した三角形(色相は縦スライダー)も TrianglePad を流用するため、つまみ・レンズ・矢印操作はそのまま得られる。レンズに映す色面のサンプラーだけ与え、大きさ変化で三角形画像を作り直す。HSL・HWB それぞれの独立三角形を同じホストへ重ねて置く。
		TriangleBarPad.LensColorSampler = SampleTriBarPad;
		TriangleBarPad.SizeChanged += OnTriangleBarPadSizeChanged;
		HwbTriangleBarPad.LensColorSampler = SampleHwbTriBarPad;
		HwbTriangleBarPad.SizeChanged += OnHwbTriangleBarPadSizeChanged;

		// 復元済みの副モードをラジオに反映し、表示を整える。ここまでの SelectionChanged は _modeSyncing で無視し、以降の操作だけ VM へ伝える。
		ModeSelector.SelectedIndex = ViewModel.HsvSubModeIndex;
		_modeSyncing = false;
		ApplyMode();

		HueRing.SizeChanged += OnHueRingSizeChanged;

		// 色相環の一辺はスクロール領域の可視高さからスライダー群の高さを引いて決めるため、スライダー群の高さが変わったら算出し直す。可視高さの変化は読み込み後に見つけるスクロール領域の SizeChanged で、エリアの幅変化は RingArea の SizeChanged で拾う。
		SliderHost.SizeChanged += OnLayoutMetricChanged;

		// 右の縦並び設定列は上段の行高の下限になるため、その高さが変わったら色相環・パッドの一辺を算出し直す。
		SideControls.SizeChanged += OnLayoutMetricChanged;

		// 円盤・直交パッドの一辺は、隣の縦スライダーの幅が確定して初めて正しく決まる。スライダーの寸法が定まったら算出し直す。
		DiskSlider.SizeChanged += OnLayoutMetricChanged;
		CartSlider.SizeChanged += OnLayoutMetricChanged;
		HueBarSlider.SizeChanged += OnLayoutMetricChanged;

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
		ApplyLayout();
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

		// 上段の行高は色相環エリアと、その右の縦並び設定列の高い方で決まる。設定列の方が嵩むと、エリアの中身(色相環・パッド)をそれ未満へ縮めても行は縮まず、中身は上端へ詰まったまま下端との間に空白が生じ、下段スライダー群との間が空く。これを防ぐため、エリアの中身の下限を既定の下限と設定列の高さの大きい方に取る。設定列の実寸が未確定の初回は既定の下限で見積もり、確定後の SizeChanged で正す。
		double minSide = Math.Max(MinRingSide, SideControls.ActualHeight);

		// 円盤レイアウトのときは正方形。縦スライダーのぶんの幅を引いた残りと残りの高さの小さい方を円盤の一辺にする。スライダーの実寸が未確定の初回は概算の幅で見積もり、確定後の SizeChanged で正す。
		if (_diskHost)
		{
			double sliderColumn = DiskSlider.ActualWidth > 0.0 ? DiskSlider.ActualWidth : 32.0;
			double widthBudget = RingArea.ActualWidth - sliderColumn - WheelLayoutRoot.ColumnSpacing;
			double diskSide = Math.Min(widthBudget, Math.Max(minSide, heightBudget));

			if (diskSide <= 0.0)
			{
				return;
			}

			if (WheelDisk.Width != diskSide)
			{
				WheelDisk.Width = diskSide;
				WheelDisk.Height = diskSide;
			}

			if (DiskSlider.Height != diskSide)
			{
				DiskSlider.Height = diskSide;
			}

			return;
		}

		// 直交パッドのとき。色相を横軸に取る配置(色相×明度/輝度・色相×彩度)は正方形に縛らず横いっぱいに広げ、縦は残りの高さに合わせる(横へ広く取るほど色相の刻みが細かくなる)。色相を軸に取らない配置(彩度×明度/輝度)は正方形にする。
		if (_cartHost)
		{
			double sliderColumn = CartSlider.ActualWidth > 0.0 ? CartSlider.ActualWidth : 32.0;
			double widthBudget = RingArea.ActualWidth - sliderColumn - PlaneLayoutRoot.ColumnSpacing;
			double padWidth;
			double padHeight;

			if (_cartX == Channel.Hue)
			{
				padWidth = widthBudget;
				padHeight = Math.Max(minSide, heightBudget);
			}
			else
			{
				double square = Math.Min(widthBudget, Math.Max(minSide, heightBudget));
				padWidth = square;
				padHeight = square;
			}

			if (padWidth <= 0.0)
			{
				return;
			}

			if (HvPad.Width != padWidth)
			{
				HvPad.Width = padWidth;
			}

			if (HvPad.Height != padHeight)
			{
				HvPad.Height = padHeight;
			}

			if (CartSlider.Height != padHeight)
			{
				CartSlider.Height = padHeight;
			}

			return;
		}

		// 独立三角形のときは箱を埋める三角形(fillBox)。純色を上辺中央、黒白を下の両隅に取り、箱の縦横をいっぱいに使う。色相を軸に取る直交パッドと同じく正方形に縛らず横いっぱいに広げ、縦は残りの可視高さに合わせる(横へ広く取るほど底辺=黒↔白の刻みが細かくなる)。三角形は正三角形でなくなるが、重心写像は affine のため値は正しく出る。色相バーの高さは三角形の高さに合わせ、ともに上端ぞろえにする。
		if (_triangleHost)
		{
			double sliderColumn = HueBarSlider.ActualWidth > 0.0 ? HueBarSlider.ActualWidth : 32.0;
			double triWidth = RingArea.ActualWidth - sliderColumn - TriangleLayoutRoot.ColumnSpacing;
			double triHeight = Math.Max(minSide, heightBudget);

			if (triWidth <= 0.0)
			{
				return;
			}

			// HSL と HWB の独立三角形は同じホストを共有するため、両方を同じ大きさにする(活性でない側は隠れている)。
			if (TriangleBarPad.Width != triWidth || TriangleBarPad.Height != triHeight)
			{
				TriangleBarPad.Width = triWidth;
				TriangleBarPad.Height = triHeight;
			}

			if (HwbTriangleBarPad.Width != triWidth || HwbTriangleBarPad.Height != triHeight)
			{
				HwbTriangleBarPad.Width = triWidth;
				HwbTriangleBarPad.Height = triHeight;
			}

			if (HueBarSlider.Height != triHeight)
			{
				HueBarSlider.Height = triHeight;
			}

			return;
		}

		double side = Math.Min(RingArea.ActualWidth, Math.Max(minSide, heightBudget));

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
		// 色相・彩度・明度のいずれかが変わったら、円盤と直交パッドの両ホストを整える。各更新はレイアウトで空振り判定とキャッシュを持つため、活性なホストだけが実際に動く。円盤は固定成分が変わると画像を塗り直し、半径成分や色相が変わるとつまみが動く。直交パッドのつまみは PlanarPad の束縛が処理し、固定成分が変わると画像を塗り直す。色相変更ではリング側の中央パッドも塗り直す。
		if (e.PropertyName == nameof(ColorEditorViewModel.H))
		{
			RegenerateActiveCenter();
			UpdateWheelThumb();
			RegenerateDisk();
			RegenerateCart();
			return;
		}

		if (e.PropertyName == nameof(ColorEditorViewModel.Saturation01) || e.PropertyName == nameof(ColorEditorViewModel.Value01)
			|| e.PropertyName == nameof(ColorEditorViewModel.HslSaturation01) || e.PropertyName == nameof(ColorEditorViewModel.Lightness01))
		{
			UpdateWheelThumb();
			RegenerateDisk();
			RegenerateCart();
			return;
		}

		if (e.PropertyName == nameof(ColorEditorViewModel.HsvLayoutIndex) || e.PropertyName == nameof(ColorEditorViewModel.HslLayoutIndex)
			|| e.PropertyName == nameof(ColorEditorViewModel.HwbLayoutIndex))
		{
			ApplyLayout();
			return;
		}

		if (e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap))
		{
			UpdateRingVisuals();
			UpdateSvPadLayer();
			RegenerateDisk();
			RegenerateCart();
			UpdateLayoutPickerIcon();
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

		// HSL・HWB の中央パッド(三角形/正方形)はレイアウトに依るため、ここでは隠し、末尾の ApplyLayout が現在のレイアウトに応じて表に出す。
		SlPad.Visibility = Visibility.Collapsed;
		SlSquarePad.Visibility = Visibility.Collapsed;
		HwbPad.Visibility = Visibility.Collapsed;
		HwbTrianglePad.Visibility = Visibility.Collapsed;
		HsvSliders.Visibility = _mode == CenterMode.Hsv ? Visibility.Visible : Visibility.Collapsed;
		HslSliders.Visibility = _mode == CenterMode.Hsl ? Visibility.Visible : Visibility.Collapsed;
		HwbSliders.Visibility = _mode == CenterMode.Hwb ? Visibility.Visible : Visibility.Collapsed;

		// 正規化トグルは HWB モード固有。ただし右列の幅をモードで変えない(三角形等のレイアウトをズラさない)ため、非 HWB でも場所は確保したまま透明・非操作にする。
		bool showNormalize = _mode == CenterMode.Hwb;
		NormalizeCheck.Opacity = showNormalize ? 1.0 : 0.0;
		NormalizeCheck.IsHitTestVisible = showNormalize;
		NormalizeCheck.IsEnabled = showNormalize;

		// HSV は色制限時だけ下地ビットマップを使うため出し分けを更新し、HSL・HWB は常にビットマップを作り直す。
		if (_mode == CenterMode.Hsv)
		{
			UpdateSvPadLayer();
		}
		else
		{
			RegenerateActiveCenter();
		}

		// 副モードが変わると円盤を選べるかどうか(HSV のときだけ)も変わるため、レイアウトの出し分けを合わせる。
		ApplyLayout();
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
		SlSquarePad.Width = metrics.InnerRadius * Math.Sqrt(2.0);
		SlSquarePad.Height = SlSquarePad.Width;
		HwbPad.Width = metrics.InnerRadius * Math.Sqrt(2.0);
		HwbPad.Height = HwbPad.Width;
		HwbTrianglePad.Width = metrics.InnerRadius * 2.0;
		HwbTrianglePad.Height = HwbTrianglePad.Width;

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




	// 彩度・輝度の正方形パッド(リング内)のビットマップを、現在の色相・パッドの大きさ・表示倍率・色制限設定に合わせて作り直す。HSL の彩度・輝度の正方形は色相に依って塗りが変わるため、HWB と同様に色制限の有無に依らず常に画像で賄う。同じ画素サイズ・色相・設定なら作り直さない。大きさは正方形パッドの実寸ではなくリングの幾何(内円に内接する正方形)から求める。
	private void RegenerateSlSquare()
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

		if (pixels == _slSquarePixels && ViewModel.H == _slSquareHue && snap == _slSquareSnap)
		{
			return;
		}

		_slSquarePixels = pixels;
		_slSquareHue = ViewModel.H;
		_slSquareSnap = snap;
		SlSquareImage.Source = SlPlane.Create(pixels, pixels, ViewModel.H, snap);
	}




	// 独立三角形(色相は縦スライダー)の三角形画像を、現在の色相・パッドの大きさ・表示倍率・色制限設定に合わせて作り直す。同じ画素サイズと色相なら作り直さない。大きさはリングではなく独立三角形パッドの実寸(縦横独立)から求め、箱を埋める頂点取り(fillBox)で描く。
	private void RegenerateTriangleBar()
	{
		if (TriangleBarPad.ActualWidth <= 0.0 || TriangleBarPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixelWidth = (int)Math.Round(TriangleBarPad.ActualWidth * scale);
		int pixelHeight = (int)Math.Round(TriangleBarPad.ActualHeight * scale);

		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		double cornerRadius = TriangleBarPad.VertexCornerRadius * scale;

		if (pixelWidth == _triBarPixelW && pixelHeight == _triBarPixelH && ViewModel.H == _triBarHue && snap == _triBarSnap && cornerRadius == _triBarCorner)
		{
			return;
		}

		_triBarPixelW = pixelWidth;
		_triBarPixelH = pixelHeight;
		_triBarHue = ViewModel.H;
		_triBarSnap = snap;
		_triBarCorner = cornerRadius;
		TriangleBarImage.Source = HslTriangle.Create(pixelWidth, pixelHeight, ViewModel.H, snap, cornerRadius, fillBox: true);
	}




	// HWB の白み・黒みの三角形画像(リング内)を、現在の色相・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズと色相なら作り直さない。大きさはパッドの実寸ではなくリングの幾何から求める。RegenerateTriangle の HSL を HWB へ替えた対。
	private void RegenerateHwbTriangleRing()
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
		double cornerRadius = HwbTrianglePad.VertexCornerRadius * scale;

		if (pixels == _hwbTriPixels && ViewModel.H == _hwbTriHue && snap == _hwbTriSnap && cornerRadius == _hwbTriCorner)
		{
			return;
		}

		_hwbTriPixels = pixels;
		_hwbTriHue = ViewModel.H;
		_hwbTriSnap = snap;
		_hwbTriCorner = cornerRadius;
		HwbTriangleImage.Source = HwbTriangle.Create(pixels, pixels, ViewModel.H, snap, cornerRadius);
	}




	// HWB の独立三角形(色相は縦スライダー)の三角形画像を、現在の色相・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズと色相なら作り直さない。大きさは独立三角形パッドの実寸(縦横独立)から求め、箱を埋める頂点取り(fillBox)で描く。RegenerateTriangleBar の HSL を HWB へ替えた対。
	private void RegenerateHwbTriangleBar()
	{
		if (HwbTriangleBarPad.ActualWidth <= 0.0 || HwbTriangleBarPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixelWidth = (int)Math.Round(HwbTriangleBarPad.ActualWidth * scale);
		int pixelHeight = (int)Math.Round(HwbTriangleBarPad.ActualHeight * scale);

		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		double cornerRadius = HwbTriangleBarPad.VertexCornerRadius * scale;

		if (pixelWidth == _hwbTriBarPixelW && pixelHeight == _hwbTriBarPixelH && ViewModel.H == _hwbTriBarHue && snap == _hwbTriBarSnap && cornerRadius == _hwbTriBarCorner)
		{
			return;
		}

		_hwbTriBarPixelW = pixelWidth;
		_hwbTriBarPixelH = pixelHeight;
		_hwbTriBarHue = ViewModel.H;
		_hwbTriBarSnap = snap;
		_hwbTriBarCorner = cornerRadius;
		HwbTriangleBarImage.Source = HwbTriangle.Create(pixelWidth, pixelHeight, ViewModel.H, snap, cornerRadius, fillBox: true);
	}




	// 現在のモードとレイアウトの中央面を塗り直す。HSL/HWB は独立三角形・正方形・色相リング内の三角形を出し分け、HSV は色制限時のみ使う彩度・明度ビットマップ。各 Regenerate は空振り判定とキャッシュを持つため、活性でない面は実際には動かない。
	private void RegenerateActiveCenter()
	{
		switch (_mode)
		{
			case CenterMode.Hsl:
				if (_triangleHost)
				{
					RegenerateTriangleBar();
				}
				else if (_hslLayout == HslLayout.RingSquare)
				{
					RegenerateSlSquare();
				}
				else
				{
					RegenerateTriangle();
				}

				break;

			case CenterMode.Hwb:
				if (_triangleHost)
				{
					RegenerateHwbTriangleBar();
				}
				else if (_hwbLayout == HwbLayout.RingTriangle)
				{
					RegenerateHwbTriangleRing();
				}
				else
				{
					RegenerateHwbPlane();
				}

				break;

			default:
				RegenerateSvPad();
				break;
		}
	}




	// 独立三角形パッドの大きさが変わったら、三角形画像を作り直す。つまみ位置は TrianglePad が束縛から合わせる。三角形を初めて表示したときの寸法確定もこの経路で拾う。
	private void OnTriangleBarPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateTriangleBar();
	}




	// HWB の独立三角形パッドの大きさが変わったら、三角形画像を作り直す。
	private void OnHwbTriangleBarPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateHwbTriangleBar();
	}




	// HWB の白み・黒みの三角形パッド(リング内、HwbTrianglePad)の色面サンプラー。
	private Color SampleHwbTriRingField(double x, double y)
	{
		return SampleTriangleField(HwbTrianglePad, x, y);
	}




	// HWB の白み・黒みの三角形パッドのレンズのサンプラー。三角形内は色面、外はその点の背後にあるリング(帯＋つまみ)を返す。
	private Color SampleHwbTriRingPad(double x, double y)
	{
		Color field = SampleHwbTriRingField(x, y);

		if (field.A != 0)
		{
			return field;
		}

		return SampleRingBehindPad(HwbTrianglePad.ActualWidth, HwbTrianglePad.ActualHeight, HwbTrianglePad.PadRotation, x, y);
	}




	// HWB の独立三角形パッド(HwbTriangleBarPad)の色面サンプラー。
	private Color SampleHwbTriBarField(double x, double y)
	{
		return SampleTriangleField(HwbTriangleBarPad, x, y);
	}




	// HWB の独立三角形パッドのレンズのサンプラー。三角形内は色面、外は透明を返す。リングを使わない配置のため背後のリングは参照しない。
	private Color SampleHwbTriBarPad(double x, double y)
	{
		return SampleHwbTriBarField(x, y);
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




	// 帯の色 under の上に、現在のモード・レイアウトの中央パッドの色面とつまみを重ねる。HSL・HWB は色相リング内が三角形(RingTriangle)のときと正方形(RingSquare)のときで中央パッドが変わる。色相リングを使わない配置(円盤・直交パッド・独立三角形)ではこのレンズ合成は出番がない。
	private Color OverlayActivePad(Color under, double x, double y)
	{
		if (_mode == CenterMode.Hsl)
		{
			return _hslLayout == HslLayout.RingSquare
				? OverlayPad(under, SlSquarePad, SlSquarePad.PadRotation, SampleSlSquareField, SlSquarePad.SampleThumbOverlay, x, y)
				: OverlayPad(under, SlPad, SlPad.PadRotation, SampleSlField, SlPad.SampleThumbOverlay, x, y);
		}

		if (_mode == CenterMode.Hwb)
		{
			return _hwbLayout == HwbLayout.RingTriangle
				? OverlayPad(under, HwbTrianglePad, HwbTrianglePad.PadRotation, SampleHwbTriRingField, HwbTrianglePad.SampleThumbOverlay, x, y)
				: OverlayPad(under, HwbPad, HwbPad.PadRotation, SampleHwbField, HwbPad.SampleThumbOverlay, x, y);
		}

		return OverlayPad(under, SvPad, SvPad.PadRotation, SampleSvField, SvPad.SampleThumbOverlay, x, y);
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




	// 三角形パッドの色面サンプラー(三角形内のみ)。指定したパッドのコントロール局所座標(画素)を三角形の重心座標へ写し、内側ならパッドの ValueModel に応じて HSL(彩度・輝度)または HWB(白み・黒み)の色を現在の色相で返す。表示の角丸三角形と同じ判定で、丸めた頂点の外は透明を返す。色制限が有効ならその丸めも掛ける。三角形画像(HslTriangle / HwbTriangle)と同じ写し方(TriangleGeometry)で、表示と一致させる。リング内の三角形と独立三角形で共有する。色相環のレンズが内側を覗くときの色面参照にも使うため、リングへのフォールバックを持たない。
	private Color SampleTriangleField(TrianglePad pad, double x, double y)
	{
		double width = pad.ActualWidth;
		double height = pad.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		TriangleVertices vertices = pad.FillBox
			? TriangleGeometry.ComputeFillVertices(width, height)
			: TriangleGeometry.ComputeVertices(width, height);
		double radius = pad.VertexCornerRadius;
		TriangleVertices insetVertices = TriangleGeometry.InsetVertices(vertices, radius);
		var point = new Point(x, y);

		if (TriangleGeometry.SignedDistanceToTriangle(point, insetVertices) > radius)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		(double wHue, double wBlack, double wWhite) = TriangleGeometry.PointToBarycentric(point, vertices);
		(wHue, wBlack, wWhite) = TriangleGeometry.ClampBarycentric(wHue, wBlack, wWhite);

		byte r;
		byte g;
		byte b;

		if (pad.ValueModel == TriangleValueModel.Hwb)
		{
			(double whiteness, double blackness) = TriangleGeometry.BarycentricToWb(wHue, wBlack, wWhite);
			(r, g, b) = ColorConversion.HwbToRgb(ViewModel.H, whiteness, blackness);
		}
		else
		{
			(double saturation, double lightness) = TriangleGeometry.BarycentricToSl(wHue, wBlack, wWhite);
			(r, g, b) = ColorConversion.HslToRgb(ViewModel.H, saturation, lightness);
		}

		SnapSettings snap = ViewModel.CurrentSnap;

		if (snap.Mode != ColorLimitMode.None)
		{
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
		}

		return Color.FromArgb(0xFF, r, g, b);
	}




	// リング内の彩度・輝度の三角形パッド(SlPad)の色面サンプラー。
	private Color SampleSlField(double x, double y)
	{
		return SampleTriangleField(SlPad, x, y);
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




	// 独立三角形パッド(TriangleBarPad)の色面サンプラー。
	private Color SampleTriBarField(double x, double y)
	{
		return SampleTriangleField(TriangleBarPad, x, y);
	}




	// 独立三角形パッドのレンズのサンプラー。三角形内は色面、外は透明を返す。リングを使わない配置のため背後のリングは参照せず、レンズの縁では下地のカード色が透ける。
	private Color SampleTriBarPad(double x, double y)
	{
		return SampleTriBarField(x, y);
	}




	// 彩度・輝度の正方形パッド(リング内、SlSquarePad)の色面サンプラー(パッド内のみ)。コントロール局所座標(画素)を彩度(横: 左0→右1)・輝度(縦: 下0→上1)へ写し、現在の色相での HSL 色を返す。色制限が有効ならその丸めも掛ける。パッドの外は透明を返す。SlPlane.Create と同じ写し方で、表示と一致させる。色相環のレンズが内側を覗くときの色面参照にも使うため、リングへのフォールバックを持たない。
	private Color SampleSlSquareField(double x, double y)
	{
		double width = SlSquarePad.ActualWidth;
		double height = SlSquarePad.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double saturation = x / width;
		double lightness = 1.0 - (y / height);

		if (saturation < 0.0 || saturation > 1.0 || lightness < 0.0 || lightness > 1.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		(byte r, byte g, byte b) = ColorConversion.HslToRgb(ViewModel.H, saturation, lightness);
		SnapSettings snap = ViewModel.CurrentSnap;

		if (snap.Mode != ColorLimitMode.None)
		{
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
		}

		return Color.FromArgb(0xFF, r, g, b);
	}




	// 彩度・輝度の正方形パッドのレンズのサンプラー。パッド内は色面、外はその点の背後にあるリング(帯＋つまみ)を返す。
	private Color SampleSlSquarePad(double x, double y)
	{
		Color field = SampleSlSquareField(x, y);

		if (field.A != 0)
		{
			return field;
		}

		return SampleRingBehindPad(SlSquarePad.ActualWidth, SlSquarePad.ActualHeight, SlSquarePad.PadRotation, x, y);
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




	// 色相環エリアの見せ方を、現在の副モードとレイアウト選択に合わせて切り替える。円盤は HSV モードのときだけ有効で、HSL・HWB では常に色相リング+パッドにする。レイアウトのセレクタは HSV モードのときだけ出す。表示を入れ替えたあと、寸法・円盤画像・つまみ位置・隅アイコンを整える。
	private void ApplyLayout()
	{
		bool hsv = _mode == CenterMode.Hsv;
		bool hsl = _mode == CenterMode.Hsl;
		bool hwb = _mode == CenterMode.Hwb;

		// 現在のモードの選択値から、使うホスト(円盤・直交パッド・独立三角形・色相リング)を決める。HSV・HSL・HWB は円盤・直交パッドの幾何が共通のため、ホスト判定と軸割り当ては同じ Channel 構成で済み、モードで変わるのは描画関数・色変換・束ねる VM プロパティだけ。HWB は第1線形軸=白み・第2線形軸=黒み。
		if (hsv)
		{
			_layout = ViewModel.HsvLayoutIndex switch
			{
				1 => DiskLayout.HueSatWheel,
				2 => DiskLayout.HueValuePlane,
				3 => DiskLayout.HueValueWheel,
				4 => DiskLayout.HueSatPlane,
				5 => DiskLayout.SvHueBar,
				_ => DiskLayout.RingSquare,
			};
			_diskHost = _layout == DiskLayout.HueSatWheel || _layout == DiskLayout.HueValueWheel;
			_cartHost = _layout == DiskLayout.HueValuePlane || _layout == DiskLayout.HueSatPlane || _layout == DiskLayout.SvHueBar;
			_triangleHost = false;
		}
		else if (hsl)
		{
			_hslLayout = ViewModel.HslLayoutIndex switch
			{
				0 => HslLayout.RingSquare,
				1 => HslLayout.HueSatWheel,
				2 => HslLayout.HueLightnessPlane,
				3 => HslLayout.HueLightnessWheel,
				4 => HslLayout.HueSatPlane,
				5 => HslLayout.SlHueBar,
				7 => HslLayout.TriangleHueBar,
				_ => HslLayout.RingTriangle,
			};
			_diskHost = _hslLayout == HslLayout.HueSatWheel || _hslLayout == HslLayout.HueLightnessWheel;
			_cartHost = _hslLayout == HslLayout.HueLightnessPlane || _hslLayout == HslLayout.HueSatPlane || _hslLayout == HslLayout.SlHueBar;
			_triangleHost = _hslLayout == HslLayout.TriangleHueBar;
		}
		else if (hwb)
		{
			_hwbLayout = ViewModel.HwbLayoutIndex switch
			{
				1 => HwbLayout.HueWhitenessWheel,
				2 => HwbLayout.HueBlacknessPlane,
				3 => HwbLayout.HueBlacknessWheel,
				4 => HwbLayout.HueWhitenessPlane,
				5 => HwbLayout.WbHueBar,
				6 => HwbLayout.RingTriangle,
				7 => HwbLayout.TriangleHueBar,
				_ => HwbLayout.RingSquare,
			};
			_diskHost = _hwbLayout == HwbLayout.HueWhitenessWheel || _hwbLayout == HwbLayout.HueBlacknessWheel;
			_cartHost = _hwbLayout == HwbLayout.HueBlacknessPlane || _hwbLayout == HwbLayout.HueWhitenessPlane || _hwbLayout == HwbLayout.WbHueBar;
			_triangleHost = _hwbLayout == HwbLayout.TriangleHueBar;
		}
		else
		{
			_diskHost = false;
			_cartHost = false;
			_triangleHost = false;
		}

		bool ring = !_diskHost && !_cartHost && !_triangleHost;

		HueRing.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
		WheelLayoutRoot.Visibility = _diskHost ? Visibility.Visible : Visibility.Collapsed;
		PlaneLayoutRoot.Visibility = _cartHost ? Visibility.Visible : Visibility.Collapsed;
		TriangleLayoutRoot.Visibility = _triangleHost ? Visibility.Visible : Visibility.Collapsed;
		LayoutPickerButton.Visibility = hsv ? Visibility.Visible : Visibility.Collapsed;
		HslLayoutPickerButton.Visibility = hsl ? Visibility.Visible : Visibility.Collapsed;
		HwbLayoutPickerButton.Visibility = hwb ? Visibility.Visible : Visibility.Collapsed;

		// 色相リングの中央パッドの出し分け。HSL は三角形(RingTriangle)と彩度・輝度の正方形(RingSquare)を、HWB は三角形(RingTriangle)と白み・黒みの正方形(RingSquare)を入れ替える。リングを使わない配置では隠す。HSV はモードで決まる単一の中央パッドのため、ここでは触れず ApplyMode が司る。
		if (hsl)
		{
			SlPad.Visibility = ring && _hslLayout == HslLayout.RingTriangle ? Visibility.Visible : Visibility.Collapsed;
			SlSquarePad.Visibility = ring && _hslLayout == HslLayout.RingSquare ? Visibility.Visible : Visibility.Collapsed;
		}
		else if (hwb)
		{
			HwbPad.Visibility = ring && _hwbLayout == HwbLayout.RingSquare ? Visibility.Visible : Visibility.Collapsed;
			HwbTrianglePad.Visibility = ring && _hwbLayout == HwbLayout.RingTriangle ? Visibility.Visible : Visibility.Collapsed;
		}

		// 独立三角形ホストは HSL と HWB で同じ場所(TriangleLayoutRoot)を共有し、中の三角形パッドだけモードで入れ替える。
		TriangleBarPad.Visibility = _triangleHost && hsl ? Visibility.Visible : Visibility.Collapsed;
		HwbTriangleBarPad.Visibility = _triangleHost && hwb ? Visibility.Visible : Visibility.Collapsed;

		if (_diskHost)
		{
			// 円盤の半径成分と、その残りの固定成分(縦スライダー)を決める。半径=第2線形軸(明度/輝度/黒み)か第1線形軸(彩度/白み)かで分け、残りを縦スライダーへ。レイアウトが変わったら画像キャッシュを無効化して必ず作り直す。
			_diskRadiusIsValue = hsv ? _layout == DiskLayout.HueValueWheel
				: hsl ? _hslLayout == HslLayout.HueLightnessWheel
				: _hwbLayout == HwbLayout.HueBlacknessWheel;
			_diskPixels = -1;
			ConfigureVerticalSlider(DiskSlider, _diskRadiusIsValue ? Channel.Saturation : Channel.Value);
		}

		if (_cartHost)
		{
			// 直交パッドの2軸と、残りの固定成分(縦スライダー)を決める。Value 軸は HSL では輝度・HWB では黒み、Saturation 軸は HWB では白みを指す。パッドの軸束縛と縦スライダーを差し替え、画像キャッシュを無効化して必ず作り直す。
			(_cartX, _cartY) = hsv
				? _layout switch
				{
					DiskLayout.HueValuePlane => (Channel.Hue, Channel.Value),
					DiskLayout.HueSatPlane => (Channel.Hue, Channel.Saturation),
					_ => (Channel.Saturation, Channel.Value),
				}
				: hsl
					? _hslLayout switch
					{
						HslLayout.HueLightnessPlane => (Channel.Hue, Channel.Value),
						HslLayout.HueSatPlane => (Channel.Hue, Channel.Saturation),
						_ => (Channel.Saturation, Channel.Value),
					}
					: _hwbLayout switch
					{
						HwbLayout.HueBlacknessPlane => (Channel.Hue, Channel.Value),
						HwbLayout.HueWhitenessPlane => (Channel.Hue, Channel.Saturation),
						_ => (Channel.Saturation, Channel.Value),
					};
			Channel sliderChannel = CartFixedChannel();
			_cartPixelWidth = -1;
			ConfigureCartPad();
			ConfigureVerticalSlider(CartSlider, sliderChannel);
		}

		UpdateRingSize();
		UpdateLayoutPickerIcon();

		if (_diskHost)
		{
			RegenerateDisk();
			UpdateWheelThumb();
		}

		if (_cartHost)
		{
			RegenerateCart();
		}

		// 色相リング+中央パッド、または独立三角形のときは、現在の中央面(三角形/正方形/独立三角形)を作り直す。各 Regenerate は空振り判定とキャッシュを持つため、活性でない面は実際には動かない。
		if (ring || _triangleHost)
		{
			RegenerateActiveCenter();
		}
	}




	// 直交パッドの2軸に取らない、残りの固定成分(縦スライダーが司る)を返す。
	private Channel CartFixedChannel()
	{
		bool hasHue = _cartX == Channel.Hue || _cartY == Channel.Hue;
		bool hasSat = _cartX == Channel.Saturation || _cartY == Channel.Saturation;

		if (!hasHue)
		{
			return Channel.Hue;
		}

		return hasSat ? Channel.Value : Channel.Saturation;
	}




	// 直交パッドの横軸・縦軸の値を、現在のレイアウトの成分へ TwoWay で束ねる。色相軸は 0–1 の正規化(HueFraction01)、彩度・明度軸はそのまま 0–1。レイアウト切り替えのたびに束縛し直す。
	private void ConfigureCartPad()
	{
		BindingOperations.SetBinding(HvPad, PlanarPad.XValueProperty, MakeViewModelBinding(PadAxisPath(_cartX), BindingMode.TwoWay));
		BindingOperations.SetBinding(HvPad, PlanarPad.YValueProperty, MakeViewModelBinding(PadAxisPath(_cartY), BindingMode.TwoWay));

		// アクセシビリティ名は軸の成分が変わるため、言語非依存の記号で「横 × 縦」を当てる。
		AutomationProperties.SetName(HvPad, $"{ChannelLetter(_cartX)} × {ChannelLetter(_cartY)}");
	}




	// 縦スライダーを、指定の成分へ束ねて値域・刻み・背景ブラシを整える。色相は 0–360 度・刻み1、彩度・明度は 0–1・刻み0.01。背景は各成分の縦向きグラデーション。レイアウト切り替えのたびに設定し直す。
	private void ConfigureVerticalSlider(GradientSlider slider, Channel channel)
	{
		// 値域・刻みを先に整えてから値を束ねる。色相は 0–360 度のため、旧い値域(0–1 等)が残ったまま値を束ねると最大値でクランプされ、束ね戻しで色相を壊すのを避ける。
		if (channel == Channel.Hue)
		{
			slider.Minimum = 0.0;
			slider.Maximum = 360.0;
			slider.StepFrequency = 1.0;
			slider.LargeChange = 10.0;
		}
		else
		{
			slider.Minimum = 0.0;
			slider.Maximum = 1.0;
			slider.StepFrequency = 0.01;
			slider.LargeChange = 0.1;
		}

		BindingOperations.SetBinding(slider, RangeBase.ValueProperty, MakeViewModelBinding(SliderValuePath(channel), BindingMode.TwoWay));
		BindingOperations.SetBinding(slider, GradientSlider.TrackBrushProperty, MakeViewModelBinding(SliderBrushPath(channel), BindingMode.OneWay));
		AutomationProperties.SetName(slider, ChannelLetter(channel));
	}




	// ViewModel をソースにした束縛を作る。コードビハインドから DP へ動的に束ねるのに使う。
	private Binding MakeViewModelBinding(string path, BindingMode mode)
	{
		return new Binding
		{
			Path = new PropertyPath(path),
			Source = ViewModel,
			Mode = mode,
		};
	}




	// 第1線形軸(Channel.Saturation が指す側)の 0–1 本体プロパティ名。HSV は彩度、HSL は彩度、HWB は白み。
	private string FirstLinearPath()
	{
		return _mode switch
		{
			CenterMode.Hsl => nameof(ColorEditorViewModel.HslSaturation01),
			CenterMode.Hwb => nameof(ColorEditorViewModel.Whiteness01),
			_ => nameof(ColorEditorViewModel.Saturation01),
		};
	}




	// 第2線形軸(Channel.Value が指す側)が直交パッド・縦スライダーで束ねる 0–1 プロパティ名。HSV は明度、HSL は輝度、HWB は黒みだが、HWB は「上ほど明るい(黒みが少ない)」向きに合わせるため、黒みそのものではなく上下を反転した HwbPadY(=1−黒み)を束ねる。これにより直交パッドのつまみ・縦スライダーが正方形(HwbPlane)や他の表色系と同じ向きになる。色面の RGB へ戻すときは SampleCartField が 1−HwbPadY で黒みを復元する。
	private string SecondLinearPath()
	{
		return _mode switch
		{
			CenterMode.Hsl => nameof(ColorEditorViewModel.Lightness01),
			CenterMode.Hwb => nameof(ColorEditorViewModel.HwbPadY),
			_ => nameof(ColorEditorViewModel.Value01),
		};
	}




	// パッドの軸が束ねる ViewModel プロパティ名。色相は 0–1 の HueFraction01。Saturation・Value 軸は現在のモードの第1・第2線形軸へ振り分ける。
	private string PadAxisPath(Channel channel)
	{
		return channel switch
		{
			Channel.Hue => nameof(ColorEditorViewModel.HueFraction01),
			Channel.Saturation => FirstLinearPath(),
			_ => SecondLinearPath(),
		};
	}




	// 縦スライダーの値が束ねる ViewModel プロパティ名。色相は 0–360 度の H。Saturation・Value は現在のモードの第1・第2線形軸へ振り分ける。
	private string SliderValuePath(Channel channel)
	{
		return channel switch
		{
			Channel.Hue => nameof(ColorEditorViewModel.H),
			Channel.Saturation => FirstLinearPath(),
			_ => SecondLinearPath(),
		};
	}




	// 縦スライダーの背景ブラシが束ねる ViewModel プロパティ名。各成分の縦向きグラデーション。Saturation・Value は現在のモードの第1・第2線形軸の縦向きブラシへ振り分ける。色相のブラシは全モード共通。
	private string SliderBrushPath(Channel channel)
	{
		return channel switch
		{
			Channel.Hue => nameof(ColorEditorViewModel.HueTrackBrushVertical),
			Channel.Saturation => _mode switch
			{
				CenterMode.Hsl => nameof(ColorEditorViewModel.HslSaturationTrackBrushVertical),
				CenterMode.Hwb => nameof(ColorEditorViewModel.WhitenessTrackBrushVertical),
				_ => nameof(ColorEditorViewModel.SaturationTrackBrushVertical),
			},
			_ => _mode switch
			{
				CenterMode.Hsl => nameof(ColorEditorViewModel.LightnessTrackBrushVertical),
				CenterMode.Hwb => nameof(ColorEditorViewModel.HwbPadYTrackBrushVertical),
				_ => nameof(ColorEditorViewModel.ValueTrackBrushVertical),
			},
		};
	}




	// 成分を表す言語非依存の1文字。アクセシビリティ名に使う。第1線形軸は HSV/HSL の彩度 S・HWB の白み W、第2線形軸は HSV の明度 V・HSL の輝度 L・HWB の黒み B。
	private string ChannelLetter(Channel channel)
	{
		return channel switch
		{
			Channel.Hue => "H",
			Channel.Saturation => _mode == CenterMode.Hwb ? "W" : "S",
			_ => _mode switch
			{
				CenterMode.Hsl => "L",
				CenterMode.Hwb => "B",
				_ => "V",
			},
		};
	}




	// 円盤の大きさが変わったら、円盤画像を作り直し、つまみ位置を合わせる。円盤を初めて表示したときの寸法確定もこの経路で拾う。
	private void OnWheelDiskSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateDisk();
		UpdateWheelThumb();
	}




	// 直交パッドの大きさが変わったら、下地画像を作り直す。つまみ位置は PlanarPad が束縛から合わせる。パッドを初めて表示したときの寸法確定もこの経路で拾う。
	private void OnHvPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateCart();
	}




	// 直交パッドの下地画像を、現在の固定成分・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。HSV モードで直交パッドレイアウトのときだけ作る。配置(2軸の組)に応じて描画を選び、2軸に取らない固定成分(彩度/明度/色相)を一定値として渡す。同じ画素サイズ・固定成分・設定なら作り直さない(レイアウト切り替え時は ApplyLayout がキャッシュを無効化する)。2軸はパッド全面に渡って変わるため鍵に含めない。
	private void RegenerateCart()
	{
		if (!_cartHost)
		{
			return;
		}

		if (HvPad.ActualWidth <= 0.0 || HvPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixelWidth = (int)Math.Round(HvPad.ActualWidth * scale);
		int pixelHeight = (int)Math.Round(HvPad.ActualHeight * scale);

		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		double fixedComponent = CartFixedComponentValue();

		if (pixelWidth == _cartPixelWidth && pixelHeight == _cartPixelHeight && fixedComponent == _cartFixed && snap == _cartSnap)
		{
			return;
		}

		_cartPixelWidth = pixelWidth;
		_cartPixelHeight = pixelHeight;
		_cartFixed = fixedComponent;
		_cartSnap = snap;

		// 2軸の組から下地画像を選ぶ。固定成分の値は色相軸=Hue なら色相、それ以外は第1線形軸(彩度/白み)または第2線形軸(明度/輝度/黒み)で、いずれも CartFixedComponentValue が返す。モードで HSV/HSL/HWB の対の描画へ振り分ける。Value 軸は HWB では黒み、Saturation 軸は HWB では白みを指す。
		if (_cartX == Channel.Hue && _cartY == Channel.Value)
		{
			HvImage.Source = _mode switch
			{
				CenterMode.Hsl => HueLightnessPlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
				CenterMode.Hwb => HueBlacknessPlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
				_ => HueValuePlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
			};
		}
		else if (_cartX == Channel.Hue && _cartY == Channel.Saturation)
		{
			HvImage.Source = _mode switch
			{
				CenterMode.Hsl => HslHueSatPlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
				CenterMode.Hwb => HueWhitenessPlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
				_ => HueSatPlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
			};
		}
		else
		{
			HvImage.Source = _mode switch
			{
				CenterMode.Hsl => SlPlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
				CenterMode.Hwb => HwbPlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
				_ => SvPlane.Create(pixelWidth, pixelHeight, fixedComponent, snap),
			};
		}
	}




	// 直交パッドの固定成分(2軸に取らない側)の現在値。色制限・寸法とともに画像キャッシュの鍵に使う。Saturation・Value は現在のモードの第1線形軸(彩度/白み)・第2線形軸(明度/輝度/黒み)を読む。
	private double CartFixedComponentValue()
	{
		return CartFixedChannel() switch
		{
			Channel.Hue => ViewModel.H,
			Channel.Saturation => FirstLinear01(),
			_ => SecondLinear01(),
		};
	}




	// 直交パッドのレンズのサンプラー。パッド局所座標(画素)を横軸の割合・縦軸の割合(下0→上1)へ写し、各軸の成分と固定成分を組み合わせた色を現在のモードの表色系で返す。固定成分(軸に取らない側)は現在値を使い、HSV/HSL で彩度・明度/輝度の対応プロパティを読む。色制限が有効ならその丸めも掛ける。パッドの外は透明を返し、レンズの縁では下地のカード色が透ける。各描画と同じ写し方で、表示と一致させる。
	private Color SampleCartField(double x, double y)
	{
		double width = HvPad.ActualWidth;
		double height = HvPad.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double fractionX = x / width;
		double fractionY = 1.0 - (y / height);

		if (fractionX < 0.0 || fractionX > 1.0 || fractionY < 0.0 || fractionY > 1.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double hue = AxisFraction(Channel.Hue, fractionX, fractionY, ViewModel.H / 360.0) * 360.0;
		byte r;
		byte g;
		byte b;

		if (_mode == CenterMode.Hwb)
		{
			// HWB は第2軸を HwbPadY(=1−黒み、上ほど明るい)で扱うため、軸の割合から黒みを 1−割合 で復元する。第1軸の白みは上下反転しない。固定成分(軸でない側)の fallback も同じ向き(白み=Whiteness01、HwbPadY)で渡す。
			double whiteness = AxisFraction(Channel.Saturation, fractionX, fractionY, ViewModel.Whiteness01);
			double padY = AxisFraction(Channel.Value, fractionX, fractionY, ViewModel.HwbPadY);
			(r, g, b) = ColorConversion.HwbToRgb(hue, whiteness, 1.0 - padY);
		}
		else
		{
			double first = AxisFraction(Channel.Saturation, fractionX, fractionY, FirstLinear01());
			double second = AxisFraction(Channel.Value, fractionX, fractionY, SecondLinear01());
			(r, g, b) = LinearRgb(hue, first, second);
		}

		SnapSettings snap = ViewModel.CurrentSnap;

		if (snap.Mode != ColorLimitMode.None)
		{
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
		}

		return Color.FromArgb(0xFF, r, g, b);
	}




	// 指定成分が直交パッドのどちらの軸に割り当てられているかを見て、その軸の割合(0–1)を返す。どちらの軸でもない固定成分のときは fallback(現在値の正規化)を返す。色相は呼び出し側で 0–1 を 0–360 度へ直す。
	private double AxisFraction(Channel channel, double fractionX, double fractionY, double fallback)
	{
		if (_cartX == channel)
		{
			return fractionX;
		}

		if (_cartY == channel)
		{
			return fractionY;
		}

		return fallback;
	}




	// 第1線形軸の現在値。HSV/HSL は彩度、HWB は白み。円盤・直交パッドの軸/固定成分の読み取りに使う。
	private double FirstLinear01()
	{
		return _mode switch
		{
			CenterMode.Hsl => ViewModel.HslSaturation01,
			CenterMode.Hwb => ViewModel.Whiteness01,
			_ => ViewModel.Saturation01,
		};
	}




	// 第2線形軸の現在値。HSV は明度、HSL は輝度、HWB は黒み。
	private double SecondLinear01()
	{
		return _mode switch
		{
			CenterMode.Hsl => ViewModel.Lightness01,
			CenterMode.Hwb => ViewModel.Blackness01,
			_ => ViewModel.Value01,
		};
	}




	// 第1線形軸へ値を書き込む。
	private void SetFirstLinear01(double value)
	{
		switch (_mode)
		{
			case CenterMode.Hsl:
				ViewModel.HslSaturation01 = value;
				break;

			case CenterMode.Hwb:
				ViewModel.Whiteness01 = value;
				break;

			default:
				ViewModel.Saturation01 = value;
				break;
		}
	}




	// 第2線形軸へ値を書き込む。
	private void SetSecondLinear01(double value)
	{
		switch (_mode)
		{
			case CenterMode.Hsl:
				ViewModel.Lightness01 = value;
				break;

			case CenterMode.Hwb:
				ViewModel.Blackness01 = value;
				break;

			default:
				ViewModel.Value01 = value;
				break;
		}
	}




	// 色相と第1線形軸・第2線形軸の値から、現在のモードの表色系で RGB を作る。HSV は HsvToRgb(色相・彩度・明度)、HSL は HslToRgb(色相・彩度・輝度)、HWB は HwbToRgb(色相・白み・黒み)。
	private (byte, byte, byte) LinearRgb(double hue, double first, double second)
	{
		return _mode switch
		{
			CenterMode.Hsl => ColorConversion.HslToRgb(hue, first, second),
			CenterMode.Hwb => ColorConversion.HwbToRgb(hue, first, second),
			_ => ColorConversion.HsvToRgb(hue, first, second),
		};
	}




	// 円盤の半径成分(中心からの距離が表す側)の現在値。半径=第2線形軸(明度/輝度/黒み)か第1線形軸(彩度/白み)かで分ける。
	private double DiskRadiusComponent()
	{
		return _diskRadiusIsValue ? SecondLinear01() : FirstLinear01();
	}




	// 円盤の半径成分へ値を書き込む。ドラッグ・矢印操作の反映に使う。
	private void SetDiskRadiusComponent(double value)
	{
		if (_diskRadiusIsValue)
		{
			SetSecondLinear01(value);
		}
		else
		{
			SetFirstLinear01(value);
		}
	}




	// 円盤の固定成分(縦スライダーが司る、半径に取らない残り1成分)の現在値。半径=第2線形軸なら第1線形軸、半径=第1線形軸なら第2線形軸。
	private double DiskFixedComponent()
	{
		return _diskRadiusIsValue ? FirstLinear01() : SecondLinear01();
	}




	// 円盤の色。角度=色相・半径成分・固定成分から、現在のモードの表色系で RGB を作る。半径=第2線形軸のとき固定成分は第1線形軸、半径=第1線形軸のとき固定成分は第2線形軸。
	private (byte, byte, byte) DiskRgb(double hue, double radiusComponent, double fixedComponent)
	{
		return _diskRadiusIsValue
			? LinearRgb(hue, fixedComponent, radiusComponent)
			: LinearRgb(hue, radiusComponent, fixedComponent);
	}




	// 円盤画像を、現在の固定成分・色制限設定・円盤の大きさ・表示倍率に合わせて作り直す。円盤ホストのときだけ作る。半径に明度/輝度を取る配置では彩度が、半径に彩度を取る配置では明度/輝度が固定成分。同じ画素サイズ・固定成分・設定なら作り直さない(レイアウト切り替え時は ApplyLayout がキャッシュを無効化する)。色相は円盤の全方位に現れるため鍵に含めない。
	private void RegenerateDisk()
	{
		if (!_diskHost)
		{
			return;
		}

		if (WheelDisk.ActualWidth <= 0.0 || WheelDisk.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(Math.Min(WheelDisk.ActualWidth, WheelDisk.ActualHeight) * scale);

		if (pixels <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		double fixedComponent = DiskFixedComponent();

		if (pixels == _diskPixels && fixedComponent == _diskFixed && snap == _diskSnap)
		{
			return;
		}

		_diskPixels = pixels;
		_diskFixed = fixedComponent;
		_diskSnap = snap;

		// 半径=第2線形軸(明度/輝度/黒み)か第1線形軸(彩度/白み)かと、モードの表色系で対の描画を選ぶ。固定成分は半径に取らない側。
		WheelImage.Source = _mode switch
		{
			CenterMode.Hsl => _diskRadiusIsValue
				? HueLightnessDisk.Create(pixels, pixels, fixedComponent, snap)
				: HslHueSatDisk.Create(pixels, pixels, fixedComponent, snap),
			CenterMode.Hwb => _diskRadiusIsValue
				? HwbBlacknessDisk.Create(pixels, pixels, fixedComponent, snap)
				: HwbWhitenessDisk.Create(pixels, pixels, fixedComponent, snap),
			_ => _diskRadiusIsValue
				? HueValueDisk.Create(pixels, pixels, fixedComponent, snap)
				: HueSatDisk.Create(pixels, pixels, fixedComponent, snap),
		};
	}




	// 円盤のつまみを、現在の色相(角度)と半径成分(中心からの半径の割合)の位置へ移す。半径成分は配置に応じて彩度または明度。角度の取り方は円盤描画と同じ RingGeometry の規約に従い、円盤の色とつまみがずれない。寸法が無いときは何もしない。
	private void UpdateWheelThumb()
	{
		if (WheelThumbOffset is null)
		{
			return;
		}

		double width = WheelDisk.ActualWidth;
		double height = WheelDisk.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		double maxRadius = Math.Min(width, height) / 2.0;
		double radiusComponent = DiskRadiusComponent();
		double radius = Math.Clamp(radiusComponent, 0.0, 1.0) * maxRadius;
		Point offset = RingGeometry.OffsetForValue(radius, ViewModel.H);
		WheelThumbOffset.X = offset.X;
		WheelThumbOffset.Y = offset.Y;
		WheelThumbDarkOffset.X = offset.X;
		WheelThumbDarkOffset.Y = offset.Y;
	}




	private void OnWheelPointerPressed(object sender, PointerRoutedEventArgs e)
	{
		double width = WheelDisk.ActualWidth;
		double height = WheelDisk.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		Point position = e.GetCurrentPoint(WheelDisk).Position;
		double maxRadius = Math.Min(width, height) / 2.0;
		double dx = position.X - (width / 2.0);
		double dy = position.Y - (height / 2.0);

		// 円の外を押したときは操作も捕捉もしない。ドラッグで縁の外へ出たぶんは彩度を縁(1)へ留める。
		if (Math.Sqrt((dx * dx) + (dy * dy)) > maxRadius)
		{
			return;
		}

		_wheelDragging = WheelDisk.CapturePointer(e.Pointer);
		WheelDisk.Focus(FocusState.Pointer);
		ApplyWheelPoint(dx, dy, maxRadius);

		if (_wheelDragging)
		{
			BeginWheelLens();
		}

		e.Handled = true;
	}




	private void OnWheelPointerMoved(object sender, PointerRoutedEventArgs e)
	{
		if (!_wheelDragging)
		{
			return;
		}

		double width = WheelDisk.ActualWidth;
		double height = WheelDisk.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		Point position = e.GetCurrentPoint(WheelDisk).Position;
		double maxRadius = Math.Min(width, height) / 2.0;
		ApplyWheelPoint(position.X - (width / 2.0), position.Y - (height / 2.0), maxRadius);
		UpdateWheelLens();
		e.Handled = true;
	}




	private void OnWheelPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (!_wheelDragging)
		{
			return;
		}

		_wheelDragging = false;
		EndWheelLens();
		WheelDisk.ReleasePointerCapture(e.Pointer);
		e.Handled = true;
	}




	private void OnWheelPointerCaptureLost(object sender, PointerRoutedEventArgs e)
	{
		_wheelDragging = false;
		EndWheelLens();
	}




	// 円盤上の中心からの相対位置(dx, dy)を、色相(角度)と半径成分(半径の割合)へ写して色1へ反映する。半径成分は配置に応じて彩度または明度。角度の取り方は円盤描画・RingGeometry と同じ規約。半径が円を越えたぶんは半径成分を1へ留める。
	private void ApplyWheelPoint(double dx, double dy, double maxRadius)
	{
		double radius = Math.Sqrt((dx * dx) + (dy * dy));
		double radiusComponent = maxRadius > 0.0 ? Math.Clamp(radius / maxRadius, 0.0, 1.0) : 0.0;
		ViewModel.H = RingGeometry.ValueFromPoint(dx, dy);
		SetDiskRadiusComponent(radiusComponent);
	}




	// 矢印キーで円盤のつまみを画面上で1段ずつ動かす。移動方向は画面に固定(右=右、上=上)で、移動後の点を色相・彩度へ写し直す。回転を持たない円盤のため、2次元パッドのような逆回転は要らない。1段の量は円盤の一辺の1%(最低1DIP)。
	private void OnWheelKeyDown(object sender, KeyRoutedEventArgs e)
	{
		double width = WheelDisk.ActualWidth;
		double height = WheelDisk.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		double step = Math.Max(1.0, Math.Min(width, height) * 0.01);
		double deltaX = 0.0;
		double deltaY = 0.0;

		switch (e.Key)
		{
			case VirtualKey.Right: deltaX = step; break;
			case VirtualKey.Left: deltaX = -step; break;
			case VirtualKey.Up: deltaY = -step; break;
			case VirtualKey.Down: deltaY = step; break;
			default: return;
		}

		double maxRadius = Math.Min(width, height) / 2.0;
		double radiusComponent = DiskRadiusComponent();
		double radius = Math.Clamp(radiusComponent, 0.0, 1.0) * maxRadius;
		Point offset = RingGeometry.OffsetForValue(radius, ViewModel.H);
		ApplyWheelPoint(offset.X + deltaX, offset.Y + deltaY, maxRadius);
		e.Handled = true;
	}




	// 円盤のドラッグ開始時にレンズを出す。つまみ(二重の輪)を隠してレンズへ置き換える。
	private void BeginWheelLens()
	{
		if (_wheelLens is null)
		{
			return;
		}

		_wheelLens.Begin();
		WheelThumb.Opacity = 0.0;
		WheelThumbDark.Opacity = 0.0;
		UpdateWheelLens();
	}




	// レンズを現在のつまみ位置(円盤局所座標)へ追従させ、その点まわりの色面を映し直す。
	private void UpdateWheelLens()
	{
		if (_wheelLens is null || !_wheelLens.IsActive)
		{
			return;
		}

		double width = WheelDisk.ActualWidth;
		double height = WheelDisk.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		double maxRadius = Math.Min(width, height) / 2.0;
		double radiusComponent = DiskRadiusComponent();
		double radius = Math.Clamp(radiusComponent, 0.0, 1.0) * maxRadius;
		Point offset = RingGeometry.OffsetForValue(radius, ViewModel.H);
		_wheelLens.Update(SampleWheelField, (width / 2.0) + offset.X, (height / 2.0) + offset.Y);
	}




	// 円盤のドラッグ終了時にレンズを退場させ、つまみを戻す。
	private void EndWheelLens()
	{
		if (_wheelLens is null)
		{
			return;
		}

		_wheelLens.End();
		WheelThumb.Opacity = 1.0;
		WheelThumbDark.Opacity = 1.0;
	}




	// 円盤のレンズのサンプラー。円盤局所座標(DIP)の点を、角度=色相・中心からの距離の割合=半径成分へ写し、固定成分を添えた HSV 色を返す。半径成分・固定成分は配置に応じて彩度↔明度が入れ替わる。色制限が有効ならその丸めも掛ける。円の外は透明を返し、レンズの縁では下地のカード色が透ける。円盤描画と同じ写し方で、表示と一致させる。
	private Color SampleWheelField(double x, double y)
	{
		double width = WheelDisk.ActualWidth;
		double height = WheelDisk.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double dx = x - (width / 2.0);
		double dy = y - (height / 2.0);
		double maxRadius = Math.Min(width, height) / 2.0;
		double radius = Math.Sqrt((dx * dx) + (dy * dy));

		if (radius > maxRadius)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double hue = RingGeometry.ValueFromPoint(dx, dy);
		double radiusComponent = maxRadius > 0.0 ? Math.Clamp(radius / maxRadius, 0.0, 1.0) : 0.0;
		(byte r, byte g, byte b) = DiskRgb(hue, radiusComponent, DiskFixedComponent());
		SnapSettings snap = ViewModel.CurrentSnap;

		if (snap.Mode != ColorLimitMode.None)
		{
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
		}

		return Color.FromArgb(0xFF, r, g, b);
	}




	// 隅のレイアウトボタンのアイコンを、現在選んでいるレイアウトの縮小見本に差し替える。アイコン自体が現在の状態表示を兼ねる。HSV・HSL のときだけそれぞれのボタンへ、それ以外ではどちらのボタンも隠れるため更新しない。HSL の円盤・パッドは輝度を最大(1)にすると白一色になり色相差が見えないため、彩度が司る配置(円盤=絵具円盤・色相×彩度のパッド)は中間の輝度(0.5)で代表させる。
	private void UpdateLayoutPickerIcon()
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(24.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;

		if (_mode == CenterMode.Hsv)
		{
			LayoutPickerIcon.Source = ViewModel.HsvLayoutIndex switch
			{
				1 => HueSatDisk.Create(pixels, pixels, 1.0, snap),
				2 => HueValuePlane.Create(pixels, pixels, 1.0, snap),
				3 => HueValueDisk.Create(pixels, pixels, 1.0, snap),
				4 => HueSatPlane.Create(pixels, pixels, 1.0, snap),
				5 => SvPlane.Create(pixels, pixels, 0.0, snap),
				_ => CreateRingThumbnail(pixels, snap),
			};
		}
		else if (_mode == CenterMode.Hsl)
		{
			HslLayoutPickerIcon.Source = ViewModel.HslLayoutIndex switch
			{
				0 => LayoutThumbnail.RingWithShape(pixels, 1.0 / Math.Sqrt(2.0), snap, s => SlPlane.Create(s, s, 0.0, snap)),
				1 => HslHueSatDisk.Create(pixels, pixels, 0.5, snap),
				2 => HueLightnessPlane.Create(pixels, pixels, 1.0, snap),
				3 => HueLightnessDisk.Create(pixels, pixels, 1.0, snap),
				4 => HslHueSatPlane.Create(pixels, pixels, 0.5, snap),
				5 => LayoutThumbnail.ShapeWithBar(pixels, snap, s => SlPlane.Create(s, s, 0.0, snap)),
				7 => LayoutThumbnail.ShapeWithBar(pixels, snap, s => HslTriangle.Create(s, s, 0.0, snap, 0.0, fillBox: true)),
				_ => LayoutThumbnail.RingWithShape(pixels, 1.0, snap, s => HslTriangle.Create(s, s, 0.0, snap, 0.0)),
			};
		}
		else if (_mode == CenterMode.Hwb)
		{
			// 円盤・直交パッドは固定成分を 0 にして純色の側を見せる(半径=白み・B=0 で純色→白、半径=黒み・W=0 で純色→黒)。正方形は色相0度の赤、三角形は HWB の三角形そのもの。
			HwbLayoutPickerIcon.Source = ViewModel.HwbLayoutIndex switch
			{
				1 => HwbWhitenessDisk.Create(pixels, pixels, 0.0, snap),
				2 => HueBlacknessPlane.Create(pixels, pixels, 0.0, snap),
				3 => HwbBlacknessDisk.Create(pixels, pixels, 0.0, snap),
				4 => HueWhitenessPlane.Create(pixels, pixels, 0.0, snap),
				5 => LayoutThumbnail.ShapeWithBar(pixels, snap, s => HwbPlane.Create(s, s, 0.0, snap)),
				6 => LayoutThumbnail.RingWithShape(pixels, 1.0, snap, s => HwbTriangle.Create(s, s, 0.0, snap, 0.0)),
				7 => LayoutThumbnail.ShapeWithBar(pixels, snap, s => HwbTriangle.Create(s, s, 0.0, snap, 0.0, fillBox: true)),
				_ => LayoutThumbnail.RingWithShape(pixels, 1.0 / Math.Sqrt(2.0), snap, s => HwbPlane.Create(s, s, 0.0, snap)),
			};
		}
	}




	// レイアウト選択のフライアウトを開くときに、全レイアウトのサムネイルを今の色制限で作り直し、現在の選択を縁取りで示す。サムネイルは各レイアウトの2次元面の代表(固定成分は最大、彩度×明度の正方形だけは色相0度の赤)で描く。
	private void OnLayoutFlyoutOpening(object sender, object e)
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(56.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;

		RingThumbImage.Source = CreateRingThumbnail(pixels, snap);
		WheelThumbImage.Source = HueSatDisk.Create(pixels, pixels, 1.0, snap);
		PlaneThumbImage.Source = HueValuePlane.Create(pixels, pixels, 1.0, snap);
		ValueWheelThumbImage.Source = HueValueDisk.Create(pixels, pixels, 1.0, snap);
		HueSatPlaneThumbImage.Source = HueSatPlane.Create(pixels, pixels, 1.0, snap);
		SvBarThumbImage.Source = SvPlane.Create(pixels, pixels, 0.0, snap);

		var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
		var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
		RingThumbBorder.BorderBrush = ViewModel.HsvLayoutIndex == 0 ? accent : clear;
		WheelThumbBorder.BorderBrush = ViewModel.HsvLayoutIndex == 1 ? accent : clear;
		PlaneThumbBorder.BorderBrush = ViewModel.HsvLayoutIndex == 2 ? accent : clear;
		ValueWheelThumbBorder.BorderBrush = ViewModel.HsvLayoutIndex == 3 ? accent : clear;
		HueSatPlaneThumbBorder.BorderBrush = ViewModel.HsvLayoutIndex == 4 ? accent : clear;
		SvBarThumbBorder.BorderBrush = ViewModel.HsvLayoutIndex == 5 ? accent : clear;
	}




	// 色相リング+パッドのレイアウトを表すサムネイル。色相環の帯を太めに描いた正方形のビットマップを返す。中央の正方形パッドは小さすぎて潰れるため、帯だけで示す。
	private static WriteableBitmap CreateRingThumbnail(int pixels, SnapSettings snap)
	{
		double outer = (pixels / 2.0) - 1.0;
		double inner = outer * 0.62;
		return HueWheel.Create(pixels, pixels, inner, outer, snap);
	}




	private void OnPickRingSquareLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HsvLayoutIndex = 0;
		LayoutPickerFlyout.Hide();
	}




	private void OnPickWheelLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HsvLayoutIndex = 1;
		LayoutPickerFlyout.Hide();
	}




	private void OnPickPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HsvLayoutIndex = 2;
		LayoutPickerFlyout.Hide();
	}




	private void OnPickValueWheelLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HsvLayoutIndex = 3;
		LayoutPickerFlyout.Hide();
	}




	private void OnPickHueSatPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HsvLayoutIndex = 4;
		LayoutPickerFlyout.Hide();
	}




	private void OnPickSvBarLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HsvLayoutIndex = 5;
		LayoutPickerFlyout.Hide();
	}




	// HSL のレイアウト選択フライアウトを開くときに、各レイアウトのサムネイルを今の色制限で作り直し、現在の選択を縁取りで示す。サムネイルは各2次元面の代表で描く。三角形を中央に置く2種(色相リング+三角形・三角形+色相バー)は三角形そのもの、彩度・輝度の正方形を置く2種(色相リング+正方形・彩度×輝度+色相バー)は正方形そのもので、形で見分けられるようにする。彩度が司る配置(絵具円盤・色相×彩度のパッド)は輝度を中間(0.5)に、輝度が司る配置(輝度の円盤・色相×輝度のパッド)は彩度を最大(1)に取って色相差を映えさせ、正方形は色相0度の赤で描く。
	private void OnHslLayoutFlyoutOpening(object sender, object e)
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(56.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;

		HslRingThumbImage.Source = LayoutThumbnail.RingWithShape(pixels, 1.0, snap, s => HslTriangle.Create(s, s, 0.0, snap, 0.0));
		HslRingSquareThumbImage.Source = LayoutThumbnail.RingWithShape(pixels, 1.0 / Math.Sqrt(2.0), snap, s => SlPlane.Create(s, s, 0.0, snap));
		HslTriangleBarThumbImage.Source = LayoutThumbnail.ShapeWithBar(pixels, snap, s => HslTriangle.Create(s, s, 0.0, snap, 0.0, fillBox: true));
		HslWheelThumbImage.Source = HslHueSatDisk.Create(pixels, pixels, 0.5, snap);
		HslLightnessWheelThumbImage.Source = HueLightnessDisk.Create(pixels, pixels, 1.0, snap);
		HslLightnessPlaneThumbImage.Source = HueLightnessPlane.Create(pixels, pixels, 1.0, snap);
		HslHueSatPlaneThumbImage.Source = HslHueSatPlane.Create(pixels, pixels, 0.5, snap);
		HslSlBarThumbImage.Source = LayoutThumbnail.ShapeWithBar(pixels, snap, s => SlPlane.Create(s, s, 0.0, snap));

		var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
		var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
		HslRingThumbBorder.BorderBrush = ViewModel.HslLayoutIndex == 6 ? accent : clear;
		HslRingSquareThumbBorder.BorderBrush = ViewModel.HslLayoutIndex == 0 ? accent : clear;
		HslTriangleBarThumbBorder.BorderBrush = ViewModel.HslLayoutIndex == 7 ? accent : clear;
		HslWheelThumbBorder.BorderBrush = ViewModel.HslLayoutIndex == 1 ? accent : clear;
		HslLightnessWheelThumbBorder.BorderBrush = ViewModel.HslLayoutIndex == 3 ? accent : clear;
		HslLightnessPlaneThumbBorder.BorderBrush = ViewModel.HslLayoutIndex == 2 ? accent : clear;
		HslHueSatPlaneThumbBorder.BorderBrush = ViewModel.HslLayoutIndex == 4 ? accent : clear;
		HslSlBarThumbBorder.BorderBrush = ViewModel.HslLayoutIndex == 5 ? accent : clear;
	}




	private void OnPickHslRingTriangleLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HslLayoutIndex = 6;
		HslLayoutPickerFlyout.Hide();
	}




	private void OnPickHslWheelLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HslLayoutIndex = 1;
		HslLayoutPickerFlyout.Hide();
	}




	private void OnPickHslLightnessWheelLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HslLayoutIndex = 3;
		HslLayoutPickerFlyout.Hide();
	}




	private void OnPickHslLightnessPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HslLayoutIndex = 2;
		HslLayoutPickerFlyout.Hide();
	}




	private void OnPickHslHueSatPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HslLayoutIndex = 4;
		HslLayoutPickerFlyout.Hide();
	}




	private void OnPickHslBarLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HslLayoutIndex = 5;
		HslLayoutPickerFlyout.Hide();
	}




	private void OnPickHslRingSquareLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HslLayoutIndex = 0;
		HslLayoutPickerFlyout.Hide();
	}




	private void OnPickHslTriangleBarLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HslLayoutIndex = 7;
		HslLayoutPickerFlyout.Hide();
	}




	// HWB のレイアウト選択フライアウトを開くときに、各レイアウトのサムネイルを今の色制限で作り直し、現在の選択を縁取りで示す。三角形を中央に置く2種は HWB の三角形そのもの、白み黒みの正方形を置く2種は正方形そのもので形で見分けられるようにする。円盤・直交パッドは固定成分を 0 にして純色の側(半径=白み・B=0 で純色→白、半径=黒み・W=0 で純色→黒)を見せ、正方形は色相0度の赤で描く。
	private void OnHwbLayoutFlyoutOpening(object sender, object e)
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(56.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;

		HwbRingSquareThumbImage.Source = LayoutThumbnail.RingWithShape(pixels, 1.0 / Math.Sqrt(2.0), snap, s => HwbPlane.Create(s, s, 0.0, snap));
		HwbRingTriangleThumbImage.Source = LayoutThumbnail.RingWithShape(pixels, 1.0, snap, s => HwbTriangle.Create(s, s, 0.0, snap, 0.0));
		HwbTriangleBarThumbImage.Source = LayoutThumbnail.ShapeWithBar(pixels, snap, s => HwbTriangle.Create(s, s, 0.0, snap, 0.0, fillBox: true));
		HwbWheelThumbImage.Source = HwbWhitenessDisk.Create(pixels, pixels, 0.0, snap);
		HwbBlacknessWheelThumbImage.Source = HwbBlacknessDisk.Create(pixels, pixels, 0.0, snap);
		HwbBlacknessPlaneThumbImage.Source = HueBlacknessPlane.Create(pixels, pixels, 0.0, snap);
		HwbWhitenessPlaneThumbImage.Source = HueWhitenessPlane.Create(pixels, pixels, 0.0, snap);
		HwbWbBarThumbImage.Source = LayoutThumbnail.ShapeWithBar(pixels, snap, s => HwbPlane.Create(s, s, 0.0, snap));

		var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
		var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
		HwbRingSquareThumbBorder.BorderBrush = ViewModel.HwbLayoutIndex == 0 ? accent : clear;
		HwbRingTriangleThumbBorder.BorderBrush = ViewModel.HwbLayoutIndex == 6 ? accent : clear;
		HwbTriangleBarThumbBorder.BorderBrush = ViewModel.HwbLayoutIndex == 7 ? accent : clear;
		HwbWheelThumbBorder.BorderBrush = ViewModel.HwbLayoutIndex == 1 ? accent : clear;
		HwbBlacknessWheelThumbBorder.BorderBrush = ViewModel.HwbLayoutIndex == 3 ? accent : clear;
		HwbBlacknessPlaneThumbBorder.BorderBrush = ViewModel.HwbLayoutIndex == 2 ? accent : clear;
		HwbWhitenessPlaneThumbBorder.BorderBrush = ViewModel.HwbLayoutIndex == 4 ? accent : clear;
		HwbWbBarThumbBorder.BorderBrush = ViewModel.HwbLayoutIndex == 5 ? accent : clear;
	}




	private void OnPickHwbRingSquareLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HwbLayoutIndex = 0;
		HwbLayoutPickerFlyout.Hide();
	}




	private void OnPickHwbRingTriangleLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HwbLayoutIndex = 6;
		HwbLayoutPickerFlyout.Hide();
	}




	private void OnPickHwbTriangleBarLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HwbLayoutIndex = 7;
		HwbLayoutPickerFlyout.Hide();
	}




	private void OnPickHwbWhitenessWheelLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HwbLayoutIndex = 1;
		HwbLayoutPickerFlyout.Hide();
	}




	private void OnPickHwbBlacknessWheelLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HwbLayoutIndex = 3;
		HwbLayoutPickerFlyout.Hide();
	}




	private void OnPickHwbBlacknessPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HwbLayoutIndex = 2;
		HwbLayoutPickerFlyout.Hide();
	}




	private void OnPickHwbWhitenessPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HwbLayoutIndex = 4;
		HwbLayoutPickerFlyout.Hide();
	}




	private void OnPickHwbBarLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.HwbLayoutIndex = 5;
		HwbLayoutPickerFlyout.Hide();
	}




	// 中央のパッドの表示モード。HSV は彩度・明度の正方形、HSL は彩度・輝度の三角形、HWB は白み・黒みの正方形。
	private enum CenterMode
	{
		Hsv,
		Hsl,
		Hwb,
	}




	// 色相環エリアの見せ方。RingSquare は色相リング+パッド。残りは「2次元コントロール+切り出した1成分の縦スライダー」で、円盤系(角度=色相・半径=残り1成分)と直交パッド系(2成分を縦横)に分かれる。HueSatWheel=半径彩度の円盤+明度、HueValueWheel=半径明度の円盤+彩度、HueValuePlane=色相×明度の矩形+彩度、HueSatPlane=色相×彩度の矩形+明度、SvHueBar=彩度×明度の正方形+色相。
	private enum DiskLayout
	{
		RingSquare,
		HueSatWheel,
		HueValuePlane,
		HueValueWheel,
		HueSatPlane,
		SvHueBar,
	}




	// HSL モードの見せ方。HSV の DiskLayout に対応する6種(明度を輝度に読み替えたもの)に、三角形(HSL 双錐の色相断面)を使う2種を加えた8種。RingSquare=色相リング+彩度・輝度の正方形、HueSatWheel=半径彩度の円盤+輝度、HueLightnessPlane=色相×輝度の矩形+彩度、HueLightnessWheel=半径輝度の円盤+彩度、HueSatPlane=色相×彩度の矩形+輝度、SlHueBar=彩度×輝度の正方形+色相、RingTriangle=色相リング+三角形、TriangleHueBar=三角形+色相の縦スライダー。
	private enum HslLayout
	{
		RingSquare,
		HueSatWheel,
		HueLightnessPlane,
		HueLightnessWheel,
		HueSatPlane,
		SlHueBar,
		RingTriangle,
		TriangleHueBar,
	}




	// HWB モードの見せ方。HSL と構造は同じ8種で、第1線形軸=白み(W)・第2線形軸=黒み(B)に読み替えたもの。RingSquare=色相リング+白み黒みの正方形、HueWhitenessWheel=半径白みの円盤+黒み、HueBlacknessPlane=色相×黒みの矩形+白み、HueBlacknessWheel=半径黒みの円盤+白み、HueWhitenessPlane=色相×白みの矩形+黒み、WbHueBar=白み×黒みの正方形+色相、RingTriangle=色相リング+三角形、TriangleHueBar=三角形+色相の縦スライダー。
	private enum HwbLayout
	{
		RingSquare,
		HueWhitenessWheel,
		HueBlacknessPlane,
		HueBlacknessWheel,
		HueWhitenessPlane,
		WbHueBar,
		RingTriangle,
		TriangleHueBar,
	}




	// HSV の成分。直交パッドの軸割り当てと縦スライダーの成分割り当てに使う。
	private enum Channel
	{
		Hue,
		Saturation,
		Value,
	}
}
