// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls.Generators.Disks;

// 角度=色相・中心からの半径=彩度(中心 0=無彩→縁=彩度軸の表示上限 chromaAxisMax)の円盤の下地を生成する。明度は引数で与える一定値とする。彩度軸(半径)の上限は引数で受け、フィット時はその明度で全色相を通じて色域が届く最大彩度へ詰めて色域を半径方向へ広げる(LchColor.ChromaAxisMaxAtLightness)。固定明度では彩度を上げると色相によって sRGB 色域を外れるため、色域内は実色、色域外はハッチで透かす。角度の取り方は RingGeometry と揃えるため、つまみの位置と円盤の色がずれない。円の外は透明にする。色相は円盤の全方位に現れるため、明度・副モード・色制限・色域外の見せ方・彩度軸の上限が変わるたびに作り直す想定。
public static class LchHueChromaDisk
{
	// 指定した画素サイズ・表色系・明度・色制限設定・表示倍率・色域外の見せ方・彩度軸の上限で、色相・彩度の円盤を描いた WriteableBitmap を作る。円の半径は画素サイズの短辺の半分。色域内は実色、色域外は style に従う。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, LchSpace space, double lightness, SnapSettings snap, double scale, GamutOutOfRangeStyle style, double chromaAxisMax)
	{
		return LchGamutField.Render(pixelWidth, pixelHeight, space, snap, scale, style, BuildMap(pixelWidth, pixelHeight, space, lightness, chromaAxisMax));
	}




	// 色相・彩度の円盤の下地の BGRA 配列を返す。WriteableBitmap などの UI 型に触れないため背景スレッドで実行してよい。ドラッグ中の連続再生成では呼び出し側がこれを背景で回し、Blit だけ UI スレッドで行う。引数の意味は Create と同じ。
	public static byte[] ComputePixels(int pixelWidth, int pixelHeight, LchSpace space, double lightness, SnapSettings snap, double scale, GamutOutOfRangeStyle style, double chromaAxisMax)
	{
		return LchGamutField.ComputePixels(pixelWidth, pixelHeight, space, snap, scale, style, BuildMap(pixelWidth, pixelHeight, space, lightness, chromaAxisMax));
	}




	// 各画素の (明度, 彩度, 色相, 被覆度) を返す写像を作る。角度=色相・半径=彩度(中心 0→縁=彩度軸の上限)で、明度は引数の一定値。円の外は被覆度 0 で透明にする。Create と ComputePixels が同じ写像を共有する。
	private static Func<int, int, LchGamutField.Sample> BuildMap(int pixelWidth, int pixelHeight, LchSpace space, double lightness, double chromaAxisMax)
	{
		double centerX = pixelWidth / 2.0;
		double centerY = pixelHeight / 2.0;
		double maxRadius = Math.Min(pixelWidth, pixelHeight) / 2.0;

		return (x, y) =>
		{
			double dx = x + 0.5 - centerX;
			double dy = y + 0.5 - centerY;
			double radius = Math.Sqrt((dx * dx) + (dy * dy));
			double coverage = LchGamutField.DiskCoverage(radius, maxRadius);

			if (coverage <= 0.0)
			{
				return new LchGamutField.Sample(0.0, 0.0, 0.0, 0.0);
			}

			double hue = RingGeometry.ValueFromPoint(dx, dy);
			double chroma = Math.Clamp(radius / maxRadius, 0.0, 1.0) * chromaAxisMax;
			return new LchGamutField.Sample(lightness, chroma, hue, coverage);
		};
	}
}
