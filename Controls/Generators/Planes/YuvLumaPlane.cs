// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;
using static Irozukume.Controls.Generators.YuvGamutMath;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 片方の色差(Cb または Cr)を横軸、輝度 Y を縦軸に取った YCbCr の断面を描いた画像を生成する。
/// 固定する側の色差は引数の一定値とする。
/// Cb×Y は横軸が Cb(左 0→右 255)・縦軸が Y(上 255→下 0)で Cr を固定、Cr×Y は横軸が Cr・縦軸が Y で Cb を固定する。
/// 各画素を指定形式の YCbCr→RGB へ変換し、RGB が 0–255 に収まる有効ガモットの内側はその色をそのまま塗る。
/// 収まらない外側の見せ方は GamutOutOfRangeStyle で切り替え、CbCrPlane と寸法・色をそろえる。
/// 色制限が有効なら有効ガモット内の色をその制限へ丸めてから並べる。
/// </summary>
/// <remarks>固定の色差・符号化形式・色域外の見せ方が変わるたびに作り直す想定。</remarks>
public static class YuvLumaPlane
{
	// 色域外を示す斜線ハッチ。線幅(垂直)0.7 DIP、周期(x+y 方向)10 DIP。45 度の線のため、x+y 方向の帯幅は線幅の √2 倍にあたる。CbCrPlane・L-C 平面・a-b 平面のハッチと寸法・色をそろえる。
	private const double HatchLineWidth = 0.7;
	private const double HatchPeriod = 10.0;

	// 黒線・白線の不透明度。黒は 0.5、白は 0.3。明るい背景では黒が、暗い背景では白が効く。色域外のハッチと、色域境界線の黒白二重線の双方でこの不透明度を使う。
	private const byte HatchBlackAlpha = 0x80;
	private const byte HatchWhiteAlpha = 0x4D;


	// 指定した画素サイズで、固定 Cr・符号化形式・色制限設定・表示倍率・色域外の見せ方・表示枠の決め方の Cb×Y 断面を描いた WriteableBitmap を作る。横軸が Cb(左 0→右 255)、縦軸が Y(上 255→下 0)、Cr は引数の一定値。表示枠は AbFitMode で決め、None は全域(横 0–255・縦 0–255)の固定枠、フィット時はその固定 Cr で色域が収まる範囲へ寄せる(YuvColor.CbLumaExtentFor)。
	public static WriteableBitmap CreateCbY(int pixelWidth, int pixelHeight, double fixedCr, YCbCrFormat format, SnapSettings snap, double scale, GamutOutOfRangeStyle style, AbFitMode fit)
	{
		return Render(pixelWidth, pixelHeight, true, fixedCr, format, snap, scale, style, fit);
	}




	// 指定した画素サイズで、固定 Cb・符号化形式・色制限設定・表示倍率・色域外の見せ方・表示枠の決め方の Cr×Y 断面を描いた WriteableBitmap を作る。横軸が Cr(左 0→右 255)、縦軸が Y(上 255→下 0)、Cb は引数の一定値。表示枠は AbFitMode で決め、None は全域(横 0–255・縦 0–255)の固定枠、フィット時はその固定 Cb で色域が収まる範囲へ寄せる(YuvColor.CrLumaExtentFor)。
	public static WriteableBitmap CreateCrY(int pixelWidth, int pixelHeight, double fixedCb, YCbCrFormat format, SnapSettings snap, double scale, GamutOutOfRangeStyle style, AbFitMode fit)
	{
		return Render(pixelWidth, pixelHeight, false, fixedCb, format, snap, scale, style, fit);
	}




