// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Irozukume.Controls;

// 三角形の中で2次元の値を選ぶ汎用パッド。HSL の彩度・輝度を、純色・白・黒を頂点とする三角形(双錐の色相断面)で選ぶことを想定する。XValue は彩度(その輝度での三角形の幅に対する割合)、YValue は輝度を表し、ともに 0–1。見せる三角形のグラデーション等は Content に置く。PadRotation を与えると Content とつまみと当たり判定面を一体で回し、入力位置も逆回転して値へ写すため、色相環へ追従して回しても操作と表示が一致する。幾何は TriangleGeometry に委ね、つまみの位置・当たり判定が三角形画像とずれないようにする。
public sealed class TrianglePad : ContentControl
{
	// テンプレート内の回転トランスフォームとつまみの平行移動。回転は Content とつまみをまとめて回し、平行移動はつまみを三角形内の値の位置へ置く。
	private RotateTransform? _rotation;
	private TranslateTransform? _thumbOffset;

	// つまみをドラッグ中かどうか。ポインタを捕捉している間だけ真にする。
	private bool _isDragging;

	// つまみ要素。ドラッグ中はレンズへ置き換えるため隠す。
	private FrameworkElement? _thumb;

	// ドラッグ中につまみをガラスレンズへ膨らませる管理役と、その置き場。テンプレートに置き場があるときだけ用意する。レンズは三角形と同じ回転枠に入るため、色面の向きと一致する。
	private Canvas? _lensHost;
	private LensController? _lens;

	// レンズに映す色面の色を返すサンプラー。コントロール局所座標(画素、未回転)を受け、その点の色(三角形の外は透明)を返す。利用側が設定する。null のときはレンズを出さず、従来どおりのつまみで操作する。
	public Func<double, double, Color>? LensColorSampler { get; set; }

	// 三角形パッドのつまみのガラスレンズの効き。他コントロールと別に調整できるよう、ここで持つ。各項目の意味と単位は GlassLensParams を参照。
	private static readonly GlassLensParams LensParams = new()
	{
		Diameter = 50.0,
		Magnify = 1.4,
		EdgeAmount = -24.0,
		Chroma = true,
		ChromaSpread = 0.4,
		BevelFraction = 0.4,
	};


	public TrianglePad()
	{
		SizeChanged += OnSizeChanged;
	}




	// 彩度。その輝度での三角形の幅に対する割合で、0(灰色軸側)から 1(純色側の辺)まで。
	public double XValue
	{
		get => (double)GetValue(XValueProperty);
		set => SetValue(XValueProperty, value);
	}

	public static readonly DependencyProperty XValueProperty =
		DependencyProperty.Register(nameof(XValue), typeof(double), typeof(TrianglePad), new PropertyMetadata(1.0, OnValueChanged));




	// 輝度。0(黒の頂点)から 1(白の頂点)まで。
	public double YValue
	{
		get => (double)GetValue(YValueProperty);
		set => SetValue(YValueProperty, value);
	}

	public static readonly DependencyProperty YValueProperty =
		DependencyProperty.Register(nameof(YValue), typeof(double), typeof(TrianglePad), new PropertyMetadata(0.5, OnValueChanged));




	// パッド全体の回転角(度, 時計回り)。Content とつまみを中心まわりに回し、入力も同じだけ逆回転して値へ写す。
	public double PadRotation
	{
		get => (double)GetValue(PadRotationProperty);
		set => SetValue(PadRotationProperty, value);
	}

	public static readonly DependencyProperty PadRotationProperty =
		DependencyProperty.Register(nameof(PadRotation), typeof(double), typeof(TrianglePad), new PropertyMetadata(0.0, OnRotationChanged));




	// つまみの直径。
	public double ThumbDiameter
	{
		get => (double)GetValue(ThumbDiameterProperty);
		set => SetValue(ThumbDiameterProperty, value);
	}

	public static readonly DependencyProperty ThumbDiameterProperty =
		DependencyProperty.Register(nameof(ThumbDiameter), typeof(double), typeof(TrianglePad), new PropertyMetadata(18.0));




	// 三角形の頂点を丸める半径(画素)。0 で角丸なし。継承元 Control.CornerRadius(矩形の角丸、ここでは無意味)との衝突を避けるため別名にする。三角形は形そのものを画像へ焼くため、この値は描画側(HslTriangle.Create)とレンズのサンプラーへ渡して使う。利用側はこのプロパティを見て、変わったら三角形画像を作り直す。
	public double VertexCornerRadius
	{
		get => (double)GetValue(VertexCornerRadiusProperty);
		set => SetValue(VertexCornerRadiusProperty, value);
	}

	public static readonly DependencyProperty VertexCornerRadiusProperty =
		DependencyProperty.Register(nameof(VertexCornerRadius), typeof(double), typeof(TrianglePad), new PropertyMetadata(3.0));




