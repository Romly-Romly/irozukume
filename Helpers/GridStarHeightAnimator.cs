// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Irozukume.Helpers;

// Grid の star 指定の行高をアニメーションで変える補助。GridLength は DoubleAnimation で直接動かせず、Storyboard はビジュアルツリーに属さない素の DependencyObject 上のプロパティを解決できないため、毎フレームの CompositionTarget.Rendering で経過時間から進捗を出し、各行の star 値を開始値から目標値へ補間して書き戻す。
internal sealed class GridStarHeightAnimator
{
	// 高さを動かす対象の Grid。各行(RowDefinition)の star 値を毎フレーム書き換える。
	private readonly Grid _grid;

	// 補間の開始値(現在の star 値)と目標値。進捗 0→1 に合わせてこの間を補間する。
	private double[] _from = Array.Empty<double>();
	private double[] _to = Array.Empty<double>();

	// アニメーションの長さと、最初のフレームで取る開始時刻(Rendering の単調増加時刻)。経過 ÷ 長さで進捗を出す。
	private TimeSpan _duration;
	private TimeSpan _startTime;
	private bool _hasStartTime;

	// Rendering を購読中か。購読はアニメーション中だけに限り、終わったら必ず解いて毎フレームの呼び出しを止める。
	private bool _running;

	// OS の「アニメーションを表示する」設定の参照元。オフのときは高さの変化を即座に確定し、利用者のアニメーション設定を尊重する。
	private static readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();



	public GridStarHeightAnimator(Grid grid)
	{
		_grid = grid;
	}




	// 現在の行高から目標の star 値へアニメーションで遷移する。行数が目標と食い違うときや OS のアニメーション設定がオフのときは、補間せず即座に目標へ確定する。
	public void AnimateTo(double[] targetStars, TimeSpan duration)
	{
		Stop();

		if (_grid.RowDefinitions.Count != targetStars.Length || !_uiSettings.AnimationsEnabled)
		{
			SetImmediately(targetStars);
			return;
		}

		// 開始値は現在の(遷移途中なら途中の)行高に取り、向きが変わっても滑らかにつなぐ。
		_from = new double[targetStars.Length];
		for (int i = 0; i < targetStars.Length; i++)
		{
			_from[i] = _grid.RowDefinitions[i].Height.Value;
		}

		_to = targetStars;
		_duration = duration;
		_hasStartTime = false;
		_running = true;
		CompositionTarget.Rendering += OnRendering;
	}




	// 毎フレームの更新。経過時間から進捗を出し、CubicEase の EaseOut を掛けて行高へ反映する。1に達したら目標で確定して購読を解く。
	private void OnRendering(object? sender, object e)
	{
		TimeSpan now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;

		if (!_hasStartTime)
		{
			_startTime = now;
			_hasStartTime = true;
		}

		double progress = _duration > TimeSpan.Zero ? (now - _startTime) / _duration : 1.0;

		if (progress >= 1.0)
		{
			Apply(1.0);
			Stop();
			return;
		}

		Apply(EaseOut(Math.Max(0.0, progress)));
	}




	// 3次の EaseOut。終わり際で緩やかに収束させ、組み込みの CubicEase(EaseOut)と同じ曲線にする。
	private static double EaseOut(double t)
	{
		double inverse = 1.0 - t;
		return 1.0 - (inverse * inverse * inverse);
	}




	// 各行の高さを、開始値と目標値の間を進捗 t で補間した star 値に揃える。
	private void Apply(double t)
	{
		int count = Math.Min(_grid.RowDefinitions.Count, Math.Min(_from.Length, _to.Length));

		for (int i = 0; i < count; i++)
		{
			double value = _from[i] + ((_to[i] - _from[i]) * t);
			_grid.RowDefinitions[i].Height = new GridLength(value, GridUnitType.Star);
		}
	}




	// アニメーションを止めて Rendering の購読を解く。新しい遷移を始める前と、進捗が1に達したときに呼ぶ。
	private void Stop()
	{
		if (_running)
		{
			CompositionTarget.Rendering -= OnRendering;
			_running = false;
		}
	}




	// アニメーションを介さず行高を即座に目標へ揃える。
	private void SetImmediately(double[] targetStars)
	{
		int count = Math.Min(_grid.RowDefinitions.Count, targetStars.Length);

		for (int i = 0; i < count; i++)
		{
			_grid.RowDefinitions[i].Height = new GridLength(targetStars[i], GridUnitType.Star);
		}
	}
}
