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

	// 表示枠の共通の余白・下限。フィット時に色域境界が平面の縁へ張り付かないよう半幅へ掛ける余白、色域が一点へ縮む端でも枠が潰れないよう半幅へ設ける下限(コード尺度 0–255 の名目半幅 128 に対する割合)。Lab の表示枠と同じ値にそろえる。
	private const double FitMarginScale = 1.06;
	private const double FitMinHalfFraction = 0.04;

	// Cb・Cr・Y のコード尺度の全域(0–255)と名目半幅。固定枠と、フィットの名目に対する拡大率の基準に使う。
	private const double CodeMax = 255.0;
	private const double CodeNominalHalf = 127.5;

	// 色域の外接枠を格子走査で求めるときの刻み数。固定成分のドラッグ中も毎フレーム呼ぶため、Lab の境界探索と同等の手数に収める。
	private const int ScanSteps = 96;

	// 指定形式・輝度で Cb・Cr が sRGB 色域に収まるか。YCbCr→RGB の各成分が 0–255 に収まるかで判定する。
	public static bool InGamut(YCbCrFormat format, double y, double cb, double cr)
	{
		(double r, double g, double b) = ColorConversion.YCbCrToRgbExact(y, cb, cr, format);
		return InRange(r) && InRange(g) && InRange(b);
	}




	// 指定した輝度における Cb×Cr 平面の表示枠を、フィットの仕方に応じて返す。横軸 X=Cb(0–255)・縦軸 Y=Cr(0–255)。None は全域(0–255)の固定枠。Isotropic・Anisotropic は、その輝度で色域に収まる Cb・Cr の外接枠を格子走査で求め、それへ枠を寄せる。両軸とも同じ色差のコード尺度のため、Isotropic は無彩点(128,128)を中心に保ったまま色域が最も張り出す向きへ正方の枠を合わせ、Anisotropic は各軸を独立に外接枠へ合わせて平面いっぱいに広げる。Lab の AbExtentFor と同じ意味でそろえる。
	public static PlaneExtent CbCrExtentFor(YCbCrFormat format, double luma, AbFitMode fit)
	{
		if (fit == AbFitMode.None)
		{
			return new PlaneExtent(0.0, CodeMax, 0.0, CodeMax);
		}

		// Cb・Cr を細かく刻み、色域に収まる点の外接枠を取る。無彩点(128,128)は色域内のため、走査で必ず含まれる。
		double cbLo = double.PositiveInfinity;
		double cbHi = double.NegativeInfinity;
		double crLo = double.PositiveInfinity;
		double crHi = double.NegativeInfinity;

		for (int yi = 0; yi <= ScanSteps; yi++)
		{
			double cr = (double)yi / ScanSteps * CodeMax;

			for (int xi = 0; xi <= ScanSteps; xi++)
			{
				double cb = (double)xi / ScanSteps * CodeMax;

				if (InGamut(format, luma, cb, cr))
				{
					cbLo = Math.Min(cbLo, cb);
					cbHi = Math.Max(cbHi, cb);
					crLo = Math.Min(crLo, cr);
					crHi = Math.Max(crHi, cr);
				}
			}
		}

		// この輝度では一切色域に入らない(無彩点が過大・過小)とき、フィットのしようが無いため固定枠へ退避する。
		if (double.IsInfinity(cbLo) || double.IsInfinity(crLo))
		{
			return new PlaneExtent(0.0, CodeMax, 0.0, CodeMax);
		}

		double minHalf = CodeNominalHalf * FitMinHalfFraction;

		if (fit == AbFitMode.Isotropic)
		{
			// 無彩点 128 中心・縦横等倍。色域が最も張り出す向きに合わせて正方の枠を取る。
			double half = Math.Max(Math.Max(128.0 - cbLo, cbHi - 128.0), Math.Max(128.0 - crLo, crHi - 128.0));
			half = Math.Max(half, minHalf) * FitMarginScale;
			return new PlaneExtent(128.0 - half, 128.0 + half, 128.0 - half, 128.0 + half);
		}

		// 縦横独立。各軸の外接枠へ別倍率で合わせ、平面いっぱいに広げる。
		double cbCenter = (cbLo + cbHi) / 2.0;
		double crCenter = (crLo + crHi) / 2.0;
		double cbHalf = Math.Max((cbHi - cbLo) / 2.0, minHalf) * FitMarginScale;
		double crHalf = Math.Max((crHi - crLo) / 2.0, minHalf) * FitMarginScale;
		return new PlaneExtent(cbCenter - cbHalf, cbCenter + cbHalf, crCenter - crHalf, crCenter + crHalf);
	}




	// 指定した固定色差における Cb×Y 平面の表示枠を、フィットの仕方に応じて返す。横軸 X=Cb(0–255)・縦軸 Y=輝度 Y(0–255)で、Cr を fixedCr に固定する。None は全域(横 0–255・縦 0–255)の固定枠。フィットは、その固定 Cr で色域に収まる (Cb, Y) の外接枠を格子走査で求めて寄せる。CartExtentFor と同じ意味でそろえる。
	public static PlaneExtent CbLumaExtentFor(YCbCrFormat format, double fixedCr, AbFitMode fit)
	{
		return LumaExtentFor(format, fixedCr, true, fit);
	}




	// 指定した固定色差における Cr×Y 平面の表示枠を、フィットの仕方に応じて返す。横軸 X=Cr(0–255)・縦軸 Y=輝度 Y(0–255)で、Cb を fixedCb に固定する。None は全域(横 0–255・縦 0–255)の固定枠。フィットは、その固定 Cb で色域に収まる (Cr, Y) の外接枠を格子走査で求めて寄せる。CartExtentFor と同じ意味でそろえる。
	public static PlaneExtent CrLumaExtentFor(YCbCrFormat format, double fixedCb, AbFitMode fit)
	{
		return LumaExtentFor(format, fixedCb, false, fit);
	}




	// 片方の色差×輝度の平面の表示枠を返す。horizontalIsCb が真なら横軸が Cb で固定成分が Cr(Cb×Y)、偽なら横軸が Cr で固定成分が Cb(Cr×Y)。輝度を細かく刻み、各輝度で横軸(色差)を走査して色域に収まる区間を拾い、横は全輝度を通じた最小・最大、縦は区間のある輝度の最小・最大を取って二次元の外接枠を組む。横軸(色差)と縦軸(輝度)はともにコード尺度 0–255 のため、Isotropic は名目全域(±128)に対する拡大率を両軸で等しく取り(先に縁へ届く軸で倍率が決まる)、Anisotropic は各軸を独立に縁いっぱいへ伸ばす。いずれも外接枠の中心へ置く。縦は輝度の有効域 0–255 を外れないよう収める。CartExtentFor と同じ意味でそろえる。
	private static PlaneExtent LumaExtentFor(YCbCrFormat format, double fixedChroma, bool horizontalIsCb, AbFitMode fit)
	{
		if (fit == AbFitMode.None)
		{
			return new PlaneExtent(0.0, CodeMax, 0.0, CodeMax);
		}

		double xLo = double.PositiveInfinity;
		double xHi = double.NegativeInfinity;
		double yLo = double.PositiveInfinity;
		double yHi = double.NegativeInfinity;

		for (int yi = 0; yi <= ScanSteps; yi++)
		{
			double luma = (double)yi / ScanSteps * CodeMax;
			bool anyHere = false;

			for (int xi = 0; xi <= ScanSteps; xi++)
			{
				double axis = (double)xi / ScanSteps * CodeMax;
				double cb = horizontalIsCb ? axis : fixedChroma;
				double cr = horizontalIsCb ? fixedChroma : axis;

				if (InGamut(format, luma, cb, cr))
				{
					xLo = Math.Min(xLo, axis);
					xHi = Math.Max(xHi, axis);
					anyHere = true;
				}
			}

			if (anyHere)
			{
				yLo = Math.Min(yLo, luma);
				yHi = Math.Max(yHi, luma);
			}
		}

		// この固定色差では一切色域に入らないとき、フィットのしようが無いため固定枠へ退避する。
		if (double.IsInfinity(xLo) || double.IsInfinity(yLo))
		{
			return new PlaneExtent(0.0, CodeMax, 0.0, CodeMax);
		}

		double xCenter = (xLo + xHi) / 2.0;
		double yCenter = (yLo + yHi) / 2.0;
		double minHalf = CodeNominalHalf * FitMinHalfFraction;
		double xHalf = Math.Max((xHi - xLo) / 2.0, minHalf) * FitMarginScale;
		double yHalf = Math.Max((yHi - yLo) / 2.0, minHalf) * FitMarginScale;

		if (fit == AbFitMode.Isotropic)
		{
			// 名目全域(±128)に対する拡大率を両軸で等しく取る。各軸の拡大率は (名目半幅 / 表示半幅)。先に縁へ届く(拡大率が小さい)軸で倍率が決まり、もう一方は同じ倍率で余白が残る。
			double magX = CodeNominalHalf / xHalf;
			double magY = CodeNominalHalf / yHalf;
			double mag = Math.Min(magX, magY);
			xHalf = CodeNominalHalf / mag;
			yHalf = CodeNominalHalf / mag;
		}

		// 縦(輝度)は有効域 0–255 を外れないよう収める。横(色差)は名目を僅かに超えても無害なため詰めない。
		double yMin = Math.Max(0.0, yCenter - yHalf);
		double yMax = Math.Min(CodeMax, yCenter + yHalf);
		return new PlaneExtent(xCenter - xHalf, xCenter + xHalf, yMin, yMax);
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
