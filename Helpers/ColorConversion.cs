// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Irozukume.Models;

namespace Irozukume.Helpers;

// RGB と各表色系(HSV・HSL・YCbCr)の相互変換をまとめた補助。色相環の描画(HueWheel)・三角形パッド(HslTriangle)・色差平面(CbCrPlane)と色編集モデル(ColorEditorViewModel)が同じ式を使い、各所で色がずれないようにする。色相は度(0–360)、彩度・明度・輝度は 0–1、YCbCr は 0–255 のデジタルコード(スタジオレンジでは Y が 16–235)で扱う。
public static class ColorConversion
{
	// 直近に Lab を前計算した格子モードと、その全代表色ぶんの Lab。色制限のパッドは画素ごとに塗るため、候補色の Lab を毎回求め直さずモードが変わるまで使い回す。表示は UI スレッドからの呼び出しのみで競合しない。
	private static ColorLimitMode _gridLabMode;
	private static (double L, double A, double B)[]? _gridLab;
	private static bool _gridLabValid;
	// HSV を RGB(各バイト)へ変換する。色相は度で、範囲外でも 0–360 へ巻き戻して扱う。彩度・明度は 0–1 に丸めて扱う。
	public static (byte R, byte G, byte B) HsvToRgb(double hue, double saturation, double value)
	{
		hue = ((hue % 360.0) + 360.0) % 360.0;
		saturation = Math.Clamp(saturation, 0.0, 1.0);
		value = Math.Clamp(value, 0.0, 1.0);

		double c = value * saturation;
		double sector = hue / 60.0;
		double x = c * (1.0 - Math.Abs((sector % 2.0) - 1.0));
		double m = value - c;

		double r1 = 0.0;
		double g1 = 0.0;
		double b1 = 0.0;

		int segment = (int)Math.Floor(sector) % 6;

		if (segment < 0)
		{
			segment += 6;
		}

		switch (segment)
		{
			case 0: r1 = c; g1 = x; break;
			case 1: r1 = x; g1 = c; break;
			case 2: g1 = c; b1 = x; break;
			case 3: g1 = x; b1 = c; break;
			case 4: r1 = x; b1 = c; break;
			default: r1 = c; b1 = x; break;
		}

		byte r = (byte)Math.Round((r1 + m) * 255.0);
		byte g = (byte)Math.Round((g1 + m) * 255.0);
		byte b = (byte)Math.Round((b1 + m) * 255.0);
		return (r, g, b);
	}




	// RGB(各バイト)を HSV へ変換する。色相は度(0–360)、彩度・明度は 0–1。無彩色(灰)は彩度 0、色相は 0 を返す。黒は明度 0 で色相・彩度ともに 0 を返す。色相・彩度が定義できない場合の保持は呼び出し側で行う。
	public static (double Hue, double Saturation, double Value) RgbToHsv(byte r, byte g, byte b)
	{
		double rf = r / 255.0;
		double gf = g / 255.0;
		double bf = b / 255.0;

		double max = Math.Max(rf, Math.Max(gf, bf));
		double min = Math.Min(rf, Math.Min(gf, bf));
		double delta = max - min;

		double hue = HueFromRgb(rf, gf, bf, max, delta);
		double saturation = max <= 0.0 ? 0.0 : delta / max;
		double value = max;
		return (hue, saturation, value);
	}




	// HSL を RGB(各バイト)へ変換する。色相は度で、範囲外でも 0–360 へ巻き戻す。彩度・輝度は 0–1 に丸める。
	public static (byte R, byte G, byte B) HslToRgb(double hue, double saturation, double lightness)
	{
		hue = ((hue % 360.0) + 360.0) % 360.0;
		saturation = Math.Clamp(saturation, 0.0, 1.0);
		lightness = Math.Clamp(lightness, 0.0, 1.0);

		double c = (1.0 - Math.Abs((2.0 * lightness) - 1.0)) * saturation;
		double sector = hue / 60.0;
		double x = c * (1.0 - Math.Abs((sector % 2.0) - 1.0));
		double m = lightness - (c / 2.0);

		double r1 = 0.0;
		double g1 = 0.0;
		double b1 = 0.0;

		int segment = (int)Math.Floor(sector) % 6;

		if (segment < 0)
		{
			segment += 6;
		}

		switch (segment)
		{
			case 0: r1 = c; g1 = x; break;
			case 1: r1 = x; g1 = c; break;
			case 2: g1 = c; b1 = x; break;
			case 3: g1 = x; b1 = c; break;
			case 4: r1 = x; b1 = c; break;
			default: r1 = c; b1 = x; break;
		}

		byte r = (byte)Math.Round((r1 + m) * 255.0);
		byte g = (byte)Math.Round((g1 + m) * 255.0);
		byte b = (byte)Math.Round((b1 + m) * 255.0);
		return (r, g, b);
	}




