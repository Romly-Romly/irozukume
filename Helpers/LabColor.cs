// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.UI;

namespace Irozukume.Helpers;

// Lab の a×b 平面の表示枠(スケール)の決め方。None は ±AbMax の固定枠で、原点中心・縦横等倍・明度に依らず不変。Isotropic・Anisotropic は明度ごとに sRGB 色域に収まる a・b の広がりへ枠を寄せて有効領域を広げる(枠は明度で縮尺が変わる)。Isotropic は原点中心・縦横等倍で色域が最も張り出す向きに合わせ、Anisotropic は各軸を独立に色域のバウンディングボックスへ合わせて平面いっぱいに広げる。
public enum AbFitMode
{
	None,
	Isotropic,
	Anisotropic,
}




// Lab の2次元パッドの表示枠。横軸 X(a×b では a、a×L では a、b×L では b)・縦軸 Y(a×b では b、a×L・b×L では明度 L)それぞれの下限・上限を素の尺度で持つ。横は左端 XMin→右端 XMax、縦は下端 YMin→上端 YMax。None の固定枠では各軸が名目全域、フィット時は色域の広がりに合わせて寄せた枠になる。下地の描画・パッドのつまみ位置・パッド操作の値はすべてこの枠を介して座標と値を読み替える。
public readonly struct PlaneExtent
{
	public PlaneExtent(double xMin, double xMax, double yMin, double yMax)
	{
		XMin = xMin;
		XMax = xMax;
		YMin = yMin;
		YMax = yMax;
	}

	public double XMin { get; }
	public double XMax { get; }
	public double YMin { get; }
	public double YMax { get; }
	public double XWidth => XMax - XMin;
	public double YHeight => YMax - YMin;
}




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




	// 表示枠の共通の余白・下限・最小倍率。フィット時に色域境界が平面の縁へ張り付かないよう半幅へ掛ける余白、色域が一点へ縮む両端でも枠が潰れないよう半幅へ設ける下限(名目全域に対する割合)。
	private const double FitMarginScale = 1.06;
	private const double FitMinHalfFraction = 0.04;




	// 指定した明度における a×b 平面の表示枠を、フィットの仕方に応じて返す。横軸 X=a・縦軸 Y=b。None は ±AbMax の固定枠。Isotropic・Anisotropic は色相を一周しての最大彩度から、その明度で色域に収まる a・b のバウンディングボックスを取り、それへ枠を寄せる。両軸とも彩度で単位が同じ平面のため、Isotropic は無彩点(原点)を中心に保ったまま色域が最も張り出す向きへ正方の枠を合わせ(角度=色相のコンパスを崩さない)、Anisotropic は各軸を独立にバウンディングボックスへ合わせて平面いっぱいに広げる。
	public static PlaneExtent AbExtentFor(LchSpace space, double l, AbFitMode fit)
	{
		double abMax = AbMax(space);

		if (fit == AbFitMode.None)
		{
			return new PlaneExtent(-abMax, abMax, -abMax, abMax);
		}

		// 色相を一周し、各色相での最大彩度から色域境界の点 (a, b) を求めてバウンディングボックスを取る。原点(無彩色)は常に含める。
		double aLo = 0.0;
		double aHi = 0.0;
		double bLo = 0.0;
		double bHi = 0.0;
		const int steps = 360;

		for (int i = 0; i < steps; i++)
		{
			double hueDeg = (double)i / steps * 360.0;
			double maxC = LchColor.MaxChroma(space, l, hueDeg);
			double radians = hueDeg * Math.PI / 180.0;
			double a = maxC * Math.Cos(radians);
			double b = maxC * Math.Sin(radians);
			aLo = Math.Min(aLo, a);
			aHi = Math.Max(aHi, a);
			bLo = Math.Min(bLo, b);
			bHi = Math.Max(bHi, b);
		}

		double minHalf = abMax * FitMinHalfFraction;

		if (fit == AbFitMode.Isotropic)
		{
			// 原点中心・縦横等倍。色域が最も張り出す向きに合わせて正方の枠を取る。
			double half = Math.Max(Math.Max(-aLo, aHi), Math.Max(-bLo, bHi));
			half = Math.Max(half, minHalf) * FitMarginScale;
			return new PlaneExtent(-half, half, -half, half);
		}

		// 縦横独立。各軸のバウンディングボックスへ別倍率で合わせ、平面いっぱいに広げる。
		double aCenter = (aLo + aHi) / 2.0;
		double bCenter = (bLo + bHi) / 2.0;
		double aHalf = Math.Max((aHi - aLo) / 2.0, minHalf) * FitMarginScale;
		double bHalf = Math.Max((bHi - bLo) / 2.0, minHalf) * FitMarginScale;
		return new PlaneExtent(aCenter - aHalf, aCenter + aHalf, bCenter - bHalf, bCenter + bHalf);
	}




	// 指定した固定成分における a×L・b×L 平面の表示枠を、フィットの仕方に応じて返す。横軸 X は変わる色軸(a×L では a、b×L では b)、縦軸 Y は明度 L。horizontalIsA が真なら横が a で固定成分は b(a×L)、偽なら横が b で固定成分は a(b×L)。None は横が ±AbMax・縦が 0–LMax の固定枠。フィットは、その固定成分で色域に収まる (色軸, L) の二次元バウンディングボックスを求め、それへ枠を寄せる。固定成分が極端だと色域に入る明度が一部の帯しか無く、横も縦も痩せるため、両軸を詰める。横軸(彩度)と縦軸(明度)は単位が異なるが、表示枠は画素寸法が決まっているため、Isotropic は名目全域に対する拡大率を両軸で等しく取り(塊の形を歪めず、先に縁へ届く軸で倍率が決まる)、Anisotropic は各軸を独立に縁いっぱいへ伸ばす。いずれもバウンディングボックスの中心へ置く(明度に対称な原点が無いため)。縦は明度の有効域を外れないよう 0–LMax へ収める。
	public static PlaneExtent CartExtentFor(LchSpace space, double fixedValue, bool horizontalIsA, AbFitMode fit)
	{
		double abMax = AbMax(space);
		double lMax = LchColor.LMax(space);

		if (fit == AbFitMode.None)
		{
			return new PlaneExtent(-abMax, abMax, 0.0, lMax);
		}

		// 明度を細かく刻み、各明度で横軸(色軸)を走査して色域に収まる区間を拾う。区間がある明度だけを縦の帯として集め、横は全明度を通じた最小・最大を取って二次元バウンディングボックスを組む。色域内かは固定成分と合わせた (a, b) で判定する。色域が非凸でも区間の最小・最大を取るだけのため走査で取りこぼさない。
		double xLo = double.PositiveInfinity;
		double xHi = double.NegativeInfinity;
		double lLo = double.PositiveInfinity;
		double lHi = double.NegativeInfinity;
		const int lSteps = 64;
		const int xSteps = 96;

		for (int li = 0; li <= lSteps; li++)
		{
			double l = (double)li / lSteps * lMax;
			bool anyHere = false;

			for (int xi = 0; xi <= xSteps; xi++)
			{
				double x = (-1.0 + (2.0 * xi / xSteps)) * abMax;
				double a = horizontalIsA ? x : fixedValue;
				double b = horizontalIsA ? fixedValue : x;

				if (InGamut(space, l, a, b))
				{
					xLo = Math.Min(xLo, x);
					xHi = Math.Max(xHi, x);
					anyHere = true;
				}
			}

			if (anyHere)
			{
				lLo = Math.Min(lLo, l);
				lHi = Math.Max(lHi, l);
			}
		}

		// この固定成分では一切色域に入らない(無彩でない固定成分が全明度で過大)とき、フィットのしようが無いため固定枠へ退避する。
		if (double.IsInfinity(xLo) || double.IsInfinity(lLo))
		{
			return new PlaneExtent(-abMax, abMax, 0.0, lMax);
		}

		double xNominalHalf = abMax;
		double yNominalHalf = lMax / 2.0;
		double xCenter = (xLo + xHi) / 2.0;
		double yCenter = (lLo + lHi) / 2.0;
		double xHalf = Math.Max((xHi - xLo) / 2.0, abMax * FitMinHalfFraction) * FitMarginScale;
		double yHalf = Math.Max((lHi - lLo) / 2.0, lMax * FitMinHalfFraction) * FitMarginScale;

		if (fit == AbFitMode.Isotropic)
		{
			// 名目全域に対する拡大率を両軸で等しく取る。各軸の拡大率は (名目半幅 / 表示半幅)。先に縁へ届く(拡大率が小さい)軸で倍率が決まり、もう一方は同じ倍率で余白が残る。単位に依らず塊の形を歪めない。
			double magX = xNominalHalf / xHalf;
			double magY = yNominalHalf / yHalf;
			double mag = Math.Min(magX, magY);
			xHalf = xNominalHalf / mag;
			yHalf = yNominalHalf / mag;
		}

		// 縦(明度)は有効域 0–LMax を外れないよう収める。横(彩度)は名目を僅かに超えても無害なため詰めない。
		double yMin = Math.Max(0.0, yCenter - yHalf);
		double yMax = Math.Min(lMax, yCenter + yHalf);
		return new PlaneExtent(xCenter - xHalf, xCenter + xHalf, yMin, yMax);
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
