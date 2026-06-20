// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls;

// Lab の直交軸のうち、片方の色軸(a または b)と明度 L を縦横に取った断面の下地を生成する。固定する色軸は引数で与える一定値とする。a×L は横軸が a(左 −上限→右 +上限)・縦軸が明度(上=最大→下 0)で b を固定、b×L は横軸が b(左 −上限→右 +上限)・縦軸が明度で a を固定する。各画素の (a, b) を彩度 C=hypot(a,b)・色相 H=atan2(b,a) の極座標へ読み替え、色域判定・実色塗り・色域外の見せ方は LchGamutField が一手に賄う。固定の色軸・明度の刻みは断面全体に渡って変わるため、固定成分・副モード・色制限・色域外の見せ方が変わるたびに作り直す想定。
public static class AbLightnessPlane
{
	// 指定した画素サイズ・表色系・固定 b・色制限設定・表示倍率・色域外の見せ方・表示枠の決め方で、a×L 断面の下地を描いた WriteableBitmap を作る。色域内は実色で塗り、色域外は style に従う。
	public static WriteableBitmap CreateAL(int pixelWidth, int pixelHeight, LchSpace space, double fixedB, SnapSettings snap, double scale, GamutOutOfRangeStyle style, AbFitMode fit)
	{
		return LchGamutField.Render(pixelWidth, pixelHeight, space, snap, scale, style, BuildMapAL(pixelWidth, pixelHeight, space, fixedB, fit));
	}




	// a×L 断面の下地の BGRA 配列を返す。WriteableBitmap などの UI 型に触れないため背景スレッドで実行してよい。ドラッグ中の連続再生成では呼び出し側がこれを背景で回し、Blit だけ UI スレッドで行う。引数の意味は CreateAL と同じ。
	public static byte[] ComputePixelsAL(int pixelWidth, int pixelHeight, LchSpace space, double fixedB, SnapSettings snap, double scale, GamutOutOfRangeStyle style, AbFitMode fit)
	{
		return LchGamutField.ComputePixels(pixelWidth, pixelHeight, space, snap, scale, style, BuildMapAL(pixelWidth, pixelHeight, space, fixedB, fit));
	}




	// 指定した画素サイズ・表色系・固定 a・色制限設定・表示倍率・色域外の見せ方・表示枠の決め方で、b×L 断面の下地を描いた WriteableBitmap を作る。色域内は実色で塗り、色域外は style に従う。
	public static WriteableBitmap CreateBL(int pixelWidth, int pixelHeight, LchSpace space, double fixedA, SnapSettings snap, double scale, GamutOutOfRangeStyle style, AbFitMode fit)
	{
		return LchGamutField.Render(pixelWidth, pixelHeight, space, snap, scale, style, BuildMapBL(pixelWidth, pixelHeight, space, fixedA, fit));
	}




	// b×L 断面の下地の BGRA 配列を返す。WriteableBitmap などの UI 型に触れないため背景スレッドで実行してよい。ドラッグ中の連続再生成では呼び出し側がこれを背景で回し、Blit だけ UI スレッドで行う。引数の意味は CreateBL と同じ。
	public static byte[] ComputePixelsBL(int pixelWidth, int pixelHeight, LchSpace space, double fixedA, SnapSettings snap, double scale, GamutOutOfRangeStyle style, AbFitMode fit)
	{
		return LchGamutField.ComputePixels(pixelWidth, pixelHeight, space, snap, scale, style, BuildMapBL(pixelWidth, pixelHeight, space, fixedA, fit));
	}




	// 各画素の (明度, 彩度, 色相, 被覆度) を返す写像を作る(a×L)。横軸が a(左=枠の下限→右=上限)、縦軸が明度(上=枠の上限→下=下限)で、b は引数の一定値。表示枠は AbFitMode で決め、None は横が ±AbMax・縦が 0–LMax の固定枠、フィット時はその固定 b で色域が収まる範囲へ寄せる(LabColor.CartExtentFor)。各画素の (a, b) を彩度・色相の極座標へ読み替える。矩形のため被覆度は常に 1。CreateAL と ComputePixelsAL が同じ写像を共有する。
	private static Func<int, int, LchGamutField.Sample> BuildMapAL(int pixelWidth, int pixelHeight, LchSpace space, double fixedB, AbFitMode fit)
	{
		PlaneExtent extent = LabColor.CartExtentFor(space, fixedB, true, fit);

		return (x, y) =>
		{
			double aAxis = extent.XMin + ((x + 0.5) / pixelWidth) * extent.XWidth;
			double lightness = extent.YMax - ((y + 0.5) / pixelHeight) * extent.YHeight;
			(double chroma, double hue) = ToPolar(aAxis, fixedB);
			return new LchGamutField.Sample(lightness, chroma, hue, 1.0);
		};
	}




	// 各画素の (明度, 彩度, 色相, 被覆度) を返す写像を作る(b×L)。横軸が b(左=枠の下限→右=上限)、縦軸が明度(上=枠の上限→下=下限)で、a は引数の一定値。表示枠は AbFitMode で決め、None は横が ±AbMax・縦が 0–LMax の固定枠、フィット時はその固定 a で色域が収まる範囲へ寄せる(LabColor.CartExtentFor)。各画素の (a, b) を彩度・色相の極座標へ読み替える。矩形のため被覆度は常に 1。CreateBL と ComputePixelsBL が同じ写像を共有する。
	private static Func<int, int, LchGamutField.Sample> BuildMapBL(int pixelWidth, int pixelHeight, LchSpace space, double fixedA, AbFitMode fit)
	{
		PlaneExtent extent = LabColor.CartExtentFor(space, fixedA, false, fit);

		return (x, y) =>
		{
			double bAxis = extent.XMin + ((x + 0.5) / pixelWidth) * extent.XWidth;
			double lightness = extent.YMax - ((y + 0.5) / pixelHeight) * extent.YHeight;
			(double chroma, double hue) = ToPolar(fixedA, bAxis);
			return new LchGamutField.Sample(lightness, chroma, hue, 1.0);
		};
	}




	// 直交座標(a・b)を極座標(彩度・色相 0–360 度)へ読み替える。LchGamutField は彩度・色相で色域判定・実色塗りを行うため、各画素の a・b をこの形へ直して渡す。
	private static (double C, double H) ToPolar(double a, double b)
	{
		double c = Math.Sqrt((a * a) + (b * b));
		double h = Math.Atan2(b, a) * 180.0 / Math.PI;

		if (h < 0.0)
		{
			h += 360.0;
		}

		return (c, h);
	}
}