	// RGB(各バイト)を HSL へ変換する。色相は度(0–360)、彩度・輝度は 0–1。無彩色(灰)は彩度 0、色相 0 を返す。黒・白の極では彩度が定まらず 0 を返す。色相・彩度が定義できない場合の保持は呼び出し側で行う。
	public static (double Hue, double Saturation, double Lightness) RgbToHsl(byte r, byte g, byte b)
	{
		double rf = r / 255.0;
		double gf = g / 255.0;
		double bf = b / 255.0;

		double max = Math.Max(rf, Math.Max(gf, bf));
		double min = Math.Min(rf, Math.Min(gf, bf));
		double delta = max - min;

		double hue = HueFromRgb(rf, gf, bf, max, delta);
		double lightness = (max + min) / 2.0;
		double denominator = 1.0 - Math.Abs((2.0 * lightness) - 1.0);
		double saturation = denominator <= 0.0 ? 0.0 : delta / denominator;
		return (hue, saturation, lightness);
	}




	// HWB を RGB(各バイト)へ変換する。色相は度で、範囲外でも 0–360 へ巻き戻す。白み・黒みは 0–1 に丸める。白み+黒みが 1 以上のときは色相を無視した無彩色で、灰の明るさは白み÷(白み+黒み)。1 未満のときは純色を残り(1−白み−黒み)で割り当て、各チャンネルを 純色×(1−白み−黒み)+白み にする。
	public static (byte R, byte G, byte B) HwbToRgb(double hue, double whiteness, double blackness)
	{
		whiteness = Math.Clamp(whiteness, 0.0, 1.0);
		blackness = Math.Clamp(blackness, 0.0, 1.0);
		double sum = whiteness + blackness;

		if (sum >= 1.0)
		{
			byte gray = (byte)Math.Round(whiteness / sum * 255.0);
			return (gray, gray, gray);
		}

		(byte pr, byte pg, byte pb) = HsvToRgb(hue, 1.0, 1.0);
		double scale = 1.0 - whiteness - blackness;
		byte r = (byte)Math.Round((((pr / 255.0) * scale) + whiteness) * 255.0);
		byte g = (byte)Math.Round((((pg / 255.0) * scale) + whiteness) * 255.0);
		byte b = (byte)Math.Round((((pb / 255.0) * scale) + whiteness) * 255.0);
		return (r, g, b);
	}




	// RGB(各バイト)を HWB へ変換する。色相は度(0–360)、白み・黒みは 0–1。白みは最小チャンネル、黒みは最大チャンネルの補数で、無彩色(灰・黒・白)でも定まる。色相は HSV・HSL と同じ式で導き、無彩色では 0 を返すため、色相の保持は呼び出し側で行う。
	public static (double Hue, double Whiteness, double Blackness) RgbToHwb(byte r, byte g, byte b)
	{
		double rf = r / 255.0;
		double gf = g / 255.0;
		double bf = b / 255.0;

		double max = Math.Max(rf, Math.Max(gf, bf));
		double min = Math.Min(rf, Math.Min(gf, bf));
		double hue = HueFromRgb(rf, gf, bf, max, max - min);
		double whiteness = min;
		double blackness = 1.0 - max;
		return (hue, whiteness, blackness);
	}




	// RGB(各バイト)を指定形式の YCbCr へ変換する。輝度 Y はフルレンジで 0–255・スタジオレンジで 16–235、色差 Cb・Cr は 0–255(無彩色 128)で、原色付近では 0–255 をわずかに超える。Cb は青方向、Cr は赤方向の色差。
	public static (double Y, double Cb, double Cr) RgbToYCbCr(byte r, byte g, byte b, YCbCrFormat format)
	{
		double kr = format.Kr;
		double kb = format.Kb;
		double luma = (kr * r) + (format.Kg * g) + (kb * b);
		double cbScale = 0.5 / (1.0 - kb);
		double crScale = 0.5 / (1.0 - kr);

		if (format.FullRange)
		{
			double cbFull = 128.0 + ((b - luma) * cbScale);
			double crFull = 128.0 + ((r - luma) * crScale);
			return (luma, cbFull, crFull);
		}

		double y = 16.0 + (luma * (219.0 / 255.0));
		double cb = 128.0 + ((b - luma) * cbScale * (224.0 / 255.0));
		double cr = 128.0 + ((r - luma) * crScale * (224.0 / 255.0));
		return (y, cb, cr);
	}




