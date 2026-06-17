// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls;

// 背景に任意のブラシ(グラデーション等)を敷けるスライダー。標準 Slider を継承し、値・キーボード操作・スナップ・アクセシビリティはそのまま引き継いだうえで、トラックの見た目だけを差し替える。RGB・CMYK・HSV・不透明度など、色要素ごとのグラデーション表示を共通の部品で賄うために用意する。具体的なグラデーションの中身は利用側が TrackBrush に与える。
public sealed class GradientSlider : Slider
{
	// テンプレート内の市松模様用レイヤー。透明度表現のときだけコードから中身を組み立てて表示する。
	private Border? _checkerboardLayer;

	// テンプレート内の色域外ハッチ用レイヤー。OutOfGamutSegments が与えられたときだけコードから斜線を組み立てて表示する。
	private Border? _gamutOverlayLayer;

	// 直近にハッチを組んだときの区間・トラック寸法・色域外の見せ方。ドラッグ中に同じ内容の通知が繰り返し来ても、いずれも変わっていなければジオメトリの組み直しを省くために覚えておく。
	private IReadOnlyList<GamutSegment>? _appliedSegments;
	private double _appliedWidth = -1.0;
	private double _appliedHeight = -1.0;
	private GamutOutOfRangeStyle _appliedStyle = (GamutOutOfRangeStyle)(-1);

	// つまみとレンズ(ルーペ)用のテンプレート要素。向き(水平/垂直)に応じて該当テンプレートの要素を選んで持つ。ドラッグ中はつまみを隠してレンズに置き換える。
	private Thumb? _lensThumb;
	private Canvas? _lensHost;

	// このスライダーが垂直向きか。テンプレート適用時に Orientation から決め、レンズの位置計算と再構成の向きに使う。
	private bool _vertical;

	// ポインタでドラッグ中か。押下で真、離す/捕捉喪失で偽。レンズの表示と、値変化時の追従の要否を判断する。キーボードでの値変更ではレンズを出さないための門でもある。
	private bool _isPointerDragging;

	// 現在組み立て済みのレンズ部品。位置の更新、表示/退場アニメーションで触る。退場後は破棄して null へ戻す。
	private Grid? _loupe;
	private ScaleTransform? _loupeScale;

	// ルーペの配置トランスフォーム。トラック(レンズ置き場)からレンズの置き先(オーバーレイ)への変換を毎フレーム書き込み、スクロール量も織り込んで現在位置へ重ねる。
	private MatrixTransform? _loupePlacement;

	// ルーペを実際に載せている先。最前面オーバーレイ、無ければレンズ置き場。退場時にここから自分のルーペだけを取り除く。
	private Canvas? _loupeTarget;

	// レンズの中身となるガラス。背後の内容(トラック帯と上下のカード背景)を拡大・屈折・色収差して見せる。ドラッグごとに組み、退場で破棄する。
	private GlassLens? _glass;

	// レンズが帯を再構成するためのトラックのグラデーション停止点。ドラッグ中は当該スライダーのトラックブラシが一定のため、開始時に一度取り出して使い回す。
	private IReadOnlyList<GlassGradientStop> _glassStops = System.Array.Empty<GlassGradientStop>();

	// レンズ用のポインタハンドラを取り付け済みか。OnApplyTemplate が複数回呼ばれても二重に取り付けないための門。
	private bool _lensHandlersAttached;

	// 向きごとのテンプレートのルート。端を越えて引っ張ったときに、当該の向きのテンプレートだけを沿軸方向へ伸ばす対象として持つ。
	private FrameworkElement? _horizontalTemplate;
	private FrameworkElement? _verticalTemplate;

	// 伸びを掛けるテンプレートの合成ビジュアル。沿軸方向のスケールで帯・つまみ・各レイヤーをまとめて伸ばす。レイアウトには影響しない見た目だけの変形。
	private Visual? _stretchVisual;

	// つまみの合成ビジュアルと、伸びを打ち消す逆スケールを取り付け済みか。テンプレートを伸ばすとつまみも横に潰れて楕円になるため、テンプレートのスケールの逆数を式アニメーションで常時当て、つまみは丸いまま端へ追従させる。
	private Visual? _thumbCounterVisual;
	private bool _counterScaleAttached;

	// つまみのグライド用の合成プロパティセット。Pos は表示中心の沿軸位置、LayoutPos はレイアウト上のつまみ中心。つまみの Translation を式で Pos − LayoutPos に結び、値が飛んだら LayoutPos を即時に新位置へ、Pos をスプリングで新位置へ寄せる。表示位置(=Pos)が連続するため、連続入力でも途切れずに追従する。
	private CompositionPropertySet? _glideProps;

	// グライドの下ごしらえ(Translation 有効化と式の結線)が済んでいるか。テンプレート再適用でつまみが入れ替わったら作り直すため false へ戻す。
	private bool _glideReady;

	// つまみが今グライド中か。静止状態からの新たなグライドでは、ドラッグや数値入力でずれている可能性のある Pos・LayoutPos を移動前の位置へ置き直してから始める。グライド中は Pos が生きた表示位置を保つため置き直さない。
	private bool _gliding;

	// グライドの世代。連続入力でスプリングを差し替えるたびに増やし、古いスプリングの完了通知で _gliding を誤って下ろさないための識別に使う。
	private int _glideGen;

	// 伸びている最中か。離したときにバウンスで戻すか、即座に解除するかの判定に使う。
	private bool _isOverscrolling;

	// Windows の「アニメーションを表示する」設定。オフのときは端を越えた引っ張りの伸び・バウンスを出さず、素直に端で止める。実行中に切り替わっても追従できるよう、その都度値を読む。
	private static readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();

	// 引っ張りで伸びる量の上限(DIP)。これ以上は引いても伸びない漸近値。
	private const double MaxOverscroll = 22.0;

	// ゴムの粘り。大きいほど同じ引っ張り量に対して伸びにくくなる(漸近カーブの時定数)。
	private const double OverscrollResistance = 90.0;

	// 1次元トラック向けのレンズ設定。ドラッグ中に背後を拡大・屈折・色収差して見せるルーペの効きを決める。各項目の意味と単位は GlassLensParams を参照。見た目の調整はここを変える。
	private static readonly GlassLensParams LensParams = new()
	{
		Diameter = 44.0,
		Magnify = 1.1,
		EdgeAmount = -20.0,
		Chroma = true,
		ChromaSpread = 0.4,
		BevelFraction = 0.3,
	};




	// トラック背面を塗るブラシ。2色グラデーションでも、多ストップの色相環でも、半透明グラデーションでも、任意の Brush を受け取る。色要素に応じた具体的な生成は利用側(ViewModel 等)が担う。
	public Brush? TrackBrush
	{
		get => (Brush?)GetValue(TrackBrushProperty);
		set => SetValue(TrackBrushProperty, value);
	}

