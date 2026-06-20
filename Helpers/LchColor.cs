// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.UI;

namespace Irozukume.Helpers;

// LCH の表色系の種類。OKLCH は OKLab を極座標で、CIE LCH は CIELAB(D65)を極座標で扱う。明度・彩度の数値尺度が異なるため、変換と範囲はこの種別で切り替える。
public enum LchSpace
{
	Oklch,
	CieLch,
}




// LCH(OKLCH・CIE LCH)と sRGB の相互変換を、表色系の種別で切り替えて一手に扱う。明度 L・彩度 C・色相 H(度)を、それぞれの表色系の素の尺度で受け渡す。色域外の組み合わせは、明度と色相を保ったまま彩度を二分法で詰めて sRGB 内へ収める。色域の判定・最大彩度・各成分の上限も提供し、スライダーの範囲とガモット可視化に使う。
public static class LchColor
{
	// 各表色系の明度の上限。OKLCH は 0–1、CIE LCH は 0–100。
	public static double LMax(LchSpace space)
	{
		return space == LchSpace.Oklch ? 1.0 : 100.0;
	}




	// 各表色系の彩度の表示上限。スライダーの最大値と、コピー・貼り付けでパーセント表記を素の値へ換算する基準に使う。OKLCH は 0.4、CIE LCH は 150。
	public static double CMax(LchSpace space)
	{
		return space == LchSpace.Oklch ? 0.4 : 150.0;
	}




	// sRGB(各バイト)を LCH(明度 L・彩度 C・色相 H=度 0–360)へ変換する。色相と彩度の基準を取り出すのに使う。
	public static (double L, double C, double H) FromRgb(LchSpace space, byte r, byte g, byte b)
	{
		double rl = SrgbToLinear(r / 255.0);
		double gl = SrgbToLinear(g / 255.0);
		double bl = SrgbToLinear(b / 255.0);

		(double lightness, double aAxis, double bAxis) = space == LchSpace.Oklch
			? LinearToOklab(rl, gl, bl)
			: LinearToCieLab(rl, gl, bl);

		double c = Math.Sqrt((aAxis * aAxis) + (bAxis * bAxis));
		double h = Math.Atan2(bAxis, aAxis) * 180.0 / Math.PI;

		if (h < 0.0)
		{
			h += 360.0;
		}

		return (lightness, c, h);
	}




	// LCH が sRGB 色域に収まるか。線形光の各チャンネルが 0–1 にあるかで判定し、丸め誤差を許す微小な余裕を見る。スライダーのガモット可視化で各位置が色域内かを調べるのに使う。
	public static bool InGamut(LchSpace space, double l, double c, double hDegrees)
	{
		ToLinear(space, l, c, hDegrees, out double r, out double g, out double b);

		const double eps = 0.0001;
		return r >= -eps && r <= 1.0 + eps
			&& g >= -eps && g <= 1.0 + eps
			&& b >= -eps && b <= 1.0 + eps;
	}




	// LCH を sRGB の不透明色へ変換する。色域内ならそのまま、色域外なら明度と色相を保ったまま彩度だけ二分法で詰めて色域内へ収める。
	public static Color ToRgb(LchSpace space, double l, double c, double hDegrees)
	{
		if (c <= 0.0 || InGamut(space, l, c, hDegrees))
		{
			return ToSrgb(space, l, c, hDegrees);
		}

		double lo = 0.0;
		double hi = c;

		for (int i = 0; i < 24; i++)
		{
			double mid = (lo + hi) / 2.0;

			if (InGamut(space, l, mid, hDegrees))
			{
				lo = mid;
			}
			else
			{
				hi = mid;
			}
		}

		return ToSrgb(space, l, lo, hDegrees);
	}




	// 指定の明度・色相で sRGB 色域に収まる最大の彩度を二分法で求める。色域の境界を表示上限(CMax)までの範囲で探す。スライダーのガモット可視化と、色域制限オン時の彩度のクランプに使う。
	public static double MaxChroma(LchSpace space, double l, double hDegrees)
	{
		double hi = CMax(space);

		if (InGamut(space, l, hi, hDegrees))
		{
			return hi;
		}

		double lo = 0.0;

		for (int i = 0; i < 24; i++)
		{
			double mid = (lo + hi) / 2.0;

			if (InGamut(space, l, mid, hDegrees))
			{
				lo = mid;
			}
			else
			{
				hi = mid;
			}
		}

		return lo;
	}




