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

/// <summary>
/// 矩形の中で 2 次元の値を選ぶ汎用パッド。
/// XValue は左 0・右 1、YValue は下 0・上 1 を表す。色を前提とせず、見せるグラデーション等は Content に置く。
/// PadRotation を与えると Content とつまみを中心まわりに回し、入力位置も同じ角度で逆回転して値へ写すため、回転しても操作と表示が一致する。
/// 中央の環状スライダーに彩度・明度パッドとして収め、色相に追従して回す用途を想定する。
/// </summary>
public sealed class PlanarPad : ContentControl
{
	/// <summary>
	/// テンプレート内の回転トランスフォームとつまみの平行移動。回転は Content とつまみをまとめて回し、平行移動はつまみを矩形内の値の位置へ置く。
	/// </summary>
	private RotateTransform? _rotation;
	private TranslateTransform? _thumbOffset;

	/// <summary>
	/// 色面を包む角丸クリップ。FieldCornerRadius を CornerRadius として書き込む。
	/// </summary>
	private Border? _fieldClip;

	/// <summary>
	/// つまみをドラッグ中かどうか。ポインタを捕捉している間だけ真にする。
	/// </summary>
	private bool _isDragging;

	/// <summary>
	/// つまみ要素。ドラッグ中はレンズへ置き換えるため隠す。
	/// </summary>
	private FrameworkElement? _thumb;

	/// <summary>
	/// ドラッグ中につまみをガラスレンズへ膨らませる管理役と、その置き場。
	/// テンプレートに置き場があるときだけ用意する。レンズはパッドと同じ回転枠に入るため、色面の向きと一致する。
	/// </summary>
	private Canvas? _lensHost;
	private LensController? _lens;

	/// <summary>
	/// レンズに映す色面の色を返すサンプラー。
	/// コントロール局所座標(画素、未回転)を受け、その点の色を返す。利用側(彩度明度なら HSV→RGB、等)が設定する。null のときはレンズを出さず、通常のつまみで操作する。
	/// </summary>
	public Func<double, double, Color>? LensColorSampler { get; set; }

	/// <summary>
	/// 2次元パッドのつまみのガラスレンズの効き。色相環とは別に調整できるよう、ここで持つ。各項目の意味と単位は <see cref="GlassLensParams"/> を参照。
	/// </summary>
	private static readonly GlassLensParams LensParams = new()
	{
		Diameter = 50.0,
		Magnify = 1.4,
		EdgeAmount = -24.0,
		Chroma = true,
		ChromaSpread = 0.4,
		BevelFraction = 0.4,
	};


	public PlanarPad()
	{
		SizeChanged += OnSizeChanged;
	}




	/// <summary>
	/// 横方向の値。左端を 0、右端を 1 とする。
	/// </summary>
	public double XValue
	{
		get => (double)GetValue(XValueProperty);
		set => SetValue(XValueProperty, value);
	}

	public static readonly DependencyProperty XValueProperty =
		DependencyProperty.Register(nameof(XValue), typeof(double), typeof(PlanarPad), new PropertyMetadata(1.0, OnValueChanged));




	/// <summary>
	/// 縦方向の値。下端を 0、上端を 1 とする。
	/// </summary>
	public double YValue
	{
		get => (double)GetValue(YValueProperty);
		set => SetValue(YValueProperty, value);
	}

	public static readonly DependencyProperty YValueProperty =
		DependencyProperty.Register(nameof(YValue), typeof(double), typeof(PlanarPad), new PropertyMetadata(1.0, OnValueChanged));




	/// <summary>
	/// パッド全体の回転角(度, 時計回り)。Content とつまみを中心まわりに回し、入力も同じだけ逆回転して値へ写す。
	/// </summary>
	public double PadRotation
	{
		get => (double)GetValue(PadRotationProperty);
		set => SetValue(PadRotationProperty, value);
	}

	public static readonly DependencyProperty PadRotationProperty =
		DependencyProperty.Register(nameof(PadRotation), typeof(double), typeof(PlanarPad), new PropertyMetadata(0.0, OnRotationChanged));




	/// <summary>
	/// つまみの直径。
	/// </summary>
	public double ThumbDiameter
	{
		get => (double)GetValue(ThumbDiameterProperty);
		set => SetValue(ThumbDiameterProperty, value);
	}

	public static readonly DependencyProperty ThumbDiameterProperty =
		DependencyProperty.Register(nameof(ThumbDiameter), typeof(double), typeof(PlanarPad), new PropertyMetadata(18.0));




	/// <summary>
	/// 色面の角丸量(画素)。色面を包む Border の角丸でまとめて切り抜く。0 で角丸なし。
	/// </summary>
	public double FieldCornerRadius
	{
		get => (double)GetValue(FieldCornerRadiusProperty);
		set => SetValue(FieldCornerRadiusProperty, value);
	}

	public static readonly DependencyProperty FieldCornerRadiusProperty =
		DependencyProperty.Register(nameof(FieldCornerRadius), typeof(double), typeof(PlanarPad), new PropertyMetadata(3.0, OnFieldCornerRadiusChanged));




	/// <summary>
	/// 利用者の操作(ポインタ・キーボード)で XValue・YValue が更新されたときに発火する。
	/// 横と縦を独立に束縛するのではなく、2軸をまとめて受け取って2次元で処理したい利用側(色域内への最近傍寄せなど)が使う。束縛や外部からの設定による変更では発火しない。
	/// </summary>
	public event EventHandler? ValuesChanged;




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

		_fieldClip = GetTemplateChild("PART_FieldClip") as Border;
		ApplyFieldCornerRadius();

