// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.UI;
using Irozukume.Models;

namespace Irozukume.Helpers;

// 解釈に成功した色。R・G・B (0–255) と不透明度 A (0–255)、その不透明度が元の文字列に明示されていたか (HasAlpha)、元の書式 (Format) を持つ。HasAlpha が偽のとき A は 255 とし、貼り付け側が不透明度に触れない判断に使う。Format は貼り付け側が書式に合うタブへ切り替える判断に使う。
public readonly struct ParsedColor
{
	public ParsedColor(byte r, byte g, byte b, byte a, bool hasAlpha, ColorSourceFormat format)
	{
		R = r;
		G = g;
		B = b;
		A = a;
		HasAlpha = hasAlpha;
		Format = format;
		Hue = 0.0;
		Whiteness = 0.0;
		Blackness = 0.0;
		LchSpace = LchSpace.Oklch;
		LchL = 0.0;
		LchC = 0.0;
		LchH = 0.0;
		LabL = 0.0;
		LabA = 0.0;
		LabB = 0.0;
	}




	// hwb() を解釈した結果。RGB に加え、解釈した色相(度)と白み・黒み(各 0–1)を併せ持つ。退化域(白み+黒み>1)では RGB だけだと灰へ潰れて元の白み・黒みが分からないため、貼り付け側が正規化オフのときに貼った値をそのまま復元できるよう持たせる。
	public ParsedColor(byte r, byte g, byte b, byte a, bool hasAlpha, double hue, double whiteness, double blackness)
	{
		R = r;
		G = g;
		B = b;
		A = a;
		HasAlpha = hasAlpha;
		Format = ColorSourceFormat.Hwb;
		Hue = hue;
		Whiteness = whiteness;
		Blackness = blackness;
		LchSpace = LchSpace.Oklch;
		LchL = 0.0;
		LchC = 0.0;
		LchH = 0.0;
		LabL = 0.0;
		LabA = 0.0;
		LabB = 0.0;
	}




	// lch()/oklch()・lab()/oklab() を解釈した結果。RGB に加え、解釈した表色系と各成分を併せ持つ。色域外や無彩色では RGB だけだと元の成分が分からないため、貼り付け側が貼った値をそのまま復元できるよう持たせる。各成分は当該表色系の素の尺度で持ち、書式が lch()/oklch() なら明度・彩度・色相(度)として LchL・LchC・LchH へ、lab()/oklab() なら明度・a 軸・b 軸として LabL・LabA・LabB へ入る。表色系は Lab 平面を共有する LchSpace で表す。
	public ParsedColor(byte r, byte g, byte b, byte a, bool hasAlpha, ColorSourceFormat format, LchSpace space, double l, double secondComponent, double thirdComponent)
	{
		R = r;
		G = g;
		B = b;
		A = a;
		HasAlpha = hasAlpha;
		Format = format;
		Hue = 0.0;
		Whiteness = 0.0;
		Blackness = 0.0;
		LchSpace = space;

		bool isLab = format == ColorSourceFormat.Lab || format == ColorSourceFormat.Oklab;
		LchL = isLab ? 0.0 : l;
		LchC = isLab ? 0.0 : secondComponent;
		LchH = isLab ? 0.0 : thirdComponent;
		LabL = isLab ? l : 0.0;
		LabA = isLab ? secondComponent : 0.0;
		LabB = isLab ? thirdComponent : 0.0;
	}




	public byte R { get; }




	public byte G { get; }




	public byte B { get; }




	public byte A { get; }




	public bool HasAlpha { get; }




	// 解釈した色の元の書式。貼り付け側が書式に合うタブ(と HSV/HSL の副モード)へ切り替える判断に使う。
	public ColorSourceFormat Format { get; }




	// hwb() 由来の貼り付けか。真のとき Hue・Whiteness・Blackness に解釈した色相と白み・黒みを持つ。
	public bool IsHwb => Format == ColorSourceFormat.Hwb;




	public double Hue { get; }




	public double Whiteness { get; }




	public double Blackness { get; }




	// lch()/oklch() 由来の貼り付けか。真のとき LchSpace・LchL・LchC・LchH に解釈した表色系と明度・彩度・色相を持つ。
	public bool IsLch => Format == ColorSourceFormat.Lch || Format == ColorSourceFormat.Oklch;




	public LchSpace LchSpace { get; }




	public double LchL { get; }




	public double LchC { get; }




	public double LchH { get; }




	// lab()/oklab() 由来の貼り付けか。真のとき LchSpace・LabL・LabA・LabB に解釈した表色系と明度・a 軸・b 軸を持つ。
	public bool IsLab => Format == ColorSourceFormat.Lab || Format == ColorSourceFormat.Oklab;




	public double LabL { get; }




	public double LabA { get; }




	public double LabB { get; }
}




