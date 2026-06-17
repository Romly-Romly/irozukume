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

// 環状に値を選ぶ汎用スライダー。角度値(度, 0–360, 端で巻き戻る)を環の上のつまみで選ぶ。色相を前提とせず、リングの塗りは利用側が TrackBrush に与える(色相環画像など)。中央は ContentPresenter として空けてあり、2次元スライダー等を Content に置ける。リングの帯の上だけを操作対象とし、中央の Content は独立して入力を受けられる。値の変化は ValueChanged で通知する。
public sealed class RingSlider : ContentControl
{
	// テンプレート内のつまみとその平行移動。値と寸法に応じて中央から環の上へずらす。
	private FrameworkElement? _thumb;
	private TranslateTransform? _thumbOffset;

	// つまみをドラッグ中かどうか。ポインタを捕捉している間だけ真にする。
	private bool _isDragging;

	// ドラッグ中につまみをガラスレンズへ膨らませる管理役と、その置き場。テンプレートに置き場があるときだけ用意する。
	private Canvas? _lensHost;
	private LensController? _lens;

	// レンズに映す色面の色を返すサンプラー。コントロール局所座標(画素)を受け、その点の色(色相環なら帯の色、帯の外は透明)を返す。利用側が設定する。null のときはレンズを出さず、従来どおりのつまみで操作する。
	public Func<double, double, Color>? LensColorSampler { get; set; }

	// 色相環のつまみのガラスレンズの効き。2次元パッドとは別に調整できるよう、ここで持つ。各項目の意味と単位は GlassLensParams を参照。
	private static readonly GlassLensParams LensParams = new()
	{
		Diameter = 48.0,
		Magnify = 1.0,
		EdgeAmount = -28.0,
		Chroma = true,
		ChromaSpread = 0.4,
		BevelFraction = 0.35,
	};

	// Value の正規化(0–360 への巻き戻し)で変更通知が再入するのを抑える。
	private bool _isCoercingValue;


	public RingSlider()
	{
		SizeChanged += OnSizeChanged;
	}




