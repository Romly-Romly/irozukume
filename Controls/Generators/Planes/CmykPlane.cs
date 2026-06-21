// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// CMY のうち2成分を2軸に取った色平面を描いた画像を生成する。
/// 横軸が xChannel(左 0→右 1)、縦軸が yChannel(下 0→上 1)で、パッドの縦方向(上ほど大)と合わせるため上端を 1 とする。
/// 残る1つの CMY 成分(fixedChannel)と墨(K)は与えた値に固定する。CMYK→RGB はどの組み合わせも有効色のため色域外は無く、全画素をそのまま塗る。
/// 色制限が有効なら各色をその制限へ丸めてから並べる。CMYK の各成分は 0–1 の比率で受け取る。
/// </summary>
/// <remarks>固定成分・K・色制限設定が変わるたびに作り直す想定。</remarks>
public static class CmykPlane
{
	// 指定した画素サイズで、xChannel×yChannel の CMY 平面を描いた WriteableBitmap を作る。チャンネルは 0=C, 1=M, 2=Y。fixedChannel を fixedValue に、墨を k に固定し、残る2成分を2軸へ割り当てる。横軸は左端 0→右端 1、縦軸は上端 1→下端 0。色制限が有効なら各画素の色をその制限へ丸める。全面を不透明で塗る。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, int xChannel, int yChannel, int fixedChannel, double fixedValue, double k, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) =>
		{
			// 横軸を 0→1、縦軸を上端 1→下端 0 とする。残る1成分と墨は固定値。
			double xValue = u;
			double yValue = 1.0 - v;
			double c = ChannelValue(0, xChannel, yChannel, fixedChannel, xValue, yValue, fixedValue);
			double m = ChannelValue(1, xChannel, yChannel, fixedChannel, xValue, yValue, fixedValue);
			double yc = ChannelValue(2, xChannel, yChannel, fixedChannel, xValue, yValue, fixedValue);
			return CmykToRgb(c, m, yc, k);
		});
	}




	// 求める成分(channel)の値を、その成分が横軸・縦軸・固定のどれに割り当てられているかで選ぶ。横軸なら xValue、縦軸なら yValue、固定なら fixedValue。いずれも 0–1。
	private static double ChannelValue(int channel, int xChannel, int yChannel, int fixedChannel, double xValue, double yValue, double fixedValue)
	{
		if (channel == xChannel)
		{
			return xValue;
		}

		if (channel == yChannel)
		{
			return yValue;
		}

		return fixedValue;
	}




	// CMYK(各 0–1)を RGB(各バイト)へ変換する。
	private static (byte R, byte G, byte B) CmykToRgb(double c, double m, double y, double k)
	{
		byte r = (byte)Math.Round(255.0 * (1.0 - c) * (1.0 - k));
		byte g = (byte)Math.Round(255.0 * (1.0 - m) * (1.0 - k));
		byte b = (byte)Math.Round(255.0 * (1.0 - y) * (1.0 - k));
		return (r, g, b);
	}




	// CMYK の4本のスライダー(C・M・Y・K を各 0→1 へ振ったランプ)を縦に4段並べた、レイアウトピッカーの「スライダー」見本を描く。各段は他成分を 0 に固定した当該成分単独のランプ(C・M・Y は白→純色、K は白→黒)で、平面レイアウトのサムネイルと並べて見せ方の違いを示す。
	public static WriteableBitmap CreateSlidersIcon(int pixelWidth, int pixelHeight)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		for (int y = 0; y < pixelHeight; y++)
		{
			// 上段から C・M・Y・K。4等分して各成分のランプを割り当てる。
			int band = Math.Min(3, y * 4 / pixelHeight);
			int rowBase = y * pixelWidth * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				double v = (x + 0.5) / pixelWidth;
				double c = band == 0 ? v : 0.0;
				double m = band == 1 ? v : 0.0;
				double yc = band == 2 ? v : 0.0;
				double k = band == 3 ? v : 0.0;
				(byte r, byte g, byte b) = CmykToRgb(c, m, yc, k);
				int index = rowBase + (x * 4);

				pixels[index] = b;
				pixels[index + 1] = g;
				pixels[index + 2] = r;
				pixels[index + 3] = 0xFF;
			}
		}

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(pixels, 0, pixels.Length);
		}

		bitmap.Invalidate();
		return bitmap;
	}
}
