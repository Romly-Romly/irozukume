// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Globalization;
using Windows.Globalization.NumberFormatting;

namespace Irozukume.Helpers;

// NumberBox 用に、0–255 の数値を16進2桁(00–FF, 大文字)で見せ、16進文字列を数値へ戻すフォーマッタ兼パーサ。値そのものは 0–255 のまま扱い、見せ方と入力解釈だけを16進にする。app の他の16進表記(#RRGGBB)に合わせて大文字2桁で揃える。NumberBox は NumberFormatter で値を整形し、同じオブジェクトが INumberParser を兼ねていれば入力解釈にそれを使うため、両方を実装する。
public sealed class HexByteNumberFormatter : INumberFormatter2, INumberParser
{
	public string FormatInt(long value)
	{
		return Format(value);
	}




	public string FormatUInt(ulong value)
	{
		return Format(value);
	}




	public string FormatDouble(double value)
	{
		return Format(value);
	}




	public long? ParseInt(string text)
	{
		return Parse(text) is double value ? (long?)value : null;
	}




	public ulong? ParseUInt(string text)
	{
		return Parse(text) is double value ? (ulong?)value : null;
	}




	public double? ParseDouble(string text)
	{
		return Parse(text);
	}




	// 0–255 の値を大文字2桁の16進にする。範囲外は端へ丸め、小数は四捨五入する。
	private static string Format(double value)
	{
		int v = (int)Math.Clamp(Math.Round(value), 0.0, 255.0);
		return v.ToString("X2", CultureInfo.InvariantCulture);
	}




	// 16進文字列を 0–255 の数値へ解釈する。先頭の "#" や "0x" は許し、解釈できなければ null を返す。範囲外は端へ丸める。
	private static double? Parse(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		string t = text.Trim();

		if (t.StartsWith("#", StringComparison.Ordinal))
		{
			t = t.Substring(1);
		}
		else if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			t = t.Substring(2);
		}

		if (int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
		{
			return Math.Clamp(value, 0, 255);
		}

		return null;
	}
}