	// 現在の角度値(度)。0 を真上とし時計回りに増える。0 と 360 は同一の位置を指すため、範囲外の値は [0, 360) へ巻き戻して保持する。
	public double Value
	{
		get => (double)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	public static readonly DependencyProperty ValueProperty =
		DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingSlider), new PropertyMetadata(0.0, OnValueChanged));




	// リングの帯の太さ。色相環などのグラデーションを見せる幅として使い、つまみの当たり判定の帯幅にもなる。
	public double RingThickness
	{
		get => (double)GetValue(RingThicknessProperty);
		set => SetValue(RingThicknessProperty, value);
	}

	public static readonly DependencyProperty RingThicknessProperty =
		DependencyProperty.Register(nameof(RingThickness), typeof(double), typeof(RingSlider), new PropertyMetadata(28.0, OnMetricsChanged));




	// つまみの直径。中央半径上に置かれ、この径の分だけリング帯の内外へはみ出す。
	public double ThumbDiameter
	{
		get => (double)GetValue(ThumbDiameterProperty);
		set => SetValue(ThumbDiameterProperty, value);
	}

	public static readonly DependencyProperty ThumbDiameterProperty =
		DependencyProperty.Register(nameof(ThumbDiameter), typeof(double), typeof(RingSlider), new PropertyMetadata(24.0, OnMetricsChanged));




	// リングの帯を塗るブラシ。色相環の画像ブラシなど、環の見た目の中身は利用側が与える。
	public Brush? TrackBrush
	{
		get => (Brush?)GetValue(TrackBrushProperty);
		set => SetValue(TrackBrushProperty, value);
	}

	public static readonly DependencyProperty TrackBrushProperty =
		DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RingSlider), new PropertyMetadata(null));




	// 矢印キーひと押しでの変化量(度)。
	public double SmallChange
	{
		get => (double)GetValue(SmallChangeProperty);
		set => SetValue(SmallChangeProperty, value);
	}

	public static readonly DependencyProperty SmallChangeProperty =
		DependencyProperty.Register(nameof(SmallChange), typeof(double), typeof(RingSlider), new PropertyMetadata(1.0));




	// PageUp/PageDown ひと押しでの変化量(度)。
	public double LargeChange
	{
		get => (double)GetValue(LargeChangeProperty);
		set => SetValue(LargeChangeProperty, value);
	}

	public static readonly DependencyProperty LargeChangeProperty =
		DependencyProperty.Register(nameof(LargeChange), typeof(double), typeof(RingSlider), new PropertyMetadata(10.0));




	// 値が変わったときに新しい値(度)を通知する。
	public event TypedEventHandler<RingSlider, double>? ValueChanged;




	protected override void OnApplyTemplate()
	{
		base.OnApplyTemplate();

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
		var self = (RingSlider)d;

		if (self._isCoercingValue)
		{
			return;
		}

		double raw = (double)e.NewValue;
		double normalized = Normalize(raw);

		if (normalized != raw)
		{
			// 巻き戻した値で設定し直す。再設定での再入は冒頭のガードで抜けるため、つまみ更新と通知はこの呼び出しで続けて行う。
			self._isCoercingValue = true;
			self.Value = normalized;
			self._isCoercingValue = false;
		}

		self.UpdateThumb();
		self.ValueChanged?.Invoke(self, normalized);
	}




	private static void OnMetricsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((RingSlider)d).UpdateThumb();
	}




	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateThumb();
	}




	// 現在の値と寸法に合わせて、つまみを中央から環の上へ移動する。
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

		RingMetrics metrics = RingGeometry.Compute(ActualWidth, ActualHeight, RingThickness, ThumbDiameter);
		Point offset = RingGeometry.OffsetForValue(metrics.MidRadius, Value);
		_thumbOffset.X = offset.X;
		_thumbOffset.Y = offset.Y;
	}




	protected override void OnPointerPressed(PointerRoutedEventArgs e)
	{
		base.OnPointerPressed(e);

		Point position = e.GetCurrentPoint(this).Position;

		if (!IsInBand(position))
		{
			return;
		}

		_isDragging = CapturePointer(e.Pointer);
		Focus(FocusState.Pointer);
		SetValueFromPosition(position);

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

		SetValueFromPosition(e.GetCurrentPoint(this).Position);
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




	// レンズを現在のつまみ位置へ追従させ、その点まわりの色面を映し直す。
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

		RingMetrics metrics = RingGeometry.Compute(ActualWidth, ActualHeight, RingThickness, ThumbDiameter);
		Point offset = RingGeometry.OffsetForValue(metrics.MidRadius, Value);
		_lens.Update(LensColorSampler, metrics.Center.X + offset.X, metrics.Center.Y + offset.Y);
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




	// 別のコントロール(中央のパッド)のレンズが色相環を覗くとき、静止しているつまみ(中空の二重リング)をその場へ描き込むためのサンプラー。リング局所座標 (x, y) の点が現在のつまみの輪の上にあれば、その色を baseColor へ重ねて返す。輪の外や帯の外(baseColor が透明)はそのまま返す。パッドのレンズはこれを帯の色の上から掛けるため、つまみも帯と一緒に拡大・屈折されてレンズに映る。色相環そのものをドラッグ中は、つまみがレンズへ置き換わって消えるのが正しいため、本サンプラーはパッド側からの覗き見にだけ使う。
	public Color SampleThumbOverlay(Color baseColor, double x, double y)
	{
		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return baseColor;
		}

		RingMetrics metrics = RingGeometry.Compute(ActualWidth, ActualHeight, RingThickness, ThumbDiameter);
		Point offset = RingGeometry.OffsetForValue(metrics.MidRadius, Value);
		return ThumbGlyph.Overlay(baseColor, x, y, metrics.Center.X + offset.X, metrics.Center.Y + offset.Y, ThumbDiameter);
	}




	protected override void OnKeyDown(KeyRoutedEventArgs e)
	{
		base.OnKeyDown(e);

		switch (e.Key)
		{
			case VirtualKey.Right:
			case VirtualKey.Up:
				Value += SmallChange;
				e.Handled = true;
				break;
			case VirtualKey.Left:
			case VirtualKey.Down:
				Value -= SmallChange;
				e.Handled = true;
				break;
			case VirtualKey.PageUp:
				Value += LargeChange;
				e.Handled = true;
				break;
			case VirtualKey.PageDown:
				Value -= LargeChange;
				e.Handled = true;
				break;
		}
	}




	// ポインタ位置がリングの帯(つまみのはみ出し分を含む)の上にあるかを判定する。中央の穴や外側の余白での押下は操作対象にせず、中央の Content が入力を受けられるようにする。
	private bool IsInBand(Point position)
	{
		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return false;
		}

		RingMetrics metrics = RingGeometry.Compute(ActualWidth, ActualHeight, RingThickness, ThumbDiameter);
		double dx = position.X - metrics.Center.X;
		double dy = position.Y - metrics.Center.Y;
		double radius = Math.Sqrt((dx * dx) + (dy * dy));
		double thumbRadius = ThumbDiameter / 2.0;
		return radius >= metrics.InnerRadius - thumbRadius && radius <= metrics.OuterRadius + thumbRadius;
	}




	// ポインタ位置の指す角度を値へ反映する。
	private void SetValueFromPosition(Point position)
	{
		RingMetrics metrics = RingGeometry.Compute(ActualWidth, ActualHeight, RingThickness, ThumbDiameter);
		double dx = position.X - metrics.Center.X;
		double dy = position.Y - metrics.Center.Y;
		Value = RingGeometry.ValueFromPoint(dx, dy);
	}




	// 任意の角度を [0, 360) へ巻き戻す。
	private static double Normalize(double value)
	{
		double wrapped = value % 360.0;

		if (wrapped < 0.0)
		{
			wrapped += 360.0;
		}

		return wrapped;
	}
}
