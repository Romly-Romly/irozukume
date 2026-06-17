// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Irozukume.Controls;

// フォーカス時(Compact のスピンボタンがポップアップする時)に幅を広げ、非フォーカス時は値だけを収める静止幅まで狭める NumberBox。スライダーと数値入力欄を並べた行で、入力欄を Auto 列に置くと、入力欄が縮んだぶんだけ隣の伸縮(*)列のスライダーが伸びる。これによりスピンボタンの逃げ場として値列を常に広く取らずに済ませ、普段はスライダーへ幅を回す。幅の変化はアニメーションで滑らかに見せる。
public sealed class ExpandingNumberBox : NumberBox
{
	// 走らせ中の幅アニメーション。新しい遷移を始める前に止め、レイアウトを巻き込む依存アニメーションが二重に走らないようにする。
	private Storyboard? _widthAnimation;

	// フォーカスがこの入力欄の中にある間は真。Compact のスピンボタンへ一瞬フォーカスが移っても畳まないよう、本当に外れた時だけ静止幅へ戻す判定に使う。
	private bool _hasFocus;

	// フォーカスが外れた際の畳みの予約。スピンボタンへの内部移動では直後の再取得で取り消し、本当に外へ出た時だけ静止幅へ戻す。
	private bool _collapsePending;

	// OS の「アニメーションを表示する」設定の参照元。オフのときは幅の変化を即座に確定し、利用者のアニメーション設定を尊重する。
	private static readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();



	public ExpandingNumberBox()
	{
		// 既定の最小幅があると静止幅まで縮みきらないため、下限を取り払う。
		MinWidth = 0;
	}




	// フォーカス時(スピンボタンのポップアップ時)に取る幅。ポップアップが値へ被らないよう広めにとる。
	public double FocusWidth
	{
		get => (double)GetValue(FocusWidthProperty);
		set => SetValue(FocusWidthProperty, value);
	}

	public static readonly DependencyProperty FocusWidthProperty =
		DependencyProperty.Register(nameof(FocusWidth), typeof(double), typeof(ExpandingNumberBox), new PropertyMetadata(double.NaN, OnWidthsChanged));




	// 非フォーカス時に取る静止幅。スピンボタンの逃げ場を見込まず、値だけを収める狭い幅にする。
	public double RestWidth
	{
		get => (double)GetValue(RestWidthProperty);
		set => SetValue(RestWidthProperty, value);
	}

	public static readonly DependencyProperty RestWidthProperty =
		DependencyProperty.Register(nameof(RestWidth), typeof(double), typeof(ExpandingNumberBox), new PropertyMetadata(double.NaN, OnWidthsChanged));




	// 静止幅と展開幅を切り替えるアニメーションの長さ。
	public TimeSpan AnimationDuration
	{
		get => (TimeSpan)GetValue(AnimationDurationProperty);
		set => SetValue(AnimationDurationProperty, value);
	}

	public static readonly DependencyProperty AnimationDurationProperty =
		DependencyProperty.Register(nameof(AnimationDuration), typeof(TimeSpan), typeof(ExpandingNumberBox), new PropertyMetadata(TimeSpan.FromMilliseconds(160)));




	// 幅の指定が変わったら、フォーカスしていない間は新しい静止幅へ即座に合わせる。フォーカス中は表示中の展開幅を保ち、次に外れた時の畳みで新しい静止幅を反映する。
	private static void OnWidthsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var box = (ExpandingNumberBox)d;

		if (!box._hasFocus)
		{
			box.SetWidthImmediately(box.RestWidth);
		}
	}




	// フォーカスを得たら展開幅へ広げる。直前に予約された畳みは取り消し、スピンボタンへの内部移動で畳まれないようにする。
	protected override void OnGotFocus(RoutedEventArgs e)
	{
		base.OnGotFocus(e);

		_collapsePending = false;
		_hasFocus = true;
		AnimateTo(FocusWidth);
	}




	// フォーカスが外れたら静止幅へ畳む。ただし Compact のスピンボタンへフォーカスが移っただけのときは直後に再取得されるため、畳みを1拍遅らせ、その間に再取得されたら取り消す。
	protected override void OnLostFocus(RoutedEventArgs e)
	{
		base.OnLostFocus(e);

		_collapsePending = true;

		DispatcherQueue?.TryEnqueue(() =>
		{
			if (!_collapsePending)
			{
				return;
			}

			_collapsePending = false;
			_hasFocus = false;
			AnimateTo(RestWidth);
		});
	}




	// アニメーションを介さず幅を即座に合わせる。初期化時と、フォーカス外で幅指定が変わったときに使う。指定が無い(NaN)ときは Auto のまま触らない。
	private void SetWidthImmediately(double width)
	{
		if (double.IsNaN(width))
		{
			return;
		}

		_widthAnimation?.Stop();
		_widthAnimation = null;
		Width = width;
	}




	// 現在の表示幅から指定幅へアニメーションで遷移する。Width はレイアウトに影響するため依存アニメーションとして走らせ、毎フレームの再レイアウトで隣のスライダー列も一緒に伸縮させる。開始値は現在の表示幅(ActualWidth)に取り、遷移の途中で向きが変わっても滑らかにつなぐ。指定が無い(NaN)ときは何もしない。
	private void AnimateTo(double target)
	{
		if (double.IsNaN(target))
		{
			return;
		}

		// OS のアニメーション設定がオフのときは、伸縮はそのままに動きだけを省いて即座に確定する。
		if (!_uiSettings.AnimationsEnabled)
		{
			SetWidthImmediately(target);
			return;
		}

		double from = ActualWidth > 0.0 ? ActualWidth : (double.IsNaN(Width) ? target : Width);

		_widthAnimation?.Stop();

		var animation = new DoubleAnimation
		{
			From = from,
			To = target,
			Duration = new Duration(AnimationDuration),
			EnableDependentAnimation = true,
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
		};

		Storyboard.SetTarget(animation, this);
		Storyboard.SetTargetProperty(animation, "Width");

		var storyboard = new Storyboard();
		storyboard.Children.Add(animation);
		storyboard.Begin();

		_widthAnimation = storyboard;
	}
}
