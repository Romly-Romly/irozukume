// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation;
using Windows.Foundation;
using Windows.UI;
using Irozukume.Helpers;

namespace Irozukume.Controls;

// Mix タブの2次元コントロール。下地に多点グラデーション画像を敷き、その上へ色数ぶんの番号付きひし形ポッチと、編集色のサンプル位置を示す丸いつまみを重ねる。ポッチは任意の位置へドラッグでき、各色をそのポッチに置いたグラデーションの配置を決める。つまみは平面上の任意の点をドラッグでき、その点の混色を編集中の色へ反映するための位置を表す。色面の生成・丸めと、つまみ位置から編集色を作る処理は利用側(タブ)が担い、ここは画像の受け取りとポッチ・つまみの配置・ドラッグだけを扱う。
public sealed class MixPad : Grid
{
	// 1つのポッチの見せ方。位置(正規化 0–1・左上原点)・色・表示番号・アクティブかを持つ。
	public readonly struct Pip
	{
		public Pip(double x, double y, Color color, int number, bool isActive)
		{
			X = x;
			Y = y;
			Color = color;
			Number = number;
			IsActive = isActive;
		}

		public double X { get; }
		public double Y { get; }
		public Color Color { get; }
		public int Number { get; }
		public bool IsActive { get; }
	}




	// ポッチの一辺(DIP)。掴みやすさのため指先より少し小さめの実用サイズにする。
	private const double PipSize = 26.0;

	// レンズへ描き込むポッチのひし形の、中心から頂点までの市松距離(マンハッタン距離)。ひし形は一辺 PipSize*0.7 の正方形を45度回したもので、その頂点は中心から半対角線(辺長×√2÷2)の距離にある。市松距離ではこの半対角線がそのまま閾値になる。
	private const double DiamondReach = PipSize * 0.7 * 0.70710678;

	// 編集色のサンプル位置を示すつまみの直径(DIP)。
	private const double ThumbSize = 24.0;

	// 下地画像。角丸クリップを掛けた枠の中に置く。
	private readonly Image _image = new() { Stretch = Stretch.Fill };
	private readonly Border _imageClip = new();