	protected override void OnApplyTemplate()
	{
		base.OnApplyTemplate();

		_rotation = GetTemplateChild("PART_Rotation") as RotateTransform;

		if (_rotation is not null)
		{
			_rotation.Angle = PadRotation;
		}

		_thumb = GetTemplateChild("PART_Thumb") as FrameworkElement;

		if (_thumb is not null)
		{
			_thumbOffset = _thumb.RenderTransform as TranslateTransform;

			if (_thumbOffset is null)
			{
				_thumbOffset = new TranslateTransform();
				_thumb.RenderTransform = _thumbOffset;
			}
		}

		_lensHost = GetTemplateChild("PART_Lens") as Canvas;
		_lens = _lensHost is not null ? new LensController(this, _lensHost, LensParams) : null;

		UpdateThumb();
	}




	private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var self = (TrianglePad)d;
		double raw = (double)e.NewValue;
		double clamped = Math.Clamp(raw, 0.0, 1.0);

		if (clamped != raw)
		{
			// クランプした値で設定し直す。再入時はここを通らず、つまみ更新へ進む。
			self.SetValue(e.Property, clamped);
			return;
		}

		self.UpdateThumb();
	}




	private static void OnRotationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var self = (TrianglePad)d;

		if (self._rotation is not null)
		{
			self._rotation.Angle = (double)e.NewValue;
		}
	}




	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateThumb();
	}




	// 現在の値と寸法に合わせて、つまみを三角形内の位置へ移動する。中心を原点とした未回転座標で置くため、回転トランスフォームによって Content と同じだけ回って表示位置が一致する。
	private void UpdateThumb()
	{
		if (_thumbOffset is null)
		{
			return;
		}

		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		TriangleVertices vertices = TriangleGeometry.ComputeVertices(ActualWidth, ActualHeight);
		(double wHue, double wBlack, double wWhite) = TriangleGeometry.SlToBarycentric(XValue, YValue);
		Point point = TriangleGeometry.BarycentricToPoint(wHue, wBlack, wWhite, vertices);
		_thumbOffset.X = point.X - (ActualWidth / 2.0);
		_thumbOffset.Y = point.Y - (ActualHeight / 2.0);
	}




	protected override void OnPointerPressed(PointerRoutedEventArgs e)
	{
		base.OnPointerPressed(e);

		if (!TryMapToValues(e.GetCurrentPoint(this).Position, out double saturation, out double lightness, out bool inside))
		{
			return;
		}

		// 回転後の三角形の外を押した場合は捕捉も処理もせず、背後のリング(色相)へ通す。
		if (!inside)
		{
			return;
		}

		_isDragging = CapturePointer(e.Pointer);
		Focus(FocusState.Pointer);
		XValue = saturation;
		YValue = lightness;

		if (_isDragging)
		{
			BeginLens();
		}

		e.Handled = true;
	}




	protected override void OnPointerMoved(PointerRoutedEventArgs e)
	{
		base.OnPointerMoved(e);

		if (!_isDragging)
		{
			return;
		}

		if (TryMapToValues(e.GetCurrentPoint(this).Position, out double saturation, out double lightness, out bool _))
		{
			XValue = saturation;
			YValue = lightness;
		}

		UpdateLens();
		e.Handled = true;
	}




	protected override void OnPointerReleased(PointerRoutedEventArgs e)
	{
		base.OnPointerReleased(e);

		if (!_isDragging)
		{
			return;
		}

		_isDragging = false;
		ReleasePointerCapture(e.Pointer);
		EndLens();
		e.Handled = true;
	}




	protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
	{
		base.OnPointerCaptureLost(e);
		_isDragging = false;
		EndLens();
	}




	// ドラッグ開始時にレンズを出す。サンプラーが設定されていなければ何もしない。つまみは隠してレンズへ置き換える。
	private void BeginLens()
	{
		if (_lens is null || LensColorSampler is null)
		{
			return;
		}

		_lens.Begin();

		if (_thumb is not null)
		{
			_thumb.Opacity = 0.0;
		}

		UpdateLens();
	}




	// レンズを現在のつまみ位置(未回転の局所座標)へ追従させ、その点まわりの色面を映し直す。つまみの位置は三角形の重心座標から求める。レンズは三角形と同じ回転枠に入るため、ここでは回転を考えず局所座標で扱う。
	private void UpdateLens()
	{
		if (_lens is null || !_lens.IsActive || LensColorSampler is null)
		{
			return;
		}

		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		TriangleVertices vertices = TriangleGeometry.ComputeVertices(ActualWidth, ActualHeight);
		(double wHue, double wBlack, double wWhite) = TriangleGeometry.SlToBarycentric(XValue, YValue);
		Point point = TriangleGeometry.BarycentricToPoint(wHue, wBlack, wWhite, vertices);
		_lens.Update(LensColorSampler, point.X, point.Y);
	}




	// ドラッグ終了時にレンズを退場させ、つまみを戻す。
	private void EndLens()
	{
		if (_lens is null)
		{
			return;
		}

		_lens.End();

		if (_thumb is not null)
		{
			_thumb.Opacity = 1.0;
		}
	}




	// 色相環のレンズが内側のこのパッドを覗くとき、静止しているつまみ(中空の二重リング)をその場へ描き込むためのサンプラー。パッド局所座標 (x, y) が現在のつまみ(彩度 XValue・輝度 YValue を三角形の重心座標へ写した位置)の輪の上にあれば、その色を baseColor へ重ねて返す。色面の外(baseColor が透明)や輪の外はそのまま返す。色相環のレンズはこれを色面の色の上から掛けるため、つまみも色面と一緒に拡大・屈折されて映る。三角形そのものをドラッグ中はつまみがレンズへ置き換わって消えるのが正しいため、本サンプラーは色相環側からの覗き見にだけ使う。
	public Color SampleThumbOverlay(Color baseColor, double x, double y)
	{
		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return baseColor;
		}

		TriangleVertices vertices = TriangleGeometry.ComputeVertices(ActualWidth, ActualHeight);
		(double wHue, double wBlack, double wWhite) = TriangleGeometry.SlToBarycentric(XValue, YValue);
		Point point = TriangleGeometry.BarycentricToPoint(wHue, wBlack, wWhite, vertices);
		return ThumbGlyph.Overlay(baseColor, x, y, point.X, point.Y, ThumbDiameter);
	}




	protected override void OnKeyDown(KeyRoutedEventArgs e)
	{
		base.OnKeyDown(e);

		const double step = 0.01;
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

		NudgeByScreenDelta(deltaX, deltaY);
		e.Handled = true;
	}




	// 画面上の位置を彩度・輝度へ写す。中心を原点に取り、PadRotation の逆回転で未回転座標へ戻してから三角形の重心座標を求める。inside には回転前の重みがすべて非負か(=三角形の内側か)を返し、外側の点はクランプして辺・頂点へ寄せた値を返す。寸法が無いときは false を返す。
	private bool TryMapToValues(Point position, out double saturation, out double lightness, out bool inside)
	{
		saturation = 0.0;
		lightness = 0.0;
		inside = false;

		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return false;
		}

		double centerX = ActualWidth / 2.0;
		double centerY = ActualHeight / 2.0;
		double vx = position.X - centerX;
		double vy = position.Y - centerY;

		double radians = PadRotation * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		double localX = (vx * cos) + (vy * sin) + centerX;
		double localY = (-vx * sin) + (vy * cos) + centerY;

		TriangleVertices vertices = TriangleGeometry.ComputeVertices(ActualWidth, ActualHeight);
		(double wHue, double wBlack, double wWhite) = TriangleGeometry.PointToBarycentric(new Point(localX, localY), vertices);
		inside = wHue >= 0.0 && wBlack >= 0.0 && wWhite >= 0.0;

		(double clampedHue, double clampedBlack, double clampedWhite) = TriangleGeometry.ClampBarycentric(wHue, wBlack, wWhite);
		(saturation, lightness) = TriangleGeometry.BarycentricToSl(clampedHue, clampedBlack, clampedWhite);
		return true;
	}




	// 矢印キーの方向を画面上の移動として扱い、現在のつまみの画面位置をその分ずらしてから値へ写し直す。回転時もポインタ操作と移動方向が一致する。引数は幅・高さに対する比率で与える。
	private void NudgeByScreenDelta(double fractionX, double fractionY)
	{
		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		TriangleVertices vertices = TriangleGeometry.ComputeVertices(ActualWidth, ActualHeight);
		(double wHue, double wBlack, double wWhite) = TriangleGeometry.SlToBarycentric(XValue, YValue);
		Point point = TriangleGeometry.BarycentricToPoint(wHue, wBlack, wWhite, vertices);

		double centerX = ActualWidth / 2.0;
		double centerY = ActualHeight / 2.0;
		double offsetX = point.X - centerX;
		double offsetY = point.Y - centerY;

		double radians = PadRotation * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		double screenX = (offsetX * cos) - (offsetY * sin) + centerX + (fractionX * ActualWidth);
		double screenY = (offsetX * sin) + (offsetY * cos) + centerY + (fractionY * ActualHeight);

		if (TryMapToValues(new Point(screenX, screenY), out double saturation, out double lightness, out bool _))
		{
			XValue = saturation;
			YValue = lightness;
		}
	}
}
