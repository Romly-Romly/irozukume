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

// LCH の2次元コントロール(色相×明度・色相×彩度の矩形、半径=明度・半径=彩度の円盤)の下地を、色域内は実色・色域外はハッチで透かして描くための共有ラスタライザ。各画素の (明度, 彩度, 色相) と、その点が描画対象か(円盤の外は対象外)を呼び出し側のラムダで与え、色域判定・実色塗り・色域外のクランプ塗り・斜線ハッチ・色域境界線の合成だけをここで一手に賄う。色域内外の判定は画素ごとに行い、境界線は上下左右の隣の画素の色域内外が切り替わる縁で拾う。寸法・色相・固定成分・色制限・色域外の見せ方が変わるたびに作り直す想定。L-C 平面(LcPlane)とハッチの寸法・色・不透明度をそろえる。
internal static class LchGamutField
{
	// 色域外を示す斜線ハッチ。黒線と白線を密着で並べ、暗い背景では白が、明るい背景では黒が効くようにして、どこでも消えないようにする。線幅(垂直)0.7 DIP、周期(x+y 方向)10 DIP。45 度の線のため、x+y 方向の帯幅は線幅の √2 倍にあたる。LcPlane・GradientSlider のハッチと寸法・色をそろえる。
	private const double HatchLineWidth = 0.7;
	private const double HatchPeriod = 10.0;

	// 黒線・白線の不透明度。黒は 0.5、白は 0.3。明るい背景では黒が、暗い背景では白が効く。色域外のハッチと、色域境界線の黒白二重線の双方でこの不透明度を使う。
	private const byte HatchBlackAlpha = 0x80;
	private const byte HatchWhiteAlpha = 0x4D;

	// 円盤の縁取りをなじませる幅(画素)。円の外周でこの幅だけ不透明度を落とし、円の外は透明にする。矩形では Coverage を常に 1 とし縁取りは生じない。
	private const double EdgeSoftness = 1.0;


	// 1画素ぶんのサンプル。その点の明度・彩度・色相(度)と、描画対象としての被覆度(円盤の縁取り・外側に使う。1=不透明、0=対象外で透明)。矩形では Coverage は常に 1。
	internal readonly struct Sample
	{
		public Sample(double lightness, double chroma, double hueDegrees, double coverage)
		{
			Lightness = lightness;
			Chroma = chroma;
			HueDegrees = hueDegrees;
			Coverage = coverage;
		}




		public double Lightness { get; }




		public double Chroma { get; }




		public double HueDegrees { get; }




		public double Coverage { get; }
	}




	// 円の中心からの距離が円盤に属する度合い(0–1)を返す。中心から maxRadius までは 1、そこから外へ EdgeSoftness の幅で 0 まで落とし、それより外は 0 にする。円盤のサンプルの被覆度に使う。
	internal static double DiskCoverage(double radius, double maxRadius)
	{
		if (radius > maxRadius + EdgeSoftness)
		{
			return 0.0;
		}

		if (radius <= maxRadius)
		{
			return 1.0;
		}

		return Math.Clamp((maxRadius + EdgeSoftness - radius) / EdgeSoftness, 0.0, 1.0);
	}




	// 指定の画素サイズ・表色系・色制限・表示倍率・色域外の見せ方で、各画素の (明度, 彩度, 色相, 被覆度) を返す map に従って下地を描いた WriteableBitmap を作る。色域内は実色、色域外は style に従う(クランプ色塗り+境界線+斜線/同+斜線無し/白塗り+斜線)。被覆度が 0 の画素は透明にして、円盤の外側や縁取りを表す。画素計算(ComputePixels)とビットマップ化(Blit)を続けて行う同期版で、サムネイル等の一度きりの生成に使う。ドラッグ中の連続再生成では、計算を背景スレッドへ回せるよう呼び出し側が ComputePixels と Blit を分けて使う。
	internal static WriteableBitmap Render(int pixelWidth, int pixelHeight, LchSpace space, SnapSettings snap, double scale, GamutOutOfRangeStyle style, Func<int, int, Sample> map)
	{
		return Blit(ComputePixels(pixelWidth, pixelHeight, space, snap, scale, style, map), pixelWidth, pixelHeight);
	}




