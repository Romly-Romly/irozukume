// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls;

// 指定した色相における LCH の明度・彩度パッドの下地を描いた画像を生成する。横軸が彩度(左 0→右=彩度軸の表示上限 chromaAxisMax)、縦軸が明度(上=最大→下 0)で、各画素を LCH→RGB へ変換する。彩度軸の上限は引数で受け、フィット時はその色相で色域が届く最大彩度(cusp)へ詰めて色域を横いっぱいへ広げる(LchColor.ChromaAxisMax)。LCH の明度・彩度の断面は sRGB 色域に収まる領域が湾曲した塊(ヒレ状)で、残りは色域外。色域内の画素は実際の色で塗る。色域外の見せ方は GamutOutOfRangeStyle で切り替える。色相・副モード・色制限・色域外の見せ方・彩度軸の上限が変わるたびに作り直す想定。色制限が有効なら色域内の画素の色をその制限へ丸めて段階的にする。
public static class LcPlane
{
	// 色域外を示す斜線ハッチ。黒線と白線を密着で並べ、暗い背景では白が、明るい背景では黒が効くようにして、グラデーションのどこでも消えないようにする。線幅(垂直)0.7 DIP、周期(x+y 方向)10 DIP。45 度の線のため、x+y 方向の帯幅は線幅の √2 倍にあたる。水平スライダーのハッチ(GradientSlider)もこの寸法・色に合わせる。
	private const double HatchLineWidth = 0.7;
	private const double HatchPeriod = 10.0;

	// 黒線・白線の不透明度。黒は 0.5、白は 0.3。明るい背景では黒が、暗い背景では白が効く。色域外のハッチと、色域境界線の黒白二重線の双方でこの不透明度を使う。
	private const byte HatchBlackAlpha = 0x80;
	private const byte HatchWhiteAlpha = 0x4D;


	// 指定した画素サイズ・表色系・色相・色制限設定・表示倍率・色域外の見せ方で、明度・彩度パッドの下地を描いた WriteableBitmap を作る。色域内は実色で塗り、色域外は style に従う。ハッチは DIP 指定の寸法を表示倍率で画素へ直し、スライダーのベクターのハッチと太さ・間隔をそろえる。画素計算(ComputePixels)とビットマップ化(LchGamutField.Blit)を続けて行う同期版。ドラッグ中の連続再生成では呼び出し側が ComputePixels を背景スレッドで回し、Blit だけ UI スレッドで行う。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, LchSpace space, double hue, SnapSettings snap, double scale, GamutOutOfRangeStyle style, double chromaAxisMax)
	{
		return LchGamutField.Blit(ComputePixels(pixelWidth, pixelHeight, space, hue, snap, scale, style, chromaAxisMax), pixelWidth, pixelHeight);
	}




	// 明度・彩度パッドの下地の BGRA 配列を返す。WriteableBitmap などの UI 型に触れないため背景スレッドで実行してよい。色相固定の L-C 断面では行ごとに最大彩度が定まるため、色域内外の判定を彩度の比較に落とし、色域外のクランプ色も行で一度だけ作る。引数の意味は Create と同じ。
	public static byte[] ComputePixels(int pixelWidth, int pixelHeight, LchSpace space, double hue, SnapSettings snap, double scale, GamutOutOfRangeStyle style, double chromaAxisMax)
	{
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];
		double lMax = LchColor.LMax(space);
		double cMax = chromaAxisMax;

		// DIP 指定のハッチを、ビットマップの画素寸法(表示倍率)に合わせて画素単位へ直す。周期は x+y 方向の量、線の帯幅は垂直の線幅(1.0 DIP)を 45 度の x+y 方向へ直した √2 倍。黒線をこの帯、続けて白線を同じ帯で密着させる。
		double hatchPeriod = HatchPeriod * scale;
		double hatchBand = HatchLineWidth * Math.Sqrt(2.0) * scale;

		// 色域外の見せ方を要素へ分解する。クランプ色で塗るか(白塗りでないか)、斜線を重ねるか、境界線を引くか。
		bool fillClamped = style != GamutOutOfRangeStyle.WhiteHatch;
		bool showHatch = style == GamutOutOfRangeStyle.FillBoundaryHatch || style == GamutOutOfRangeStyle.WhiteHatch;
		bool showBoundary = style != GamutOutOfRangeStyle.WhiteHatch;

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行(明度)で sRGB 色域に収まる最大彩度を先に求める。色相固定の L-C 断面では、色域内外の判定・境界線・色域外のクランプ色のすべてがこの行ごとの一値から定まる。これにより画素ごとの彩度二分法(色域外のクランプ)を全廃し、判定を彩度の単純比較に落とす。色域外は同じ行なら明度・色相が同じでクランプ色も一つに定まるため、行ごとに一度だけ色を作って使い回せる。
		double[] maxChroma = new double[pixelHeight];

