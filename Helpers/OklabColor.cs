// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.UI;

namespace Irozukume.Helpers;

// OKLab/OKLCh と sRGB の相互変換。明度(OKLab の L)だけを動かして色相と彩度を保つコントラスト調整に使う。指定の色相と彩度が sRGB 色域を外れる組み合わせでは、色相を保ったまま彩度を落として域内へ収める。
public static class OklabColor
{
	// OKLab 行列で使う線形光へ sRGB の1チャンネル(0–1)を戻す。
	private static double SrgbToLinear(double c)
	{
		return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	}




	// 線形光(0–1)を sRGB の1チャンネル(0–1)へ符号化する。
	private static double LinearToSrgb(double c)
	{
		return c <= 0.0031308 ? 12.92 * c : (1.055 * Math.Pow(c, 1.0 / 2.4)) - 0.055;
	}




	// sRGB(各バイト)を OKLCh(明度 L=0–1・彩度 C・色相 H=ラジアン)へ変換する。色相と彩度の基準を取り出すのに使う。
	public static (double L, double C, double H) ToOklch(Color color)
	{
		double r = SrgbToLinear(color.R / 255.0);
		double g = SrgbToLinear(color.G / 255.0);
		double b = SrgbToLinear(color.B / 255.0);

		double l = (0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b);
		double m = (0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b);
		double s = (0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b);

		double lRoot = Math.Cbrt(l);
		double mRoot = Math.Cbrt(m);
		double sRoot = Math.Cbrt(s);

		double okL = (0.2104542553 * lRoot) + (0.7936177850 * mRoot) - (0.0040720468 * sRoot);
		double okA = (1.9779984951 * lRoot) - (2.4285922050 * mRoot) + (0.4505937099 * sRoot);
		double okB = (0.0259040371 * lRoot) + (0.7827717662 * mRoot) - (0.8086757660 * sRoot);

		double c = Math.Sqrt((okA * okA) + (okB * okB));
		double h = Math.Atan2(okB, okA);
		return (okL, c, h);
	}




	// OKLCh を sRGB の不透明色へ変換する。指定の色が sRGB 色域を外れるときは、明度と色相を保ったまま彩度だけ二分法で詰めて域内へ収める。
	public static Color FromOklch(double okL, double c, double h)
	{
		okL = Math.Clamp(okL, 0.0, 1.0);

		if (c <= 0.0 || InGamut(okL, c, h))
		{
			return ToSrgb(okL, c, h);
		}

		double lo = 0.0;
		double hi = c;

		for (int i = 0; i < 24; i++)
		{
			double mid = (lo + hi) / 2.0;

			if (InGamut(okL, mid, h))
			{
				lo = mid;
			}
			else
			{
				hi = mid;
			}
		}

		return ToSrgb(okL, lo, h);
	}




	// OKLCh を線形光の RGB へ展開する。色域判定と sRGB 化で共通に使う。
	private static void ToLinear(double okL, double c, double h, out double r, out double g, out double b)
	{
		double okA = c * Math.Cos(h);
		double okB = c * Math.Sin(h);

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




	// OKLCh が sRGB 色域に収まるか。線形光の各チャンネルが 0–1 にあるかで判定し、丸め誤差を許す微小な余裕を見る。
	private static bool InGamut(double okL, double c, double h)
	{
		ToLinear(okL, c, h, out double r, out double g, out double b);

		const double eps = 0.0001;
		return r >= -eps && r <= 1.0 + eps
			&& g >= -eps && g <= 1.0 + eps
			&& b >= -eps && b <= 1.0 + eps;
	}




	// OKLCh を sRGB の不透明色へ符号化する。線形光は 0–1 へ切り詰めてから sRGB へ戻す。
	private static Color ToSrgb(double okL, double c, double h)
	{
		ToLinear(okL, c, h, out double r, out double g, out double b);

		byte cr = ToByte(LinearToSrgb(Math.Clamp(r, 0.0, 1.0)));
		byte cg = ToByte(LinearToSrgb(Math.Clamp(g, 0.0, 1.0)));
		byte cb = ToByte(LinearToSrgb(Math.Clamp(b, 0.0, 1.0)));
		return Color.FromArgb(0xFF, cr, cg, cb);
	}




	// 0–1 の値を 0–255 のバイトへ丸める。
	private static byte ToByte(double v)
	{
		int i = (int)Math.Round(v * 255.0);
		return (byte)Math.Clamp(i, 0, 255);
	}
}