	// 各画素の BGRA を詰めた配列を返す。WriteableBitmap などの UI 型に触れないため背景スレッドで実行してよい。色域内外の判定・実色塗り・クランプ塗り・斜線ハッチ・境界線の合成をここで一手に行う。引数の意味は Render と同じ。
	internal static byte[] ComputePixels(int pixelWidth, int pixelHeight, LchSpace space, SnapSettings snap, double scale, GamutOutOfRangeStyle style, Func<int, int, Sample> map)
	{
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		// DIP 指定のハッチを、ビットマップの画素寸法(表示倍率)に合わせて画素単位へ直す。周期は x+y 方向の量、線の帯幅は垂直の線幅を 45 度の x+y 方向へ直した √2 倍。
		double hatchPeriod = HatchPeriod * scale;
		double hatchBand = HatchLineWidth * Math.Sqrt(2.0) * scale;

		// 色域外の見せ方を要素へ分解する。クランプ色で塗るか(白塗りでないか)、斜線を重ねるか、境界線を引くか。
		bool fillClamped = style != GamutOutOfRangeStyle.WhiteHatch;
		bool showHatch = style == GamutOutOfRangeStyle.FillBoundaryHatch || style == GamutOutOfRangeStyle.WhiteHatch;
		bool showBoundary = style != GamutOutOfRangeStyle.WhiteHatch;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 第1段。各画素の被覆度・色域内外・実色(色域内なら実色、色域外ならクランプ色)を先に求める。境界線は隣の画素の色域内外を見て拾うため、判定を画素配列へ控えておく。
		float[] coverage = new float[pixelWidth * pixelHeight];
		bool[] inGamut = new bool[pixelWidth * pixelHeight];
		byte[] baseColor = new byte[pixelWidth * pixelHeight * 3];

		Parallel.For(0, pixelHeight, y =>
		{
			for (int x = 0; x < pixelWidth; x++)
			{
				int i = (y * pixelWidth) + x;
				Sample sample = map(x, y);
				double cov = sample.Coverage;
				coverage[i] = (float)cov;

				if (cov <= 0.0)
				{
					continue;
				}

				bool ig = LchColor.InGamut(space, sample.Lightness, sample.Chroma, sample.HueDegrees);
				inGamut[i] = ig;

				// 色域内なら実色、色域外なら明度・色相を保って彩度を境界へ詰めたクランプ色。いずれも ToRgb が返す。色制限が有効ならその丸めも掛ける。
				Color color = LchColor.ToRgb(space, sample.Lightness, sample.Chroma, sample.HueDegrees);
				(byte r, byte g, byte b) = ColorConversion.Snap(snap, color.R, color.G, color.B);
				int ci = i * 3;
				baseColor[ci] = r;
				baseColor[ci + 1] = g;
				baseColor[ci + 2] = b;
			}
		});

		// 第2段。被覆度・色域内外・実色/クランプ色から、境界線・斜線・白塗りを合成して画素を並べる。各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。
		Parallel.For(0, pixelHeight, y =>
		{
			bool hasUp = y > 0;
			bool hasDown = y + 1 < pixelHeight;

			for (int x = 0; x < pixelWidth; x++)
			{
				int i = (y * pixelWidth) + x;
				int index = i * 4;
				double cov = coverage[i];

				if (cov <= 0.0)
				{
					pixels[index] = 0;
					pixels[index + 1] = 0;
					pixels[index + 2] = 0;
					pixels[index + 3] = 0;
					continue;
				}

				bool ig = inGamut[i];
				int ci = i * 3;
				byte cr = baseColor[ci];
				byte cg = baseColor[ci + 1];
				byte cb = baseColor[ci + 2];

				// 描画対象の隣(被覆度 > 0)で色域内外が切り替わる縁を境界線にする。円盤の外(被覆度 0)との境は形の縁であって色域の境ではないため、被覆度 0 の隣は縁として数えない。
				if (showBoundary)
				{
					bool hasLeft = x > 0;
					bool hasRight = x + 1 < pixelWidth;
					int left = i - 1;
					int right = i + 1;
					int up = i - pixelWidth;
					int down = i + pixelWidth;

					if (ig)
					{
						bool innerEdge = (hasRight && coverage[right] > 0.0 && !inGamut[right])
							|| (hasLeft && coverage[left] > 0.0 && !inGamut[left])
							|| (hasUp && coverage[up] > 0.0 && !inGamut[up])
							|| (hasDown && coverage[down] > 0.0 && !inGamut[down]);

						if (innerEdge)
						{
							// 下地は実色。その上へ黒を α=HatchBlackAlpha で重ねる。
							byte br = (byte)(cr * (255 - HatchBlackAlpha) / 255);
							byte bg = (byte)(cg * (255 - HatchBlackAlpha) / 255);
							byte bb = (byte)(cb * (255 - HatchBlackAlpha) / 255);
							WritePixel(pixels, index, br, bg, bb, cov);
							continue;
						}
					}
					else
					{
						bool outerEdge = (hasLeft && coverage[left] > 0.0 && inGamut[left])
							|| (hasRight && coverage[right] > 0.0 && inGamut[right])
							|| (hasUp && coverage[up] > 0.0 && inGamut[up])
							|| (hasDown && coverage[down] > 0.0 && inGamut[down]);

						if (outerEdge)
						{
							// 下地はクランプ色。その上へ白を α=HatchWhiteAlpha で重ねる。
							byte br = (byte)(((cr * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							byte bg = (byte)(((cg * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							byte bb = (byte)(((cb * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							WritePixel(pixels, index, br, bg, bb, cov);
							continue;
						}
					}
				}

				if (ig)
				{
					WritePixel(pixels, index, cr, cg, cb, cov);
					continue;
				}

				// 色域外。下地はクランプ色(白塗りなら白)。斜線を重ねる設定ならその上へ黒白の斜線を合成する。
				byte or;
				byte og;
				byte ob;

				if (fillClamped)
				{
					or = cr;
					og = cg;
					ob = cb;
				}
				else
				{
					or = 0xFF;
					og = 0xFF;
					ob = 0xFF;
				}

				if (showHatch)
				{
					double position = (x + y) % hatchPeriod;

					if (position < hatchBand)
					{
						or = (byte)(or * (255 - HatchBlackAlpha) / 255);
						og = (byte)(og * (255 - HatchBlackAlpha) / 255);
						ob = (byte)(ob * (255 - HatchBlackAlpha) / 255);
					}
					else if (position < hatchBand * 2.0)
					{
						or = (byte)(((or * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
						og = (byte)(((og * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
						ob = (byte)(((ob * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
					}
				}

				WritePixel(pixels, index, or, og, ob, cov);
			}
		});

		return pixels;
	}




	// 計算済みの BGRA 配列を WriteableBitmap へ写す。WriteableBitmap は生成・書き込み・Invalidate を同じ UI スレッドで行う必要があるため、この一手だけは UI スレッドで呼ぶ。pixelWidth・pixelHeight は配列を作ったときと同じ寸法を渡す。
	internal static WriteableBitmap Blit(byte[] pixels, int pixelWidth, int pixelHeight)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(pixels, 0, pixels.Length);
		}

		bitmap.Invalidate();
		return bitmap;
	}




	// 1画素を、被覆度(アルファ)を掛けて乗算済み BGRA で書き込む。WriteableBitmap はアルファ乗算済みを期待するため、被覆度が 1 未満の縁取りでは各色にアルファを掛ける。
	private static void WritePixel(byte[] pixels, int index, byte r, byte g, byte b, double coverage)
	{
		if (coverage >= 1.0)
		{
			pixels[index] = b;
			pixels[index + 1] = g;
			pixels[index + 2] = r;
			pixels[index + 3] = 0xFF;
			return;
		}

		byte alpha = (byte)Math.Round(coverage * 255.0);
		pixels[index] = (byte)(b * alpha / 255);
		pixels[index + 1] = (byte)(g * alpha / 255);
		pixels[index + 2] = (byte)(r * alpha / 255);
		pixels[index + 3] = alpha;
	}
}
