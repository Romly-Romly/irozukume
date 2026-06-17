// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Irozukume.Models;

namespace Irozukume.Helpers;

// ターミナル(ANSI)の色パレットを内部に持ち、色とインデックスの相互変換、および最も近いインデックスの探索を提供する。256色は「基本16色(0-15) + 6×6×6 キューブ(16-231) + 24段グレー(232-255)」、16色は 0-15、8色は 0-7。基本16色は端末の配色テーマで実RGBが変わるため、主要ターミナルの既知の配色をプリセット(TerminalTheme)として持ち、参照する。キューブとグレーは xterm 標準で固定。
public static class TerminalPalette
{
	// 6×6×6 キューブの各チャンネルが取る6段。xterm 標準の非線形の刻み。
	private static readonly byte[] CubeLevels = { 0, 95, 135, 175, 215, 255 };

	// テーマ別の基本16色(0-15)を 0xRRGGBB で持つ。並びは ANSI の 黒・赤・緑・黄・青・マゼンタ・シアン・白、続いて明色8色。端末の配色テーマでこの16色だけが変わる。

	// Windows Terminal 既定(Campbell)。
	private static readonly uint[] CampbellBase =
	{
		0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
		0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2,
	};

	// 標準 VGA テキストモードの16色。
	private static readonly uint[] VgaBase =
	{
		0x000000, 0xAA0000, 0x00AA00, 0xAA5500, 0x0000AA, 0xAA00AA, 0x00AAAA, 0xAAAAAA,
		0x555555, 0xFF5555, 0x55FF55, 0xFFFF55, 0x5555FF, 0xFF55FF, 0x55FFFF, 0xFFFFFF,
	};

	// xterm 既定の16色。
	private static readonly uint[] XtermBase =
	{
		0x000000, 0xCD0000, 0x00CD00, 0xCDCD00, 0x0000EE, 0xCD00CD, 0x00CDCD, 0xE5E5E5,
		0x7F7F7F, 0xFF0000, 0x00FF00, 0xFFFF00, 0x5C5CFF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
	};

	// 直近に Lab を前計算したテーマと、その256色ぶんの Lab。色制限の表示はパッドを画素ごとに塗るため、候補色の Lab を毎回求め直さず、テーマが変わるまで使い回す。表示は UI スレッドからの呼び出しのみで競合しない。
	private static (double L, double A, double B)[]? _labCache;
	private static TerminalTheme _labCacheTheme;
	private static bool _labCacheValid;




