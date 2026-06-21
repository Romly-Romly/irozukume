// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

// 指定した明度における LCH の色相・彩度パッドの下地を描いた画像を生成する。横軸が色相(左 0 度→右 360 度)、縦軸が彩度(上=彩度軸の表示上限 chromaAxisMax→下 0)で、明度は引数で与える一定値とする。彩度軸の上限は引数で受け、フィット時はその明度で全色相を通じて色域が届く最大彩度へ詰めて色域を縦いっぱいへ広げる(LchColor.ChromaAxisMaxAtLightness)。固定明度の色相×彩度の断面は、彩度を上げると色相によって sRGB 色域を外れるため、色域内は実色、色域外はハッチで透かす。色相・彩度の2軸はパッド全面に渡って変わるため、明度・副モード・色制限・色域外の見せ方・彩度軸の上限が変わるたびに作り直す想定。
public static class LchHueChromaPlane
{
	// 指定した画素サイズ・表色系・明度・色制限設定・表示倍率・色域外の見せ方・彩度軸の上限で、色相・彩度パッドの下地を描いた WriteableBitmap を作る。色域内は実色で塗り、色域外は style に従う。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, LchSpace space, double lightness, SnapSettings snap, double scale, GamutOutOfRangeStyle style, double chromaAxisMax)
	{
		return LchGamutField.Render(pixelWidth, pixelHeight, space, snap, scale, style, BuildMap(pixelWidth, pixelHeight, space, lightness, chromaAxisMax));
	}




	// 色相・彩度パッドの下地の BGRA 配列を返す。WriteableBitmap などの UI 型に触れないため背景スレッドで実行してよい。ドラッグ中の連続再生成では呼び出し側がこれを背景で回し、Blit だけ UI スレッドで行う。引数の意味は Create と同じ。
	public static byte[] ComputePixels(int pixelWidth, int pixelHeight, LchSpace space, double lightness, SnapSettings snap, double scale, GamutOutOfRangeStyle style, double chromaAxisMax)
	{
		return LchGamutField.ComputePixels(pixelWidth, pixelHeight, space, snap, scale, style, BuildMap(pixelWidth, pixelHeight, space, lightness, chromaAxisMax));
	}




	// 各画素の (明度, 彩度, 色相, 被覆度) を返す写像を作る。横軸が色相(左 0 度→右 360 度)、縦軸が彩度(上=彩度軸の上限→下 0)で、明度は引数の一定値。Create と ComputePixels が同じ写像を共有する。
	private static Func<int, int, LchGamutField.Sample> BuildMap(int pixelWidth, int pixelHeight, LchSpace space, double lightness, double chromaAxisMax)
	{
		return (x, y) =>
		{
			double hue = (x + 0.5) / pixelWidth * 360.0;
			double chroma = (1.0 - ((y + 0.5) / pixelHeight)) * chromaAxisMax;
			return new LchGamutField.Sample(lightness, chroma, hue, 1.0);
		};
	}
}
