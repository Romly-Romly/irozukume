// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 2つの RGB 成分を2軸に取った色平面を描いた画像を生成する。
/// 横軸が xChannel(左 0→右 255)、縦軸が yChannel(下 0→上 255)で、パッドの縦方向(上ほど大)と合わせるため上端を 255 とする。
/// 残る1成分(fixedChannel)は与えた固定値に保つ。
/// RGB はどの組み合わせも有効色のため色域外は無く、全画素をそのまま塗る。
/// 色制限が有効なら各色をその制限へ丸めてから並べる。
/// </summary>
/// <remarks>固定成分の値・色制限設定が変わるたびに作り直す想定。</remarks>
public static class RgbPlane
{
	// 指定した画素サイズで、xChannel×yChannel の RGB 平面を描いた WriteableBitmap を作る。fixedChannel(0=R, 1=G, 2=B)を fixedValue に固定し、残る2成分を2軸へ割り当てる。横軸は左端 0→右端 255、縦軸は上端 255→下端 0。色制限が有効なら各画素の色をその制限へ丸める。全面を不透明で塗る。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, int xChannel, int yChannel, int fixedChannel, byte fixedValue, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) =>
		{
			// 横軸を 0→255、縦軸を上端 255→下端 0 とする。残る1成分は固定値。
			byte xValue = (byte)Math.Round(u * 255.0);
			byte yValue = (byte)Math.Round((1.0 - v) * 255.0);
			byte r = ChannelByte(0, xChannel, yChannel, fixedChannel, xValue, yValue, fixedValue);
			byte g = ChannelByte(1, xChannel, yChannel, fixedChannel, xValue, yValue, fixedValue);
			byte b = ChannelByte(2, xChannel, yChannel, fixedChannel, xValue, yValue, fixedValue);
			return (r, g, b);
		});
	}




	// 求める成分(channel)の値を、その成分が横軸・縦軸・固定のどれに割り当てられているかで選ぶ。横軸なら xValue、縦軸なら yValue、固定なら fixedValue。
	private static byte ChannelByte(int channel, int xChannel, int yChannel, int fixedChannel, byte xValue, byte yValue, byte fixedValue)
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




	// RGB の3本のスライダー(R・G・B を各 0→255 へ振った水平ランプ)を縦に3段並べた、レイアウトピッカーの「スライダー」見本を描く。各段は他成分を 0 に固定した当該成分単独のランプ(黒→純色)で、平面レイアウトのサムネイルと並べて見せ方の違いを示す。
	public static WriteableBitmap CreateSlidersIcon(int pixelWidth, int pixelHeight)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		for (int y = 0; y < pixelHeight; y++)
		{
			// 上段=R、中段=G、下段=B。3等分して各成分のランプを割り当てる。
			int band = Math.Min(2, y * 3 / pixelHeight);
			int rowBase = y * pixelWidth * 4;

			for (int x = 0; x < pixelWidth; x++)
			{
				byte v = (byte)Math.Round((x + 0.5) / pixelWidth * 255.0);
				byte r = band == 0 ? v : (byte)0;
				byte g = band == 1 ? v : (byte)0;
				byte b = band == 2 ? v : (byte)0;
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
