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
using Irozukume.Models;

namespace Irozukume.Controls;

// 配色タブのモノトーン用コントロール。下地に白(上)→黒(下)の縦グラデーションを敷き、その上へ各色を表す番号つきの丸いマーカーを横一列に並べる。横位置は色の並び順で固定し、マーカーは縦にだけドラッグできて、その縦位置が明度になる(上ほど明るい)。マーカーどうしを番号順に結ぶ折れ線がトーンカーブになる。明度から縦位置への対応・色作り・カーブのプリセットは利用側(タブ)が担い、ここはマーカーの配置・結線・縦ドラッグの通知だけを扱う。
public sealed class MonotoneRamp : Grid
{
	// 1つのマーカーの見せ方。横位置(正規化 0–1・スロット)・縦位置(正規化 0–1・上が明・左上原点)・色・表示番号を持つ。
	public readonly struct Marker
	{
		public Marker(double x, double y, Color color, int number)
		{
			X = x;
			Y = y;
			Color = color;
			Number = number;
		}

		public double X { get; }
		public double Y { get; }
		public Color Color { get; }
		public int Number { get; }
	}




	// マーカーの直径(DIP)。HarmonyDisc と揃え、掴みやすい実用サイズにする。
	private const double MarkerSize = 24.0;

	// 下地の白→黒グラデーション。縦に明度を示し、サイズに合わせて伸縮する。
	private readonly Border _background = new();

	// トーンカーブ(マーカーを番号順に結ぶ折れ線)を描く層。マーカーの下に敷き、ポインタは素通りさせる。
	private readonly Canvas _lineLayer = new() { IsHitTestVisible = false };

	// マーカーを重ねる層。各マーカーは掴み判定の根となる Grid で、縦ドラッグはこの層の各要素が受ける。
	private readonly Canvas _markerLayer = new();

	// 現在のマーカー群と、それぞれに対応する画面上の要素(掴み判定の根)。並びは番号順。
	private readonly List<Marker> _markers = new();
	private readonly List<Grid> _markerRoots = new();

	// ドラッグ中のマーカーの番号(0 始まり)。掴んでいない間は -1。
	private int _dragIndex = -1;

	// 下地のグレー階調に掛ける色制限(表示レンズ)。既定は丸めなし。利用側が SetSnap で設定する。
	private SnapSettings _snap;




	public MonotoneRamp()
	{
		_background.CornerRadius = new CornerRadius(4.0);
		_background.Background = MakeRampBrush();
		Children.Add(_background);
		Children.Add(_lineLayer);
		Children.Add(_markerLayer);
		SizeChanged += OnSizeChanged;
	}




	// マーカーが縦ドラッグで動いたときの通知。番号(0 始まり)と新しい縦の正規化位置(0–1・上が明)を渡す。利用側は位置から明度を読み(明度 = 1 − y)、その色へ反映する。
	public event Action<int, double>? MarkerMoved;

	// マーカーのドラッグの開始・終了の通知。利用側は連続編集のまとまり(1段の元に戻す)の境界に使う。
	public event Action? DragStarted;
	public event Action? DragEnded;




	// マーカー群を差し替えて描き直す。並びは番号順で、各マーカーの色も反映する。件数が変わる更新で使う。
	public void SetMarkers(IReadOnlyList<Marker> markers)
	{
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
		PositionMarkers();
		RebuildLines();
	}




	// 白(上)→黒(下)の縦グラデーションブラシを、色制限(表示レンズ)の丸めを通して作る。丸めなしのときは滑らかな白→黒。丸めありのときは、各縦位置の素のグレー(上=255〜下=0)を Snap で丸め、丸め後の色が変わる境に硬い継ぎ目を入れて、表示できるグレー階調を段(バンド)として見せる。横軸の各列は同じ明度のため、縦1方向のグラデーションで足りる。
	private LinearGradientBrush MakeRampBrush()
	{
		var brush = new LinearGradientBrush
		{
			StartPoint = new Point(0.5, 0.0),
			EndPoint = new Point(0.5, 1.0),
		};

		if (_snap.Mode == ColorLimitMode.None)
		{
			brush.GradientStops.Add(new GradientStop { Offset = 0.0, Color = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) });
			brush.GradientStops.Add(new GradientStop { Offset = 1.0, Color = Color.FromArgb(0xFF, 0, 0, 0) });
			return brush;
		}

		const int steps = 256;
		Color previous = Color.FromArgb(0, 0, 0, 0);
		bool started = false;

		for (int i = 0; i <= steps; i++)
		{
			double t = (double)i / steps;
			byte gray = (byte)Math.Round(255.0 * (1.0 - t));
			(byte r, byte g, byte b) = ColorConversion.Snap(_snap, gray, gray, gray);
			Color color = Color.FromArgb(0xFF, r, g, b);

			if (!started)
			{
				brush.GradientStops.Add(new GradientStop { Offset = 0.0, Color = color });
				previous = color;
				started = true;
				continue;
			}

			// 丸め後の色が前の段と変わったら、その境で同じ位置に2つの停止点を置き、間を補間させずに段差として描く。
			if (color.R != previous.R || color.G != previous.G || color.B != previous.B)
			{
				brush.GradientStops.Add(new GradientStop { Offset = t, Color = previous });
				brush.GradientStops.Add(new GradientStop { Offset = t, Color = color });
				previous = color;
			}
		}