	// 指定の明度で、全色相が sRGB 色域に収まる最大の彩度を求める。色相を一周細かくサンプルし、各色相の最大色域内彩度(MaxChroma)の最小値を返す。色相スライダーの基準背景で、色相環の全域を色域内に収めつつ最も鮮やかにするのに使う。
	public static double MaxChromaForAllHues(LchSpace space, double l)
	{
		const int samples = 90;
		double min = CMax(space);

		for (int i = 0; i < samples; i++)
		{
			double h = (double)i / samples * 360.0;
			double c = MaxChroma(space, l, h);

			if (c < min)
			{
				min = c;
			}
		}

		return min;
	}




	// 指定した色相で sRGB 色域内の彩度が最大になる明度(cusp の明度)を返す。まず明度を粗くサンプルして各明度での最大色域内彩度(MaxChroma)が最も大きくなる山を見つけ、その前後の区間を三分探索で詰めて連続値へ精密化する。粗いサンプルのまま量子化した値を返すと、明度ディスクの「cusp を縁に置く」型で色相間に段差(色相方向のバンド)が出るため、連続にして滑らかにする。半径と明度の対応の基準に使う。
	public static double CuspLightness(LchSpace space, double hDegrees)
	{
		double lMax = LMax(space);
		const int samples = 32;
		int bestI = 0;
		double bestC = -1.0;

		for (int i = 0; i <= samples; i++)
		{
			double c = MaxChroma(space, (double)i / samples * lMax, hDegrees);

			if (c > bestC)
			{
				bestC = c;
				bestI = i;
			}
		}

		// 山を挟む区間で三分探索する。MaxChroma は明度に対して単峰のため、区間を 1/3 ずつ詰めて頂点へ寄せられる。
		double lo = (double)Math.Max(bestI - 1, 0) / samples * lMax;
		double hi = (double)Math.Min(bestI + 1, samples) / samples * lMax;

		for (int i = 0; i < 24; i++)
		{
			double m1 = lo + ((hi - lo) / 3.0);
			double m2 = hi - ((hi - lo) / 3.0);

			if (MaxChroma(space, m1, hDegrees) < MaxChroma(space, m2, hDegrees))
			{
				lo = m1;
			}
			else
			{
				hi = m2;
			}
		}

		return (lo + hi) / 2.0;
	}




	// 指定した色相における L-C 平面の彩度軸の表示上限。fit が偽なら表示上限 CMax をそのまま使う。真なら、その色相で色域が届く最大の彩度(cusp の彩度)まで軸を詰めて、彩度方向に色域をパッドいっぱいへ広げ選びやすくする。明度は L-C 平面で常に全域を使うため、フィットは彩度軸だけに効く。
	public static double ChromaAxisMax(LchSpace space, double hue, bool fit)
	{
		double cMax = CMax(space);

		if (!fit)
		{
			return cMax;
		}

		double cusp = MaxChroma(space, CuspLightness(space, hue), hue);
		return FitChromaAxis(cMax, cusp);
	}




	// 指定した明度における色相×彩度の平面・円盤の彩度軸(円盤では半径)の表示上限。fit が偽なら表示上限 CMax。真なら、その明度で全色相を通じて色域が届く最大の彩度まで軸を詰めて、彩度方向に色域をいっぱいへ広げ選びやすくする。色相は平面の横軸・円盤の角度に全域現れるため、彩度の最大は単一色相の cusp ではなく全色相を通じた最大を取る。色相は常に全域を使うため、フィットは彩度軸だけに効く。
	public static double ChromaAxisMaxAtLightness(LchSpace space, double l, bool fit)
	{
		double cMax = CMax(space);

		if (!fit)
		{
			return cMax;
		}

		return FitChromaAxis(cMax, MaxChromaAtLightness(space, l));
	}




