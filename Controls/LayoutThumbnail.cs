// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls;

// 見せ方(レイアウト)ピッカーのサムネイルのうち、中央の2次元面だけでは区別がつかない配置を、構造込みで描き分けるための合成画像を作る。色相リング+正方形/三角形は中央の形を外周の色相リングの中へ、正方形/三角形+色相の縦スライダーは中央の形の右へ縦の色相バーを添える。これにより「リング+形」と「形+バー」を見分けられる。円盤・直交パッドの配置は下地そのもので既に区別がつくため、こちらは使わない(構造を足すと中央が小さくなるため)。各形の下地は呼び出し側がファクトリで与え、表色系(HSL/HWB)に依らず合成だけを担う。
public static class LayoutThumbnail
{
	// 中央の形を外周の色相リングの中へ重ねたサムネイルを作る。リングの帯は CreateRingThumbnail と同じ太さ。shapeScale は内円の直径に対する形の大きさの割合で、内円に内接する正方形は 1/√2、内円に外接円を合わせる三角形は 1。形は中央へ配置し、リングの透明な内側へ重ねる。色制限はリングと形の双方へ掛かる(形はファクトリ側で掛ける)。
	public static WriteableBitmap RingWithShape(int pixels, double shapeScale, SnapSettings snap, Func<int, WriteableBitmap> shapeFactory)
	{
		double outer = (pixels / 2.0) - 1.0;
		double inner = outer * 0.62;
		WriteableBitmap ring = HueWheel.Create(pixels, pixels, inner, outer, snap);
		int shapeSize = Math.Max(1, (int)Math.Round(2.0 * inner * shapeScale));
		WriteableBitmap shape = shapeFactory(shapeSize);

		byte[] ringPixels = ReadPixels(ring);
		byte[] shapePixels = ReadPixels(shape);
		int offset = (pixels - shapeSize) / 2;

		// 形(前景)を色相リング(背景)の中央へ重ねる。どちらもアルファ乗算済みの BGRA のため、over 合成は out = src + dst·(1−src_a) で済む。リングの内側は透明なので、その上に形がそのまま乗る。
		for (int y = 0; y < shapeSize; y++)
		{
			int destY = offset + y;

			if (destY < 0 || destY >= pixels)
			{
				continue;
			}

			for (int x = 0; x < shapeSize; x++)
			{
				int destX = offset + x;

				if (destX < 0 || destX >= pixels)
				{
					continue;
				}

				int si = ((y * shapeSize) + x) * 4;
				byte alpha = shapePixels[si + 3];

				if (alpha == 0)
				{
					continue;
				}

				int di = ((destY * pixels) + destX) * 4;
				int inv = 255 - alpha;
				ringPixels[di] = (byte)(shapePixels[si] + (ringPixels[di] * inv / 255));
				ringPixels[di + 1] = (byte)(shapePixels[si + 1] + (ringPixels[di + 1] * inv / 255));
				ringPixels[di + 2] = (byte)(shapePixels[si + 2] + (ringPixels[di + 2] * inv / 255));
				ringPixels[di + 3] = (byte)(alpha + (ringPixels[di + 3] * inv / 255));
			}
		}

		return WritePixels(pixels, pixels, ringPixels);
	}




	// 中央の形の右へ縦の色相バーを添えたサムネイルを作る。形は正方形で左に、色相バー(下端=色相0度・上端=色相360度)は右に置き、両者は同じ高さで縦中央ぞろえにする。色相を縦スライダーで決める配置(正方形/三角形+色相)を表す。
	public static WriteableBitmap ShapeWithBar(int pixels, SnapSettings snap, Func<int, WriteableBitmap> shapeFactory)
	{
		int barWidth = Math.Max(3, (int)Math.Round(pixels / 6.0));
		int gap = Math.Max(1, (int)Math.Round(pixels / 20.0));
		int shapeSize = Math.Max(1, pixels - barWidth - gap);
		WriteableBitmap shape = shapeFactory(shapeSize);
		WriteableBitmap bar = CreateHueBar(barWidth, shapeSize, snap);

		byte[] shapePixels = ReadPixels(shape);
		byte[] barPixels = ReadPixels(bar);
		byte[] outPixels = new byte[pixels * pixels * 4];
		int yOffset = (pixels - shapeSize) / 2;
		int barX = pixels - barWidth;

		// 形を左、色相バーを右へ、いずれも縦中央へ並べる。重なりは無いため行ごとに素直に複写する。間と上下の余白は透明のまま残す。
		for (int y = 0; y < shapeSize; y++)
		{
			int destRow = ((yOffset + y) * pixels) * 4;
			Array.Copy(shapePixels, (y * shapeSize) * 4, outPixels, destRow, shapeSize * 4);
			Array.Copy(barPixels, (y * barWidth) * 4, outPixels, destRow + (barX * 4), barWidth * 4);
		}

		return WritePixels(pixels, pixels, outPixels);
	}




	// 縦の色相バーの下地を作る。各行の中心の色相(下端 0 度→上端 360 度)を全彩度・全明度で塗り、色制限が有効ならその丸めも掛ける。全面不透明。
	private static WriteableBitmap CreateHueBar(int width, int height, SnapSettings snap)
	{
		byte[] pixels = new byte[width * height * 4];

		// 最近傍探索の前計算表を単一スレッドで一度温めておく。
		ColorConversion.Snap(snap, 0, 0, 0);

		for (int y = 0; y < height; y++)
		{
			double hue = (1.0 - ((y + 0.5) / height)) * 360.0;
			(byte r, byte g, byte b) = ColorConversion.HsvToRgb(hue, 1.0, 1.0);
			(r, g, b) = ColorConversion.Snap(snap, r, g, b);
			int rowBase = (y * width) * 4;

			for (int x = 0; x < width; x++)
			{
				int i = rowBase + (x * 4);
				pixels[i] = b;
				pixels[i + 1] = g;
				pixels[i + 2] = r;
				pixels[i + 3] = 0xFF;
			}
		}

		return WritePixels(width, height, pixels);
	}




	// WriteableBitmap のアルファ乗算済み BGRA 画素を byte 配列へ読み出す。
	private static byte[] ReadPixels(WriteableBitmap bitmap)
	{
		byte[] buffer = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			// Stream.Read は要求未満で返ることがあるため、全画素を確実に読み切る。
			stream.ReadExactly(buffer, 0, buffer.Length);
		}

		return buffer;
	}




	// byte 配列の BGRA 画素から WriteableBitmap を作る。
	private static WriteableBitmap WritePixels(int width, int height, byte[] pixels)
	{
		var bitmap = new WriteableBitmap(width, height);

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(pixels, 0, pixels.Length);
		}

		bitmap.Invalidate();
		return bitmap;
	}
}
