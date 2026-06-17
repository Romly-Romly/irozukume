// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Irozukume.Models;

namespace Irozukume.Helpers;

// YCbCr の色差平面(輝度固定)で sRGB 色域の内外判定と、色域外の点を色域内へ寄せる最近傍を担う。YCbCr→RGB の変換は ColorConversion に委ね、ここでは色域(各 RGB 成分が 0–255)の幾何だけを扱う。色域外の Cb・Cr は、輝度と無彩色 128 から見た方向(Cb:Cr の比)を保ったまま無彩色軸 (128,128) へ向けて縮め、色域境界へ収める。輝度固定の断面は無彩色を内部に含む凸多角形のため、無彩色からの半径方向は境界とちょうど一度だけ交わる。
public static class YuvColor
{
	// 数値誤差を見込んだ色域判定の許容幅。
	private const double Epsilon = 1e-6;

	// 指定形式・輝度で Cb・Cr が sRGB 色域に収まるか。YCbCr→RGB の各成分が 0–255 に収まるかで判定する。
	public static bool InGamut(YCbCrFormat format, double y, double cb, double cr)
	{
		(double r, double g, double b) = ColorConversion.YCbCrToRgbExact(y, cb, cr, format);
		return InRange(r) && InRange(g) && InRange(b);
	}




	// 輝度固定の Cb-Cr 平面で、色域外の (cb, cr) に最も近い色域内の点を返す。色域内ならそのまま返す。色域外なら、無彩色 128 から見た方向(Cb:Cr の比)を保ったまま、無彩色軸へ向けて半径方向で色域境界まで縮める。二次元パッドで色域外へ動かしたとき、つまみを色域の縁へ滑らかに沿わせるのに使う。
	public static (double Cb, double Cr) NearestInGamut(YCbCrFormat format, double y, double cb, double cr)
	{
		(double r, double g, double b) = ColorConversion.YCbCrToRgbExact(y, cb, cr, format);

		if (InRange(r) && InRange(g) && InRange(b))
		{
			return (cb, cr);
		}

		// 無彩色(cb=cr=128)での各成分。色差を 0 にした輝度のみの色で、無彩色軸は色域内にある。ここから (cb,cr) の指す色まで線形に結び、各成分が 0–255 をはみ出す手前の縮小率を求め、その最小を採る。
		(double nr, double ng, double nb) = ColorConversion.YCbCrToRgbExact(y, 128.0, 128.0, format);

		double t = 1.0;
		t = Math.Min(t, MaxScale(nr, r));
		t = Math.Min(t, MaxScale(ng, g));
		t = Math.Min(t, MaxScale(nb, b));

		if (t < 0.0)
		{
			t = 0.0;
		}

		double clampedCb = 128.0 + (t * (cb - 128.0));
		double clampedCr = 128.0 + (t * (cr - 128.0));
		return (clampedCb, clampedCr);
	}




	// 無彩色での成分値 n(色域内)から目標値 v へ線形に進むとき、0–255 に収まる最大の縮小率(0–1 を上限)。v が範囲内なら 1 を、範囲外ならはみ出す手前の比率を返す。
	private static double MaxScale(double n, double v)
	{
		double delta = v - n;

		if (delta > Epsilon)
		{
			return (255.0 - n) / delta;
		}

		if (delta < -Epsilon)
		{
			return -n / delta;
		}

		return 1.0;
	}




	// 数値誤差を見込んで 0–255 に収まるか。
	private static bool InRange(double value)
	{
		return value >= -Epsilon && value <= 255.0 + Epsilon;
	}
}
