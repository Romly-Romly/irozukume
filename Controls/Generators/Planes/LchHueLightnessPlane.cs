// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

// 指定した彩度における LCH の色相・明度パッドの下地を描いた画像を生成する。横軸が色相(左 0 度→右 360 度)、縦軸が明度(上=最大→下 0)で、彩度は引数で与える一定値とする。固定彩度の色相×明度の断面は sRGB 色域に収まる領域と外れる領域が混在するため、色域内は実色、色域外はハッチで透かす。色相・明度の2軸はパッド全面に渡って変わるため、彩度・副モード・色制限・色域外の見せ方が変わるたびに作り直す想定。
public static class LchHueLightnessPlane
{
	// 指定した画素サイズ・表色系・彩度・色制限設定・表示倍率・色域外の見せ方で、色相・明度パッドの下地を描いた WriteableBitmap を作る。色域内は実色で塗り、色域外は style に従う。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, LchSpace space, double chroma, SnapSettings snap, double scale, GamutOutOfRangeStyle style)
	{
		return LchGamutField.Render(pixelWidth, pixelHeight, space, snap, scale, style, BuildMap(pixelWidth, pixelHeight, space, chroma));
	}




	// 色相・明度パッドの下地の BGRA 配列を返す。WriteableBitmap などの UI 型に触れないため背景スレッドで実行してよい。ドラッグ中の連続再生成では呼び出し側がこれを背景で回し、Blit だけ UI スレッドで行う。引数の意味は Create と同じ。
	public static byte[] ComputePixels(int pixelWidth, int pixelHeight, LchSpace space, double chroma, SnapSettings snap, double scale, GamutOutOfRangeStyle style)
	{
		return LchGamutField.ComputePixels(pixelWidth, pixelHeight, space, snap, scale, style, BuildMap(pixelWidth, pixelHeight, space, chroma));
	}




	// 各画素の (明度, 彩度, 色相, 被覆度) を返す写像を作る。横軸が色相(左 0 度→右 360 度)、縦軸が明度(上=最大→下 0)で、彩度は引数の一定値。Create と ComputePixels が同じ写像を共有する。
	private static Func<int, int, LchGamutField.Sample> BuildMap(int pixelWidth, int pixelHeight, LchSpace space, double chroma)
	{
		double lMax = LchColor.LMax(space);

		return (x, y) =>
		{
			double hue = (x + 0.5) / pixelWidth * 360.0;
			double lightness = (1.0 - ((y + 0.5) / pixelHeight)) * lMax;
			return new LchGamutField.Sample(lightness, chroma, hue, 1.0);
		};
	}
}
