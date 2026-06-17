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

// 指定した明度における Lab の a・b パッドの下地を描いた画像を生成する。横軸が a(左 −上限→右 +上限)、縦軸が b(上 +上限→下 −上限)で、各画素を Lab→RGB へ変換する。Lab の a・b 断面は sRGB 色域に収まる領域が明度に応じて伸び縮みする湾曲した塊で、残りは色域外。色域内の画素は実際の色で塗る。色域外の見せ方は GamutOutOfRangeStyle で切り替える。明度・副モード・色制限・色域外の見せ方が変わるたびに作り直す想定。色制限が有効なら色域内の画素の色をその制限へ丸めて段階的にする。
public static class AbPlane
{
	// 色域外を示す斜線ハッチ。黒線と白線を密着で並べ、暗い背景では白が、明るい背景では黒が効くようにして、グラデーションのどこでも消えないようにする。線幅(垂直)0.7 DIP、周期(x+y 方向)10 DIP。45 度の線のため、x+y 方向の帯幅は線幅の √2 倍にあたる。L-C パッド(LcPlane)・水平スライダーのハッチ(GradientSlider)もこの寸法・色に合わせる。
	private const double HatchLineWidth = 0.7;
	private const double HatchPeriod = 10.0;

	// 黒線・白線の不透明度。黒は 0.5、白は 0.3。明るい背景では黒が、暗い背景では白が効く。色域外のハッチと、色域境界線の黒白二重線の双方でこの不透明度を使う。
	private const byte HatchBlackAlpha = 0x80;
	private const byte HatchWhiteAlpha = 0x4D;


	// 指定した画素サイズ・表色系・明度・色制限設定・表示倍率・色域外の見せ方で、a・b パッドの下地を描いた WriteableBitmap を作る。色域内は実色で塗り、色域外は style に従う。ハッチは DIP 指定の寸法を表示倍率で画素へ直し、スライダーのベクターのハッチと太さ・間隔をそろえる。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, LchSpace space, double lightness, SnapSettings snap, double scale, GamutOutOfRangeStyle style)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];
		double abMax = LabColor.AbMax(space);

		double hatchPeriod = HatchPeriod * scale;
		double hatchBand = HatchLineWidth * Math.Sqrt(2.0) * scale;

		// 色域外の見せ方を要素へ分解する。クランプ色で塗るか(白塗りでないか)、斜線を重ねるか、境界線を引くか。
		bool fillClamped = style != GamutOutOfRangeStyle.WhiteHatch;
		bool showHatch = style == GamutOutOfRangeStyle.FillBoundaryHatch || style == GamutOutOfRangeStyle.WhiteHatch;
		bool showBoundary = style != GamutOutOfRangeStyle.WhiteHatch;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 色域外をクランプ色で塗る場合、その色は色相(原点から見た角度)ごとに一定で、半径方向には境界の一色へ詰まる。色相別に境界の色を先に求めておき、色域外の各画素はこの引き表から引くだけにして画素ごとの彩度二分法を避ける。角度の刻みは、最大半径の縁でも1画素より細かくなるよう画素幅に応じて増やす。
		int hueBuckets = Math.Clamp(pixelWidth * 2, 720, 2048);
		byte[] boundaryR = new byte[fillClamped ? hueBuckets : 0];
		byte[] boundaryG = new byte[fillClamped ? hueBuckets : 0];
		byte[] boundaryB = new byte[fillClamped ? hueBuckets : 0];

		if (fillClamped)
		{
			Parallel.For(0, hueBuckets, bucket =>
			{
				double hueDeg = (double)bucket / hueBuckets * 360.0;
				double maxC = LchColor.MaxChroma(space, lightness, hueDeg);
				Color clamped = LchColor.ToRgb(space, lightness, maxC, hueDeg);
				(boundaryR[bucket], boundaryG[bucket], boundaryB[bucket]) = ColorConversion.Snap(snap, clamped.R, clamped.G, clamped.B);
			});
		}

		// 境界線の検出のため、各画素が色域内かの真偽表を先に作る。輪郭は隣接画素との内外の違いで拾うため、塗りの前に全画素ぶん揃えておく。
		bool[] inGamut = new bool[pixelWidth * pixelHeight];

		Parallel.For(0, pixelHeight, y =>
		{
			double bAxis = (1.0 - (2.0 * (y + 0.5) / pixelHeight)) * abMax;
			int gridRow = y * pixelWidth;

			for (int x = 0; x < pixelWidth; x++)
			{
				double aAxis = ((2.0 * (x + 0.5) / pixelWidth) - 1.0) * abMax;
				inGamut[gridRow + x] = LabColor.InGamut(space, lightness, aAxis, bAxis);
			}
		});

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。画素数が多いとき1スレッドでは塗りきれず明度ドラッグでカクつくため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			// 上端を b の +上限、下端を −上限とし、パッドの縦方向(上ほど黄寄り)に合わせる。
			double bAxis = (1.0 - (2.0 * (y + 0.5) / pixelHeight)) * abMax;
			int gridRow = y * pixelWidth;
			int rowBase = gridRow * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				// 左端を a の −上限、右端を +上限とする。
				double aAxis = ((2.0 * (x + 0.5) / pixelWidth) - 1.0) * abMax;
				int gi = gridRow + x;
				int index = rowBase + (x * 4);
				bool ig = inGamut[gi];

