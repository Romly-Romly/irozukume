// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators;

/// <summary>
/// CbCrPlane・YuvLumaPlane が共有する、YCbCr 断面の色域外クランプと範囲判定の算術。
/// 色域外の下地色(無彩色から目標 RGB へ向かう半径方向で境界へ詰めた色)・0–255 の範囲判定・バイトへのクランプ・色制限の任意適用をまとめる。
/// </summary>
internal static class YuvGamutMath
{
	// 色域外の画素の下地色。クランプ色で塗る設定なら、無彩色(輝度の線形値)から目標の RGB へ向かう半径方向で色域境界へ詰めた色を色制限へ丸めて返す。白塗りの設定なら白を返す。
	internal static (byte R, byte G, byte B) FillColor(bool fillClamped, SnapSettings snap, double lumaLinear, double rr, double gg, double bb)
	{
		if (!fillClamped)
		{
			return (0xFF, 0xFF, 0xFF);
		}

		double t = 1.0;
		t = Math.Min(t, MaxScale(lumaLinear, rr));
		t = Math.Min(t, MaxScale(lumaLinear, gg));
		t = Math.Min(t, MaxScale(lumaLinear, bb));

		if (t < 0.0)
		{
			t = 0.0;
		}

		byte r = ClampByte(lumaLinear + (t * (rr - lumaLinear)));
		byte g = ClampByte(lumaLinear + (t * (gg - lumaLinear)));
		byte b = ClampByte(lumaLinear + (t * (bb - lumaLinear)));
		return SnapIf(snap, r, g, b);
	}




	// 無彩色での成分値 n(色域内)から目標値 v へ線形に進むとき、0–255 に収まる最大の縮小率(0–1 を上限)。v が範囲内なら 1 を、範囲外ならはみ出す手前の比率を返す。
	private static double MaxScale(double n, double v)
	{
		double delta = v - n;

		if (delta > 1e-6)
		{
			return (255.0 - n) / delta;
		}

		if (delta < -1e-6)
		{
			return -n / delta;
		}

		return 1.0;
	}




	// 色制限が有効なら指定の制限へ丸め、無効ならそのまま返す。
	internal static (byte R, byte G, byte B) SnapIf(SnapSettings snap, byte r, byte g, byte b)
	{
		if (snap.Mode == ColorLimitMode.None)
		{
			return (r, g, b);
		}

		return ColorConversion.Snap(snap, r, g, b);
	}




	// 数値誤差を見込んで 0–255 に収まるか。
	internal static bool InRange(double value)
	{
		return value >= -1e-6 && value <= 255.0 + 1e-6;
	}




	// 実数値を 0–255 のバイトへクランプして丸める。
	internal static byte ClampByte(double value)
	{
		return (byte)Math.Round(Math.Clamp(value, 0.0, 255.0));
	}
}
