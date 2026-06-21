// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Numerics;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls;

// 透明度表現用の市松模様を、自身の大きさいっぱいに敷くパネル。明色の下地の上に暗色の升を敷き詰める。半透明の色をこのパネルの上に重ねると、透けた分だけ市松が見えて不透明度が伝わる。升の寸法・色は CheckerboardGeometry に集約した値を使い、GradientSlider の市松と揃える。
// LightFill・DarkFill を指定すると下地・升の塗りを既定の色から差し替えられる。色覚シミュレーションのセルは、市松との合成結果へフィルタを掛けた2色をこれで流し込み、不透明な市松として描く。PhaseX は市松の横の位相(升の並びの原点を左へずらす量)で、横に並ぶ別のパネルと升の並びを切れ目なく繋げるのに使う。
public sealed class CheckerboardPanel : Grid
{
	// 暗色の升をまとめて描く単一の図形。大きさ・位相が変わるたびに作り直す。
	private readonly Path _cells = new();

	// 升の図形を左へずらす変換。位相(PhaseX)を市松の横周期へ畳んだ量だけ動かす。
	private readonly TranslateTransform _cellsShift = new();

	// 升の図形を載せる土台。Grid は子をその割り当て幅(=このパネルの幅)へレイアウトで切り抜くため、位相のぶんパネルより横長に作った図形の右端が切り落とされ、左ずらしと相まって右端に欠けが出る。Canvas は子をレイアウトで切り抜かないので、横長の図形をそのまま右端まで描ける。最終的な範囲外の切り落としは、このパネルに掛けた合成クリップ(正しい寸法)だけが担う。
	private readonly Canvas _cellsHost = new();

	// 市松の横の位相。RebuildCells を経由して反映する。
	private double _phaseX;

	public static readonly DependencyProperty LightFillProperty = DependencyProperty.Register(
		nameof(LightFill), typeof(Brush), typeof(CheckerboardPanel), new PropertyMetadata(null, OnFillChanged));

	public static readonly DependencyProperty DarkFillProperty = DependencyProperty.Register(
		nameof(DarkFill), typeof(Brush), typeof(CheckerboardPanel), new PropertyMetadata(null, OnFillChanged));




	public CheckerboardPanel()
	{
		ApplyFills();

		_cells.RenderTransform = _cellsShift;
		_cellsHost.Children.Add(_cells);
		Children.Add(_cellsHost);
		SizeChanged += OnSizeChanged;
	}




	// 明色(下地)の塗り。未指定なら CheckerboardGeometry の既定色を使う。
	public Brush? LightFill
	{
		get => (Brush?)GetValue(LightFillProperty);
		set => SetValue(LightFillProperty, value);
	}




	// 暗色(升)の塗り。未指定なら CheckerboardGeometry の既定色を使う。
	public Brush? DarkFill
	{
		get => (Brush?)GetValue(DarkFillProperty);
		set => SetValue(DarkFillProperty, value);
	}




	// 市松の横の位相(DIP)。このパネルの左端が、繋げたい市松全体の原点からどれだけ右にあるかを与えると、升の並びがその位置から続いているように描かれる。
	public double PhaseX
	{
		get => _phaseX;
		set
		{
			if (_phaseX == value)
			{
				return;
			}

			_phaseX = value;
			RebuildCells();
		}
	}




	private static void OnFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((CheckerboardPanel)d).ApplyFills();
	}




	// 下地と升の塗りを反映する。未指定の側は既定の色で塗る。
	private void ApplyFills()
	{
		Background = LightFill ?? new SolidColorBrush(CheckerboardGeometry.LightColor);
		_cells.Fill = DarkFill ?? new SolidColorBrush(CheckerboardGeometry.DarkColor);
	}




	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		// 升は領域の端で最大1升ぶんはみ出し、位相ぶん左へもはみ出すため、自身の矩形でクリップして範囲外へあふれさせない。あわせて CornerRadius のぶん角を丸める。Border の CornerRadius は子要素の塗りを切り抜かないので、市松の塗りはここで丸めて外枠の角丸に揃える。CornerRadius 既定値(0)なら角丸なしの矩形クリップになる。
		Visual visual = ElementCompositionPreview.GetElementVisual(this);
		CompositionRoundedRectangleGeometry clipGeometry = visual.Compositor.CreateRoundedRectangleGeometry();
		clipGeometry.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
		float radius = (float)CornerRadius.TopLeft;
		clipGeometry.CornerRadius = new Vector2(radius, radius);
		visual.Clip = visual.Compositor.CreateGeometricClip(clipGeometry);

		RebuildCells();
	}




	// 升の図形を作り直す。位相を市松の横周期(升2つぶん)へ畳み、そのぶん横長に作った図形を左へずらして描くことで、左端の升が位相の途中から現れる。
	private void RebuildCells()
	{
		double shift = _phaseX % (CheckerboardGeometry.CellSize * 2.0);
		_cells.Data = CheckerboardGeometry.BuildDarkCells(ActualWidth + shift, ActualHeight, CheckerboardGeometry.CellSize);
		_cellsShift.X = -shift;
	}
}