	// 片方の色差×輝度の断面を描いた WriteableBitmap を作る。horizontalIsCb が真なら横軸が Cb で固定成分が Cr(Cb×Y)、偽なら横軸が Cr で固定成分が Cb(Cr×Y)。全面を不透明で塗り、色域内は実色(色制限が有効ならその制限へ丸めた色)、色域外は style に従う。ハッチは DIP 指定の寸法を表示倍率で画素へ直し、CbCrPlane と太さ・間隔をそろえる。
	private static WriteableBitmap Render(int pixelWidth, int pixelHeight, bool horizontalIsCb, double fixedChroma, YCbCrFormat format, SnapSettings snap, double scale, GamutOutOfRangeStyle style, AbFitMode fit)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		// 表示枠。横軸(色差)を左端 XMin→右端 XMax、縦軸(輝度 Y)を上端 YMax→下端 YMin へ線形に割り当てる。None の固定枠では (0, 255) の全域、フィット時は色域の広がりに合わせた枠になる。
		PlaneExtent extent = horizontalIsCb
			? YuvColor.CbLumaExtentFor(format, fixedChroma, fit)
			: YuvColor.CrLumaExtentFor(format, fixedChroma, fit);

		// DIP 指定のハッチを、ビットマップの画素寸法(表示倍率)に合わせて画素単位へ直す。周期は x+y 方向の量、線の帯幅は垂直の線幅を 45 度の x+y 方向へ直した √2 倍。
		double hatchPeriod = HatchPeriod * scale;
		double hatchBand = HatchLineWidth * Math.Sqrt(2.0) * scale;

		// 色域外の見せ方を要素へ分解する。クランプ色で塗るか(白塗りでないか)、斜線を重ねるか、境界線を引くか。
		bool fillClamped = style != GamutOutOfRangeStyle.WhiteHatch;
		bool showHatch = style == GamutOutOfRangeStyle.FillBoundaryHatch || style == GamutOutOfRangeStyle.WhiteHatch;
		bool showBoundary = style != GamutOutOfRangeStyle.WhiteHatch;

		// YCbCr→RGB の係数は形式だけで決まるため先に求める。色差に掛かる追加係数(スタジオレンジは 255/224)もここで畳む。
		double kr = format.Kr;
		double kb = format.Kb;
		double kg = format.Kg;
		double chromaScale = format.FullRange ? 1.0 : (255.0 / 224.0);
		double rCoef = 2.0 * (1.0 - kr) * chromaScale;
		double bCoef = 2.0 * (1.0 - kb) * chromaScale;
		double gPrCoef = (kr / kg) * rCoef;
		double gPbCoef = (kb / kg) * bCoef;

		// 固定成分(Cr または Cb)のオフセット。横軸に取らない側で全画素に共通。
		double fixedOffset = fixedChroma - 128.0;

		// 各画素が有効ガモットの内側かを先に一面ぶん求めておく。境界線の検出(内外の隣接判定)が上下左右の隣の内外を要するため、画素ごとに隣を測り直さずこの表を読む。横軸(色差)・縦軸(Y)の両方に依って二次元に変わる。
		bool[] inGamut = new bool[pixelWidth * pixelHeight];

		Parallel.For(0, pixelHeight, y =>
		{
			double luma = extent.YMax - ((y + 0.5) / pixelHeight) * extent.YHeight;
			double lumaLinear = format.FullRange ? luma : (luma - 16.0) * (255.0 / 219.0);
			int rowBase = y * pixelWidth;

			for (int x = 0; x < pixelWidth; x++)
			{
				double axisOffset = extent.XMin + ((x + 0.5) / pixelWidth) * extent.XWidth - 128.0;
				double cbOffset = horizontalIsCb ? axisOffset : fixedOffset;
				double crOffset = horizontalIsCb ? fixedOffset : axisOffset;
				double rr = lumaLinear + (crOffset * rCoef);
				double bb = lumaLinear + (cbOffset * bCoef);
				double gg = lumaLinear - (gPrCoef * crOffset) - (gPbCoef * cbOffset);
				inGamut[rowBase + x] = InRange(rr) && InRange(gg) && InRange(bb);
			}
		});

