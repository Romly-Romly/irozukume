// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Globalization;
using Irozukume.Models;

namespace Irozukume.Helpers;

// 色 (R・G・B と不透明度) を各種の文字列形式へ整形する。貼り付け側の ColorStringParser と対になり、現在の色をクリップボードへ書き出す文字列を作る。16進は大文字、rgb()/hsl() は整数とパーセント、CSS 関数のアルファは WebAlphaUnit で 0–1 か 0–100% かを選んで表記する。色相・彩度・輝度・白み・黒みは現在の RGB から導く。
public static class ColorStringFormatter
{
	// 名前付きカラーを、色から名前へ引く逆引き表。WebNamedColors の一覧から一度だけ作る。
	private static readonly Dictionary<int, string> NamedByRgb = BuildNamedByRgb();




	public static string HexRgb(byte r, byte g, byte b)
	{
		return $"#{r:X2}{g:X2}{b:X2}";
	}




	public static string HexRgba(byte r, byte g, byte b, byte a)
	{
		return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
	}




	public static string Rgb(byte r, byte g, byte b)
	{
		return $"rgb({r}, {g}, {b})";
	}




	public static string Rgba(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		return $"rgba({r}, {g}, {b}, {AlphaText(a, alphaUnit)})";
	}




	// CSS Color 4 のモダン記法。区切りをカンマでなく空白にする。
	public static string RgbModern(byte r, byte g, byte b)
	{
		return $"rgb({r} {g} {b})";
	}




	// モダン記法で不透明度を添える。関数名は rgb() のまま、不透明度をスラッシュの後ろに置く。
	public static string RgbaModern(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		return $"rgb({r} {g} {b} / {AlphaText(a, alphaUnit)})";
	}




	public static string Hsl(byte r, byte g, byte b)
	{
		(double h, double s, double l) = ColorConversion.RgbToHsl(r, g, b);
		return $"hsl({Degrees(h)}, {Percent(s)}%, {Percent(l)}%)";
	}




	public static string Hsla(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		(double h, double s, double l) = ColorConversion.RgbToHsl(r, g, b);
		return $"hsla({Degrees(h)}, {Percent(s)}%, {Percent(l)}%, {AlphaText(a, alphaUnit)})";
	}




	// モダン記法の hsl()。成分を空白で区切る。
	public static string HslModern(byte r, byte g, byte b)
	{
		(double h, double s, double l) = ColorConversion.RgbToHsl(r, g, b);
		return $"hsl({Degrees(h)} {Percent(s)}% {Percent(l)}%)";
	}




	// モダン記法の hsl() に不透明度を添える。関数名は hsl() のまま、不透明度をスラッシュの後ろに置く。
	public static string HslaModern(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		(double h, double s, double l) = ColorConversion.RgbToHsl(r, g, b);
		return $"hsl({Degrees(h)} {Percent(s)}% {Percent(l)}% / {AlphaText(a, alphaUnit)})";
	}




	public static string Hwb(byte r, byte g, byte b)
	{
		(double h, double whiteness, double blackness) = ColorConversion.RgbToHwb(r, g, b);
		return $"hwb({Degrees(h)} {Percent(whiteness)}% {Percent(blackness)}%)";
	}




	// hwb() に不透明度を添える。hwb() は空白区切りのみで、不透明度をスラッシュの後ろに置く。
	public static string Hwba(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		(double h, double whiteness, double blackness) = ColorConversion.RgbToHwb(r, g, b);
		return $"hwb({Degrees(h)} {Percent(whiteness)}% {Percent(blackness)}% / {AlphaText(a, alphaUnit)})";
	}




	// CIE LCH。明度(0–100)・彩度・色相(度)を CIELAB 基準で書き出す。明度・彩度は小数2桁、色相は整数にする。
	public static string Lch(byte r, byte g, byte b)
	{
		(double l, double c, double h) = LchColor.FromRgb(LchSpace.CieLch, r, g, b);
		return $"lch({FormatNumber(l, "0.##")} {FormatNumber(c, "0.##")} {Degrees(h)})";
	}




	// CIE LCH に不透明度を添える。空白区切りのみで、不透明度をスラッシュの後ろに置く。
	public static string Lcha(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		(double l, double c, double h) = LchColor.FromRgb(LchSpace.CieLch, r, g, b);
		return $"lch({FormatNumber(l, "0.##")} {FormatNumber(c, "0.##")} {Degrees(h)} / {AlphaText(a, alphaUnit)})";
	}




	// OKLCH。明度(0–100% 表記)・彩度(小数3桁)・色相(度)を OKLab 基準で書き出す。
	public static string Oklch(byte r, byte g, byte b)
	{
		(double l, double c, double h) = LchColor.FromRgb(LchSpace.Oklch, r, g, b);
		return $"oklch({FormatNumber(l * 100.0, "0.#")}% {FormatNumber(c, "0.###")} {Degrees(h)})";
	}