	public static readonly DependencyProperty TrackBrushProperty =
		DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(GradientSlider), new PropertyMetadata(null));




	// トラック背面に市松模様を敷くかどうか。透明→不透明のような半透明グラデーションを、背景に溶けさせず正しく見せるために使う。
	public bool ShowCheckerboard
	{
		get => (bool)GetValue(ShowCheckerboardProperty);
		set => SetValue(ShowCheckerboardProperty, value);
	}

	public static readonly DependencyProperty ShowCheckerboardProperty =
		DependencyProperty.Register(nameof(ShowCheckerboard), typeof(bool), typeof(GradientSlider), new PropertyMetadata(false, OnShowCheckerboardChanged));




	// トラックの太さ(高さ)。細いレールではなくグラデーションを見せる帯として描くための値。
	public double TrackThickness
	{
		get => (double)GetValue(TrackThicknessProperty);
		set => SetValue(TrackThicknessProperty, value);
	}

	public static readonly DependencyProperty TrackThicknessProperty =
		DependencyProperty.Register(nameof(TrackThickness), typeof(double), typeof(GradientSlider), new PropertyMetadata(24.0));




	// 実際にトラックへ適用される角丸量。グラデーション帯(Rectangle)の RadiusX/RadiusY と市松模様・色域外ハッチの角丸クリップの双方に同じ値を適用して輪郭を揃える。Rectangle の半径と揃えるため double で持つ。値は向きに応じて HorizontalCornerRadius / VerticalCornerRadius から ApplyCornerRadius が書き込むため、利用側が直に設定する必要はない。
	public double TrackCornerRadius
	{
		get => (double)GetValue(TrackCornerRadiusProperty);
		set => SetValue(TrackCornerRadiusProperty, value);
	}

	public static readonly DependencyProperty TrackCornerRadiusProperty =
		DependencyProperty.Register(nameof(TrackCornerRadius), typeof(double), typeof(GradientSlider), new PropertyMetadata(4.0));




	// 水平向きのトラックの角丸量。既定は帯の太さ(TrackThickness 既定 24)の半分の 12 で、左右の端が半円になりピル形(端が円)になる。帯の太さを変えたときは、端を円に保つならこれもその半分へ合わせる。
	public double HorizontalCornerRadius
	{
		get => (double)GetValue(HorizontalCornerRadiusProperty);
		set => SetValue(HorizontalCornerRadiusProperty, value);
	}

	public static readonly DependencyProperty HorizontalCornerRadiusProperty =
		DependencyProperty.Register(nameof(HorizontalCornerRadius), typeof(double), typeof(GradientSlider), new PropertyMetadata(12.0, OnCornerRadiusChanged));




	// 垂直向きのトラックの角丸量。水平とは別に調整できる。既定は 2 で、端をわずかに丸める程度にとどめる。
	public double VerticalCornerRadius
	{
		get => (double)GetValue(VerticalCornerRadiusProperty);
		set => SetValue(VerticalCornerRadiusProperty, value);
	}

	public static readonly DependencyProperty VerticalCornerRadiusProperty =
		DependencyProperty.Register(nameof(VerticalCornerRadius), typeof(double), typeof(GradientSlider), new PropertyMetadata(2.0, OnCornerRadiusChanged));




	// トラック上で sRGB 色域を外れる区間(値の小さい端 0・大きい端 1 の割合。水平向きは左→右、垂直向きは下→上)。与えると、その区間に斜線ハッチを重ねて、つまみを動かせるが実際の色を表示できない範囲を示す。空・null のときはハッチを出さない。LCH・Lab タブが色域可視化に使う。
	public IReadOnlyList<GamutSegment>? OutOfGamutSegments
	{
		get => (IReadOnlyList<GamutSegment>?)GetValue(OutOfGamutSegmentsProperty);
		set => SetValue(OutOfGamutSegmentsProperty, value);
	}

	public static readonly DependencyProperty OutOfGamutSegmentsProperty =
		DependencyProperty.Register(nameof(OutOfGamutSegments), typeof(IReadOnlyList<GamutSegment>), typeof(GradientSlider), new PropertyMetadata(null, OnOutOfGamutSegmentsChanged));




	// 色域外区間の見せ方。OutOfGamutSegments が与えられたときだけ効き、その区間を「クランプ色+境界線」「同+斜線」「白塗り+斜線」のどれで示すかを切り替える。2次元パッド(LcPlane・AbPlane)と同じ設定を共有して見せ方をそろえる。
	public GamutOutOfRangeStyle OutOfGamutStyle
	{
		get => (GamutOutOfRangeStyle)GetValue(OutOfGamutStyleProperty);
		set => SetValue(OutOfGamutStyleProperty, value);
	}

	public static readonly DependencyProperty OutOfGamutStyleProperty =
		DependencyProperty.Register(nameof(OutOfGamutStyle), typeof(GamutOutOfRangeStyle), typeof(GradientSlider), new PropertyMetadata(GamutOutOfRangeStyle.FillBoundaryHatch, OnOutOfGamutSegmentsChanged));




	protected override void OnApplyTemplate()
	{
		base.OnApplyTemplate();

		if (_checkerboardLayer is not null)
		{
			_checkerboardLayer.SizeChanged -= OnCheckerboardLayerSizeChanged;
		}

		_checkerboardLayer = GetTemplateChild("CheckerboardLayer") as Border;

		if (_checkerboardLayer is not null)
		{
			_checkerboardLayer.SizeChanged += OnCheckerboardLayerSizeChanged;
		}

		if (_gamutOverlayLayer is not null)
		{
			_gamutOverlayLayer.SizeChanged -= OnGamutOverlayLayerSizeChanged;
		}

		_gamutOverlayLayer = GetTemplateChild("GamutOverlayLayer") as Border;

		if (_gamutOverlayLayer is not null)
		{
			_gamutOverlayLayer.SizeChanged += OnGamutOverlayLayerSizeChanged;
		}

		// 向きに応じて、該当テンプレートのつまみとレンズ置き場を選ぶ。レンズの位置計算と再構成の向きもこれに合わせる。
		_vertical = Orientation == Orientation.Vertical;
		_lensThumb = GetTemplateChild(_vertical ? "VerticalThumb" : "HorizontalThumb") as Thumb;
		_lensHost = GetTemplateChild(_vertical ? "VerticalLens" : "HorizontalLens") as Canvas;

		// 端を越えた引っ張りで伸ばす対象。向きに応じて該当テンプレートのルートを選ぶ。テンプレートを取り直したらキャッシュ済みのビジュアルと逆スケールの取り付けも作り直す。
		_horizontalTemplate = GetTemplateChild("HorizontalTemplate") as FrameworkElement;
		_verticalTemplate = GetTemplateChild("VerticalTemplate") as FrameworkElement;
		_stretchVisual = null;
		_thumbCounterVisual = null;
		_counterScaleAttached = false;
		_glideProps = null;
		_glideReady = false;
		_gliding = false;

		// 標準 Slider は交差軸(横向きなら高さ)をクリック目標として大きく取り、独自テンプレートでトラックを差し替えてもその大きさが残る。横向きのときだけ高さの上限をトラックの太さとつまみ(20)に収まる値へ絞り、コントロールを帯へ密着させて、スライダーが縦に並ぶときに上下へ余白が空くのを防ぐ。縦向きは交差軸が幅のため高さは縛らず、トラックの長さを妨げない。
		if (_vertical)
		{
			MaxHeight = double.PositiveInfinity;
		}
		else
		{
			MaxHeight = Math.Max(TrackThickness, 20.0) + 4.0;
		}

		// レンズの開始・終了は、処理済みのイベントも拾う形で取り付ける。つまみ(Thumb)が PointerPressed/Released を処理済みにするため、handledEventsToo を true にしないと押下を取りこぼす。
		if (!_lensHandlersAttached)
		{
			AddHandler(PointerPressedEvent, new PointerEventHandler(OnSliderPointerPressed), true);
			AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSliderPointerReleased), true);
			AddHandler(PointerMovedEvent, new PointerEventHandler(OnSliderPointerMoved), true);
			AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSliderPointerCaptureLost), true);
			AddHandler(PointerCanceledEvent, new PointerEventHandler(OnSliderPointerCanceled), true);
			_lensHandlersAttached = true;
		}

		// 向きに応じた角丸量をトラックへ反映する。市松・色域外ハッチのクリップの作り直しもこの中で行う。
		ApplyCornerRadius();
	}




	// 向きに応じた角丸量(水平 or 垂直)を実際のトラックへ書き込み、市松模様・色域外ハッチの角丸クリップを作り直す。テンプレート未適用でも値は書き込めるが、クリップの作り直しはパーツが揃ってから効く(各 Update がパーツ無しで早期に戻る)。
	private void ApplyCornerRadius()
	{
		TrackCornerRadius = _vertical ? VerticalCornerRadius : HorizontalCornerRadius;
		UpdateCheckerboard();
		UpdateGamutOverlay();
	}




	private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((GradientSlider)d).ApplyCornerRadius();
	}




	private static void OnShowCheckerboardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((GradientSlider)d).UpdateCheckerboard();
	}




	private void OnCheckerboardLayerSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateCheckerboard();
	}




	private static void OnOutOfGamutSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((GradientSlider)d).UpdateGamutOverlay();
	}




	private void OnGamutOverlayLayerSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateGamutOverlay();
	}




	// 色域外ハッチの表示を、現在の OutOfGamutSegments とトラック寸法に合わせて更新する。区間ごとに、その範囲(水平向きは x 範囲、垂直向きは y 範囲)へ矩形クリップした 45 度の斜線パスを重ねる。UIElement.Clip は矩形しか受け付けないため、区間ごとに別パスへ分けてクリップする。沿軸と直交方向のはみ出しはレイヤー全体の矩形クリップで切る。
	private void UpdateGamutOverlay()
	{
		if (_gamutOverlayLayer is null)
		{
			return;
		}

		IReadOnlyList<GamutSegment>? segments = OutOfGamutSegments;

		if (segments is null || segments.Count == 0)
		{
			_gamutOverlayLayer.Visibility = Visibility.Collapsed;
			_gamutOverlayLayer.Child = null;
			_gamutOverlayLayer.Clip = null;
			ClearGamutOverlayClip();
			_appliedSegments = segments;
			_appliedWidth = -1.0;
			_appliedHeight = -1.0;
			_appliedStyle = (GamutOutOfRangeStyle)(-1);
			return;
		}

		// 寸法を測る前に可視へ切り替える。Collapsed のままだとレイアウトされず ActualWidth が 0 のままで、寸法変化(SizeChanged)の通知も来ないため、いつまでもハッチを組めない。可視にすればレイアウト後の SizeChanged で寸法が定まり、その通知で組み直しへ入れる。
		_gamutOverlayLayer.Visibility = Visibility.Visible;

		double width = _gamutOverlayLayer.ActualWidth;
		double height = _gamutOverlayLayer.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			_gamutOverlayLayer.Child = null;
			ClearGamutOverlayClip();
			return;
		}

		GamutOutOfRangeStyle style = OutOfGamutStyle;

		// 区間も寸法も見せ方も前回と同じなら、ジオメトリの組み直しを省く。ドラッグ中に内容の変わらない通知が繰り返し来ても UI スレッドの負荷を増やさない。
		if (width == _appliedWidth && height == _appliedHeight && style == _appliedStyle && SegmentsEqual(segments, _appliedSegments))
		{
			return;
		}

		_appliedSegments = segments;
		_appliedWidth = width;
		_appliedHeight = height;
		_appliedStyle = style;

		// 見せ方を要素へ分解する。クランプ色のトラックは VM のグラデーション(MakeTrackBrush)が既に描いているため、ここでは白塗り(その上を白で覆う)・斜線・境界線の重ねを組む。
		bool showHatch = style == GamutOutOfRangeStyle.FillBoundaryHatch || style == GamutOutOfRangeStyle.WhiteHatch;
		bool showBoundary = style != GamutOutOfRangeStyle.WhiteHatch;
		bool fillWhite = style == GamutOutOfRangeStyle.WhiteHatch;

		// トラックの枠線(1px)にかからないよう、枠の太さぶん内側へ寄せて描く。角丸への収めは、組み上げた後にレイヤーへ掛ける角丸の合成クリップ(ApplyGamutOverlayClip)が担う。
		const double inset = 1.0;
		double innerWidth = width - (inset * 2.0);
		double innerHeight = height - (inset * 2.0);

		if (innerWidth <= 0.0 || innerHeight <= 0.0)
		{
			_gamutOverlayLayer.Child = null;
			ClearGamutOverlayClip();
			return;
		}

		var root = new Grid();

		// 黒線と白線を密着で並べ、暗い背景では白が、明るい背景では黒が効くようにして、グラデーションのどこでも消えないようにする。線幅 0.7 DIP、黒 α0.5・白 α0.3、x 方向の周期 10 DIP。白は黒の隣へ垂直 0.7 DIP ぶん(45 度なので x 方向では 0.7×√2)ずらして密着させる。L-C パッドのハッチ(LcPlane)と寸法・色をそろえる。境界線も同じ黒白で引く。
		var black = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
		var white = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
		var whiteFill = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
		const double spacing = 10.0;
		const double lineWidth = 0.7;
		double whiteOffset = lineWidth * Math.Sqrt(2.0);
		const double eps = 1e-4;

		foreach (GamutSegment segment in segments)
		{
			// 区間(0–1)をトラック上の矩形へ写す。水平向きは左端 0→右端 1 の x 範囲、垂直向きは下端 0→上端 1(値最大が上)の y 範囲にする。
			double startNorm = Math.Clamp(segment.Start, 0.0, 1.0);
			double endNorm = Math.Clamp(segment.End, 0.0, 1.0);

			if (endNorm <= startNorm)
			{
				continue;
			}

			Rect band = _vertical
				? new Rect(inset, inset + ((1.0 - endNorm) * innerHeight), innerWidth, (endNorm - startNorm) * innerHeight)
				: new Rect(inset + (startNorm * innerWidth), inset, (endNorm - startNorm) * innerWidth, innerHeight);

			// 白塗りはグラデーショントラックと同じ座標系で配置する必要があるため、インセットを一切省く。インセットを加えると座標系がずれ、グラデーション外縁に細い色の帯が残る。ハッチ用の band とは別の矩形を使う。
			Rect fillBand = _vertical
				? new Rect(0.0, (1.0 - endNorm) * height, width, (endNorm - startNorm) * height)
				: new Rect(startNorm * width, 0.0, (endNorm - startNorm) * width, height);

			// 白塗りは、クランプ色のトラックを区間ぶん不透明な白で覆い、実際の色を隠す。斜線はその上へ重ねる。
			if (fillWhite)
			{
				root.Children.Add(new Rectangle
				{
					Width = fillBand.Width,
					Height = fillBand.Height,
					Fill = whiteFill,
					HorizontalAlignment = HorizontalAlignment.Left,
					VerticalAlignment = VerticalAlignment.Top,
					Margin = new Thickness(fillBand.X, fillBand.Y, 0.0, 0.0),
				});
			}

			// 区間の矩形へクリップした斜線を、黒線と、密着するよう位相をずらした白線の2本重ねる。枠を避けて内側へ収める。Geometry もクリップも親を1つしか持てないため、線ごとに作り直す。
			if (showHatch)
			{
				root.Children.Add(new Path
				{
					Data = BuildDiagonalLines(band.X, band.X + band.Width, band.Y, band.Height, spacing, 0.0),
					Stroke = black,
					StrokeThickness = lineWidth,
					Clip = new RectangleGeometry { Rect = band },
				});
				root.Children.Add(new Path
				{
					Data = BuildDiagonalLines(band.X, band.X + band.Width, band.Y, band.Height, spacing, whiteOffset),
					Stroke = white,
					StrokeThickness = lineWidth,
					Clip = new RectangleGeometry { Rect = band },
				});
			}

			// 境界線は、色域内と外が切り替わる端(0 や 1 のトラック端ではなく内側の端)へ、色域内側に黒・外側に白のタテ線を引く。区間の開始端は色域内が値の小さい側、終了端は値の大きい側にある。
			if (showBoundary)
			{
				if (startNorm > eps)
				{
					AddBoundaryTick(root, black, white, startNorm, true, inset, innerWidth, innerHeight);
				}

				if (endNorm < 1.0 - eps)
				{
					AddBoundaryTick(root, black, white, endNorm, false, inset, innerWidth, innerHeight);
				}
			}
		}

		if (root.Children.Count > 0)
		{
			_gamutOverlayLayer.Child = root;
			ApplyGamutOverlayClip(width, height);
		}
		else
		{
			_gamutOverlayLayer.Child = null;
			ClearGamutOverlayClip();
		}
	}




	// 色域外オーバーレイ(白塗り・斜線・境界線)を、グラデーション帯の塗り面に合わせた角丸の合成クリップで収める。帯は枠線(1px)のぶん塗りが内側へ寄るため、同じだけ内側へ寄せた角丸矩形でクリップし、白塗りが帯より外へはみ出さず、丸い端も四角く出ないようにする。UIElement.Clip は矩形しか扱えず、Border の角丸は実画面の合成で子を切り抜かないため、角丸の合成クリップを使う。
	private void ApplyGamutOverlayClip(double width, double height)
	{
		if (_gamutOverlayLayer is null)
		{
			return;
		}

		// 帯の枠線(1px)の塗りの内寄り量。Shape は塗りを枠線の半分ずつ内側へ詰めるため片側 0.5px。
		const double inset = 0.5;
		double clipWidth = Math.Max(0.0, width - (inset * 2.0));
		double clipHeight = Math.Max(0.0, height - (inset * 2.0));
		float radius = (float)Math.Max(0.0, TrackCornerRadius - inset);

		Visual visual = ElementCompositionPreview.GetElementVisual(_gamutOverlayLayer);

		using CanvasGeometry geometry = CanvasGeometry.CreateRoundedRectangle(CanvasDevice.GetSharedDevice(), (float)inset, (float)inset, (float)clipWidth, (float)clipHeight, radius, radius);
		CompositionPathGeometry pathGeometry = visual.Compositor.CreatePathGeometry(new CompositionPath(geometry));
		visual.Clip = visual.Compositor.CreateGeometricClip(pathGeometry);
	}




	// 色域外オーバーレイの合成クリップを解除する。オーバーレイを隠すときに使う。
	private void ClearGamutOverlayClip()
	{
		if (_gamutOverlayLayer is null)
		{
			return;
		}

		ElementCompositionPreview.GetElementVisual(_gamutOverlayLayer).Clip = null;
	}




	// 色域の境界へ、色域内側に黒・外側に白のタテ線(向きに直交する1px帯)を密着で2本引く。norm は境界の沿軸位置(0–1)、isStart は区間の開始端か(色域内が値の小さい側か)。水平は沿軸=x で値最大が右、垂直は沿軸=y で値最大が上。明暗どちらの下地でも一方が効くよう黒白を隣り合わせる。
	private void AddBoundaryTick(Grid root, Brush black, Brush white, double norm, bool isStart, double inset, double innerWidth, double innerHeight)
	{
		const double lineW = 1.0;

		if (_vertical)
		{
			double pos = inset + ((1.0 - norm) * innerHeight);

			// 開始端は色域内が値の小さい側=下(座標大)、終了端は値の大きい側=上(座標小)。黒を色域内側へ置く。
			bool inGamutLargerCoord = isStart;
			double blackTop = inGamutLargerCoord ? pos : pos - lineW;
			double whiteTop = inGamutLargerCoord ? pos - lineW : pos;
			root.Children.Add(MakeTick(black, innerWidth, lineW, inset, blackTop));
			root.Children.Add(MakeTick(white, innerWidth, lineW, inset, whiteTop));
		}
		else
		{
			double pos = inset + (norm * innerWidth);

			// 開始端は色域内が値の小さい側=左(座標小)、終了端は値の大きい側=右(座標大)。黒を色域内側へ置く。
			bool inGamutLargerCoord = !isStart;
			double blackLeft = inGamutLargerCoord ? pos : pos - lineW;
			double whiteLeft = inGamutLargerCoord ? pos - lineW : pos;
			root.Children.Add(MakeTick(black, lineW, innerHeight, blackLeft, inset));
			root.Children.Add(MakeTick(white, lineW, innerHeight, whiteLeft, inset));
		}
	}




	// 左上を原点に、指定の幅・高さ・位置で塗りつぶした小矩形を作る。境界線のタテ線1本ぶんに使う。
	private static Rectangle MakeTick(Brush fill, double width, double height, double left, double top)
	{
		return new Rectangle
		{
			Width = width,
			Height = height,
			Fill = fill,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(left, top, 0.0, 0.0),
		};
	}




	// 指定の x 範囲を覆う 45 度の斜線(右上がり、x+y=一定)をまとめたジオメトリを作る。縦は top から top+height の帯に収め、左へ height 分ずらした位置から周期ごとに引き、offset で帯の位相をずらす(黒線と白線を密着させるのに使う)。矩形クリップで区間内へ絞る前提で並べる。線を重ねる各パスは Geometry を共有できないため、その都度作り直す。
	private static GeometryGroup BuildDiagonalLines(double x, double right, double top, double height, double spacing, double offset)
	{
		var lines = new GeometryGroup();

		for (double sx = x - height + offset; sx < right; sx += spacing)
		{
			lines.Children.Add(new LineGeometry
			{
				StartPoint = new Point(sx, top + height),
				EndPoint = new Point(sx + height, top),
			});
		}

		return lines;
	}




	// 2つの色域外区間の並びが同じ内容か。null と空はどちらも「ハッチなし」として同一に扱う。ドラッグ中の組み直しを省く判定に使う。
	private static bool SegmentsEqual(IReadOnlyList<GamutSegment>? a, IReadOnlyList<GamutSegment>? b)
	{
		int countA = a?.Count ?? 0;
		int countB = b?.Count ?? 0;

		if (countA != countB)
		{
			return false;
		}

		for (int i = 0; i < countA; i++)
		{
			if (a![i].Start != b![i].Start || a[i].End != b[i].End)
			{
				return false;
			}
		}

		return true;
	}




	// 市松模様の表示を、現在の ShowCheckerboard とトラック寸法に合わせて更新する。市松はグラデーション帯(HorizontalTrackRect)と同一形状の Rectangle シェイプで描く。帯は Shape で枠線(1px)のぶん塗り面が内側へ寄るため、市松にも同じ太さの透明な枠線を与えて塗り面を帯とぴたり一致させ、帯の塗りより外へはみ出さないようにする。枠線が無いと市松が帯より1pxほど大きくなり、丸い輪郭の上下・端から升が漏れる。角丸は Shape 自身が塗りを丸めるため、合成クリップや Border の角丸には依存しない。
	private void UpdateCheckerboard()
	{
		if (_checkerboardLayer is null)
		{
			return;
		}

		if (!ShowCheckerboard)
		{
			_checkerboardLayer.Visibility = Visibility.Collapsed;
			_checkerboardLayer.Background = null;
			_checkerboardLayer.Child = null;
			return;
		}

		_checkerboardLayer.Visibility = Visibility.Visible;
		_checkerboardLayer.Background = null;
		_checkerboardLayer.CornerRadius = new CornerRadius(0);

		int width = (int)Math.Round(_checkerboardLayer.ActualWidth);
		int height = (int)Math.Round(_checkerboardLayer.ActualHeight);

		if (width <= 0 || height <= 0)
		{
			_checkerboardLayer.Child = null;
			return;
		}

		_checkerboardLayer.Child = new Rectangle
		{
			RadiusX = TrackCornerRadius,
			RadiusY = TrackCornerRadius,
			Stroke = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
			StrokeThickness = 1.0,
			Fill = new ImageBrush
			{
				ImageSource = BuildCheckerboardBitmap(width, height),
				Stretch = Stretch.Fill,
			},
		};
	}




	// 指定の画素寸法へ市松模様を焼き込んだ不透明なビットマップを作る。明色の下地に暗色の升を CheckerboardGeometry の刻みで敷く。トラックの Rectangle の塗り(ImageBrush)に使い、角丸はその Rectangle が処理する。色はアルファ乗算済み(BGRA、ここでは全て不透明)で書き込む。
	private static WriteableBitmap BuildCheckerboardBitmap(int width, int height)
	{
		var bitmap = new WriteableBitmap(width, height);
		int cell = (int)CheckerboardGeometry.CellSize;
		Color light = CheckerboardGeometry.LightColor;
		Color dark = CheckerboardGeometry.DarkColor;
		byte[] pixels = new byte[width * height * 4];
		int i = 0;

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				Color c = ((x / cell) + (y / cell)) % 2 == 1 ? dark : light;
				pixels[i++] = c.B;
				pixels[i++] = c.G;
				pixels[i++] = c.R;
				pixels[i++] = 0xFF;
			}
		}

		using (System.IO.Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(pixels, 0, pixels.Length);
		}

		bitmap.Invalidate();
		return bitmap;
	}




	// ポインタ押下でレンズを出す。標準 Slider ではつまみ(Thumb)が PointerPressed を処理済みにするため、protected の OnPointerPressed override では受け取れない。処理済みのイベントも拾う AddHandler(handledEventsToo) で、つまみ掴み・トラック押下のどちらでも開始を捉える。
	private void OnSliderPointerPressed(object sender, PointerRoutedEventArgs e)
	{
		BeginLens();
	}




	// ポインタを離したらレンズを退場させる。Thumb が処理済みにする経路もあるため、押下と同じく handledEventsToo で受け取る。
	private void OnSliderPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		EndLens();
	}




	// 捕捉を失ったとき(別要素へ奪われた・ウィンドウ外で離した等)もレンズを退場させ、出しっぱなしを防ぐ。
	private void OnSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
	{
		EndLens();
	}




	// ポインタ操作が取り消されたときもレンズを退場させる。
	private void OnSliderPointerCanceled(object sender, PointerRoutedEventArgs e)
	{
		EndLens();
	}




	// ドラッグ中、つまみが端に張り付いた状態でさらに引っ張ると、その越えた量に応じてトラックを引いた方向へ伸ばす。キーボードやコードでの値変更ではドラッグ中でないため効かない。
	private void OnSliderPointerMoved(object sender, PointerRoutedEventArgs e)
	{
		if (!_isPointerDragging)
		{
			return;
		}

		// Windows の「アニメーションを表示する」がオフなら伸ばさない。つまみは端で止まり、引っ張りに反応しない。
		if (!_uiSettings.AnimationsEnabled)
		{
			return;
		}

		FrameworkElement? template = _vertical ? _verticalTemplate : _horizontalTemplate;

		if (template is null)
		{
			return;
		}

		Point pointer = e.GetCurrentPoint(template).Position;
		UpdateOverscroll(pointer, template);
	}




	// ポインタ位置と現在値から、端を越えた引っ張り量を求めて伸びへ変換する。値が端(最大/最小)に達していて、かつポインタがその端のつまみ可動域の外側にあるときだけ伸ばす。それ以外は伸びを 0 に戻す。
	private void UpdateOverscroll(Point pointer, FrameworkElement template)
	{
		double range = Maximum - Minimum;

		if (range <= 0.0)
		{
			ApplyStretch(0.0, true);
			return;
		}

		const double eps = 1e-6;
		bool atMax = Value >= Maximum - eps;
		bool atMin = Value <= Minimum + eps;

		if (!atMax && !atMin)
		{
			ApplyStretch(0.0, true);
			return;
		}

		double half = _vertical
			? (_lensThumb?.ActualHeight ?? 28.0) / 2.0
			: (_lensThumb?.ActualWidth ?? 20.0) / 2.0;
		double length = _vertical ? template.ActualHeight : template.ActualWidth;
		double along = _vertical ? pointer.Y : pointer.X;

		double overshoot;
		bool pullMax;

		if (_vertical)
		{
			// 値最大が上(座標小)、最小が下(座標大)。上端より上へ引けば最大側、下端より下へ引けば最小側のはみ出し。
			double topEdge = half;
			double bottomEdge = length - half;

			if (atMax && along < topEdge)
			{
				overshoot = topEdge - along;
				pullMax = true;
			}
			else if (atMin && along > bottomEdge)
			{
				overshoot = along - bottomEdge;
				pullMax = false;
			}
			else
			{
				ApplyStretch(0.0, true);
				return;
			}
		}
		else
		{
			// 値最大が右、最小が左。右端より右へ引けば最大側、左端より左へ引けば最小側のはみ出し。
			double leftEdge = half;
			double rightEdge = length - half;

			if (atMax && along > rightEdge)
			{
				overshoot = along - rightEdge;
				pullMax = true;
			}
			else if (atMin && along < leftEdge)
			{
				overshoot = leftEdge - along;
				pullMax = false;
			}
			else
			{
				ApplyStretch(0.0, true);
				return;
			}
		}

		// 引くほど伸びにくくなる漸近カーブ。生のはみ出し量を上限 MaxOverscroll へ滑らかに近づける(ゴムの手応え)。
		double stretch = MaxOverscroll * (1.0 - (1.0 / (1.0 + (overshoot / OverscrollResistance))));
		ApplyStretch(stretch, pullMax);
	}




	// 伸び量をテンプレートの沿軸スケールへ反映する。伸びる向きと反対側の端を固定点にし、引いた方向だけへ伸ばす。つまみは逆スケールで丸いまま保つ。レンズの位置も伸びた端へ合わせて更新する。
	private void ApplyStretch(double stretchPx, bool atMax)
	{
		FrameworkElement? template = _vertical ? _verticalTemplate : _horizontalTemplate;

		if (template is null)
		{
			return;
		}

		double length = _vertical ? template.ActualHeight : template.ActualWidth;

		if (length <= 0.0)
		{
			return;
		}

		_stretchVisual ??= ElementCompositionPreview.GetElementVisual(template);
		EnsureCounterScale();

		_isOverscrolling = stretchPx > 0.5;

		double scale = (length + stretchPx) / length;

		// 反対側の端を固定する。水平は右引き=左端固定・左引き=右端固定、垂直は上引き=下端固定・下引き=上端固定。
		float anchor = _vertical
			? (atMax ? (float)length : 0.0f)
			: (atMax ? 0.0f : (float)length);

		_stretchVisual.StopAnimation("Scale");

		if (_vertical)
		{
			_stretchVisual.CenterPoint = new Vector3(0.0f, anchor, 0.0f);
			_stretchVisual.Scale = new Vector3(1.0f, (float)scale, 1.0f);
		}
		else
		{
			_stretchVisual.CenterPoint = new Vector3(anchor, 0.0f, 0.0f);
			_stretchVisual.Scale = new Vector3((float)scale, 1.0f, 1.0f);
		}

		UpdateLensPosition();
	}




	// つまみへ、テンプレートのスケールの逆数を当てる式アニメーションを取り付ける。テンプレートを伸ばすとつまみも沿軸方向へ潰れて楕円になるため、つまみ自身の中心まわりで逆スケールを掛けて円形を保つ。位置はテンプレートのスケールに乗るので、つまみは丸いまま伸びた端へ追従する。手動の伸びにもバウンスにも自動で追従するよう式で結ぶ。
	private void EnsureCounterScale()
	{
		if (_counterScaleAttached || _lensThumb is null || _stretchVisual is null)
		{
			return;
		}

		double thumbWidth = _lensThumb.ActualWidth > 0.0 ? _lensThumb.ActualWidth : (_vertical ? 28.0 : 20.0);
		double thumbHeight = _lensThumb.ActualHeight > 0.0 ? _lensThumb.ActualHeight : (_vertical ? 28.0 : 20.0);

		_thumbCounterVisual = ElementCompositionPreview.GetElementVisual(_lensThumb);
		_thumbCounterVisual.CenterPoint = new Vector3((float)(thumbWidth / 2.0), (float)(thumbHeight / 2.0), 0.0f);

		Compositor compositor = _stretchVisual.Compositor;
		ExpressionAnimation counter = _vertical
			? compositor.CreateExpressionAnimation("Vector3(1, 1.0f / t.Scale.Y, 1)")
			: compositor.CreateExpressionAnimation("Vector3(1.0f / t.Scale.X, 1, 1)");
		counter.SetReferenceParameter("t", _stretchVisual);
		_thumbCounterVisual.StartAnimation("Scale", counter);

		_counterScaleAttached = true;
	}




	// 離したときに、伸びをスプリングで元(スケール1)へ戻す。減衰比を1未満にして一度行き過ぎてから収まるバウンスにする。つまみの逆スケールは式でテンプレートのスケールに結んであるため、戻る間も丸いまま追従する。伸びていなければ何もしない。
	private void AnimateStretchBack()
	{
		// 伸びていない、または途中でアニメーション設定がオフになったら、バウンスせず即座にスケール1へ戻す。
		if (_stretchVisual is null || !_isOverscrolling || !_uiSettings.AnimationsEnabled)
		{
			ClearOverscroll();
			return;
		}

		Compositor compositor = _stretchVisual.Compositor;
		SpringVector3NaturalMotionAnimation spring = compositor.CreateSpringVector3Animation();
		spring.FinalValue = Vector3.One;
		spring.DampingRatio = 0.35f;
		spring.Period = TimeSpan.FromMilliseconds(45);
		_stretchVisual.StartAnimation("Scale", spring);

		_isOverscrolling = false;
	}




	// 伸びを即座に解除してスケールを1へ戻す。バウンスを要しない経路(伸びが無い状態での終了など)で使う。
	private void ClearOverscroll()
	{
		if (_stretchVisual is not null)
		{
			_stretchVisual.StopAnimation("Scale");
			_stretchVisual.Scale = Vector3.One;
		}

		_isOverscrolling = false;
	}




	// 矢印キーは StepFrequency ぶん、Page Up/Page Down は LargeChange ぶん値を増減させ、いずれも StepFrequency 刻みへ丸めてから Minimum〜Maximum に収める。向きに関わらず Right/Up/Page Up を増加方向とする。値を正規化値へ TwoWay 束縛し、セッターが色を再計算して通知を返すスライダーでは、標準 Slider の矢印キー処理がその再入で内部状態を崩して値が端へ飛ぶため、矢印キーも独自に処理する。Page はつまみが大きく飛ぶため移動前の位置からグライドさせ(GlideThumbFrom)、矢印は細かな調整のため即時に動かす。Home/End は標準 Slider に委ね、変わったときだけグライドを足す。
	protected override void OnKeyDown(KeyRoutedEventArgs e)
	{
		if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.PageUp or VirtualKey.PageDown)
		{
			bool increase = e.Key is VirtualKey.Right or VirtualKey.Up or VirtualKey.PageUp;
			bool large = e.Key is VirtualKey.PageUp or VirtualKey.PageDown;
			double magnitude = large ? LargeChange : StepFrequency;
			double next = Value + (increase ? magnitude : -magnitude);
			double step = StepFrequency;

			if (step > 0.0)
			{
				next = Minimum + (Math.Round((next - Minimum) / step) * step);
			}

			next = Math.Clamp(next, Minimum, Maximum);

			if (next != Value)
			{
				double previous = Value;
				Value = next;

				if (large)
				{
					GlideThumbFrom(previous);
				}
			}

			e.Handled = true;
			return;
		}

		// Home/End は標準 Slider が最小・最大へ飛ばす。値の変更は委ね、変わったときだけつまみのグライドを足す。
		if (e.Key is VirtualKey.Home or VirtualKey.End)
		{
			double previous = Value;
			base.OnKeyDown(e);

			if (Value != previous)
			{
				GlideThumbFrom(previous);
			}

			return;
		}

		base.OnKeyDown(e);
	}




	// キーボードで値が飛んだ直後に、つまみを移動前の位置から現在位置へスプリングでグライドさせる。値とレイアウトは即座に確定させ、つまみの表示位置だけを合成側で滑らせるため、束縛値や色の再計算には影響しない。表示位置は式 Translation = Pos − LayoutPos で表し、LayoutPos を新位置へ即時に移したうえで Pos をスプリングで新位置へ寄せる。これで押下の瞬間も表示位置(=Pos)が連続し、連続入力でもスプリングが速度を保って寄せ先を差し替える。Windows のアニメーション設定がオフのときは滑らせず即座に合わせる。テンプレート未適用やトラック寸法が未確定のときは何もしない。
	private void GlideThumbFrom(double oldValue)
	{
		if (_lensThumb is null || _lensHost is null)
		{
			return;
		}

		double w = _lensHost.ActualWidth;
		double h = _lensHost.ActualHeight;

		if (w <= 0.0 || h <= 0.0)
		{
			return;
		}

		double oldCenter = ThumbCenterFor(oldValue, w, h);
		double newCenter = ThumbCenterFor(Value, w, h);

		EnsureGlide(oldCenter);

		if (_glideProps is null)
		{
			return;
		}

		// 静止状態からの開始では、ドラッグや数値入力でずれている可能性のある表示位置を移動前の位置へ置き直す。Translation は 0 のままなので見た目は動かない。グライド中は Pos が生きた表示位置を保つため置き直さない。
		if (!_gliding)
		{
			_glideProps.StopAnimation("Pos");
			_glideProps.InsertScalar("Pos", (float)oldCenter);
			_glideProps.InsertScalar("LayoutPos", (float)oldCenter);
		}

		// レイアウト上のつまみは新しい値の位置へ即座に移る。
		_glideProps.InsertScalar("LayoutPos", (float)newCenter);

		// アニメーションを出さない設定では表示中心も即座に新位置へ合わせ、ずれを残さない。
		if (!_uiSettings.AnimationsEnabled)
		{
			_glideProps.StopAnimation("Pos");
			_glideProps.InsertScalar("Pos", (float)newCenter);
			_gliding = false;
			return;
		}

		Compositor compositor = _glideProps.Compositor;
		int gen = ++_glideGen;
		CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

		SpringScalarNaturalMotionAnimation spring = compositor.CreateSpringScalarAnimation();
		spring.FinalValue = (float)newCenter;
		spring.DampingRatio = 0.7f;
		spring.Period = TimeSpan.FromMilliseconds(45);
		_glideProps.StartAnimation("Pos", spring);

		batch.End();

		// 最新の世代のスプリングが終わったときだけグライド終了とする。連続入力で差し替えられた古いスプリングの完了では下ろさない。
		batch.Completed += (_, _) =>
		{
			if (gen == _glideGen)
			{
				_gliding = false;
			}
		};

		_gliding = true;
	}




	// つまみのグライドの下ごしらえ。合成の Translation を有効化し、Translation = Pos − LayoutPos の式を一度だけ結ぶ。Pos・LayoutPos は静止位置(restCenter)で初期化し、結線直後の Translation を 0(ずれなし)にする。式の向きは沿軸(水平は x、垂直は y)に合わせる。
	private void EnsureGlide(double restCenter)
	{
		if (_glideReady || _lensThumb is null)
		{
			return;
		}

		ElementCompositionPreview.SetIsTranslationEnabled(_lensThumb, true);
		Visual thumb = ElementCompositionPreview.GetElementVisual(_lensThumb);
		Compositor compositor = thumb.Compositor;

		_glideProps = compositor.CreatePropertySet();
		_glideProps.InsertScalar("Pos", (float)restCenter);
		_glideProps.InsertScalar("LayoutPos", (float)restCenter);

		ExpressionAnimation link = compositor.CreateExpressionAnimation(_vertical ? "Vector3(0, glide.Pos - glide.LayoutPos, 0)" : "Vector3(glide.Pos - glide.LayoutPos, 0, 0)");
		link.SetReferenceParameter("glide", _glideProps);
		thumb.StartAnimation("Translation", link);

		_glideReady = true;
	}




	// 指定の値に対するつまみ中央の沿軸位置(水平は x、垂直は y)を、トラック寸法とつまみ径から求める。つまみは両端に半径ぶんの余白を残して動く。レンズ位置の算出(UpdateLensPosition)と同じ写像を使う。
	private double ThumbCenterFor(double value, double w, double h)
	{
		double range = Maximum - Minimum;
		double vNorm = range > 0.0 ? (value - Minimum) / range : 0.0;
		vNorm = Math.Clamp(vNorm, 0.0, 1.0);

		if (_vertical)
		{
			double halfThumb = (_lensThumb?.ActualHeight ?? 28.0) / 2.0;
			return (h - halfThumb) - (vNorm * (h - (2.0 * halfThumb)));
		}

		double half = (_lensThumb?.ActualWidth ?? 28.0) / 2.0;
		return half + (vNorm * (w - (2.0 * half)));
	}




	// 値が変わったら、ドラッグ中だけレンズを現在位置へ追従させる。キーボードやコードでの変更ではレンズを出していないため何もしない。
	protected override void OnValueChanged(double oldValue, double newValue)
	{
		base.OnValueChanged(oldValue, newValue);

		if (_isPointerDragging)
		{
			UpdateLensPosition();
		}
	}




	// レンズの表示を開始する。ルーペは最前面オーバーレイへ載せて切り抜きを避け、つまみを隠してルーペへ置き換える。
	private void BeginLens()
	{
		if (_lensHost is null)
		{
			return;
		}

		_isPointerDragging = true;

		BuildLoupe();
		UpdateLensPosition();

		if (_lensThumb is not null)
		{
			_lensThumb.Opacity = 0.0;
		}

		AnimateLoupe(show: true);
	}




	// レンズの表示を終える。退場アニメーションの完了でルーペを片付け、つまみと重なり順を元へ戻す。出していない(ドラッグ中でない)ときは何もしない。
	private void EndLens()
	{
		if (!_isPointerDragging)
		{
			return;
		}

		_isPointerDragging = false;
		AnimateStretchBack();
		AnimateLoupe(show: false);
	}




	// 現在の値・寸法に合わせて、ルーペ本体をつまみ中央へ重ね、拡大トラックの平行移動で現在位置の色を中央へ合わせる。
	private void UpdateLensPosition()
	{
		if (_lensHost is null || _loupe is null)
		{
			return;
		}

		double w = _lensHost.ActualWidth;
		double h = _lensHost.ActualHeight;

		if (w <= 0.0 || h <= 0.0)
		{
			return;
		}

		// つまみは沿軸の両端に半径ぶんの余白を残して動くため、その可動域に合わせて沿軸の中心位置を求める。水平は沿軸=x(左ほど値小)、垂直は沿軸=y(下ほど値小)。レンズはつまみ中心へ重なり、背後の実内容(トラック・周りのカード背景等)をガラス越しに拡大して見せる。
		double range = Maximum - Minimum;
		double vNorm = range > 0.0 ? (Value - Minimum) / range : 0.0;
		vNorm = Math.Clamp(vNorm, 0.0, 1.0);

		double centerX;
		double centerY;
		double centerAlong;
		double trackLength;

		if (_vertical)
		{
			double halfThumb = (_lensThumb?.ActualHeight ?? 28.0) / 2.0;
			// 値最大が上、最小が下。つまみ中心の y は上端余白から下へ。
			centerAlong = (h - halfThumb) - (vNorm * (h - (2.0 * halfThumb)));
			trackLength = h;
			centerX = w / 2.0;
			centerY = centerAlong;
		}
		else
		{
			double halfThumb = (_lensThumb?.ActualWidth ?? 28.0) / 2.0;
			centerAlong = halfThumb + (vNorm * (w - (2.0 * halfThumb)));
			trackLength = w;
			centerX = centerAlong;
			centerY = h / 2.0;
		}

		double d = LensParams.Diameter;
		double highlightRot = 0.0;

		// ルーペの配置を、トラック(レンズ置き場)からレンズの置き先への変換で求める。置き先がオーバーレイなら祖先のスクロール量も織り込まれ、スクロール中でも現在位置へ重なる。
		if (_loupePlacement is not null && _loupeTarget is not null)
		{
			GeneralTransform toTarget = _lensHost.TransformToVisual(_loupeTarget);
			_loupePlacement.Matrix = LensOverlayService.ComputePlacement(toTarget, centerX - (d / 2.0), centerY - (d / 2.0));

			// レンズ中心を置き先(オーバーレイ)の座標へ写し、その位置から仮想光源への方位で鏡面ハイライトの回転を求める。
			Point centerInTarget = toTarget.TransformPoint(new Point(centerX, centerY));
			highlightRot = GlassLens.ComputeHighlightRotation(centerInTarget, _loupeTarget.ActualWidth, _loupeTarget.ActualHeight);
		}

		// レンズへ現在の向き・つまみ位置・トラック寸法・グラデーション・色域外区間・その見せ方を渡し、背後の像を組み直して拡大・屈折・色収差を掛け直す。色域外区間があればトラックと同じ白塗り・斜線・境界線も重なる。
		var track = new GlassTrack
		{
			Vertical = _vertical,
			CenterAlong = centerAlong,
			TrackLength = trackLength,
			BandThickness = TrackThickness,
			CornerRadius = TrackCornerRadius,
		};
		_glass?.Update(track, _glassStops, OutOfGamutSegments, OutOfGamutStyle, highlightRot);
	}




	// ルーペの部品を組み立てて最前面オーバーレイへ載せる。中身は背後の実内容を拡大・屈折・色収差して見せるガラス合成(SpriteVisual)で、円形に切り抜く。その上へ、明暗どちらの像でも縁が見えるよう白いリングの外へ半透明の暗いリングを重ねる(つまみと同じ作法)。ガラス合成が使えない環境では中身なしで縁のリングだけ出す。
	private void BuildLoupe()
	{
		if (_lensHost is null)
		{
			return;
		}

		RemoveLoupe();
		DisposeGlass();

		double d = LensParams.Diameter;

		// ガラスの中身となる Win2D 描画コントロール。作れない環境では中身を諦め、縁のリングだけ見せる。
		UIElement? glassContent = null;

		try
		{
			_glass = new GlassLens();
			glassContent = _glass.Build(GlassLens.ApplyTuning(LensParams), ResolveCardColor(), ShowCheckerboard);
			_glassStops = ExtractStops(TrackBrush);
		}
		catch
		{
			DisposeGlass();
			glassContent = null;
		}

		var darkRing = new Ellipse
		{
			Width = d,
			Height = d,
			Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
			StrokeThickness = 4.0,
		};

		var whiteRing = new Ellipse
		{
			Width = d,
			Height = d,
			Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
			StrokeThickness = 2.0,
		};

		// 拡大(ポップ)はレンズ中心まわりで、配置(平行移動)は置き場から置き先への変換で行う。両者を合成して RenderTransform へ与える。
		_loupeScale = new ScaleTransform { CenterX = d / 2.0, CenterY = d / 2.0 };
		_loupePlacement = new MatrixTransform();
		_loupe = new Grid
		{
			Width = d,
			Height = d,
			RenderTransform = new TransformGroup { Children = { _loupeScale, _loupePlacement } },
			IsHitTestVisible = false,
		};
		if (glassContent is not null)
		{
			_loupe.Children.Add(glassContent);
		}

		_loupe.Children.Add(darkRing);
		_loupe.Children.Add(whiteRing);

		_loupeTarget = LensOverlayService.Get(this.XamlRoot) ?? _lensHost;
		_loupeTarget.Children.Add(_loupe);
	}




	// 載せているルーペを置き先から取り除く。オーバーレイは複数のレンズ置き場で共有するため、全消去ではなく自分のルーペだけを外す。
	private void RemoveLoupe()
	{
		if (_loupe is not null)
		{
			_loupeTarget?.Children.Remove(_loupe);
		}
	}




	// ガラスを破棄する。ドラッグの組み直しと退場で、Win2D のリソースを解放する。
	private void DisposeGlass()
	{
		_glass?.Dispose();
		_glass = null;
		_glassStops = System.Array.Empty<GlassGradientStop>();
	}




	// レンズの帯の上下に敷くカード背景色。スライダーの背後はテーマのカード色のため、実効テーマに合わせて近い不透明色を返す。
	private Color ResolveCardColor()
	{
		bool dark = ActualTheme == ElementTheme.Dark;
		return dark ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);
	}




	// トラックのブラシからグラデーションの停止点を取り出す。レンズが帯を再構成して描くのに使う。線形グラデーション以外(画像ブラシ等)では取り出せないため空を返し、その場合はレンズに帯を描かない。
	private static System.Collections.Generic.IReadOnlyList<GlassGradientStop> ExtractStops(Brush? brush)
	{
		var stops = new System.Collections.Generic.List<GlassGradientStop>();

		if (brush is LinearGradientBrush gradient)
		{
			foreach (GradientStop stop in gradient.GradientStops)
			{
				stops.Add(new GlassGradientStop(stop.Color, (float)stop.Offset));
			}
		}

		return stops;
	}




	// ルーペの拡大・退場をアニメーションする。表示はつまみ大からレンズ大へ膨らませながら現し、退場はその逆で畳んでから片付ける。拡大とフェードはどちらも合成側で走る独立アニメーションのため、レイアウトを巻き込まない。
	private void AnimateLoupe(bool show)
	{
		if (_loupe is null || _loupeScale is null)
		{
			return;
		}

		// ルーペは静止時のつまみの大きさから膨らませる。向きで異なるつまみ径(横20・縦28)に合わせ、実寸を基準にする。
		double thumbSize = _lensThumb is not null && _lensThumb.ActualWidth > 0.0 ? _lensThumb.ActualWidth : 28.0;
		double thumbScale = thumbSize / LensParams.Diameter;
		double from = show ? thumbScale : 1.0;
		double to = show ? 1.0 : thumbScale;
		var duration = new Duration(TimeSpan.FromMilliseconds(120));
		var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };

		var scaleX = new DoubleAnimation { From = from, To = to, Duration = duration, EasingFunction = ease };
		var scaleY = new DoubleAnimation { From = from, To = to, Duration = duration, EasingFunction = ease };
		var opacity = new DoubleAnimation { From = show ? 0.0 : 1.0, To = show ? 1.0 : 0.0, Duration = duration };

		Storyboard.SetTarget(scaleX, _loupeScale);
		Storyboard.SetTargetProperty(scaleX, "ScaleX");
		Storyboard.SetTarget(scaleY, _loupeScale);
		Storyboard.SetTargetProperty(scaleY, "ScaleY");
		Storyboard.SetTarget(opacity, _loupe);
		Storyboard.SetTargetProperty(opacity, "Opacity");

		var storyboard = new Storyboard();
		storyboard.Children.Add(scaleX);
		storyboard.Children.Add(scaleY);
		storyboard.Children.Add(opacity);

		if (!show)
		{
			storyboard.Completed += (_, _) => FinishHideLens();
		}

		storyboard.Begin();
	}




	// 退場アニメーションの完了後にルーペを片付け、つまみを元へ戻す。畳んでいる途中に再びドラッグが始まっていたら、新しいルーペを消さないよう何もしない。
	private void FinishHideLens()
	{
		if (_isPointerDragging)
		{
			return;
		}

		if (_lensThumb is not null)
		{
			_lensThumb.Opacity = 1.0;
		}

		RemoveLoupe();
		DisposeGlass();
		_loupe = null;
		_loupeScale = null;
		_loupePlacement = null;
		_loupeTarget = null;
	}
}
