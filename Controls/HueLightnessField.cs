// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls;

// 配色タブの「明度ディスク」の下地を描いた画像を生成する。中心からの角度を色相、中心からの距離を明度に対応させ、各画素をその色相・明度で sRGB 色域に収まる最大彩度の色で塗る。彩度は常に色域内の最大へ詰めるため色域外は生じず、ハッチや境界線は要らない。半径から明度への写し方は LightnessDiscPattern で切り替える(中心白・縁黒/中心黒・縁白の全域、中心白・縁 cusp、中心黒・縁 cusp)。cusp(その色相が最も鮮やかになる明度)を使う型では、色相ごとの cusp 明度を先に求めてから塗る。色制限が有効なら塗る色をその制限へ丸める。下地は編集対象の色に依存しないため、画素サイズ・表色系・色制限・型が変わるときだけ作り直す。円形の切り抜きは利用側(HarmonyDisc)が行う。
public static class HueLightnessField
{
	// 明度ディスクの型と色相から、円盤の縁(距離1)に取る明度を返す。WhiteToCusp と BlackToCusp はその色相の cusp(彩度が最大になる明度)、FullRangeReversed は白(1)、FullRange は黒(0)。下地の塗りとマーカーの配置で同じ縁の明度を使う。
	public static double EdgeLightness(LightnessDiscPattern pattern, LchSpace space, double hue)
	{
		return pattern switch
		{
			LightnessDiscPattern.WhiteToCusp or LightnessDiscPattern.BlackToCusp => LchColor.CuspLightness(space, hue),
			LightnessDiscPattern.FullRangeReversed => 1.0,
			_ => 0.0,
		};
	}




	// 縁の明度の取り方が中心黒の系統(中心が黒・縁が明るい側)かを返す。中心黒系は BlackToCusp・FullRangeReversed。残り(FullRange・WhiteToCusp)は中心白系。半径と明度の対応式の場合分けに使う。
	private static bool IsBlackCenter(LightnessDiscPattern pattern)
	{
		return pattern is LightnessDiscPattern.BlackToCusp or LightnessDiscPattern.FullRangeReversed;
	}




	// 明度ディスクの型と縁の明度から、正規化半径(0–1)を明度へ写す。中心黒系は中心(距離0)を黒・縁(距離1)を edgeLightness に、中心白系は中心を白・縁を edgeLightness に取る(FullRange は edgeLightness=0 で縁が黒)。下地の塗りとマーカーの配置で同じ対応を使う。
	public static double LightnessFromRadius(LightnessDiscPattern pattern, double radius, double edgeLightness)
	{
		double r = Math.Clamp(radius, 0.0, 1.0);
		return IsBlackCenter(pattern) ? r * edgeLightness : 1.0 - (r * (1.0 - edgeLightness));
	}




	// LightnessFromRadius の逆。明度を正規化半径(0–1)へ戻す。中心と縁が表せる明度の範囲を外れる色は、縁(0 か 1)へ詰める。
	public static double RadiusFromLightness(LightnessDiscPattern pattern, double lightness, double edgeLightness)
	{
		const double eps = 1e-6;

		double r = IsBlackCenter(pattern)
			? lightness / Math.Max(edgeLightness, eps)
			: (1.0 - lightness) / Math.Max(1.0 - edgeLightness, eps);

		return Math.Clamp(r, 0.0, 1.0);
	}




	// 指定した画素サイズ・表色系・色制限・型で、明度ディスクの下地を描いた WriteableBitmap を作る。各画素は中心からの距離と型から明度を、角度から色相を決め、その明度・色相で色域に収まる最大彩度の色で塗る。cusp を使う型は色相別の cusp 明度を先に求めて引く。四隅(円盤の外)は縁の明度へ詰め、円形の切り抜きは利用側に委ねる。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, LchSpace space, SnapSettings snap, LightnessDiscPattern pattern)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		// 最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// cusp を縁に置く型では、色相ごとの cusp 明度を先に求めておく。画素ごとに cusp を探すのを避け、角度の刻みは縁でも1画素より細かくする。FullRange と FullRangeReversed は縁の明度が色相に依らないため事前計算は要らない。
		bool needsCusp = pattern == LightnessDiscPattern.WhiteToCusp || pattern == LightnessDiscPattern.BlackToCusp;
		int hueBuckets = Math.Clamp(pixelWidth * 2, 720, 2048);
		double[] cuspL = new double[needsCusp ? hueBuckets : 0];

		if (needsCusp)
		{
			Parallel.For(0, hueBuckets, bucket =>
			{
				double hueDeg = (double)bucket / hueBuckets * 360.0;
				cuspL[bucket] = LchColor.CuspLightness(space, hueDeg);
			});
		}

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。
		Parallel.For(0, pixelHeight, y =>
		{
			// 上端を +1、下端を −1とした正規化座標。中心からの距離が円盤の半径(1)を表す。
			double bNorm = 1.0 - (2.0 * (y + 0.5) / pixelHeight);
			int rowBase = y * pixelWidth * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				double aNorm = (2.0 * (x + 0.5) / pixelWidth) - 1.0;
				int index = rowBase + (x * 4);

				double radius = Math.Sqrt((aNorm * aNorm) + (bNorm * bNorm));

				// 色相 0(赤)を上(北)に置き、時計回りに増やす。画面の角度(東基準・反時計回り)から 90 度回し符号を反転して色相へ写す。
				double hue = 90.0 - (Math.Atan2(bNorm, aNorm) * 180.0 / Math.PI);
				hue = ((hue % 360.0) + 360.0) % 360.0;

				// 中心からの距離と型から明度を決める。cusp を使う型は色相別の引き表から cusp 明度を取り、それ以外は色相に依らない一定の縁の明度を取る。
				double edge = needsCusp
					? cuspL[(int)Math.Round(hue / 360.0 * hueBuckets) % hueBuckets]
					: EdgeLightness(pattern, space, hue);
				double lightness = LightnessFromRadius(pattern, radius, edge);

				// その明度・色相で色域に収まる最大彩度の色を作る。常に色域内のため色域外処理は要らない。
				double maxC = LchColor.MaxChroma(space, lightness, hue);
				Color color = LchColor.ToRgb(space, lightness, maxC, hue);
				(byte r, byte g, byte b) = ColorConversion.Snap(snap, color.R, color.G, color.B);

				// WriteableBitmap はアルファ乗算済みの BGRA を期待する。下地は全面不透明のため色をそのまま並べる。
				pixels[index] = b;
				pixels[index + 1] = g;
				pixels[index + 2] = r;
				pixels[index + 3] = 0xFF;
			}
		});

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(pixels, 0, pixels.Length);
		}

		bitmap.Invalidate();
		return bitmap;
	}
}