	// 指定形式の YCbCr を RGB の実数値へ変換する。丸めもクランプもせず 0–255 の範囲外もそのまま返すため、ガモットの内外判定や境界のなじませを呼び出し側で行える。
	public static (double R, double G, double B) YCbCrToRgbExact(double y, double cb, double cr, YCbCrFormat format)
	{
		double kr = format.Kr;
		double kb = format.Kb;
		double kg = format.Kg;
		double rScale = 2.0 * (1.0 - kr);
		double bScale = 2.0 * (1.0 - kb);

		double luma;
		double pb;
		double pr;

		if (format.FullRange)
		{
			luma = y;
			pb = (cb - 128.0) * bScale;
			pr = (cr - 128.0) * rScale;
		}
		else
		{
			luma = (y - 16.0) * (255.0 / 219.0);
			pb = (cb - 128.0) * bScale * (255.0 / 224.0);
			pr = (cr - 128.0) * rScale * (255.0 / 224.0);
		}

		double r = luma + pr;
		double g = luma - ((kr / kg) * pr) - ((kb / kg) * pb);
		double b = luma + pb;
		return (r, g, b);
	}




	// 指定形式の YCbCr を RGB(各バイト)へ変換する。ガモット外は 0–255 にクランプして丸める。色1へ反映する通常の変換に使う。
	public static (byte R, byte G, byte B) YCbCrToRgb(double y, double cb, double cr, YCbCrFormat format)
	{
		(double r, double g, double b) = YCbCrToRgbExact(y, cb, cr, format);
		byte rb = (byte)Math.Round(Math.Clamp(r, 0.0, 255.0));
		byte gb = (byte)Math.Round(Math.Clamp(g, 0.0, 255.0));
		byte bb = (byte)Math.Round(Math.Clamp(b, 0.0, 255.0));
		return (rb, gb, bb);
	}




	// RGB(各バイト)を、指定の色制限設定に従って最も近い表せる色へ丸める。None は素の色をそのまま返す。表示・グラデーション・パッドの丸めはすべてこの一点を通す。格子モード(WebSafe・RGB565 等)は SnapGrid、ターミナルモード(Term256/16/8)は TerminalPalette の最近傍探索へ委ねる。
	public static (byte R, byte G, byte B) Snap(SnapSettings snap, byte r, byte g, byte b)
	{
		switch (snap.Mode)
		{
			case ColorLimitMode.None:
				return (r, g, b);

			case ColorLimitMode.Term256:
			case ColorLimitMode.Term16:
			case ColorLimitMode.Term8:
				return TerminalPalette.SnapRgb(snap.Mode, snap.Metric, snap.Theme, r, g, b);

			default:
				return SnapGrid(snap.Mode, snap.Metric, r, g, b);
		}
	}




	// 格子モード(各チャンネルが独立した段の直積)で最も近い色へ丸める。Rgb は各チャンネルを最寄り段へ丸めるだけでよく、これはチャンネルに分離できる距離での最近傍と一致する。Lab・Redmean は分離できないため、各チャンネル±1段の近傍(最大27候補)を当該距離で測り直して最小を選ぶ。高段数モード(RGB565 等)でも候補を近傍に限るため総当たりにならない。
	private static (byte R, byte G, byte B) SnapGrid(ColorLimitMode mode, SnapMetric metric, byte r, byte g, byte b)
	{
		(int maxR, int maxG, int maxB) = GridMax(mode);
		int ir = NearestStep(r, maxR);
		int ig = NearestStep(g, maxG);
		int ib = NearestStep(b, maxB);

		if (metric == SnapMetric.Rgb)
		{
			return (StepValue(ir, maxR), StepValue(ig, maxG), StepValue(ib, maxB));
		}

		// Lab のときは候補色の Lab を画素ごとに求め直さず、モード別の前計算表(GridLabFor)を引く。表内の位置は段番号 (ir,ig,ib) から求める。redmean は cbrt を伴わないため前計算しない。
		(double L, double A, double B)[]? gridLab = null;
		double sl = 0.0;
		double sa = 0.0;
		double sb = 0.0;

		if (metric == SnapMetric.Lab)
		{
			gridLab = GridLabFor(mode);
			(sl, sa, sb) = ColorMetrics.RgbToLab(r, g, b);
		}

		int strideG = maxG + 1;
		int strideB = maxB + 1;
		byte bestR = StepValue(ir, maxR);
		byte bestG = StepValue(ig, maxG);
		byte bestB = StepValue(ib, maxB);
		double bestDist = double.MaxValue;

		for (int dr = -1; dr <= 1; dr++)
		{
			int jr = ir + dr;

			if (jr < 0 || jr > maxR)
			{
				continue;
			}

			byte cr = StepValue(jr, maxR);

			for (int dg = -1; dg <= 1; dg++)
			{
				int jg = ig + dg;

				if (jg < 0 || jg > maxG)
				{
					continue;
				}

				byte cg = StepValue(jg, maxG);

				for (int db = -1; db <= 1; db++)
				{
					int jb = ib + db;

					if (jb < 0 || jb > maxB)
					{
						continue;
					}

					byte cb = StepValue(jb, maxB);
					double d;

					if (gridLab is not null)
					{
						(double cl, double ca, double clb) = gridLab[((jr * strideG) + jg) * strideB + jb];
						double dl = sl - cl;
						double da = sa - ca;
						double dbb = sb - clb;
						d = (dl * dl) + (da * da) + (dbb * dbb);
					}
					else
					{
						d = ColorMetrics.DistanceSquared(metric, r, g, b, cr, cg, cb);
					}

					if (d < bestDist)
					{
						bestDist = d;
						bestR = cr;
						bestG = cg;
						bestB = cb;
					}
				}
			}
		}

		return (bestR, bestG, bestB);
	}