// クリップボードなどの文字列を色として解釈する。対応するのは #RGB・#RGBA・#RRGGBB・#RRGGBBAA の16進、0xRRGGBB・0xAARRGGBB の 0x パック値 (コピーの 0x 形式と対になり、8桁は先頭2桁を不透明度とする ARGB 並びのため CSS の #RRGGBBAA と不透明度の位置が異なる)、rgb()/rgba()、hsl()/hsla()、hwb()、lch()/oklch()、lab()/oklab()、Web の名前付きカラー (と transparent)。いずれにも当てはまらない文字列は解釈失敗として false を返し、呼び出し側は何もしない。大文字小文字・前後の空白・区切りの空白は問わず、CSS 宣言から値をコピーした際に末尾へ付くセミコロンも無視する。値が範囲を超えるときは端へ丸め、各成分の数値とパーセントは独立に解釈する。HSV・color() など未対応の関数や、# も 0x も無い裸の16進は受け付けない。
public static class ColorStringParser
{
	// 名前付きカラーの引き表。大文字小文字を区別せず色名から色を引く。WebNamedColors の一覧から一度だけ作って使い回す。
	private static readonly Dictionary<string, Color> NamedColors = BuildNamedColors();




	// 文字列を色として解釈する。成功すれば true を返し color に結果を入れる。解釈できなければ false を返し、color は既定値のままにする。
	public static bool TryParse(string? text, out ParsedColor color)
	{
		color = default;

		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		// CSS の宣言から値をコピーすると末尾にセミコロンが付くことがある。関数形式は末尾が ")" であることを前提に解釈するため、末尾のセミコロンと空白を先に落とす。
		string s = text.Trim().TrimEnd(';', ' ', '\t');

		if (s.StartsWith('#'))
		{
			return TryParseHex(s, out color);
		}

		if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return TryParsePacked(s, out color);
		}

		int open = s.IndexOf('(');

		if (open > 0 && s.EndsWith(')'))
		{
			string name = s.Substring(0, open).Trim().ToLowerInvariant();
			string inner = s.Substring(open + 1, s.Length - open - 2);

			return name switch
			{
				"rgb" or "rgba" => TryParseRgbFunction(inner, out color),
				"hsl" or "hsla" => TryParseHslFunction(inner, out color),
				"hwb" => TryParseHwbFunction(inner, out color),
				"lch" => TryParseLchFunction(LchSpace.CieLch, inner, out color),
				"oklch" => TryParseLchFunction(LchSpace.Oklch, inner, out color),
				"lab" => TryParseLabFunction(LchSpace.CieLch, inner, out color),
				"oklab" => TryParseLabFunction(LchSpace.Oklch, inner, out color),
				_ => false,
			};
		}