	// 指定した明度で、全色相を通じて色域に収まる最大の彩度を返す。色相を細かくサンプルし、各色相の最大色域内彩度(MaxChroma)の最大値を取る。色相×彩度の平面・円盤で彩度軸をその明度の最も鮮やかな色域端へ詰めるのに使う。全色相を色域内に収める最小値を返す MaxChromaForAllHues とは逆に、最も張り出す最大値を返す。
	public static double MaxChromaAtLightness(LchSpace space, double l)
	{
		const int samples = 90;
		double max = 0.0;

		for (int i = 0; i < samples; i++)
		{
			double h = (double)i / samples * 360.0;
			max = Math.Max(max, MaxChroma(space, l, h));
		}

		return max;
	}




	// 色域が届く最大彩度 maxInGamutChroma を、彩度軸の表示上限へ均す。境界が縁へ張り付かないよう僅かに余白を足し、色域がほぼ無い退化でも軸が潰れないよう下限を設け、表示上限 cMax は超えない。彩度軸フィットの上限算出で共通に使う。
	private static double FitChromaAxis(double cMax, double maxInGamutChroma)
	{
		return Math.Min(cMax, Math.Max(maxInGamutChroma, cMax * 0.04) * 1.06);
	}




	// 明度・彩度の平面(色相固定)で、色域外の点 (l, c) に最も近い色域内の点を返す。色域内ならそのまま返す。色域外なら、各明度での色域境界(最大彩度)の曲線上から、明度・彩度をそれぞれの上限で正規化した距離が最小の点を選ぶ。二次元スライダーで色域外へ動かしたとき、つまみを色域の縁へ滑らかに寄せる(縁をなぞらせる)のに使う。
	public static (double L, double C) NearestInGamut(LchSpace space, double l, double c, double hDegrees)
	{
		if (InGamut(space, l, c, hDegrees))
		{
			return (l, c);
		}

		double lMax = LMax(space);
		double cMax = CMax(space);
		double lTarget = l / lMax;
		double cTarget = c / cMax;

		const int samples = 32;
		double bestL = l;
		double bestC = 0.0;
		double bestDistance = double.MaxValue;

		for (int i = 0; i <= samples; i++)
		{
			double candidateL = (double)i / samples * lMax;
			double candidateC = MaxChroma(space, candidateL, hDegrees);
			double dl = (candidateL / lMax) - lTarget;
			double dc = (candidateC / cMax) - cTarget;
			double distance = (dl * dl) + (dc * dc);

			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestL = candidateL;
				bestC = candidateC;
			}
		}