		// 行を全コアへ割り振る前に、最近傍探索の前計算表を単一スレッドで一度温めておく。並列ループ中に複数スレッドが同時に初回構築へ入るのを避ける。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 各行は互いに素な画素範囲へ書き込み、内外表は読み取るだけなので、行単位で並列化してよい。
		Parallel.For(0, pixelHeight, y =>
		{
			double luma = extent.YMax - ((y + 0.5) / pixelHeight) * extent.YHeight;
			double lumaLinear = format.FullRange ? luma : (luma - 16.0) * (255.0 / 219.0);
			double crRow = horizontalIsCb ? lumaLinear : (lumaLinear + (fixedOffset * rCoef));
			int rowIndex = y * pixelWidth;
			int rowBase = rowIndex * 4;

			bool hasUp = y > 0;
			bool hasDown = y + 1 < pixelHeight;

			for (int x = 0; x < pixelWidth; x++)
			{
				double axisOffset = extent.XMin + ((x + 0.5) / pixelWidth) * extent.XWidth - 128.0;
				double cbOffset = horizontalIsCb ? axisOffset : fixedOffset;
				double crOffset = horizontalIsCb ? fixedOffset : axisOffset;
				double rr = lumaLinear + (crOffset * rCoef);
				double bb = lumaLinear + (cbOffset * bCoef);
				double gg = lumaLinear - (gPrCoef * crOffset) - (gPbCoef * cbOffset);
				int index = rowBase + (x * 4);
				bool ig = inGamut[rowIndex + x];

				// 境界線。色域内側の縁の画素には黒、色域外側の縁の画素には白を、斜線と同じ不透明度で下地へ重ねる。縁は上下左右いずれかの隣が反対側であることで拾う。
				if (showBoundary)
				{
					bool hasLeft = x > 0;
					bool hasRight = x + 1 < pixelWidth;

					if (ig)
					{
						bool innerEdge = (hasRight && !inGamut[rowIndex + x + 1])
							|| (hasLeft && !inGamut[rowIndex + x - 1])
							|| (hasUp && !inGamut[rowIndex - pixelWidth + x])
							|| (hasDown && !inGamut[rowIndex + pixelWidth + x]);

						if (innerEdge)
						{
							// 下地は実色。その上へ黒を α=HatchBlackAlpha で重ねる。
							(byte br, byte bg, byte bb2) = SnapIf(snap, ClampByte(rr), ClampByte(gg), ClampByte(bb));
							pixels[index] = (byte)(bb2 * (255 - HatchBlackAlpha) / 255);
							pixels[index + 1] = (byte)(bg * (255 - HatchBlackAlpha) / 255);
							pixels[index + 2] = (byte)(br * (255 - HatchBlackAlpha) / 255);
							pixels[index + 3] = 0xFF;
							continue;
						}
					}
					else
					{
						bool outerEdge = (hasLeft && inGamut[rowIndex + x - 1])
							|| (hasRight && inGamut[rowIndex + x + 1])
							|| (hasUp && inGamut[rowIndex - pixelWidth + x])
							|| (hasDown && inGamut[rowIndex + pixelWidth + x]);

						if (outerEdge)
						{
							// 下地はクランプ色(白塗りなら白)。その上へ白を α=HatchWhiteAlpha で重ねる。
							(byte fr, byte fg, byte fb) = FillColor(fillClamped, snap, lumaLinear, rr, gg, bb);
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
					(byte r, byte g, byte b) = SnapIf(snap, ClampByte(rr), ClampByte(gg), ClampByte(bb));

					// WriteableBitmap はアルファ乗算済みの BGRA を期待する。色域内は全面不透明のため色をそのまま並べる。
					pixels[index] = b;
					pixels[index + 1] = g;
					pixels[index + 2] = r;
					pixels[index + 3] = 0xFF;
					continue;
				}

				// 色域外。下地(クランプ色か白)を置き、斜線を重ねる設定ならその上へ黒白の斜線を合成する。全面不透明。
				(byte or, byte og, byte ob) = FillColor(fillClamped, snap, lumaLinear, rr, gg, bb);

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
}
