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

// 「LCH」タブの中身。色1(編集中)を色相環(リング)と中央の L-C パッドで編集し、下段の明度・彩度・色相スライダーで精密に詰める。
// 色相環は色相を、L-C パッドは明度(縦)と彩度(横)を担う。L-C 平面は sRGB 色域に収まる部分だけを実色で塗り、色域外はハッチで透かして可視化する。
// 副モードのラジオで OKLCH(OKLab 基準)と CIE LCH(CIELAB 基準)を切り替える。色相環の画像と L-C 平面の画像は、リングの実寸と表示倍率に合わせてコードで用意する。
// 編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class LchTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// 色相環画像を生成した際の画素サイズ・帯の内外半径(画素)・色制限設定・副モード。同じ条件での作り直しを避けるために覚えておく。色相環は表色系で色が変わるため副モードも判定の鍵に含める。
	private int _wheelPixelWidth;
	private int _wheelPixelHeight;
	private double _wheelInnerRadius;
	private double _wheelOuterRadius;
	private SnapSettings _wheelSnap;
	private int _wheelSpaceIndex = -1;

	// L-C 平面の画像を生成した際の画素サイズ・色相・色制限設定・副モード・色域外の見せ方。同じ条件での作り直しを避けるために覚えておく。色相は未生成を表す NaN で初期化する。
	private int _lcPixels = -1;
	private double _lcHue = double.NaN;
	private SnapSettings _lcSnap;
	private int _lcSpaceIndex = -1;
	private GamutOutOfRangeStyle _lcStyle = (GamutOutOfRangeStyle)(-1);

	// ドラッグ中のレンズが読む、生成済みの色相環ホイールと L-C 平面のビットマップサンプラー。色相環のレンズは内側のパッドを、パッドのレンズは外側の色相環を映すため、双方の合成サンプラーがどちらの色面も参照できるよう、生成のたびにここへ控える。画像はドラッグ中は内容が変わらないため据え置きで足りる。
	private BitmapFieldSampler? _wheelSampler;
	private BitmapFieldSampler? _planeSampler;

	// 副モードのラジオを VM の復元値に合わせる間、SelectionChanged が VM を上書きしないようにする。構築中の初期化を無視するため真で始める。
	private bool _spaceSyncing = true;

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)を色相環の一辺の算出に使う。
	private ScrollViewer? _scrollHost;

	// 色相環の一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinRingSide = 180.0;


	public LchTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 復元済みの副モードをラジオに反映する。ここまでの SelectionChanged は _spaceSyncing で無視し、以降の操作だけ VM へ伝える。
		SpaceSelector.SelectedIndex = ViewModel.LchSpaceIndex;
		_spaceSyncing = false;

		// L-C パッドの操作は明度・彩度を二次元でまとめて扱う(色域内への最近傍寄せ)ため、横・縦の個別束縛ではなく ValuesChanged からまとめて VM へ渡す。
		LcPad.ValuesChanged += OnLcPadValuesChanged;

		HueRing.SizeChanged += OnHueRingSizeChanged;

		// 色相環の一辺はスクロール領域の可視高さからスライダー群の高さを引いて決めるため、スライダー群の高さが変わったら算出し直す。
		SliderHost.SizeChanged += OnLayoutMetricChanged;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 表示倍率(DPI)の変化で色相環・L-C 平面の画像を作り直せるよう XamlRoot の変化を購読する。表示先が変わっていれば購読を張り替える。
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// 色相・副モード・色制限モードの変更で L-C 平面や色相環を塗り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
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




	// 色1の色相・副モード・色制限モードが変わったら L-C 平面や色相環を塗り直す。色相が変わるのは L-C 平面のヒレの形だけで、色相環は色相に依らないため作り直さない。副モードが変わると色相環の色も尺度・色域も変わるため双方を作り直す。色制限を切り替えると色相環・L-C 平面の丸めが一斉に変わる。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorEditorViewModel.LchH))
		{
			RegenerateLcPlane();
			return;
		}

		if (e.PropertyName == nameof(ColorEditorViewModel.LchSpaceIndex) || e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap))
		{
			UpdateRingVisuals();
			return;
		}

		// 色域外の見せ方は L-C 平面の色域外の描き方だけを変える。色相環は色域外を持たないため作り直さない。
		if (e.PropertyName == nameof(ColorEditorViewModel.OutOfRangeStyle))
		{
			RegenerateLcPlane();
		}
	}




	// L-C パッドを操作したら、明度・彩度を二次元でまとめて VM へ渡す。色域制限オン時はカーソルに最も近い色域内の点へ寄せる処理が VM 側で行われる。
	private void OnLcPadValuesChanged(object? sender, EventArgs e)
	{
		ViewModel.SetLchPad(LcPad.XValue, LcPad.YValue);
	}




	// OKLCH / CIE LCH の切り替え。選んだ副モードを VM へ伝える。構築中の初期化(_spaceSyncing)は無視する。
	private void OnSpaceChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_spaceSyncing)
		{
			return;
		}

		ViewModel.LchSpaceIndex = SpaceSelector.SelectedIndex;
	}




	// 副モードを OKLCH へ切り替える。貼り付け連動から呼ぶ。SpaceSelector の選択を変えることで表示の切り替えを行う。既に OKLCH のときは選択が変わらず何も起きない。
	public void ShowOklchMode()
	{
		SpaceSelector.SelectedIndex = 0;
	}




	// 副モードを CIE LCH へ切り替える。貼り付け連動から呼ぶ。SpaceSelector の選択を変えることで表示の切り替えを行う。既に CIE LCH のときは選択が変わらず何も起きない。
	public void ShowLchMode()
	{
		SpaceSelector.SelectedIndex = 1;
	}




	// 色相環を正方形のまま、エリアの幅と、スライダー群を除いた残りの可視高さの小さい方へ合わせる。可視高さはスクロール領域のビューポート高さからスライダー群の高さと行間を引いて求める。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる。色相環の大きさが変わると、それを受けた SizeChanged で色相環・L-C 平面の画像とパッドの寸法が作り直される。
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




	// 色相環画像と L-C パッドの大きさを、リングの実寸と表示倍率に合わせて更新する。L-C パッドは内円に内接する正方形(内径÷√2 の辺長)にする。色相環の画像は同じ条件なら作り直さない。
	private void UpdateRingVisuals()
	{
		double width = HueRing.ActualWidth;
		double height = HueRing.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		RingMetrics metrics = RingGeometry.Compute(width, height, HueRing.RingThickness, HueRing.ThumbDiameter);

		LcPad.Width = metrics.InnerRadius * Math.Sqrt(2.0);
		LcPad.Height = LcPad.Width;

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
		int spaceIndex = ViewModel.LchSpaceIndex;

		// 画素サイズ・帯の内外半径・色制限設定・副モードが前回と同じなら作り直さない。
		if (pixelWidth != _wheelPixelWidth || pixelHeight != _wheelPixelHeight || innerRadius != _wheelInnerRadius || outerRadius != _wheelOuterRadius || snap != _wheelSnap || spaceIndex != _wheelSpaceIndex)
		{
			_wheelPixelWidth = pixelWidth;
			_wheelPixelHeight = pixelHeight;
			_wheelInnerRadius = innerRadius;
			_wheelOuterRadius = outerRadius;
			_wheelSnap = snap;
			_wheelSpaceIndex = spaceIndex;

			LchSpace wheelSpace = spaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;
			WriteableBitmap wheel = LchHueWheel.Create(pixelWidth, pixelHeight, innerRadius, outerRadius, wheelSpace, snap);
			HueRing.TrackBrush = new ImageBrush
			{
				ImageSource = wheel,
				Stretch = Stretch.Fill,
			};

			// ドラッグ中のレンズは、生成したこの色相環ビットマップをそのまま読んで映す。色相環は色相ドラッグ中も内容が変わらないため、据え置きで足りる。色相環のレンズは合成サンプラーで、帯の外で内側へ入る点は中央の L-C パッドの色面とつまみを映す。
			_wheelSampler = new BitmapFieldSampler(wheel, HueRing);
			HueRing.LensColorSampler = SampleRingThroughLens;
		}

		RegenerateLcPlane();
	}




	// L-C 平面の画像を、現在の色相・副モード・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・色相・設定・副モードなら作り直さない。大きさはパッドの実寸ではなくリングの幾何(内円に内接する正方形)から求める。
	private void RegenerateLcPlane()
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
		int spaceIndex = ViewModel.LchSpaceIndex;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;

		if (pixels == _lcPixels && ViewModel.LchH == _lcHue && snap == _lcSnap && spaceIndex == _lcSpaceIndex && style == _lcStyle)
		{
			return;
		}

		_lcPixels = pixels;
		_lcHue = ViewModel.LchH;
		_lcSnap = snap;
		_lcSpaceIndex = spaceIndex;
		_lcStyle = style;

		LchSpace space = spaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;

		WriteableBitmap plane = LcPlane.Create(pixels, pixels, space, ViewModel.LchH, snap, scale, style);
		LcImage.Source = plane;

		// ドラッグ中のレンズは、色域ハッチなど計算の込み入った L-C 平面を作り直さず、生成したこのビットマップをそのまま読んで映す。色相はドラッグ中一定のため、平面は据え置きで足りる。パッドのレンズは合成サンプラーで、パッドの外側は、その点の背後にあるリング(帯＋つまみ)を映す。
		_planeSampler = new BitmapFieldSampler(plane, LcPad);
		LcPad.LensColorSampler = SamplePadThroughLens;
	}




	// 色相環のレンズのサンプラー(合成)。重ね順は奥から「ホイールの帯 → L-C 平面の色面 → パッドのつまみ」。まず帯(または帯の外は透明)を求め、その上にパッド内なら平面の色面を重ね、最後にパッドのつまみを最前面で重ねる。つまみは色面の縁から帯側へはみ出すため、帯に隠さない。色相環そのものをドラッグ中はつまみがレンズへ置き換わるため、ここでは色相環自身のつまみは描かない。参照するのは平面の色面のみで、相互参照のループにはならない。
	private Color SampleRingThroughLens(double x, double y)
	{
		if (_wheelSampler is null)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		Color result = _wheelSampler.Sample(x, y);

		if (_planeSampler is null || HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0 || LcPad.ActualWidth <= 0.0 || LcPad.ActualHeight <= 0.0)
		{
			return result;
		}

		Point pad = RingGeometry.RingPointToPad(LcPad.ActualWidth, LcPad.ActualHeight, HueRing.ActualWidth, HueRing.ActualHeight, LcPad.PadRotation, x, y);

		// パッド内なら平面の色面を帯の上へ重ねる。色面は帯の内側で帯とは重ならないが、つまみは色面の縁から帯側へはみ出すため、つまみは矩形の内外に依らず最後に最前面で重ねる。
		if (pad.X >= 0.0 && pad.X <= LcPad.ActualWidth && pad.Y >= 0.0 && pad.Y <= LcPad.ActualHeight)
		{
			Color field = _planeSampler.Sample(pad.X, pad.Y);

			if (field.A != 0)
			{
				result = field;
			}
		}

		return LcPad.SampleThumbOverlay(result, pad.X, pad.Y);
	}




	// L-C パッドのレンズのサンプラー(合成)。パッドの矩形内は L-C 平面の色面、外はその点の背後にあるリング(帯＋つまみ)を映す。平面は色域外で半透明の画素を持つため、内外はアルファではなくパッドの矩形で判定する。
	private Color SamplePadThroughLens(double x, double y)
	{
		if (_planeSampler is not null && x >= 0.0 && x <= LcPad.ActualWidth && y >= 0.0 && y <= LcPad.ActualHeight)
		{
			return _planeSampler.Sample(x, y);
		}

		if (_wheelSampler is null || HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		Point ring = RingGeometry.PadPointToRing(LcPad.ActualWidth, LcPad.ActualHeight, HueRing.ActualWidth, HueRing.ActualHeight, LcPad.PadRotation, x, y);
		Color band = _wheelSampler.Sample(ring.X, ring.Y);
		return HueRing.SampleThumbOverlay(band, ring.X, ring.Y);
	}
}