		return (bestL, bestC);
	}




	// LCH を線形光の RGB へ展開する。色域判定と sRGB 化で共通に使う。チャンネルは 0–1 を外れうる。
	private static void ToLinear(LchSpace space, double l, double c, double hDegrees, out double r, out double g, out double b)
	{
		double radians = hDegrees * Math.PI / 180.0;
		double aAxis = c * Math.Cos(radians);
		double bAxis = c * Math.Sin(radians);

		if (space == LchSpace.Oklch)
		{
			OklabToLinear(l, aAxis, bAxis, out r, out g, out b);
		}
		else
		{
			CieLabToLinear(l, aAxis, bAxis, out r, out g, out b);
		}
	}




	// LCH を sRGB の不透明色へ符号化する。線形光は 0–1 へ切り詰めてから sRGB へ戻す。
	private static Color ToSrgb(LchSpace space, double l, double c, double hDegrees)
	{
		ToLinear(space, l, c, hDegrees, out double r, out double g, out double b);

		byte cr = ToByte(LinearToSrgb(Math.Clamp(r, 0.0, 1.0)));
		byte cg = ToByte(LinearToSrgb(Math.Clamp(g, 0.0, 1.0)));
		byte cb = ToByte(LinearToSrgb(Math.Clamp(b, 0.0, 1.0)));
		return Color.FromArgb(0xFF, cr, cg, cb);
	}




	// 線形光の RGB を OKLab(L=0–1・a・b)へ変換する。
	private static (double L, double A, double B) LinearToOklab(double r, double g, double b)
	{
		double l = (0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b);
		double m = (0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b);
		double s = (0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b);

		double lRoot = Math.Cbrt(l);
		double mRoot = Math.Cbrt(m);
		double sRoot = Math.Cbrt(s);

		double okL = (0.2104542553 * lRoot) + (0.7936177850 * mRoot) - (0.0040720468 * sRoot);
		double okA = (1.9779984951 * lRoot) - (2.4285922050 * mRoot) + (0.4505937099 * sRoot);
		double okB = (0.0259040371 * lRoot) + (0.7827717662 * mRoot) - (0.8086757660 * sRoot);
		return (okL, okA, okB);
	}




	// OKLab を線形光の RGB へ戻す。チャンネルは 0–1 を外れうる。
	private static void OklabToLinear(double okL, double okA, double okB, out double r, out double g, out double b)
	{
		double lRoot = okL + (0.3963377774 * okA) + (0.2158037573 * okB);
		double mRoot = okL - (0.1055613458 * okA) - (0.0638541728 * okB);
		double sRoot = okL - (0.0894841775 * okA) - (1.2914855480 * okB);

		double l = lRoot * lRoot * lRoot;
		double m = mRoot * mRoot * mRoot;
		double s = sRoot * sRoot * sRoot;

		r = (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s);
		g = (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s);
		b = (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s);
	}




	// 線形光の RGB を CIELAB(L=0–100・a・b、D65)へ変換する。
	private static (double L, double A, double B) LinearToCieLab(double r, double g, double b)
	{
		double x = (0.4124564 * r) + (0.3575761 * g) + (0.1804375 * b);
		double y = (0.2126729 * r) + (0.7151522 * g) + (0.0721750 * b);
		double z = (0.0193339 * r) + (0.1191920 * g) + (0.9503041 * b);

		double fx = LabF(x / 0.95047);
		double fy = LabF(y / 1.00000);
		double fz = LabF(z / 1.08883);

		double l = (116.0 * fy) - 16.0;
		double a = 500.0 * (fx - fy);
		double bb = 200.0 * (fy - fz);
		return (l, a, bb);
	}




	// CIELAB を線形光の RGB へ戻す。チャンネルは 0–1 を外れうる。
	private static void CieLabToLinear(double l, double a, double b, out double rOut, out double gOut, out double bOut)
	{
		double fy = (l + 16.0) / 116.0;
		double fx = fy + (a / 500.0);
		double fz = fy - (b / 200.0);

		double xr = LabFInverse(fx);
		double yr = LabFInverse(fy);
		double zr = LabFInverse(fz);

		double x = xr * 0.95047;
		double y = yr * 1.00000;
		double z = zr * 1.08883;

		rOut = (3.2404542 * x) - (1.5371385 * y) - (0.4985314 * z);
		gOut = (-0.9692660 * x) + (1.8760108 * y) + (0.0415560 * z);
		bOut = (0.0556434 * x) - (0.2040259 * y) + (1.0572252 * z);
	}




	// CIELAB の補助関数 f(t)。立方根に近いが、暗部では線形に切り替えて数値を安定させる。
	private static double LabF(double t)
	{
		const double epsilon = 216.0 / 24389.0;
		const double kappa = 24389.0 / 27.0;
		return t > epsilon ? Math.Cbrt(t) : (((kappa * t) + 16.0) / 116.0);
	}




	// CIELAB の補助関数 f(t) の逆関数。f(t) の立方が閾値を超えるかで、立方と線形を切り替える。
	private static double LabFInverse(double f)
	{
		const double epsilon = 216.0 / 24389.0;
		const double kappa = 24389.0 / 27.0;
		double cube = f * f * f;
		return cube > epsilon ? cube : (((116.0 * f) - 16.0) / kappa);
	}




	// sRGB の1チャンネル(0–1)を線形光(0–1)へ戻す。
	private static double SrgbToLinear(double c)
	{
		return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	}




	// 線形光(0–1)を sRGB の1チャンネル(0–1)へ符号化する。
	private static double LinearToSrgb(double c)
	{
		return c <= 0.0031308 ? 12.92 * c : (1.055 * Math.Pow(c, 1.0 / 2.4)) - 0.055;
	}




	// 0–1 の値を 0–255 のバイトへ丸める。
	private static byte ToByte(double v)
	{
		int i = (int)Math.Round(v * 255.0);
		return (byte)Math.Clamp(i, 0, 255);
	}
}