	// 基本16色(0-15)の1色を、指定テーマの参照値で返す。
	public static (byte R, byte G, byte B) BaseColor(TerminalTheme theme, int index)
	{
		uint[] table = theme switch
		{
			TerminalTheme.Vga => VgaBase,
			TerminalTheme.Xterm => XtermBase,
			_ => CampbellBase,
		};

		uint c = table[index & 0xF];
		return ((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
	}




	// 256色インデックス(0-255)を RGB にする。0-15 はテーマの参照値、16-231 はキューブ、232-255 はグレー段。範囲外は端へ丸める。
	public static (byte R, byte G, byte B) IndexToRgb(int index, TerminalTheme theme)
	{
		index = Math.Clamp(index, 0, 255);

		if (index < 16)
		{
			return BaseColor(theme, index);
		}

		if (index < 232)
		{
			int n = index - 16;
			return (CubeLevels[n / 36], CubeLevels[(n / 6) % 6], CubeLevels[n % 6]);
		}

		int gray = 8 + ((index - 232) * 10);
		return ((byte)gray, (byte)gray, (byte)gray);
	}




	// 色に最も近いパレットインデックスを返す。Term256 は「キューブの最寄りセル1色 + グレー24色 + 基本16色」、Term16 は 0-15、Term8 は 0-7 を候補に、指定の距離計算で最近傍を選ぶ。キューブの最寄りセルはチャンネル独立に直接求め、216色の総当たりを避ける。Lab のときは候補色の Lab を画素ごとに求め直さず、テーマ別の前計算表(LabFor)を引く。
	public static int NearestIndex(byte r, byte g, byte b, ColorLimitMode mode, SnapMetric metric, TerminalTheme theme)
	{
		(double L, double A, double B)[]? labCache = null;
		double sl = 0.0;
		double sa = 0.0;
		double sb = 0.0;

		if (metric == SnapMetric.Lab)
		{
			labCache = LabFor(theme);
			(sl, sa, sb) = ColorMetrics.RgbToLab(r, g, b);
		}

		int best = 0;
		double bestDist = double.MaxValue;

		if (mode == ColorLimitMode.Term256)
		{
			int cubeIndex = 16 + (36 * NearestCubeLevel(r)) + (6 * NearestCubeLevel(g)) + NearestCubeLevel(b);
			EvaluateCandidate(cubeIndex, theme, labCache, metric, sl, sa, sb, r, g, b, ref best, ref bestDist);

			for (int i = 232; i < 256; i++)
			{
				EvaluateCandidate(i, theme, labCache, metric, sl, sa, sb, r, g, b, ref best, ref bestDist);
			}

			for (int i = 0; i < 16; i++)
			{
				EvaluateCandidate(i, theme, labCache, metric, sl, sa, sb, r, g, b, ref best, ref bestDist);
			}
		}
		else
		{
			int count = mode == ColorLimitMode.Term8 ? 8 : 16;

			for (int i = 0; i < count; i++)
			{
				EvaluateCandidate(i, theme, labCache, metric, sl, sa, sb, r, g, b, ref best, ref bestDist);
			}
		}

		return best;
	}




	// 色を、指定のターミナルモード・距離計算・テーマで最も近いパレット色へ丸めた RGB を返す。ColorConversion.Snap のターミナル経路が使う。
	public static (byte R, byte G, byte B) SnapRgb(ColorLimitMode mode, SnapMetric metric, TerminalTheme theme, byte r, byte g, byte b)
	{
		int index = NearestIndex(r, g, b, mode, metric, theme);
		return IndexToRgb(index, theme);
	}




	// 16色・8色のインデックス(0-15)を前景の SGR 番号にする。0-7 は 30-37、明色 8-15 は 90-97。
	public static int ForegroundSgr(int index)
	{
		return index < 8 ? 30 + index : 90 + (index - 8);
	}




	// 16色・8色のインデックス(0-15)を背景の SGR 番号にする。0-7 は 40-47、明色 8-15 は 100-107。
	public static int BackgroundSgr(int index)
	{
		return index < 8 ? 40 + index : 100 + (index - 8);
	}




	// 候補インデックス1色との距離を測り、これまでの最小を下回れば best を更新する。labCache があれば(Lab のとき)あらかじめ求めた基準色の Lab と前計算した候補の Lab で測り、無ければ基準色のバイトから直接測る。
	private static void EvaluateCandidate(int index, TerminalTheme theme, (double L, double A, double B)[]? labCache, SnapMetric metric, double sl, double sa, double sb, byte r, byte g, byte b, ref int best, ref double bestDist)
	{
		double d;

		if (labCache is not null)
		{
			(double cl, double ca, double cb) = labCache[index];
			double dl = sl - cl;
			double da = sa - ca;
			double db = sb - cb;
			d = (dl * dl) + (da * da) + (db * db);
		}
		else
		{
			(byte cr, byte cg, byte cb) = IndexToRgb(index, theme);
			d = ColorMetrics.DistanceSquared(metric, r, g, b, cr, cg, cb);
		}

		if (d < bestDist)
		{
			bestDist = d;
			best = index;
		}
	}




	// 指定テーマの256色ぶんの Lab を返す。直近と同じテーマなら前計算した表をそのまま返し、違えば一度だけ作り直す。0-15 はテーマ依存、16-255 は固定だが、まとめて1つの表に持つ。
	private static (double L, double A, double B)[] LabFor(TerminalTheme theme)
	{
		if (_labCacheValid && _labCacheTheme == theme && _labCache is not null)
		{
			return _labCache;
		}

		var built = new (double L, double A, double B)[256];

		for (int i = 0; i < 256; i++)
		{
			(byte r, byte g, byte b) = IndexToRgb(i, theme);
			built[i] = ColorMetrics.RgbToLab(r, g, b);
		}

		_labCache = built;
		_labCacheTheme = theme;
		_labCacheValid = true;
		return built;
	}




	// 1チャンネル(0-255)に最も近いキューブの段の番号(0-5)を返す。
	private static int NearestCubeLevel(byte value)
	{
		int best = 0;
		int bestDist = int.MaxValue;

		for (int i = 0; i < CubeLevels.Length; i++)
		{
			int d = Math.Abs(value - CubeLevels[i]);

			if (d < bestDist)
			{
				bestDist = d;
				best = i;
			}
		}

		return best;
	}
}
