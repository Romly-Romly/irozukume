// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Automation;
using Windows.Foundation;
using Windows.UI;
using Irozukume.Helpers;

namespace Irozukume.Controls;

// 配色タブの2次元コントロール。下地に OKLCH の色相×彩度の盤面画像(中心が無彩色、中心からの角度が色相、距離が彩度)を円形に切り抜いて敷き、その上へ配色の各色を表す番号つきの丸いマーカーと、配色に組み込まれた色どうしを結ぶ線・中心からのスポークを重ねる。マーカーは盤面上をドラッグでき、その正規化位置から各色の色相・彩度が決まる。下地画像の生成・色域表示と、位置から色を作る処理・配色の角度拘束は利用側(タブ)が担い、ここは画像の受け取りとマーカー・線の配置・ドラッグ通知だけを扱う。
public sealed class HarmonyDisc : Grid
{
	// 1つのマーカーの見せ方。位置(正規化 0–1・左上原点)・色・表示番号・アクティブ(編集対象)か・仮(IsGhost)かを持つ。仮マーカーは、配色が要する数に対して色がまだ足りないぶんの予告で、配色の色を薄く見せるだけで掴めない。実在のマーカーと仮マーカーはまとめて線で結び、配色の形を示す。
	public readonly struct Marker
	{
		public Marker(double x, double y, Color color, int number, bool isActive, bool isGhost)
		{
			X = x;
			Y = y;
			Color = color;
			Number = number;
			IsActive = isActive;
			IsGhost = isGhost;
		}

		public double X { get; }
		public double Y { get; }
		public Color Color { get; }
		public int Number { get; }
		public bool IsActive { get; }
		public bool IsGhost { get; }
	}




	// マーカーの直径(DIP)。掴みやすさのため指先より少し小さめの実用サイズにする。
	private const double MarkerSize = 24.0;

	// 下地画像と、それを円形に切り抜く枠。枠の角丸を一辺の半分にして正円に切り、四隅(彩度が上限を超え多くが色域外になる領域)を落とす。
	private readonly Image _image = new() { Stretch = Stretch.Fill };
	private readonly Border _imageClip = new();

	// 配色の線(中心からのスポークと、組み込まれた色どうしの連結線)を描く層。マーカーの下に敷き、ポインタは素通りさせる。
	private readonly Canvas _lineLayer = new() { IsHitTestVisible = false };

	// マーカーを重ねる層。各マーカーは掴み判定の根となる Grid で、ドラッグはこの層の各要素が受ける。
	private readonly Canvas _markerLayer = new();

	// 配色の拘束を見せる案内層。トーンを揃える配色で、マーカーが乗る曲線(輪)と、トーナルで動けない範囲の薄い覆いを描く。マーカー・線の下、盤面の上に敷き、ポインタは素通りさせる。
	private readonly Canvas _guideLayer = new() { IsHitTestVisible = false };

	// 案内に使う正規化座標(0–1)の点列。_guideRing は共有トーンでマーカーが乗る曲線、_guideBandA・_guideBandB はトーナルの帯の縁。帯が無ければ null。サイズ変更で実画素へ引き直せるよう正規化のまま覚えておく。
	private IReadOnlyList<Point>? _guideRing;
	private IReadOnlyList<Point>? _guideBandA;
	private IReadOnlyList<Point>? _guideBandB;

	// 現在のマーカー群と、それぞれに対応する画面上の要素(掴み判定の根)。並びは番号順。
	private readonly List<Marker> _markers = new();
	private readonly List<Grid> _markerRoots = new();

	// 配色の線を多角形として閉じるか。利用側が配色の種類から決めて渡す。
	private bool _closed;

	// ドラッグ中のマーカーの番号(0 始まり)。掴んでいない間は -1。
	private int _dragIndex = -1;




	public HarmonyDisc()
	{
		_imageClip.Child = _image;
		Children.Add(_imageClip);
		Children.Add(_guideLayer);
		Children.Add(_lineLayer);
		Children.Add(_markerLayer);
		SizeChanged += OnSizeChanged;
	}




	// 下地の盤面画像。利用側が OKLCH の色相×彩度の盤面を作って差し込む。
	public ImageSource? FieldImage
	{
		get => _image.Source;
		set => _image.Source = value;
	}




	// マーカーどうしを番号順に結ぶ連結線を出すか。既定は出す。トーンを揃える配色のように、マーカーが輪の上を自由に動き番号順の結びが意味を持たない配色では、利用側が偽にして連結線を消す(中心からのスポークは残す)。SetMarkers / UpdateMarkers の前に設定する。
	public bool ConnectMarkers { get; set; } = true;