				// 境界線。色域内側の縁の画素には黒、色域外側の縁の画素には白を、斜線と同じ不透明度で下地へ重ねる。縁は上下左右いずれかの隣と内外が食い違う画素で拾う。
				if (showBoundary)
				{
					if (ig)
					{
						bool innerEdge = (x + 1 < pixelWidth && !inGamut[gi + 1])
							|| (x > 0 && !inGamut[gi - 1])
							|| (y + 1 < pixelHeight && !inGamut[gi + pixelWidth])
							|| (y > 0 && !inGamut[gi - pixelWidth]);

						if (innerEdge)
						{
							// 下地は実色。その上へ黒を α=HatchBlackAlpha で重ねる。
							Color color = LabColor.ToRgb(space, lightness, aAxis, bAxis);
							(byte br, byte bg, byte bb) = ColorConversion.Snap(snap, color.R, color.G, color.B);
							pixels[index] = (byte)(bb * (255 - HatchBlackAlpha) / 255);
							pixels[index + 1] = (byte)(bg * (255 - HatchBlackAlpha) / 255);
							pixels[index + 2] = (byte)(br * (255 - HatchBlackAlpha) / 255);
							pixels[index + 3] = 0xFF;
							continue;
						}
					}
					else
					{
						bool outerEdge = (x + 1 < pixelWidth && inGamut[gi + 1])
							|| (x > 0 && inGamut[gi - 1])
							|| (y + 1 < pixelHeight && inGamut[gi + pixelWidth])
							|| (y > 0 && inGamut[gi - pixelWidth]);

						if (outerEdge)
						{
							// 下地はその色相のクランプ色。その上へ白を α=HatchWhiteAlpha で重ねる。
							int bucket = HueBucket(aAxis, bAxis, hueBuckets);
							byte fr = boundaryR[bucket];
							byte fg = boundaryG[bucket];
							byte fb = boundaryB[bucket];
							pixels[index] = (byte)(((fb * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							pixels[index + 1] = (byte)(((fg * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							pixels[index + 2] = (byte)(((fr * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							pixels[index + 3] = 0xFF;
							continue;
						}
					}
				}

				if (ig)
				{
					Color color = LabColor.ToRgb(space, lightness, aAxis, bAxis);
					(byte r, byte g, byte b) = ColorConversion.Snap(snap, color.R, color.G, color.B);

					// WriteableBitmap はアルファ乗算済みの BGRA を期待する。色域内は全面不透明のため色をそのまま並べる。
					pixels[index] = b;
					pixels[index + 1] = g;
					pixels[index + 2] = r;
					pixels[index + 3] = 0xFF;
					continue;
				}

				// 色域外。下地(クランプ色か白)を置き、斜線を重ねる設定ならその上へ黒白の斜線を合成する。全面不透明。
				byte or;
				byte og;
				byte ob;

				if (fillClamped)
				{
					int bucket = HueBucket(aAxis, bAxis, hueBuckets);
					or = boundaryR[bucket];
					og = boundaryG[bucket];
					ob = boundaryB[bucket];
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
						// 黒線を α=HatchBlackAlpha で上に重ねる。
						or = (byte)(or * (255 - HatchBlackAlpha) / 255);
						og = (byte)(og * (255 - HatchBlackAlpha) / 255);
						ob = (byte)(ob * (255 - HatchBlackAlpha) / 255);
					}
					else if (position < hatchBand * 2.0)
					{
						// 白線を α=HatchWhiteAlpha で上に重ねる。
						or = (byte)(((or * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
						og = (byte)(((og * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
						ob = (byte)(((ob * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
					}
				}

				pixels[index] = ob;
				pixels[index + 1] = og;
				pixels[index + 2] = or;
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




	// a・b から原点まわりの色相(度)を求め、引き表の角度刻みへ丸めた番号を返す。色域外のクランプ色を色相別の引き表から引くのに使う。
	private static int HueBucket(double aAxis, double bAxis, int hueBuckets)
	{
		double hueDeg = Math.Atan2(bAxis, aAxis) * 180.0 / Math.PI;

		if (hueDeg < 0.0)
		{
			hueDeg += 360.0;
		}

		int bucket = (int)Math.Round(hueDeg / 360.0 * hueBuckets) % hueBuckets;
		return bucket < 0 ? bucket + hueBuckets : bucket;
	}
}