	// 格子モードの各チャンネルが取れる最大の段番号(段数-1)を返す。WebSafe は 6 段(0-5)、RGB565 は 32/64/32 段、RGB555 は 32 段、RGB444 は 16 段、RGB332 は 8/8/4 段。
	private static (int R, int G, int B) GridMax(ColorLimitMode mode)
	{
		return mode switch
		{
			ColorLimitMode.WebSafe => (5, 5, 5),
			ColorLimitMode.Rgb565 => (31, 63, 31),
			ColorLimitMode.Rgb555 => (31, 31, 31),
			ColorLimitMode.Rgb444 => (15, 15, 15),
			ColorLimitMode.Rgb332 => (7, 7, 3),
			_ => (255, 255, 255),
		};
	}




	// 値(0-255)を、max+1 段へ均等割りした段のうち最も近い段の番号(0-max)にする。
	private static int NearestStep(byte value, int max)
	{
		return (int)Math.Round(value * max / 255.0);
	}




	// 段の番号(0-max)を、0-255 へ均等に引き伸ばした代表値にする。
	private static byte StepValue(int index, int max)
	{
		return (byte)Math.Round(index * 255.0 / max);
	}




	// 指定の格子モードの全代表色ぶんの Lab を返す。直近と同じモードなら前計算した表をそのまま返し、違えば一度だけ作り直す。並びは段番号 (ir,ig,ib) の三重ループ順で、SnapGrid の引き方と揃える。
	private static (double L, double A, double B)[] GridLabFor(ColorLimitMode mode)
	{
		if (_gridLabValid && _gridLabMode == mode && _gridLab is not null)
		{
			return _gridLab;
		}

		(int maxR, int maxG, int maxB) = GridMax(mode);
		var built = new (double L, double A, double B)[(maxR + 1) * (maxG + 1) * (maxB + 1)];
		int index = 0;

		for (int ir = 0; ir <= maxR; ir++)
		{
			for (int ig = 0; ig <= maxG; ig++)
			{
				for (int ib = 0; ib <= maxB; ib++)
				{
					built[index++] = ColorMetrics.RgbToLab(StepValue(ir, maxR), StepValue(ig, maxG), StepValue(ib, maxB));
				}
			}
		}

		_gridLab = built;
		_gridLabMode = mode;
		_gridLabValid = true;
		return built;
	}




	// RGB(各 0–1)と最大値・最大最小差から色相(度, 0–360)を求める。HSV・HSL で共通。無彩色(差が 0)は 0 を返す。
	private static double HueFromRgb(double rf, double gf, double bf, double max, double delta)
	{
		if (delta <= 0.0)
		{
			return 0.0;
		}

		double hue;

		if (max == rf)
		{
			hue = 60.0 * (((gf - bf) / delta) % 6.0);
		}
		else if (max == gf)
		{
			hue = 60.0 * (((bf - rf) / delta) + 2.0);
		}
		else
		{
			hue = 60.0 * (((rf - gf) / delta) + 4.0);
		}

		if (hue < 0.0)
		{
			hue += 360.0;
		}

		return hue;
	}
}