	// つまみを置き、平面の地(ポッチ以外)へのポインタ操作を受ける層。下地画像とポッチ層の間に挟み、全面を透明な当たり判定にする。ポッチは上の層にあるため掴みが優先され、地のドラッグはこの層が受ける。
	private readonly Canvas _thumbLayer = new() { Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
	private FrameworkElement? _thumb;
	private double _thumbX = 0.5;
	private double _thumbY = 0.5;
	private bool _thumbDragging;

	// ポッチを重ねる層。下地画像と同じ寸法に広げる。地(ポッチ以外)は透けさせ、下のつまみ層へポインタを通す。
	private readonly Canvas _pipLayer = new();

	// つまみドラッグ中のガラスレンズ(ルーペ)の置き場(座標基準)と管理役、そして下地画像を読み出すサンプラー。レンズは最前面オーバーレイへ載るため、この層は座標の基準だけに使う。サンプラーは下地画像が差し替わるたびに作り直す。
	private readonly Canvas _lensHost = new() { IsHitTestVisible = false };
	private readonly LensController _lens;
	private BitmapFieldSampler? _sampler;

	// つまみのガラスレンズの効き。2次元パッドのつまみと同じ値にそろえる。各項目の意味と単位は GlassLensParams を参照。
	private static readonly GlassLensParams LensParams = new()
	{
		Diameter = 50.0,
		Magnify = 1.4,
		EdgeAmount = -24.0,
		Chroma = true,
		ChromaSpread = 0.4,
		BevelFraction = 0.4,
	};

	// 現在のポッチ群と、それぞれに対応する画面上の要素(掴み判定の根)。並びは番号順。
	private readonly List<Pip> _pips = new();
	private readonly List<Grid> _pipRoots = new();

	// ドラッグ中のポッチの番号(0 始まり)。掴んでいない間は -1。
	private int _dragIndex = -1;




	public MixPad()
	{
		_imageClip.CornerRadius = new CornerRadius(FieldCornerRadius);
		_imageClip.Child = _image;
		Children.Add(_imageClip);

		_thumb = BuildThumb();
		_thumbLayer.Children.Add(_thumb);
		_thumbLayer.PointerPressed += OnThumbSurfacePressed;
		_thumbLayer.PointerMoved += OnThumbSurfaceMoved;
		_thumbLayer.PointerReleased += OnThumbSurfaceReleased;
		_thumbLayer.PointerCaptureLost += OnThumbSurfaceCaptureLost;
		Children.Add(_thumbLayer);

		Children.Add(_pipLayer);

		// レンズの座標基準となる層を最前面に重ねる。当たり判定は持たせず、レンズ本体は最前面オーバーレイへ載せる。
		Children.Add(_lensHost);
		_lens = new LensController(this, _lensHost, LensParams);

		SizeChanged += OnSizeChanged;
	}




	// 下地のグラデーション画像。利用側が色面を作って差し込む。差し替えのたびに、ドラッグ中のレンズが読み出すサンプラーも作り直して表示と一致させる。
	public ImageSource? FieldImage
	{
		get => _image.Source;
		set
		{
			_image.Source = value;
			_sampler = value is WriteableBitmap bitmap ? new BitmapFieldSampler(bitmap, this) : null;
		}
	}




	// 色面の角丸量(画素)。下地画像を包む枠の角丸でまとめて切り抜く。
	public double FieldCornerRadius
	{
		get => (double)GetValue(FieldCornerRadiusProperty);
		set => SetValue(FieldCornerRadiusProperty, value);
	}

	public static readonly DependencyProperty FieldCornerRadiusProperty =
		DependencyProperty.Register(nameof(FieldCornerRadius), typeof(double), typeof(MixPad), new PropertyMetadata(6.0, OnFieldCornerRadiusChanged));




	// ポッチがドラッグで動いたときの通知。番号(0 始まり)と新しい正規化位置(0–1)を渡す。
	public event Action<int, double, double>? PipMoved;

	// つまみがドラッグ(または地のクリック)で動いたときの通知。新しい正規化位置(0–1)を渡す。利用側はその点の混色を編集中の色へ反映する。
	public event Action<double, double>? ThumbMoved;

	// つまみのドラッグの開始・終了の通知。利用側は開始でグラデーションをスナップショットして固定し(編集色の反映が平面へ戻って色がドリフトする自己参照を防ぐ)、終了で最新の色へ塗り直す。
	public event Action? ThumbDragStarted;
	public event Action? ThumbDragEnded;




	private static void OnFieldCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var self = (MixPad)d;
		self._imageClip.CornerRadius = new CornerRadius((double)e.NewValue);
	}




	// ポッチ群を差し替えて描き直す。並びは番号順で、各ポッチの色・アクティブ状態も反映する。位置は SizeChanged と合わせて画面座標へ写す。
	public void SetPips(IReadOnlyList<Pip> pips)
	{
		_pips.Clear();
		_pips.AddRange(pips);
		RebuildPips();
	}




	// つまみの位置を覚えて画面へ置く。色の反映は伴わず、位置の表示だけを更新する。復元時や外部からの位置設定に使う。
	public void SetThumb(double x, double y)
	{
		_thumbX = Math.Clamp(x, 0.0, 1.0);
		_thumbY = Math.Clamp(y, 0.0, 1.0);
		PositionThumb();
	}




	// ポッチの要素を作り直す。ドラッグ判定の根となる Grid に、回転したひし形の縁取りと、上向きの番号文字を重ねる。
	private void RebuildPips()
	{
		_pipLayer.Children.Clear();
		_pipRoots.Clear();

		for (int i = 0; i < _pips.Count; i++)
		{
			Pip pip = _pips[i];

			var root = new Grid
			{
				Width = PipSize,
				Height = PipSize,
				Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
			};

			// ひし形本体。正方形を45度回し、対角線がポッチの一辺に収まるよう辺を縮める。アクティブなポッチはアクセント色の太枠、それ以外は背景に映える明暗の縁で囲む。
			Color outline = pip.IsActive
				? (Color)Application.Current.Resources["SystemAccentColor"]
				: ContrastColor(pip.Color);

			var diamond = new Border
			{
				Width = PipSize * 0.7,
				Height = PipSize * 0.7,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				CornerRadius = new CornerRadius(2),
				Background = new SolidColorBrush(pip.Color),
				BorderBrush = new SolidColorBrush(outline),
				BorderThickness = new Thickness(pip.IsActive ? 2.5 : 1.5),
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = new RotateTransform { Angle = 45.0 },
			};

			var label = new TextBlock
			{
				Text = pip.Number.ToString(),
				FontSize = 11.0,
				FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = new SolidColorBrush(ContrastColor(pip.Color)),
				IsHitTestVisible = false,
			};

			root.Children.Add(diamond);
			root.Children.Add(label);

			AutomationProperties.SetName(root, Loc.Get("SwatchColorN", pip.Number));

			int index = i;
			root.PointerPressed += (_, args) => OnPipPointerPressed(index, args);
			root.PointerMoved += (_, args) => OnPipPointerMoved(index, args);
			root.PointerReleased += (_, args) => OnPipPointerReleased(index, args);
			root.PointerCaptureLost += (_, _) => _dragIndex = -1;

			_pipLayer.Children.Add(root);
			_pipRoots.Add(root);
		}

		PositionPips();
	}