		brush.GradientStops.Add(new GradientStop { Offset = 1.0, Color = previous });
		return brush;
	}




	// 下地のグレー階調に掛ける色制限(表示レンズ)を設定し、グラデーションを作り直す。同じ設定なら何もしない。
	public void SetSnap(SnapSettings snap)
	{
		if (snap.Equals(_snap))
		{
			return;
		}

		_snap = snap;
		_background.Background = MakeRampBrush();
	}




	// マーカーの要素を作り直す。ドラッグ判定の根となる Grid に、色の丸と番号文字を重ねる。
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




	// マーカー1つの中身(色の丸と番号文字)を根へ載せる。下地のどの明度の上でも読めるよう、色に映える明暗の縁と文字色で囲む。
	private void FillMarker(Grid root, Marker marker)
	{
		Color outline = ContrastColor(marker.Color);

		var circle = new Border
		{
			Width = MarkerSize,
			Height = MarkerSize,
			CornerRadius = new CornerRadius(MarkerSize / 2.0),
			Background = new SolidColorBrush(marker.Color),
			BorderBrush = new SolidColorBrush(outline),
			BorderThickness = new Thickness(1.5),
		};

		var label = new TextBlock
		{
			Text = marker.Number.ToString(),
			FontSize = 11.0,
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(outline),
			IsHitTestVisible = false,
		};

		root.Children.Add(circle);
		root.Children.Add(label);
	}




	// 各マーカーを正規化位置から画面座標へ置く。横位置は領域の幅をそのまま使う。利用側が縁へ張り付かない等間隔の位置を渡す前提のため、横は内寄せしない(これで両端の余白もマーカー間隔と揃う)。縦位置は端(純白・純黒)まで使うため、見切れないよう置ける範囲を直径の半分だけ内側へ詰める。中心がその位置に乗るよう直径の半分だけ戻す。
	private void PositionMarkers()
	{
		double width = ActualWidth;
		double height = ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		double inset = MarkerSize / 2.0;
		double spanY = Math.Max(0.0, height - MarkerSize);

		for (int i = 0; i < _markerRoots.Count && i < _markers.Count; i++)
		{
			double cx = _markers[i].X * width;
			double cy = inset + (_markers[i].Y * spanY);
			Canvas.SetLeft(_markerRoots[i], cx - (MarkerSize / 2.0));
			Canvas.SetTop(_markerRoots[i], cy - (MarkerSize / 2.0));
		}
	}




	// 案内の線を引き直す。まず各マーカーの縦の軌道線(動ける範囲)、その上にトーンカーブ(マーカーを番号順に結ぶ折れ線)を重ねる。どの下地明度の上でも見えるよう、いずれも暗い下線の上に明るい線を重ねた二重線にする。マーカーの配置と同じく、横は領域の幅をそのまま、縦は端を内側へ詰めた範囲で点を取る。
	private void RebuildLines()
	{
		_lineLayer.Children.Clear();

		double width = ActualWidth;
		double height = ActualHeight;

		if (width <= 0.0 || height <= 0.0 || _markers.Count == 0)
		{
			return;
		}

		double inset = MarkerSize / 2.0;
		double spanY = Math.Max(0.0, height - MarkerSize);
		double top = inset;
		double bottom = inset + spanY;

		// 各マーカーが縦にだけ動けることを示す軌道線。横へは動かない手がかりにし、自由に動かせるという誤解を防ぐ。控えめな細い二重線にする。
		for (int i = 0; i < _markers.Count; i++)
		{
			double cx = _markers[i].X * width;
			_lineLayer.Children.Add(MakeLine(cx, top, cx, bottom, Color.FromArgb(0x40, 0, 0, 0), 2.0));
			_lineLayer.Children.Add(MakeLine(cx, top, cx, bottom, Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), 1.0));
		}

		if (_markers.Count < 2)
		{
			return;
		}

		var points = new List<Point>(_markers.Count);

		for (int i = 0; i < _markers.Count; i++)
		{
			double cx = _markers[i].X * width;
			double cy = inset + (_markers[i].Y * spanY);
			points.Add(new Point(cx, cy));
		}

		_lineLayer.Children.Add(MakePolyline(points, Color.FromArgb(0x66, 0, 0, 0), 3.5));
		_lineLayer.Children.Add(MakePolyline(points, Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 1.75));
	}




	// 点列を結ぶ折れ線を作る。
	private static Polyline MakePolyline(IReadOnlyList<Point> points, Color color, double thickness)
	{
		var collection = new PointCollection();

		foreach (Point p in points)
		{
			collection.Add(p);
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




	// 2点を結ぶ線分を作る。マーカーの縦の軌道線に使う。
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
			IsHitTestVisible = false,
		};
	}




	private void OnMarkerPointerPressed(int index, PointerRoutedEventArgs e)
	{
		_dragIndex = index;
		_markerRoots[index].CapturePointer(e.Pointer);
		DragStarted?.Invoke();
		e.Handled = true;
	}




	// 縦ドラッグだけを受ける。ポインタの縦位置を正規化位置(0–1)へ写し、横位置(スロット)は動かさない。端は範囲内へ詰める。
	private void OnMarkerPointerMoved(int index, PointerRoutedEventArgs e)
	{
		if (_dragIndex != index || ActualHeight <= 0.0)
		{
			return;
		}

		double inset = MarkerSize / 2.0;
		double spanY = Math.Max(1e-6, ActualHeight - MarkerSize);
		double cy = e.GetCurrentPoint(_markerLayer).Position.Y;
		double y = Math.Clamp((cy - inset) / spanY, 0.0, 1.0);

		MarkerMoved?.Invoke(index, y);
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




	// 与えた色の上で読みやすい黒か白を返す。番号文字と縁取りに使う。
	private static Color ContrastColor(Color color)
	{
		double luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
		return luminance > 0.55 ? Color.FromArgb(0xFF, 0, 0, 0) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
	}
}