	// OKLCH に不透明度を添える。空白区切りのみで、不透明度をスラッシュの後ろに置く。
	public static string Oklcha(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		(double l, double c, double h) = LchColor.FromRgb(LchSpace.Oklch, r, g, b);
		return $"oklch({FormatNumber(l * 100.0, "0.#")}% {FormatNumber(c, "0.###")} {Degrees(h)} / {AlphaText(a, alphaUnit)})";
	}




	// CIE Lab。明度(0–100)と a・b 軸を CIELAB 基準で書き出す。いずれも小数2桁にする。
	public static string Lab(byte r, byte g, byte b)
	{
		(double l, double aAxis, double bAxis) = LabColor.FromRgb(LchSpace.CieLch, r, g, b);
		return $"lab({FormatNumber(l, "0.##")} {FormatNumber(aAxis, "0.##")} {FormatNumber(bAxis, "0.##")})";
	}




	// CIE Lab に不透明度を添える。空白区切りのみで、不透明度をスラッシュの後ろに置く。
	public static string Laba(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		(double l, double aAxis, double bAxis) = LabColor.FromRgb(LchSpace.CieLch, r, g, b);
		return $"lab({FormatNumber(l, "0.##")} {FormatNumber(aAxis, "0.##")} {FormatNumber(bAxis, "0.##")} / {AlphaText(a, alphaUnit)})";
	}




	// OKLab。明度(0–100% 表記)と a・b 軸(小数3桁)を OKLab 基準で書き出す。
	public static string Oklab(byte r, byte g, byte b)
	{
		(double l, double aAxis, double bAxis) = LabColor.FromRgb(LchSpace.Oklch, r, g, b);
		return $"oklab({FormatNumber(l * 100.0, "0.#")}% {FormatNumber(aAxis, "0.###")} {FormatNumber(bAxis, "0.###")})";
	}




	// OKLab に不透明度を添える。空白区切りのみで、不透明度をスラッシュの後ろに置く。
	public static string Oklaba(byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		(double l, double aAxis, double bAxis) = LabColor.FromRgb(LchSpace.Oklch, r, g, b);
		return $"oklab({FormatNumber(l * 100.0, "0.#")}% {FormatNumber(aAxis, "0.###")} {FormatNumber(bAxis, "0.###")} / {AlphaText(a, alphaUnit)})";
	}




	public static string PackedRgb(byte r, byte g, byte b)
	{
		return $"0x{r:X2}{g:X2}{b:X2}";
	}




	public static string PackedArgb(byte a, byte r, byte g, byte b)
	{
		return $"0x{a:X2}{r:X2}{g:X2}{b:X2}";
	}




	// 色が CSS の名前付きカラーと完全一致すればその名前を返す。複数の綴りが同じ色を指す場合は WebNamedColors に先に現れる名前を返す。
	public static bool TryNamedColor(byte r, byte g, byte b, out string name)
	{
		if (NamedByRgb.TryGetValue((r << 16) | (g << 8) | b, out string? found))
		{
			name = found;
			return true;
		}

		name = "";
		return false;
	}




	// 色相 (度) を 0–359 の整数にする。丸めで 360 になった場合は 0 に畳む。
	private static int Degrees(double hue)
	{
		return (int)Math.Round(hue) % 360;
	}




	// 0–1 の割合をパーセントの整数にする。
	private static int Percent(double fraction)
	{
		return (int)Math.Round(fraction * 100.0);
	}




	// 実数を指定の書式で、文化圏に依らず点を小数点として整える。末尾の余分な 0 は書式側で落とす。0 へ丸まる微小な負値が "-0" にならないよう正の 0 へ畳む。LCH の明度・彩度、Lab の各成分の表記に使う。
	private static string FormatNumber(double value, string format)
	{
		string text = value.ToString(format, CultureInfo.InvariantCulture);
		return text == "-0" ? "0" : text;
	}




	// 不透明度 (0–255) を CSS 関数のアルファ表記にする。Number は 0–1、Percent は 0–100% にし、いずれも末尾の余分な 0 を落とす。不透明は "1" または "100%" になる。
	private static string AlphaText(byte a, WebAlphaUnit unit)
	{
		if (unit == WebAlphaUnit.Percent)
		{
			return (a / 255.0 * 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%";
		}

		return (a / 255.0).ToString("0.##", CultureInfo.InvariantCulture);
	}




	private static Dictionary<int, string> BuildNamedByRgb()
	{
		var map = new Dictionary<int, string>();

		foreach (NamedColor entry in WebNamedColors.Palette.Colors)
		{
			int key = (entry.Color.R << 16) | (entry.Color.G << 8) | entry.Color.B;

			if (!map.ContainsKey(key))
			{
				map[key] = entry.Name;
			}
		}

		return map;
	}
}
