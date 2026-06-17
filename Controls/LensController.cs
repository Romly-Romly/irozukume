// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Irozukume.Helpers;

namespace Irozukume.Controls;

// ドラッグ中のつまみをガラスレンズ(ルーペ)へ膨らませる処理を、2次元パッドと色相環で共有するための管理役。背後の色面を拡大・屈折・色収差して見せる GlassLens と縁のリングを組み、表示・追従・退場を面倒見る。色面の色は利用側が渡すサンプラー(コントロール局所座標→色)で与える。GradientSlider は1次元トラック専用の経路を自前で持つため、本管理役は2次元の色面側だけで使う。レンズはウィンドウ最前面の透明オーバーレイ(LensOverlayService)へ載せ、スクロール領域や色相環の控えに切り抜かれないようにする。
internal sealed class LensController
{
	// レンズの効き。コントロール(色相環・2次元パッド)ごとに別の値を持てるよう、利用側がコンストラクタで渡す。各項目の意味と単位は GlassLensParams を参照。
	private readonly GlassLensParams _params;
	private readonly FrameworkElement _owner;

	// レンズ置き場のテンプレート要素。レンズはここではなくオーバーレイへ載せるが、この要素の局所座標で利用側がつまみ中心を渡すため、オーバーレイへの座標変換の基準として使う(パッドでは回転枠の内側にあり、回転も織り込まれる)。オーバーレイが無い環境ではここをレンズの置き場へフォールバックする。
	private readonly Canvas _host;

	private Grid? _loupe;
	private ScaleTransform? _scale;
	private MatrixTransform? _placement;
	private GlassLens? _glass;

	// レンズを実際に載せている先。最前面オーバーレイ、無ければレンズ置き場。退場時にここから自分のルーペだけを取り除く。
	private Canvas? _target;
	private bool _active;




	public LensController(FrameworkElement owner, Canvas host, GlassLensParams parameters)
	{
		_owner = owner;
		_host = host;
		_params = parameters;
	}




	public bool IsActive => _active;




	// レンズを出し始める。ガラスと縁のリングを組んでオーバーレイへ載せ、つまみ大からレンズ大へ膨らませながら現す。ガラスが作れない環境では中身なしで縁のリングだけ出す。色面の中身と位置は以後 Update で渡す。
	public void Begin()
	{
		_active = true;
		RemoveLoupe();
		DisposeGlass();

		double d = _params.Diameter;
		UIElement? glassContent = null;

		try
		{
			_glass = new GlassLens();
			glassContent = _glass.BuildSampler(GlassLens.ApplyTuning(_params), ResolveCardColor());
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

		// 拡大(ポップ)はレンズ中心まわりで、配置(平行移動・回転・拡大)は置き場からオーバーレイへの変換で行う。両者を合成して RenderTransform へ与える。
		_scale = new ScaleTransform { CenterX = d / 2.0, CenterY = d / 2.0 };
		_placement = new MatrixTransform();
		_loupe = new Grid
		{
			Width = d,
			Height = d,
			RenderTransform = new TransformGroup { Children = { _scale, _placement } },
			IsHitTestVisible = false,
		};

		if (glassContent is not null)
		{
			_loupe.Children.Add(glassContent);
		}

		_loupe.Children.Add(darkRing);
		_loupe.Children.Add(whiteRing);

		_target = LensOverlayService.Get(_owner.XamlRoot) ?? _host;
		_target.Children.Add(_loupe);

		Animate(true);
	}




	// レンズの中心をつまみのコントロール局所座標へ合わせ、その点まわりの色面をサンプラーから映し直す。ドラッグ中の値変化ごとに呼ぶ。配置は置き場からオーバーレイへの変換で求め、スクロールや色相環の回転に追従させる。
	public void Update(Func<double, double, Color> sampler, double centerX, double centerY)
	{
		double highlightRot = 0.0;

		if (_loupe is not null && _placement is not null && _target is not null)
		{
			double half = _params.Diameter / 2.0;
			GeneralTransform toTarget = _host.TransformToVisual(_target);
			_placement.Matrix = LensOverlayService.ComputePlacement(toTarget, centerX - half, centerY - half);

			// レンズ中心を置き先(オーバーレイ)の座標へ写し、その位置から仮想光源への方位で鏡面ハイライトの回転を求める。
			Point centerInTarget = toTarget.TransformPoint(new Point(centerX, centerY));
			highlightRot = GlassLens.ComputeHighlightRotation(centerInTarget, _target.ActualWidth, _target.ActualHeight);
		}

		_glass?.UpdateField(sampler, centerX, centerY, highlightRot);
	}




	// レンズを退場させる。退場アニメーションの完了で片付ける。出していないときは何もしない。
	public void End()
	{
		if (!_active)
		{
			return;
		}

		_active = false;
		Animate(false);
	}




	private void Animate(bool show)
	{
		if (_loupe is null || _scale is null)
		{
			return;
		}

		double thumbScale = 28.0 / _params.Diameter;
		double from = show ? thumbScale : 1.0;
		double to = show ? 1.0 : thumbScale;
		var duration = new Duration(TimeSpan.FromMilliseconds(120));
		var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };

		var scaleX = new DoubleAnimation { From = from, To = to, Duration = duration, EasingFunction = ease };
		var scaleY = new DoubleAnimation { From = from, To = to, Duration = duration, EasingFunction = ease };
		var opacity = new DoubleAnimation { From = show ? 0.0 : 1.0, To = show ? 1.0 : 0.0, Duration = duration };

		Storyboard.SetTarget(scaleX, _scale);
		Storyboard.SetTargetProperty(scaleX, "ScaleX");
		Storyboard.SetTarget(scaleY, _scale);
		Storyboard.SetTargetProperty(scaleY, "ScaleY");
		Storyboard.SetTarget(opacity, _loupe);
		Storyboard.SetTargetProperty(opacity, "Opacity");

		var storyboard = new Storyboard();
		storyboard.Children.Add(scaleX);
		storyboard.Children.Add(scaleY);
		storyboard.Children.Add(opacity);

		if (!show)
		{
			storyboard.Completed += (_, _) => FinishHide();
		}

		storyboard.Begin();
	}




	// 退場アニメーションの完了後にレンズを片付ける。畳んでいる途中に再びドラッグが始まっていたら、新しいレンズを消さないよう何もしない。
	private void FinishHide()
	{
		if (_active)
		{
			return;
		}

		RemoveLoupe();
		DisposeGlass();
		_loupe = null;
		_scale = null;
		_placement = null;
		_target = null;
	}




	// 載せているルーペを置き先から取り除く。オーバーレイは複数のレンズ置き場で共有するため、全消去ではなく自分のルーペだけを外す。
	private void RemoveLoupe()
	{
		if (_loupe is not null)
		{
			_target?.Children.Remove(_loupe);
		}
	}




	// レンズの円内に敷く下地色。パッド・色相環の背後はテーマのカード色のため、実効テーマに合わせて近い不透明色を返す。GradientSlider のトラックレンズと同じ値にそろえる。
	private Color ResolveCardColor()
	{
		bool dark = _owner.ActualTheme == ElementTheme.Dark;
		return dark ? Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);
	}




	private void DisposeGlass()
	{
		_glass?.Dispose();
		_glass = null;
	}
}