		Parallel.For(0, pixelHeight, y =>
		{
			double lightness = (1.0 - ((y + 0.5) / pixelHeight)) * lMax;
			maxChroma[y] = LchColor.MaxChroma(space, lightness, hue);
		});

		// 各行は互いに素な画素範囲へ書き込むため、行単位で並列化してよい。画素数が多いとき1スレッドでは塗りきれず色相ドラッグでカクつくため、全コアへ分散する。
		Parallel.For(0, pixelHeight, y =>
		{
			// 上端を明度の最大、下端を明度 0 とし、パッドの縦方向(上ほど明るい)に合わせる。
			double lightness = (1.0 - ((y + 0.5) / pixelHeight)) * lMax;
			int rowBase = (y * pixelWidth) * 4;
			double rowMaxC = maxChroma[y];

			// 上下の行の最大彩度。境界線の縦方向(ヒレの上端・下端)の検出に使う。画像の端には隣の行が無いため、その有無を持っておく。
			bool hasUp = y > 0;
			bool hasDown = y + 1 < pixelHeight;
			double upMaxC = hasUp ? maxChroma[y - 1] : 0.0;
			double downMaxC = hasDown ? maxChroma[y + 1] : 0.0;

			// 色域外の下地色。クランプ色で塗るなら行ごとに一定(明度・色相が同じで、はみ出した彩度はすべて境界へ詰まる)ので行で一度だけ作る。白塗りなら白で固定する。
			byte fillR;
			byte fillG;
			byte fillB;

			if (fillClamped)
			{
				Color clamped = LchColor.ToRgb(space, lightness, rowMaxC, hue);
				(fillR, fillG, fillB) = ColorConversion.Snap(snap, clamped.R, clamped.G, clamped.B);
			}
			else
			{
				fillR = 0xFF;
				fillG = 0xFF;
				fillB = 0xFF;
			}

			for (int x = 0; x < pixelWidth; x++)
			{
				// 左端を彩度 0、右端を表示上限とする。
				double chroma = (x + 0.5) / pixelWidth * cMax;
				int index = rowBase + (x * 4);
				bool ig = chroma <= rowMaxC;

				// 境界線。色域内側の縁の画素には黒、色域外側の縁の画素には白を、斜線と同じ不透明度で下地へ重ねる。縁は、内側=右か上下の隣が色域外の色域内画素、外側=左か上下の隣が色域内の色域外画素で拾う。左右の隣の彩度はこの行の最大彩度と、上下は隣の行の最大彩度と比べる。
				if (showBoundary)
				{
					double chromaLeft = (x - 0.5) / pixelWidth * cMax;
					double chromaRight = (x + 1.5) / pixelWidth * cMax;
					bool hasLeft = x > 0;
					bool hasRight = x + 1 < pixelWidth;

					if (ig)
					{
						bool innerEdge = (hasRight && chromaRight > rowMaxC)
							|| (hasUp && chroma > upMaxC)
							|| (hasDown && chroma > downMaxC);

						if (innerEdge)
						{
							// 下地は実色。その上へ黒を α=HatchBlackAlpha で重ねる。
							Color color = LchColor.ToRgb(space, lightness, chroma, hue);
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
						bool outerEdge = (hasLeft && chromaLeft <= rowMaxC)
							|| (hasUp && chroma <= upMaxC)
							|| (hasDown && chroma <= downMaxC);

						if (outerEdge)
						{
							// 下地はクランプ色。その上へ白を α=HatchWhiteAlpha で重ねる。
							pixels[index] = (byte)(((fillB * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							pixels[index + 1] = (byte)(((fillG * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							pixels[index + 2] = (byte)(((fillR * (255 - HatchWhiteAlpha)) + (255 * HatchWhiteAlpha)) / 255);
							pixels[index + 3] = 0xFF;
							continue;
						}
					}
				}

				if (ig)
				{
					Color color = LchColor.ToRgb(space, lightness, chroma, hue);
					(byte r, byte g, byte b) = ColorConversion.Snap(snap, color.R, color.G, color.B);

					// WriteableBitmap はアルファ乗算済みの BGRA を期待する。色域内は全面不透明のため色をそのまま並べる。
					pixels[index] = b;
					pixels[index + 1] = g;
					pixels[index + 2] = r;
					pixels[index + 3] = 0xFF;
					continue;
				}

				// 色域外。下地(クランプ色か白)を置き、斜線を重ねる設定ならその上へ黒白の斜線を合成する。全面不透明。
				byte or = fillR;
				byte og = fillG;
				byte ob = fillB;

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

		return pixels;
	}
}