		UpdateThumb();
	}




	/// <summary>
	/// 色面の角丸量を、それを包む Border の CornerRadius へ反映する。
	/// </summary>
	private void ApplyFieldCornerRadius()
	{
		if (_fieldClip is not null)
		{
			_fieldClip.CornerRadius = new CornerRadius(FieldCornerRadius);
		}
	}




	private static void OnFieldCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((PlanarPad)d).ApplyFieldCornerRadius();
	}




	private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var self = (PlanarPad)d;
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
		var self = (PlanarPad)d;

		if (self._rotation is not null)
		{
			self._rotation.Angle = (double)e.NewValue;
		}
	}




	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateThumb();
	}




	/// <summary>
	/// 現在の値と寸法に合わせて、つまみを矩形内の位置へ移動する。
	/// 中心を原点とした未回転座標で置くため、回転トランスフォームによって Content と同じだけ回って表示位置が一致する。
	/// </summary>
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

		_thumbOffset.X = (XValue - 0.5) * ActualWidth;
		_thumbOffset.Y = (0.5 - YValue) * ActualHeight;
	}




	protected override void OnPointerPressed(PointerRoutedEventArgs e)
	{
		base.OnPointerPressed(e);

		if (!TryMapToValues(e.GetCurrentPoint(this).Position, out double fractionX, out double fractionY))
		{
			return;
		}

		// 回転後のパッドの外(矩形 [0,1] の外)を押した場合は捕捉も処理もせず、背後のリング(色相)へ通す。
		if (fractionX < 0.0 || fractionX > 1.0 || fractionY < 0.0 || fractionY > 1.0)
		{
			return;
		}

		_isDragging = CapturePointer(e.Pointer);
		Focus(FocusState.Pointer);
		XValue = fractionX;
		YValue = fractionY;
		ValuesChanged?.Invoke(this, EventArgs.Empty);

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

		if (TryMapToValues(e.GetCurrentPoint(this).Position, out double fractionX, out double fractionY))
		{
			XValue = Math.Clamp(fractionX, 0.0, 1.0);
			YValue = Math.Clamp(fractionY, 0.0, 1.0);
			ValuesChanged?.Invoke(this, EventArgs.Empty);
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




	/// <summary>
	/// ドラッグ開始時にレンズを出す。サンプラーが設定されていなければ何もしない。つまみは隠してレンズへ置き換える。
	/// </summary>
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




	/// <summary>
	/// レンズを現在のつまみ位置(未回転の局所座標)へ追従させ、その点まわりの色面を映し直す。レンズはパッドと同じ回転枠に入るため、ここでは回転を考えず局所座標で扱う。
	/// </summary>
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

		double centerX = XValue * ActualWidth;
		double centerY = (1.0 - YValue) * ActualHeight;
		_lens.Update(LensColorSampler, centerX, centerY);
	}




	/// <summary>
	/// ドラッグ終了時にレンズを退場させ、つまみを戻す。
	/// </summary>
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




	/// <summary>
	/// 色相環のレンズが内側のこのパッドを覗くとき、静止しているつまみ(中空の二重リング)をその場へ描き込むためのサンプラー。
	/// パッド局所座標 (x, y) が現在のつまみ(XValue・YValue の位置)の輪の上にあれば、その色を baseColor へ重ねて返す。
	/// 色面の外(baseColor が透明)や輪の外はそのまま返す。色相環のレンズはこれを色面の色の上から掛けるため、つまみも色面と一緒に拡大・屈折されて映る。
	/// パッドそのものをドラッグ中はつまみがレンズへ置き換わって消えるのが正しいため、本サンプラーは色相環側からの覗き見にだけ使う。
	/// </summary>
	public Color SampleThumbOverlay(Color baseColor, double x, double y)
	{
		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return baseColor;
		}

		double centerX = XValue * ActualWidth;
		double centerY = (1.0 - YValue) * ActualHeight;
		return ThumbGlyph.Overlay(baseColor, x, y, centerX, centerY, ThumbDiameter);
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




	/// <summary>
	/// 画面上の位置を XValue・YValue の比率へ写す。
	/// 中心を原点に取り、PadRotation の逆回転で未回転座標へ戻してから矩形内の比率を求めるため、回転していても見た目どおりの位置が値になる。
	/// 比率はクランプせずに返し、矩形 [0,1] の内外判定は呼び出し側に委ねる。寸法が無いときは false を返す。
	/// </summary>
	private bool TryMapToValues(Point position, out double fractionX, out double fractionY)
	{
		fractionX = 0.0;
		fractionY = 0.0;

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
		double localX = (vx * cos) + (vy * sin);
		double localY = (-vx * sin) + (vy * cos);

		fractionX = (localX + centerX) / ActualWidth;
		fractionY = 1.0 - ((localY + centerY) / ActualHeight);
		return true;
	}




	/// <summary>
	/// 矢印キーの方向を画面上の移動として扱い、現在のつまみの画面位置をその分ずらしてから値へ写し直す。
	/// 回転時もポインタ操作と移動方向が一致する。引数は幅・高さに対する比率で与える。
	/// </summary>
	private void NudgeByScreenDelta(double fractionX, double fractionY)
	{
		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		double centerX = ActualWidth / 2.0;
		double centerY = ActualHeight / 2.0;
		double offsetX = (XValue - 0.5) * ActualWidth;
		double offsetY = (0.5 - YValue) * ActualHeight;

		double radians = PadRotation * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		double screenX = (offsetX * cos) - (offsetY * sin) + centerX + (fractionX * ActualWidth);
		double screenY = (offsetX * sin) + (offsetY * cos) + centerY + (fractionY * ActualHeight);

		if (TryMapToValues(new Point(screenX, screenY), out double fx, out double fy))
		{
			XValue = Math.Clamp(fx, 0.0, 1.0);
			YValue = Math.Clamp(fy, 0.0, 1.0);
			ValuesChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
