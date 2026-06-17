// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.UI;
using Irozukume.Models;

namespace Irozukume.Helpers;

// 色の知覚的な指標をまとめた補助。背景色に重ねる文字色の選択や、最も近いパレット色を探す距離計算など、複数の箇所で同じ基準を使うために集約する。
public static class ColorMetrics
{
	// sRGB の各バイト値を線形光へ戻す前計算表。Lab 変換は最近傍探索で同じ入力を何度も使うため、256 段を一度だけ求めて使い回す。
	private static readonly double[] LinearLut = BuildLinearLut();
	// sRGB の1チャンネル(0–255)を線形光(0–1)へ戻す。WCAG の相対輝度の算出に使う。
	private static double SrgbToLinear(byte channel)
	{
		double v = channel / 255.0;
		return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
	}




	// WCAG の相対輝度(0–1)。各チャンネルを線形光に戻し、視感度の重みで合成する。コントラスト比の計算に使う。
	public static double RelativeLuminance(Color color)
	{
		return (0.2126 * SrgbToLinear(color.R)) + (0.7152 * SrgbToLinear(color.G)) + (0.0722 * SrgbToLinear(color.B));
	}




	// 背景色の上に重ねる文字が読みやすくなる前景色(黒か白)を選ぶ。黒と白それぞれの WCAG コントラスト比を比べ、比が大きい方を返す。単純な明度の閾値では選び損ねる彩度の高い色でも、実際に読みやすい側を選べる。
	public static Color ContrastColor(Color background)
	{
		double luminance = RelativeLuminance(background);
		double contrastWithBlack = (luminance + 0.05) / 0.05;
		double contrastWithWhite = 1.05 / (luminance + 0.05);
		byte tone = contrastWithBlack >= contrastWithWhite ? (byte)0x00 : (byte)0xFF;
		return Color.FromArgb(0xFF, tone, tone, tone);
	}




	// 前景色(アルファ付き)を不透明な背景色の上に重ねた合成色を返す。sRGB 空間で非乗算アルファの over 合成 (a*前景 + (1-a)*背景) を行う。アルファを含めたコントラスト評価で、背景に透けて見える実効的な文字色を求めるのに使う。背景は不透明とみなし、結果も不透明で返す。
	public static Color AlphaComposite(Color foreground, Color background)
	{
		double a = foreground.A / 255.0;
		byte Blend(byte f, byte b) => (byte)Math.Round((f * a) + (b * (1.0 - a)));
		return Color.FromArgb(0xFF, Blend(foreground.R, background.R), Blend(foreground.G, background.G), Blend(foreground.B, background.B));
	}




	// 2色の WCAG コントラスト比(1.0–21.0)。両色の相対輝度から (明るい方+0.05)/(暗い方+0.05) で求める。文字色と背景色の読みやすさの判定に使う。
	public static double ContrastRatio(Color a, Color b)
	{
		double la = RelativeLuminance(a);
		double lb = RelativeLuminance(b);
		double lighter = Math.Max(la, lb);
		double darker = Math.Min(la, lb);
		return (lighter + 0.05) / (darker + 0.05);
	}




	// sRGB(各バイト)を CIELAB(L*・a*・b*)へ変換する。D65 白色点。最近傍探索の知覚的距離(CIE76)に使う。
	public static (double L, double A, double B) RgbToLab(byte r, byte g, byte b)
	{
		double rl = LinearLut[r];
		double gl = LinearLut[g];
		double bl = LinearLut[b];

		// 線形 sRGB を XYZ(D65)へ。係数は sRGB の標準変換行列。
		double x = (rl * 0.4124564) + (gl * 0.3575761) + (bl * 0.1804375);
		double y = (rl * 0.2126729) + (gl * 0.7151522) + (bl * 0.0721750);
		double z = (rl * 0.0193339) + (gl * 0.1191920) + (bl * 0.9503041);

		// D65 白色点で正規化してから Lab の補助関数を通す。
		double fx = LabF(x / 0.95047);
		double fy = LabF(y / 1.00000);
		double fz = LabF(z / 1.08883);

		double l = (116.0 * fy) - 16.0;
		double a = 500.0 * (fx - fy);
		double bb = 200.0 * (fy - fz);
		return (l, a, bb);
	}




	// あらかじめ求めた基準色の Lab と候補色との CIE76 距離の2乗。最近傍探索で基準の Lab を一度だけ求めて使い回すための形。平方根は取らない(最近傍の大小判定には不要)。
	public static double LabDistanceSquared(double l, double a, double b, byte cr, byte cg, byte cb)
	{
		(double cl, double ca, double cbb) = RgbToLab(cr, cg, cb);
		double dl = l - cl;
		double da = a - ca;
		double db = b - cbb;
		return (dl * dl) + (da * da) + (db * db);
	}




	// 2色の距離の2乗を、指定の距離計算で返す。最近傍の比較にのみ使うため平方根は取らない。Lab は基準色を1色ずつ変換するため、基準を固定して多数の候補と比べる探索では LabDistanceSquared を使う方が速い。距離計算ごとに尺度は異なるが、同一計算内での大小だけを使うため問題ない。
	public static double DistanceSquared(SnapMetric metric, byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
	{
		switch (metric)
		{
			case SnapMetric.Lab:
			{
				(double l1, double a1, double bb1) = RgbToLab(r1, g1, b1);
				return LabDistanceSquared(l1, a1, bb1, r2, g2, b2);
			}

			case SnapMetric.Redmean:
			{
				// 赤成分の平均で R・B の重みを振り分ける近似。人の目の感度に寄せた簡易な距離。
				double rmean = (r1 + r2) / 2.0;
				double dr = r1 - r2;
				double dg = g1 - g2;
				double db = b1 - b2;
				return ((2.0 + (rmean / 256.0)) * dr * dr) + (4.0 * dg * dg) + ((2.0 + ((255.0 - rmean) / 256.0)) * db * db);
			}

			default:
			{
				double dr = r1 - r2;
				double dg = g1 - g2;
				double db = b1 - b2;
				return (dr * dr) + (dg * dg) + (db * db);
			}
		}
	}




	// Lab の補助関数 f(t)。立方根に近いが、暗部では線形に切り替えて数値を安定させる。
	private static double LabF(double t)
	{
		const double epsilon = 216.0 / 24389.0;
		const double kappa = 24389.0 / 27.0;
		return t > epsilon ? Math.Cbrt(t) : (((kappa * t) + 16.0) / 116.0);
	}




	// sRGB の256段を線形光へ戻す前計算表を作る。
	private static double[] BuildLinearLut()
	{
		var lut = new double[256];

		for (int i = 0; i < 256; i++)
		{
			lut[i] = SrgbToLinear((byte)i);
		}

		return lut;
	}
}
