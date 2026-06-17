// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Controls;

// スライダーのトラック上で sRGB 色域を外れる区間を、トラック左端 0・右端 1 の割合で表す。GradientSlider がこの区間に斜線ハッチを重ねて、つまみを動かせる範囲のうち実際の色を表示できない部分を示す。
public readonly struct GamutSegment
{
	public GamutSegment(double start, double end)
	{
		Start = start;
		End = end;
	}




	// 区間の始点(トラック左端 0・右端 1 の割合)。
	public double Start { get; }




	// 区間の終点(トラック左端 0・右端 1 の割合)。
	public double End { get; }
}
