// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

// 指定した色相における LCH の明度・彩度パッドの下地を描いた画像を生成する。横軸が明度(左 0→右=最大)、縦軸が彩度(上=彩度軸の表示上限 chromaAxisMax→下 0)で、色相は引数で与える一定値とする。彩度軸の上限は引数で受け、フィット時はその色相で色域が届く最大彩度(cusp)へ詰めて色域を縦いっぱいへ広げる(LchColor.ChromaAxisMax)。LcPlane の縦横(彩度を横・明度を縦)を入れ替えた向きで、明度を横いっぱいに広げて細かく選べるようにする配置に使う。明度・彩度の断面は sRGB 色域に収まる領域と外れる領域が混在するため、色域内は実色、色域外はハッチで透かす。色相・副モード・色制限・色域外の見せ方・彩度軸の上限が変わるたびに作り直す想定。
public static class LchLightnessChromaPlane
{
	// 指定した画素サイズ・表色系・色相・色制限設定・表示倍率・色域外の見せ方・彩度軸の上限で、明度・彩度パッドの下地を描いた WriteableBitmap を作る。色域内は実色で塗り、色域外は style に従う。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, LchSpace space, double hue, SnapSettings snap, double scale, GamutOutOfRangeStyle style, double chromaAxisMax)
	{
		return LchGamutField.Render(pixelWidth, pixelHeight, space, snap, scale, style, BuildMap(pixelWidth, pixelHeight, space, hue, chromaAxisMax));
	}




	// 明度・彩度パッドの下地の BGRA 配列を返す。WriteableBitmap などの UI 型に触れないため背景スレッドで実行してよい。ドラッグ中の連続再生成では呼び出し側がこれを背景で回し、Blit だけ UI スレッドで行う。引数の意味は Create と同じ。
	public static byte[] ComputePixels(int pixelWidth, int pixelHeight, LchSpace space, double hue, SnapSettings snap, double scale, GamutOutOfRangeStyle style, double chromaAxisMax)
	{
		return LchGamutField.ComputePixels(pixelWidth, pixelHeight, space, snap, scale, style, BuildMap(pixelWidth, pixelHeight, space, hue, chromaAxisMax));
	}




	// 各画素の (明度, 彩度, 色相, 被覆度) を返す写像を作る。横軸が明度(左 0→右=最大)、縦軸が彩度(上=彩度軸の上限→下 0)で、色相は引数の一定値。Create と ComputePixels が同じ写像を共有する。
	private static Func<int, int, LchGamutField.Sample> BuildMap(int pixelWidth, int pixelHeight, LchSpace space, double hue, double chromaAxisMax)
	{
		double lMax = LchColor.LMax(space);

		return (x, y) =>
		{
			double lightness = (x + 0.5) / pixelWidth * lMax;
			double chroma = (1.0 - ((y + 0.5) / pixelHeight)) * chromaAxisMax;
			return new LchGamutField.Sample(lightness, chroma, hue, 1.0);
		};
	}
}
