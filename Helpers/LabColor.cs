// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.UI;

namespace Irozukume.Helpers;

// Lab(OKLab・CIELAB)の直交軸(明度 L・a 軸・b 軸)と sRGB の相互変換を、表色系の種別で切り替えて一手に扱う。表色系の種別・変換・色域判定は同じ Lab 平面を極座標で扱う LchColor に委ね、ここでは直交座標(a・b)と極座標(彩度・色相)の読み替えだけを担う。色域外の組み合わせは、明度と色相(a:b の比)を保ったまま彩度を二分法で詰めて sRGB 内へ収める。
public static class LabColor
{
	// 各表色系の明度の上限。OKLab は 0–1、CIELAB は 0–100。
	public static double LMax(LchSpace space)
	{
		return LchColor.LMax(space);
	}




	// 各表色系の a・b 軸の表示上限(±この値)。スライダー・パッドの範囲と、コピー・貼り付けでパーセント表記を素の値へ換算する基準に使う。CSS の lab()/oklab() が 100% に対応させる値(OKLab は 0.4、CIELAB は 125)に合わせる。
	public static double AbMax(LchSpace space)
	{
		return space == LchSpace.Oklch ? 0.4 : 125.0;
	}




	// sRGB(各バイト)を Lab(明度 L・a 軸・b 軸)へ変換する。
	public static (double L, double A, double B) FromRgb(LchSpace space, byte r, byte g, byte b)
	{
		(double l, double c, double h) = LchColor.FromRgb(space, r, g, b);
		double radians = h * Math.PI / 180.0;
		return (l, c * Math.Cos(radians), c * Math.Sin(radians));
	}




	// Lab が sRGB 色域に収まるか。スライダー・パッドのガモット可視化で各位置が色域内かを調べるのに使う。
	public static bool InGamut(LchSpace space, double l, double a, double b)
	{
		(double c, double h) = ToPolar(a, b);
		return LchColor.InGamut(space, l, c, h);
	}




	// Lab を sRGB の不透明色へ変換する。色域内ならそのまま、色域外なら明度と色相(a:b の比)を保ったまま彩度だけを詰めて色域内へ収める。
	public static Color ToRgb(LchSpace space, double l, double a, double b)
	{
		(double c, double h) = ToPolar(a, b);
		return LchColor.ToRgb(space, l, c, h);
	}




	// a・b 平面(明度固定)で、色域外の点 (a, b) に最も近い色域内の点を返す。色域内ならそのまま返す。色域外なら、色相(a:b の比)を保ったまま彩度を色域境界まで縮めて、原点へ向かう半径方向で縁へ寄せる。二次元パッドで色域外へ動かしたとき、つまみを色域の縁へ滑らかに沿わせるのに使う。
	public static (double A, double B) NearestInGamut(LchSpace space, double l, double a, double b)
	{
		(double c, double h) = ToPolar(a, b);

		if (c <= 0.0 || LchColor.InGamut(space, l, c, h))
		{
			return (a, b);
		}

		double maxChroma = LchColor.MaxChroma(space, l, h);
		double ratio = maxChroma / c;
		return (a * ratio, b * ratio);
	}




	// 直交座標(a・b)を極座標(彩度・色相 0–360 度)へ読み替える。
	private static (double C, double H) ToPolar(double a, double b)
	{
		double c = Math.Sqrt((a * a) + (b * b));
		double h = Math.Atan2(b, a) * 180.0 / Math.PI;

		if (h < 0.0)
		{
			h += 360.0;
		}

		return (c, h);
	}
}