	// 編集色のサンプル位置を示す丸いつまみを作る。どの背景の上でも見えるよう、太い暗縁の上に細い明縁を重ねた二重リングにする。当たり判定はつまみ層が担うため、この要素自体は素通りさせる。
	private FrameworkElement BuildThumb()
	{
		var root = new Grid
		{
			Width = ThumbSize,
			Height = ThumbSize,
			IsHitTestVisible = false,
		};

		var dark = new Border
		{
			CornerRadius = new CornerRadius(ThumbSize / 2.0),
			BorderThickness = new Thickness(4.0),
			BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
		};

		var light = new Border
		{
			CornerRadius = new CornerRadius(ThumbSize / 2.0),
			BorderThickness = new Thickness(2.0),
			BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
		};

		root.Children.Add(dark);
		root.Children.Add(light);
		return root;
	}




	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		PositionPips();
		PositionThumb();
	}




	// 各ポッチを正規化位置から画面座標へ置く。ポッチの中心が位置に乗るよう一辺の半分だけ戻す。
	private void PositionPips()
	{
		double width = ActualWidth;
		double height = ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		for (int i = 0; i < _pipRoots.Count && i < _pips.Count; i++)
		{
			Canvas.SetLeft(_pipRoots[i], (_pips[i].X * width) - (PipSize / 2.0));
			Canvas.SetTop(_pipRoots[i], (_pips[i].Y * height) - (PipSize / 2.0));
		}
	}




	// つまみを正規化位置から画面座標へ置く。中心が位置に乗るよう直径の半分だけ戻す。
	private void PositionThumb()
	{
		if (_thumb is null || ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		Canvas.SetLeft(_thumb, (_thumbX * ActualWidth) - (ThumbSize / 2.0));
		Canvas.SetTop(_thumb, (_thumbY * ActualHeight) - (ThumbSize / 2.0));
	}




	private void OnPipPointerPressed(int index, PointerRoutedEventArgs e)
	{
		_dragIndex = index;
		_pipRoots[index].CapturePointer(e.Pointer);
		e.Handled = true;
	}




	private void OnPipPointerMoved(int index, PointerRoutedEventArgs e)
	{
		if (_dragIndex != index)
		{
			return;
		}

		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		Point p = e.GetCurrentPoint(_pipLayer).Position;
		double x = Math.Clamp(p.X / ActualWidth, 0.0, 1.0);
		double y = Math.Clamp(p.Y / ActualHeight, 0.0, 1.0);

		// 控えのポッチ位置も更新して、寸法変化での置き直しに追従させる。色面の塗り直しは利用側が PipMoved を受けて行う。
		_pips[index] = new Pip(x, y, _pips[index].Color, _pips[index].Number, _pips[index].IsActive);
		Canvas.SetLeft(_pipRoots[index], (x * ActualWidth) - (PipSize / 2.0));
		Canvas.SetTop(_pipRoots[index], (y * ActualHeight) - (PipSize / 2.0));

		PipMoved?.Invoke(index, x, y);
		e.Handled = true;
	}




	private void OnPipPointerReleased(int index, PointerRoutedEventArgs e)
	{
		if (_dragIndex != index)
		{
			return;
		}

		_dragIndex = -1;
		_pipRoots[index].ReleasePointerCapture(e.Pointer);
		e.Handled = true;
	}




	// 平面の地を押したら、その点へつまみを移してドラッグを始める。ポッチは上の層で掴みが優先されるため、ここへ来るのはポッチ以外の点だけ。掴めたらレンズ(ルーペ)を出す。
	private void OnThumbSurfacePressed(object sender, PointerRoutedEventArgs e)
	{
		_thumbDragging = _thumbLayer.CapturePointer(e.Pointer);

		if (_thumbDragging)
		{
			// グラデーションを固定してから最初のサンプルを採る。固定より先にサンプルすると自己参照が始まる。
			ThumbDragStarted?.Invoke();
			BeginLens();
		}

		UpdateThumbFromPointer(e);
		e.Handled = true;
	}




	private void OnThumbSurfaceMoved(object sender, PointerRoutedEventArgs e)
	{
		if (!_thumbDragging)
		{
			return;
		}

		UpdateThumbFromPointer(e);
		e.Handled = true;
	}




	private void OnThumbSurfaceReleased(object sender, PointerRoutedEventArgs e)
	{
		if (!_thumbDragging)
		{
			return;
		}

		_thumbDragging = false;
		_thumbLayer.ReleasePointerCapture(e.Pointer);
		EndLens();
		ThumbDragEnded?.Invoke();
		e.Handled = true;
	}




	private void OnThumbSurfaceCaptureLost(object sender, PointerRoutedEventArgs e)
	{
		_thumbDragging = false;
		EndLens();
		ThumbDragEnded?.Invoke();
	}




	// ポインタ位置をつまみの正規化位置へ写し、つまみを動かして利用側へ知らせる。利用側が編集色を更新して下地画像を差し替えた後にレンズを追従させ、レンズが最新の色面を映すようにする。
	private void UpdateThumbFromPointer(PointerRoutedEventArgs e)
	{
		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		Point p = e.GetCurrentPoint(_thumbLayer).Position;
		_thumbX = Math.Clamp(p.X / ActualWidth, 0.0, 1.0);
		_thumbY = Math.Clamp(p.Y / ActualHeight, 0.0, 1.0);
		PositionThumb();
		ThumbMoved?.Invoke(_thumbX, _thumbY);
		UpdateLens();
	}




	// つまみドラッグの開始でレンズを出す。下地のサンプラーが無ければ出さない。つまみのリングはレンズへ置き換えるため隠す。
	private void BeginLens()
	{
		if (_sampler is null)
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




	// レンズをつまみ位置へ追従させ、その点まわりの色面(下地画像にポッチを重ねたもの)を映し直す。
	private void UpdateLens()
	{
		if (!_lens.IsActive || _sampler is null || ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		_lens.Update(SampleField, _thumbX * ActualWidth, _thumbY * ActualHeight);
	}




	// レンズ用の色面サンプラー。下地画像の色に、その点に重なるポッチのひし形(縁取り＋塗り)を合成して返す。ポッチは別レイヤーの XAML 要素で下地画像には焼かれていないため、レンズに映すにはここで描き込む。番号文字は画素単位では描けないため省く。
	private Color SampleField(double x, double y)
	{
		Color result = _sampler is null ? Color.FromArgb(0, 0, 0, 0) : _sampler.Sample(x, y);

		if (ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return result;
		}

		for (int i = 0; i < _pips.Count; i++)
		{
			Pip pip = _pips[i];
			double cx = pip.X * ActualWidth;
			double cy = pip.Y * ActualHeight;
			double manhattan = Math.Abs(x - cx) + Math.Abs(y - cy);

			if (manhattan > DiamondReach)
			{
				continue;
			}

			Color outline = pip.IsActive
				? (Color)Application.Current.Resources["SystemAccentColor"]
				: ContrastColor(pip.Color);
			double thickness = pip.IsActive ? 2.5 : 1.5;

			// 縁に近い帯は縁取り色、内側は塗り色。縁の太さは45度の辺に直交する向きで効くため、市松距離(マンハッタン)では √2 倍に伸ばして帯幅へ写す。
			result = manhattan >= DiamondReach - (thickness * 1.41421356)
				? outline
				: pip.Color;
		}

		return result;
	}




	// つまみドラッグの終了でレンズを退場させ、つまみのリングを戻す。
	private void EndLens()
	{
		_lens.End();

		if (_thumb is not null)
		{
			_thumb.Opacity = 1.0;
		}
	}




	// 与えた色の上で読みやすい黒か白を返す。番号文字と非アクティブの縁取りに使う。
	private static Color ContrastColor(Color color)
	{
		double luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
		return luminance > 0.55 ? Color.FromArgb(0xFF, 0, 0, 0) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
	}
}
