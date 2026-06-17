// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;

namespace Irozukume.Helpers;

// 文字列を自然順(natural order)で比較する。数字の並びは数値として、それ以外は文字コード(序数)で比べるため、"2" が "10" より前に並ぶ。番号始まりの名前(ターミナルパレットの "0 Black"・"196" 等)をインデックス順に並べるのに使う。数字塊は先頭ゼロを落としてから桁数→文字コードの順で比べ、整数変換を介さず桁数無制限でも正しく順序付ける。数字を含まない名前(CSS の色名等)では各文字を序数で比べるだけになり、StringComparer.Ordinal と同じ並びになる。
public sealed class NaturalStringComparer : IComparer<string>
{
	// 共有インスタンス。状態を持たないため一つで足りる。
	public static NaturalStringComparer Ordinal { get; } = new NaturalStringComparer();




	public int Compare(string? x, string? y)
	{
		if (x is null)
		{
			return y is null ? 0 : -1;
		}

		if (y is null)
		{
			return 1;
		}

		int i = 0;
		int j = 0;

		while (i < x.Length && j < y.Length)
		{
			char cx = x[i];
			char cy = y[j];

			if (IsDigit(cx) && IsDigit(cy))
			{
				int byNumber = CompareNumberRun(x, ref i, y, ref j);

				if (byNumber != 0)
				{
					return byNumber;
				}
			}
			else if (cx != cy)
			{
				return cx < cy ? -1 : 1;
			}
			else
			{
				i++;
				j++;
			}
		}

		// ここまで等しく、片方が尽きたら短い方を小さいとする。
		return (x.Length - i) - (y.Length - j);
	}




	// 両方の現在位置から続く数字の塊を取り出し、数値として比べる。先頭ゼロを落とし、桁数の多い方を大きいとし、同桁なら文字コードで比べる。比べ終えた位置まで i・j を進める。
	private static int CompareNumberRun(string x, ref int i, string y, ref int j)
	{
		int startX = i;
		int startY = j;

		while (i < x.Length && IsDigit(x[i]))
		{
			i++;
		}

		while (j < y.Length && IsDigit(y[j]))
		{
			j++;
		}

		// 先頭ゼロを読み飛ばした、実質の桁の開始位置。塊がすべてゼロでも最後の1桁は残す。
		int sx = startX;

		while (sx < i - 1 && x[sx] == '0')
		{
			sx++;
		}

		int sy = startY;

		while (sy < j - 1 && y[sy] == '0')
		{
			sy++;
		}

		int lengthX = i - sx;
		int lengthY = j - sy;

		if (lengthX != lengthY)
		{
			return lengthX < lengthY ? -1 : 1;
		}

		for (int k = 0; k < lengthX; k++)
		{
			char dx = x[sx + k];
			char dy = y[sy + k];

			if (dx != dy)
			{
				return dx < dy ? -1 : 1;
			}
		}

		return 0;
	}




	private static bool IsDigit(char c)
	{
		return c >= '0' && c <= '9';
	}
}