		return TryParseNamed(s, out color);
	}




	// #RGB・#RGBA・#RRGGBB・#RRGGBBAA を解釈する。3桁・4桁は各桁を2回繰り返して8ビットへ広げ、4桁・8桁は末尾を不透明度として扱う。
	private static bool TryParseHex(string s, out ParsedColor color)
	{
		color = default;
		string h = s.Substring(1);

		if (!IsAllHexDigits(h))
		{
			return false;
		}

		switch (h.Length)
		{
			case 3:
				color = new ParsedColor(Expand(h[0]), Expand(h[1]), Expand(h[2]), 255, false, ColorSourceFormat.Hex);
				return true;

			case 4:
				color = new ParsedColor(Expand(h[0]), Expand(h[1]), Expand(h[2]), Expand(h[3]), true, ColorSourceFormat.Hex);
				return true;

			case 6:
				color = new ParsedColor(Pair(h, 0), Pair(h, 2), Pair(h, 4), 255, false, ColorSourceFormat.Hex);
				return true;

			case 8:
				color = new ParsedColor(Pair(h, 0), Pair(h, 2), Pair(h, 4), Pair(h, 6), true, ColorSourceFormat.Hex);
				return true;

			default:
				return false;
		}
	}




	// 0xRRGGBB・0xAARRGGBB を解釈する。コピーの 0x パック値と対になる。6桁は赤・緑・青、8桁は先頭2桁を不透明度とする ARGB 並びで、CSS の16進(#RRGGBBAA)とは不透明度の位置が異なる。書式は #RGB と同じく RGB の16進表記のため Hex として扱う。
	private static bool TryParsePacked(string s, out ParsedColor color)
	{
		color = default;
		string h = s.Substring(2);

		if (!IsAllHexDigits(h))
		{
			return false;
		}

		switch (h.Length)
		{
			case 6:
				color = new ParsedColor(Pair(h, 0), Pair(h, 2), Pair(h, 4), 255, false, ColorSourceFormat.Hex);
				return true;

			case 8:
				color = new ParsedColor(Pair(h, 2), Pair(h, 4), Pair(h, 6), Pair(h, 0), true, ColorSourceFormat.Hex);
				return true;

			default:
				return false;
		}
	}




	// rgb()/rgba() の中身を解釈する。3成分は赤・緑・青で、4つ目 (カンマ区切り) または "/" の後ろが不透明度。
	private static bool TryParseRgbFunction(string inner, out ParsedColor color)
	{
		color = default;

		if (!SplitComponents(inner, out string[] comps, out string? alphaToken))
		{
			return false;
		}

		if (!TryParseColorComponent(comps[0], out byte r)
			|| !TryParseColorComponent(comps[1], out byte g)
			|| !TryParseColorComponent(comps[2], out byte b))
		{
			return false;
		}

		if (!TryResolveAlpha(alphaToken, out byte a, out bool hasAlpha))
		{
			return false;
		}

		color = new ParsedColor(r, g, b, a, hasAlpha, ColorSourceFormat.Rgb);
		return true;
	}




	// hsl()/hsla() の中身を解釈する。色相・彩度・輝度から RGB を作り、4つ目または "/" の後ろが不透明度。
	private static bool TryParseHslFunction(string inner, out ParsedColor color)
	{
		color = default;

		if (!SplitComponents(inner, out string[] comps, out string? alphaToken))
		{
			return false;
		}

		if (!TryParseHue(comps[0], out double hue)
			|| !TryParsePercent(comps[1], out double saturation)
			|| !TryParsePercent(comps[2], out double lightness))
		{
			return false;
		}

		if (!TryResolveAlpha(alphaToken, out byte a, out bool hasAlpha))
		{
			return false;
		}

		(byte r, byte g, byte b) = ColorConversion.HslToRgb(hue, saturation, lightness);
		color = new ParsedColor(r, g, b, a, hasAlpha, ColorSourceFormat.Hsl);
		return true;
	}




	// hwb() の中身を解釈する。色相・白み・黒みから RGB を作り、"/" の後ろが不透明度。
	private static bool TryParseHwbFunction(string inner, out ParsedColor color)
	{
		color = default;

		if (!SplitComponents(inner, out string[] comps, out string? alphaToken))
		{
			return false;
		}

		if (!TryParseHue(comps[0], out double hue)
			|| !TryParsePercent(comps[1], out double whiteness)
			|| !TryParsePercent(comps[2], out double blackness))
		{
			return false;
		}

		if (!TryResolveAlpha(alphaToken, out byte a, out bool hasAlpha))
		{
			return false;
		}

		(byte r, byte g, byte b) = HwbToRgb(hue, whiteness, blackness);
		color = new ParsedColor(r, g, b, a, hasAlpha, hue, whiteness, blackness);
		return true;
	}




	// lch()/oklch() の中身を解釈する。明度・彩度・色相から RGB を作り、"/" の後ろが不透明度。色域外の組み合わせは明度・色相を保ったまま彩度を詰めて色域内へ収める。明度・彩度・色相は表色系の素の尺度で持たせ、貼り付け側が貼った値をそのまま復元できるようにする。
	private static bool TryParseLchFunction(LchSpace space, string inner, out ParsedColor color)
	{
		color = default;

		if (!SplitComponents(inner, out string[] comps, out string? alphaToken))
		{
			return false;
		}

		if (!TryParseLchLightness(comps[0], space, out double l)
			|| !TryParseLchChroma(comps[1], space, out double c)
			|| !TryParseHue(comps[2], out double hue))
		{
			return false;
		}

		if (!TryResolveAlpha(alphaToken, out byte a, out bool hasAlpha))
		{
			return false;
		}

		double normalized = ((hue % 360.0) + 360.0) % 360.0;
		Color rgb = LchColor.ToRgb(space, l, c, normalized);
		ColorSourceFormat format = space == LchSpace.Oklch ? ColorSourceFormat.Oklch : ColorSourceFormat.Lch;
		color = new ParsedColor(rgb.R, rgb.G, rgb.B, a, hasAlpha, format, space, l, c, normalized);
		return true;
	}




	// lab()/oklab() の中身を解釈する。明度・a 軸・b 軸から RGB を作り、"/" の後ろが不透明度。色域外の組み合わせは明度・色相(a:b の比)を保ったまま彩度を詰めて色域内へ収める。明度・a・b は表色系の素の尺度で持たせ、貼り付け側が貼った値をそのまま復元できるようにする。
	private static bool TryParseLabFunction(LchSpace space, string inner, out ParsedColor color)
	{
		color = default;

		if (!SplitComponents(inner, out string[] comps, out string? alphaToken))
		{
			return false;
		}

		if (!TryParseLchLightness(comps[0], space, out double l)
			|| !TryParseLabAxis(comps[1], space, out double aAxis)
			|| !TryParseLabAxis(comps[2], space, out double bAxis))
		{
			return false;
		}

		if (!TryResolveAlpha(alphaToken, out byte a, out bool hasAlpha))
		{
			return false;
		}

		Color rgb = LabColor.ToRgb(space, l, aAxis, bAxis);
		ColorSourceFormat format = space == LchSpace.Oklch ? ColorSourceFormat.Oklab : ColorSourceFormat.Lab;
		color = new ParsedColor(rgb.R, rgb.G, rgb.B, a, hasAlpha, format, space, l, aAxis, bAxis);
		return true;
	}




	// Lab の a・b 軸の1成分を表色系の素の尺度へ解釈する。パーセントは ±100% を表示上限(OKLab は ±0.4、CIE Lab は ±125)へ写し、裸の数値は素の尺度そのものとして読む。none は 0 とする。色域外の大きな値はそのまま通し、RGB 化のときに色域内へ収める。
	private static bool TryParseLabAxis(string token, LchSpace space, out double value)
	{
		value = 0.0;
		string t = token.Trim();

		if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (t.EndsWith('%'))
		{
			if (!TryParseNumber(t.Substring(0, t.Length - 1), out double percent))
			{
				return false;
			}

			value = percent / 100.0 * LabColor.AbMax(space);
			return true;
		}

		if (!TryParseNumber(t, out double number))
		{
			return false;
		}

		value = number;
		return true;
	}




	// LCH の明度を表色系の素の尺度へ解釈する。lab()/oklab() の明度も同じ尺度のため共用する。パーセントは 0–100% を上限(OKLCH は 1、CIE LCH は 100)へ写し、裸の数値は素の尺度そのものとして読む。none は 0、範囲外は端へ丸める。
	private static bool TryParseLchLightness(string token, LchSpace space, out double lightness)
	{
		lightness = 0.0;
		string t = token.Trim();

		if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		double max = LchColor.LMax(space);

		if (t.EndsWith('%'))
		{
			if (!TryParseNumber(t.Substring(0, t.Length - 1), out double percent))
			{
				return false;
			}

			lightness = Math.Clamp(percent, 0.0, 100.0) / 100.0 * max;
			return true;
		}

		if (!TryParseNumber(t, out double number))
		{
			return false;
		}

		lightness = Math.Clamp(number, 0.0, max);
		return true;
	}




	// LCH の彩度を表色系の素の尺度へ解釈する。パーセントは 0–100% を表示上限(OKLCH は 0.4、CIE LCH は 150)へ写し、裸の数値は素の尺度そのものとして読む。none は 0、負値は 0 へ丸める。色域外の大きな彩度はそのまま通し、RGB 化のときに色域内へ収める。
	private static bool TryParseLchChroma(string token, LchSpace space, out double chroma)
	{
		chroma = 0.0;
		string t = token.Trim();

		if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (t.EndsWith('%'))
		{
			if (!TryParseNumber(t.Substring(0, t.Length - 1), out double percent))
			{
				return false;
			}

			chroma = Math.Max(0.0, percent) / 100.0 * LchColor.CMax(space);
			return true;
		}

		if (!TryParseNumber(t, out double number))
		{
			return false;
		}

		chroma = Math.Max(0.0, number);
		return true;
	}




	// 名前付きカラー (と transparent) を解釈する。大文字小文字は問わない。
	private static bool TryParseNamed(string s, out ParsedColor color)
	{
		color = default;

		if (s.Equals("transparent", StringComparison.OrdinalIgnoreCase))
		{
			color = new ParsedColor(0, 0, 0, 0, true, ColorSourceFormat.Transparent);
			return true;
		}

		if (NamedColors.TryGetValue(s, out Color named))
		{
			color = new ParsedColor(named.R, named.G, named.B, 255, false, ColorSourceFormat.Named);
			return true;
		}

		return false;
	}




	// 関数の中身を3つの成分と省略可能な不透明度トークンへ分ける。カンマ区切りなら3個 (不透明度なし) か4個 (4つ目が不透明度)。空白区切りなら3個で、"/" があればその後ろを不透明度とする。成分が3つに揃わなければ失敗とする。
	private static bool SplitComponents(string inner, out string[] comps, out string? alphaToken)
	{
		comps = Array.Empty<string>();
		alphaToken = null;

		string body = inner.Trim();

		if (body.Length == 0)
		{
			return false;
		}

		if (body.Contains(','))
		{
			string[] parts = body.Split(',');

			for (int i = 0; i < parts.Length; i++)
			{
				parts[i] = parts[i].Trim();
			}

			if (parts.Length == 3)
			{
				comps = parts;
				return AllNonEmpty(comps);
			}

			if (parts.Length == 4)
			{
				comps = new[] { parts[0], parts[1], parts[2] };
				alphaToken = parts[3];
				return AllNonEmpty(comps) && alphaToken.Length > 0;
			}

			return false;
		}

		string main = body;

		if (body.Contains('/'))
		{
			string[] split = body.Split('/');

			if (split.Length != 2)
			{
				return false;
			}

			main = split[0].Trim();
			alphaToken = split[1].Trim();

			if (alphaToken.Length == 0)
			{
				return false;
			}
		}

		string[] tokens = main.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

		if (tokens.Length != 3)
		{
			return false;
		}

		comps = tokens;
		return true;
	}




	// 不透明度トークンを 0–255 へ解釈する。トークンが無ければ不透明 (255) で HasAlpha は偽。あれば値を解釈して HasAlpha を真にする。解釈できなければ失敗とする。
	private static bool TryResolveAlpha(string? token, out byte a, out bool hasAlpha)
	{
		if (token is null)
		{
			a = 255;
			hasAlpha = false;
			return true;
		}

		hasAlpha = true;
		return TryParseAlpha(token, out a);
	}




	// 赤・緑・青の1成分を 0–255 へ解釈する。数値はそのまま、パーセントは 0–255 へ、none は 0 とし、範囲外は端へ丸める。
	private static bool TryParseColorComponent(string token, out byte value)
	{
		value = 0;
		string t = token.Trim();

		if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (t.EndsWith('%'))
		{
			if (!TryParseNumber(t.Substring(0, t.Length - 1), out double percent))
			{
				return false;
			}

			value = (byte)Math.Round(Math.Clamp(percent / 100.0 * 255.0, 0.0, 255.0));
			return true;
		}

		if (!TryParseNumber(t, out double number))
		{
			return false;
		}

		value = (byte)Math.Round(Math.Clamp(number, 0.0, 255.0));
		return true;
	}




	// 不透明度の1成分を 0–255 へ解釈する。数値は 0–1、パーセントは 0–100% として扱い、none は 0、範囲外は端へ丸める。
	private static bool TryParseAlpha(string token, out byte value)
	{
		value = 255;
		string t = token.Trim();

		if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			value = 0;
			return true;
		}

		if (t.EndsWith('%'))
		{
			if (!TryParseNumber(t.Substring(0, t.Length - 1), out double percent))
			{
				return false;
			}

			value = (byte)Math.Round(Math.Clamp(percent / 100.0, 0.0, 1.0) * 255.0);
			return true;
		}

		if (!TryParseNumber(t, out double number))
		{
			return false;
		}

		value = (byte)Math.Round(Math.Clamp(number, 0.0, 1.0) * 255.0);
		return true;
	}




	// 色相を度へ解釈する。単位なし・deg はそのまま、grad・rad・turn は度へ換算する。none は 0 度とする。
	private static bool TryParseHue(string token, out double degrees)
	{
		degrees = 0.0;
		string t = token.Trim().ToLowerInvariant();

		if (t == "none")
		{
			return true;
		}

		double multiplier = 1.0;
		string number = t;

		if (t.EndsWith("deg"))
		{
			number = t.Substring(0, t.Length - 3);
		}
		else if (t.EndsWith("grad"))
		{
			number = t.Substring(0, t.Length - 4);
			multiplier = 360.0 / 400.0;
		}
		else if (t.EndsWith("rad"))
		{
			number = t.Substring(0, t.Length - 3);
			multiplier = 180.0 / Math.PI;
		}
		else if (t.EndsWith("turn"))
		{
			number = t.Substring(0, t.Length - 4);
			multiplier = 360.0;
		}

		if (!TryParseNumber(number, out double value))
		{
			return false;
		}

		degrees = value * multiplier;
		return true;
	}




	// 彩度・輝度・白み・黒みを 0–1 へ解釈する。パーセントでも裸の数値でもよく、none は 0、範囲外は端へ丸める。
	private static bool TryParsePercent(string token, out double fraction)
	{
		fraction = 0.0;
		string t = token.Trim();

		if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		string number = t.EndsWith('%') ? t.Substring(0, t.Length - 1) : t;

		if (!TryParseNumber(number, out double value))
		{
			return false;
		}

		fraction = Math.Clamp(value / 100.0, 0.0, 1.0);
		return true;
	}




	// 文字列を実数へ解釈する。小数・符号・指数を許し、文化圏に依らず点を小数点とする。空文字や数値以外、NaN・無限大は失敗とする。色の各成分へ非有限の値を流さず、解釈できない文字列を確実に弾く。
	private static bool TryParseNumber(string s, out double value)
	{
		if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
		{
			return true;
		}

		value = 0.0;
		return false;
	}




	// 色相・白み・黒みから RGB を作る。白みと黒みの和が1以上のときは灰色になり、その明るさは白みと黒みの比で決まる。それ以外は純色を白みと黒みで内側へ縮める。
	private static (byte R, byte G, byte B) HwbToRgb(double hue, double whiteness, double blackness)
	{
		if (whiteness + blackness >= 1.0)
		{
			byte gray = (byte)Math.Round(whiteness / (whiteness + blackness) * 255.0);
			return (gray, gray, gray);
		}

		(byte pr, byte pg, byte pb) = ColorConversion.HslToRgb(hue, 1.0, 0.5);
		double scale = 1.0 - whiteness - blackness;
		byte r = (byte)Math.Round(((pr / 255.0 * scale) + whiteness) * 255.0);
		byte g = (byte)Math.Round(((pg / 255.0 * scale) + whiteness) * 255.0);
		byte b = (byte)Math.Round(((pb / 255.0 * scale) + whiteness) * 255.0);
		return (r, g, b);
	}




	// 1桁の16進文字を、2回繰り返した8ビット値へ広げる。f なら 0xFF、0 なら 0x00。
	private static byte Expand(char c)
	{
		int v = HexValue(c);
		return (byte)((v * 16) + v);
	}




	// 文字列の指定位置から2桁の16進を8ビット値へ読む。
	private static byte Pair(string s, int index)
	{
		return (byte)((HexValue(s[index]) * 16) + HexValue(s[index + 1]));
	}




	// 1桁の16進文字を 0–15 の値へ変換する。呼び出し前に16進数字であることを確かめておく。
	private static int HexValue(char c)
	{
		if (c >= '0' && c <= '9')
		{
			return c - '0';
		}

		if (c >= 'a' && c <= 'f')
		{
			return c - 'a' + 10;
		}

		return c - 'A' + 10;
	}




	// 文字列がすべて16進数字 (0–9・a–f・A–F) で、かつ空でないか。
	private static bool IsAllHexDigits(string s)
	{
		if (s.Length == 0)
		{
			return false;
		}

		foreach (char c in s)
		{
			bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

			if (!isHex)
			{
				return false;
			}
		}

		return true;
	}




	// 配列の各要素が空でないか。
	private static bool AllNonEmpty(string[] tokens)
	{
		foreach (string token in tokens)
		{
			if (token.Length == 0)
			{
				return false;
			}
		}

		return true;
	}




	// WebNamedColors の一覧から、色名を引いて色を返す引き表を作る。大文字小文字は区別しない。
	private static Dictionary<string, Color> BuildNamedColors()
	{
		var map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

		foreach (NamedColor entry in WebNamedColors.Palette.Colors)
		{
			map[entry.Name] = entry.Color;
		}

		return map;
	}
}
