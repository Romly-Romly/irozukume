// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.UI;
using Irozukume.ScreenPicker.Capture;

namespace Irozukume.ScreenPicker.Glass;

// scRGB（リニア・Rec.709、1.0=80nit）と sRGB 8bit の相互変換、および輝度の nits 換算。
// HDR デスクトップでは SDR 白が scRGB 1.0 ではなく sdrWhiteScale 倍にあるため、SDR 色を得る際はその値で割って正規化する。SDR デスクトップでは sdrWhiteScale=1.0。
internal static class ScRgbColorMath
{
	private const double NitsPerScRgbUnit = 80.0;

	// linear(0..1)→sRGB 8bit エンコードの参照表。表示経路で 1 画素あたり 3 回走る Math.Pow を引き表に置き換えるためのもの。
	// 4096 段で量子化しており誤差は最大でも 1 階調未満。厳密一致を要する採色値の算出では使わず、ガラスに見せる表示色の構築にのみ使う。
	private const int LutSize = 4096;
	private static readonly byte[] _srgbLut = BuildSrgbLut();




	private static byte[] BuildSrgbLut()
	{
		var lut = new byte[LutSize + 1];
		for (int i = 0; i <= LutSize; i++)
		{
			lut[i] = LinearToSrgbByte((double)i / LutSize);
		}
		return lut;
	}




	public static Color ScRgbToSrgb8(ScRgbColor c, double sdrWhiteScale)
	{
		double s = sdrWhiteScale <= 0 ? 1.0 : sdrWhiteScale;
		return Color.FromArgb(
			255,
			LinearToSrgbByte(c.R / s),
			LinearToSrgbByte(c.G / s),
			LinearToSrgbByte(c.B / s));
	}




	// scRGB の1画素を、SDR 白レベルで正規化した sRGB エンコード済みの 0..255 バイト(B,G,R,A)へ書き出す。ガラスへ渡す表示用ビットマップの構築に使う。
	// 毎フレーム領域全体に走るホットパスのため、OETF を LUT 引きで近似する(誤差 1 階調未満)。
	public static void ScRgbToSrgbBgra(ScRgbColor c, double sdrWhiteScale, byte[] dst, int offset)
	{
		double s = sdrWhiteScale <= 0 ? 1.0 : sdrWhiteScale;
		dst[offset + 0] = LinearToSrgbByteFast(c.B / s);
		dst[offset + 1] = LinearToSrgbByteFast(c.G / s);
		dst[offset + 2] = LinearToSrgbByteFast(c.R / s);
		dst[offset + 3] = 255;
	}




	private static byte LinearToSrgbByteFast(double linear)
	{
		double v = linear <= 0.0 ? 0.0 : (linear >= 1.0 ? 1.0 : linear);
		return _srgbLut[(int)(v * LutSize + 0.5)];
	}




	private static byte LinearToSrgbByte(double linear)
	{
		double v = Math.Clamp(linear, 0.0, 1.0);
		double encoded = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
		int b = (int)Math.Round(encoded * 255.0);
		return (byte)Math.Clamp(b, 0, 255);
	}




	// Rec.709 相対輝度を nits へ換算する。scRGB 1.0 = 80nit を基準とする絶対輝度であり、SDR 白レベルとは独立。
	public static double Nits(ScRgbColor c)
	{
		double luminance = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
		return luminance * NitsPerScRgbUnit;
	}




	// SDR 白レベルを基準に、いずれかのチャンネルが SDR 表現可能範囲を超えていれば真。真の HDR ハイライトの検出に使う。
	public static bool IsOutsideSdr(ScRgbColor c, double sdrWhiteScale)
	{
		double s = sdrWhiteScale <= 0 ? 1.0 : sdrWhiteScale;
		const double hi = 1.001;
		const double lo = -0.001;
		return c.R / s > hi || c.G / s > hi || c.B / s > hi || c.R < lo || c.G < lo || c.B < lo;
	}
}