	// マーカーがドラッグで動いたときの通知。番号(0 始まり)・新しい正規化位置(0–1)・Shift を押しているかを渡す。利用側は位置から色相・彩度を読み、配色の拘束を当てて各色へ反映する。Shift のときは掴んだ点の中心からの距離(彩度)も保ち、色相だけ回す拘束に使う。
	public event Action<int, double, double, bool>? MarkerMoved;

	// マーカーのドラッグの開始・終了の通知。利用側は連続編集のまとまり(1段の元に戻す)の境界に使う。
	public event Action? DragStarted;
	public event Action? DragEnded;




	// マーカー群と線の閉じ方を差し替えて描き直す。並びは番号順で、各マーカーの色・アクティブ・配色への組み込みも反映する。件数が変わるドラッグ以外の更新で使う。
	public void SetMarkers(IReadOnlyList<Marker> markers, bool closed)
	{
		_closed = closed;
		_markers.Clear();
		_markers.AddRange(markers);
		RebuildMarkers();
	}




	// マーカーの位置・色だけをその場で更新する。掴み判定の根は作り直さないため、ドラッグ中にポインタの捕捉を失わずに全マーカーを動かせる。件数が合わないときは作り直しに委ねる。
	public void UpdateMarkers(IReadOnlyList<Marker> markers)
	{
		if (markers.Count != _markerRoots.Count)
		{
			_markers.Clear();
			_markers.AddRange(markers);
			RebuildMarkers();
			return;
		}

		_markers.Clear();
		_markers.AddRange(markers);

		for (int i = 0; i < _markerRoots.Count; i++)
		{
			Grid root = _markerRoots[i];
			root.Children.Clear();
			FillMarker(root, _markers[i]);
		}

		PositionMarkers();
		RebuildLines();
	}




	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		_imageClip.CornerRadius = new CornerRadius(Math.Min(ActualWidth, ActualHeight) / 2.0);
		PositionMarkers();
		RebuildLines();
		RebuildGuide();
	}




	// マーカーの要素を作り直す。ドラッグ判定の根となる Grid に、色の丸と上向きの番号文字を重ねる。
	private void RebuildMarkers()
	{
		_markerLayer.Children.Clear();
		_markerRoots.Clear();

		for (int i = 0; i < _markers.Count; i++)
		{
			var root = new Grid
			{
				Width = MarkerSize,
				Height = MarkerSize,
				Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
			};

			FillMarker(root, _markers[i]);
			AutomationProperties.SetName(root, Loc.Get("SwatchColorN", _markers[i].Number));

			int index = i;
			root.PointerPressed += (_, args) => OnMarkerPointerPressed(index, args);
			root.PointerMoved += (_, args) => OnMarkerPointerMoved(index, args);
			root.PointerReleased += (_, args) => OnMarkerPointerReleased(index, args);
			root.PointerCaptureLost += (_, _) => EndDrag();

			_markerLayer.Children.Add(root);
			_markerRoots.Add(root);
		}

		PositionMarkers();
		RebuildLines();
	}




	// マーカー1つの中身(色の丸と番号文字)を根へ載せる。アクティブなマーカーはアクセント色の太い縁、それ以外は背景に映える明暗の縁で囲む。仮マーカーは、まだ色になっていない予告と分かるよう全体を薄く描く。
	private void FillMarker(Grid root, Marker marker)
	{
		Color outline = marker.IsActive
			? (Color)Application.Current.Resources["SystemAccentColor"]
			: ContrastColor(marker.Color);

		var circle = new Border
		{
			Width = MarkerSize,
			Height = MarkerSize,
			CornerRadius = new CornerRadius(MarkerSize / 2.0),
			Background = new SolidColorBrush(marker.Color),
			BorderBrush = new SolidColorBrush(outline),
			BorderThickness = new Thickness(marker.IsActive ? 3.0 : 1.5),
		};

		var label = new TextBlock
		{
			Text = marker.Number.ToString(),
			FontSize = 11.0,
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ContrastColor(marker.Color)),
			IsHitTestVisible = false,
		};

		if (marker.IsGhost)
		{
			circle.Opacity = 0.4;
			label.Opacity = 0.4;
		}

		root.Children.Add(circle);
		root.Children.Add(label);
	}




	// 各マーカーを正規化位置から画面座標へ置く。マーカーの中心が位置に乗るよう直径の半分だけ戻す。
	private void PositionMarkers()
	{
		double width = ActualWidth;
		double height = ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		for (int i = 0; i < _markerRoots.Count && i < _markers.Count; i++)
		{
			Canvas.SetLeft(_markerRoots[i], (_markers[i].X * width) - (MarkerSize / 2.0));
			Canvas.SetTop(_markerRoots[i], (_markers[i].Y * height) - (MarkerSize / 2.0));
		}
	}




	// 配色の線を引き直す。中心から各マーカーへスポークを描いて角度(色相)と距離(彩度)を示し、マーカーどうしを番号順に連結線で結ぶ。仮マーカーも含めて結び、色がまだ足りなくても配色の形を見せる。閉じる配色では末尾から先頭へ戻して多角形にする。どの盤面色の上でも見えるよう、暗い下線の上に明るい線を重ねた二重線にする。
	private void RebuildLines()
	{
		_lineLayer.Children.Clear();

		double width = ActualWidth;
		double height = ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		double centerX = width / 2.0;
		double centerY = height / 2.0;

		var points = new List<Point>();

		for (int i = 0; i < _markers.Count; i++)
		{
			points.Add(new Point(_markers[i].X * width, _markers[i].Y * height));
		}

		// 中心から各マーカーへのスポーク。控えめな一本線で、角度と距離を読み取る手がかりにする。
		foreach (Point p in points)
		{
			_lineLayer.Children.Add(MakeLine(centerX, centerY, p.X, p.Y, Color.FromArgb(0x66, 0, 0, 0), 2.5));
			_lineLayer.Children.Add(MakeLine(centerX, centerY, p.X, p.Y, Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 1.0));
		}

		// マーカーどうしの連結線。番号順に結び、閉じる配色なら末尾から先頭へも結ぶ。トーンを揃える配色のように番号順の結びに意味がない配色では、利用側が ConnectMarkers を切って出さない。
		if (ConnectMarkers && points.Count >= 2)
		{
			var dark = MakePolyline(points, _closed, Color.FromArgb(0x66, 0, 0, 0), 3.5);
			var light = MakePolyline(points, _closed, Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 1.75);
			_lineLayer.Children.Add(dark);
			_lineLayer.Children.Add(light);
		}
	}




	// 中心と1点を結ぶ線分を作る。
	private static Line MakeLine(double x1, double y1, double x2, double y2, Color color, double thickness)
	{
		return new Line
		{
			X1 = x1,
			Y1 = y1,
			X2 = x2,
			Y2 = y2,
			Stroke = new SolidColorBrush(color),
			StrokeThickness = thickness,
			StrokeLineJoin = PenLineJoin.Round,
			IsHitTestVisible = false,
		};
	}




	// 点列を結ぶ折れ線を作る。閉じる指定なら先頭の点を末尾へ加えて多角形にする。
	private static Polyline MakePolyline(IReadOnlyList<Point> points, bool closed, Color color, double thickness)
	{
		var collection = new PointCollection();

		foreach (Point p in points)
		{
			collection.Add(p);
		}

		if (closed && points.Count >= 3)
		{
			collection.Add(points[0]);
		}

		return new Polyline
		{
			Points = collection,
			Stroke = new SolidColorBrush(color),
			StrokeThickness = thickness,
			StrokeLineJoin = PenLineJoin.Round,
			IsHitTestVisible = false,
		};
	}




	// 配色の拘束の案内を差し替える。ring はマーカーが乗る曲線(共有トーンの輪)、bandA・bandB はトーナルの帯の縁(動ける範囲)。帯を渡すと、その外側を薄く覆って動けない範囲を示す。点列はいずれも正規化座標(0–1)。
	public void SetToneGuide(IReadOnlyList<Point> ring, IReadOnlyList<Point>? bandA, IReadOnlyList<Point>? bandB)
	{
		_guideRing = ring;
		_guideBandA = bandA;
		_guideBandB = bandB;
		RebuildGuide();
	}




	// 配色の拘束の案内を消す。トーンを揃えない配色へ切り替えたときに呼ぶ。
	public void ClearToneGuide()
	{
		_guideRing = null;
		_guideBandA = null;
		_guideBandB = null;
		_guideLayer.Children.Clear();
	}




	// 案内(曲線と帯の覆い)を実画素で引き直す。トーナルの帯があれば、盤面の円から帯の内外2本の曲線を even-odd で抜いて、動ける帯だけを覆いから外す。曲線(輪)は、どの盤面色でも見えるよう暗い下線に明るい線を重ねた二重線にする。
	private void RebuildGuide()
	{
		_guideLayer.Children.Clear();

		double width = ActualWidth;
		double height = ActualHeight;

		if (width <= 0.0 || height <= 0.0 || _guideRing is null)
		{
			return;
		}

		if (_guideBandA is not null && _guideBandB is not null)
		{
			double radius = Math.Min(width, height) / 2.0;

			var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
			group.Children.Add(new EllipseGeometry { Center = new Point(width / 2.0, height / 2.0), RadiusX = radius, RadiusY = radius });
			group.Children.Add(BuildClosedGeometry(_guideBandA, width, height));
			group.Children.Add(BuildClosedGeometry(_guideBandB, width, height));

			_guideLayer.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
			{
				Data = group,
				Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
				IsHitTestVisible = false,
			});
		}

		_guideLayer.Children.Add(MakeGuidePolyline(_guideRing, width, height, Color.FromArgb(0x66, 0, 0, 0), 3.0));
		_guideLayer.Children.Add(MakeGuidePolyline(_guideRing, width, height, Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 1.25));
	}




	// 正規化座標の点列を、実画素の閉じた塗り図形へ変換する。帯の覆いを even-odd で抜くのに使う。
	private static PathGeometry BuildClosedGeometry(IReadOnlyList<Point> points, double width, double height)
	{
		var figure = new PathFigure
		{
			StartPoint = new Point(points[0].X * width, points[0].Y * height),
			IsClosed = true,
			IsFilled = true,
		};

		var segment = new PolyLineSegment();

		for (int i = 1; i < points.Count; i++)
		{
			segment.Points.Add(new Point(points[i].X * width, points[i].Y * height));
		}

		figure.Segments.Add(segment);

		var geometry = new PathGeometry();
		geometry.Figures.Add(figure);
		return geometry;
	}




	// 正規化座標の点列を、閉じた折れ線(輪)へ変換する。
	private static Polyline MakeGuidePolyline(IReadOnlyList<Point> points, double width, double height, Color color, double thickness)
	{
		var collection = new PointCollection();

		foreach (Point p in points)
		{
			collection.Add(new Point(p.X * width, p.Y * height));
		}

		if (points.Count > 0)
		{
			collection.Add(new Point(points[0].X * width, points[0].Y * height));
		}

		return new Polyline
		{
			Points = collection,
			Stroke = new SolidColorBrush(color),
			StrokeThickness = thickness,
			StrokeLineJoin = PenLineJoin.Round,
			IsHitTestVisible = false,
		};
	}




	private void OnMarkerPointerPressed(int index, PointerRoutedEventArgs e)
	{
		// 仮マーカーはまだ色になっていない予告のため掴めない。掴みと選択は実在のマーカーだけが受ける。
		if (_markers[index].IsGhost)
		{
			return;
		}

		_dragIndex = index;
		_markerRoots[index].CapturePointer(e.Pointer);
		DragStarted?.Invoke();
		e.Handled = true;
	}




	private void OnMarkerPointerMoved(int index, PointerRoutedEventArgs e)
	{
		if (_dragIndex != index || ActualWidth <= 0.0 || ActualHeight <= 0.0)
		{
			return;
		}

		Point p = e.GetCurrentPoint(_markerLayer).Position;
		double x = p.X / ActualWidth;
		double y = p.Y / ActualHeight;

		// 円盤(彩度が上限の縁)の外へは出さない。中心からの正規化距離が半径(0.5)を超えたら縁へ詰めて、彩度が上限を超えないようにする。
		double dx = x - 0.5;
		double dy = y - 0.5;
		double r = Math.Sqrt((dx * dx) + (dy * dy));

		if (r > 0.5)
		{
			double scale = 0.5 / r;
			x = 0.5 + (dx * scale);
			y = 0.5 + (dy * scale);
		}

		bool keepRadius = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);
		MarkerMoved?.Invoke(index, x, y, keepRadius);
		e.Handled = true;
	}




	private void OnMarkerPointerReleased(int index, PointerRoutedEventArgs e)
	{
		if (_dragIndex != index)
		{
			return;
		}

		_markerRoots[index].ReleasePointerCapture(e.Pointer);
		EndDrag();
		e.Handled = true;
	}




	// ドラッグの終了を一度だけ通知して掴みを解く。ポインタの解放と捕捉喪失の双方から呼ぶ。
	private void EndDrag()
	{
		if (_dragIndex < 0)
		{
			return;
		}

		_dragIndex = -1;
		DragEnded?.Invoke();
	}




	// 与えた色の上で読みやすい黒か白を返す。番号文字と非アクティブの縁取りに使う。
	private static Color ContrastColor(Color color)
	{
		double luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
		return luminance > 0.55 ? Color.FromArgb(0xFF, 0, 0, 0) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
	}
}
