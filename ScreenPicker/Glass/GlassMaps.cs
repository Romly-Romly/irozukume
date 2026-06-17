// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;
using Irozukume.Glass;

namespace Irozukume.ScreenPicker.Glass;

// 屈折・可変ぼかし・白みで使う各種マップを焼く。結果を BGRA8(乗算済みアルファ)のビットマップにする。
// 変位マップとエッジマスクはマップ全体(本体＋余白)サイズ、白みマスクは本体サイズで作る。
internal static class GlassMaps
{
	// 変位マップ。縁付近だけ外向き法線方向のずれを R(横)・G(縦)へ書き、中央と余白はずれ0(灰)にする。アルファは不透明。
	public static CanvasBitmap BuildDisplacementMap(CanvasDevice device, GlassGeometry g, double bevel)
	{
		int w = g.MapW;
		int h = g.MapH;
		var data = new byte[w * h * 4];

		double cx = w / 2.0;
		double cy = h / 2.0;
		double hx = g.CardW / 2.0;
		double hy = g.CardH / 2.0;
		double r = g.Radius;

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				double d = Sdf.RoundedRect(x + 0.5, y + 0.5, cx, cy, hx, hy, r);

				double gx = Sdf.RoundedRect(x + 1, y, cx, cy, hx, hy, r) - Sdf.RoundedRect(x - 1, y, cx, cy, hx, hy, r);
				double gy = Sdf.RoundedRect(x, y + 1, cx, cy, hx, hy, r) - Sdf.RoundedRect(x, y - 1, cx, cy, hx, hy, r);
				double len = Math.Sqrt(gx * gx + gy * gy);
				if (len == 0) len = 1;
				double nx = gx / len;
				double ny = gy / len;

				double depth = -d;
				double t = 0;

				if (depth >= 0 && depth < bevel)
				{
					double k = depth / bevel;
					t = (1 - k) * (1 - k);
				}

				byte rr = Clamp(128 + nx * t * 127);
				byte gg = Clamp(128 + ny * t * 127);
				int i = (y * w + x) * 4;
				data[i + 0] = 128;  // B
				data[i + 1] = gg;   // G
				data[i + 2] = rr;   // R
				data[i + 3] = 255;  // A
			}
		}

		return CanvasBitmap.CreateFromBytes(device, data, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);
	}




	// 縁ほど強い効果用の強さマスク(可変ぼかし・可変の彩度明度強調が共用)。縁で最大、内側へ falloff の幅で 0 まで二次減衰する値をアルファに書く。
	// アルファだけが使われるが、乗算済みアルファとして見た目用に rgb もアルファと同値にしておく。マップ全体サイズ。
	public static CanvasBitmap BuildEdgeMask(CanvasDevice device, GlassGeometry g, double falloff)
	{
		int w = g.MapW;
		int h = g.MapH;
		var data = new byte[w * h * 4];

		double cx = w / 2.0;
		double cy = h / 2.0;
		double hx = g.CardW / 2.0;
		double hy = g.CardH / 2.0;
		double r = g.Radius;
		double width = Math.Max(1, falloff);

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				double depth = -Sdf.RoundedRect(x + 0.5, y + 0.5, cx, cy, hx, hy, r);
				double a;

				if (depth <= 0)
				{
					a = 1;
				}
				else if (depth >= width)
				{
					a = 0;
				}
				else
				{
					double k = depth / width;
					a = (1 - k) * (1 - k);
				}

				byte av = (byte)(a * 255);
				int i = (y * w + x) * 4;
				data[i + 0] = av;
				data[i + 1] = av;
				data[i + 2] = av;
				data[i + 3] = av;
			}
		}

		return CanvasBitmap.CreateFromBytes(device, data, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);
	}




	// 白み強さマスク。本体サイズで、縁で最大、内側へ falloff の幅で 0 まで二次減衰する値をアルファに書く。乗算済みアルファ用に rgb もアルファと同値。
	public static CanvasBitmap BuildTintMask(CanvasDevice device, GlassGeometry g, double falloff)
	{
		int w = g.CardW;
		int h = g.CardH;
		var data = new byte[w * h * 4];

		double cx = w / 2.0;
		double cy = h / 2.0;
		double hx = w / 2.0;
		double hy = h / 2.0;
		double r = g.Radius;
		double width = Math.Max(1, falloff);

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				double depth = -Sdf.RoundedRect(x + 0.5, y + 0.5, cx, cy, hx, hy, r);
				double a = 0;

				if (depth > 0 && depth < width)
				{
					double k = depth / width;
					a = (1 - k) * (1 - k);
				}

				byte av = (byte)(a * 255);
				int i = (y * w + x) * 4;
				data[i + 0] = av;
				data[i + 1] = av;
				data[i + 2] = av;
				data[i + 3] = av;
			}
		}

		return CanvasBitmap.CreateFromBytes(device, data, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);
	}




	// 光沢マップ。内側の縁に沿った面取りの明るみを本体サイズへ焼く。3つの内側グローを合成する。
	// 左上の縁は明るく(法線が左上向きの所、近い帯)、右下の縁は淡く、加えて全周に広い柔らかな内側グローを乗せる。
	// それぞれを白の被覆率とみなし、screen 的に 1-(1-i1)(1-i2)(1-i3) でまとめる。乗算済みアルファの白(rgb=v, a=v)。
	public static CanvasBitmap BuildGlossMap(CanvasDevice device, GlassGeometry g)
	{
		int w = g.CardW;
		int h = g.CardH;
		var data = new byte[w * h * 4];

		double cx = w / 2.0;
		double cy = h / 2.0;
		double hx = w / 2.0;
		double hy = h / 2.0;
		double r = g.Radius;
		const double invSqrt2 = 0.70710678;

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				double depth = -Sdf.RoundedRect(x + 0.5, y + 0.5, cx, cy, hx, hy, r);
				double a = 0;

				if (depth > 0)
				{
					double gx = Sdf.RoundedRect(x + 1, y, cx, cy, hx, hy, r) - Sdf.RoundedRect(x - 1, y, cx, cy, hx, hy, r);
					double gy = Sdf.RoundedRect(x, y + 1, cx, cy, hx, hy, r) - Sdf.RoundedRect(x, y - 1, cx, cy, hx, hy, r);
					double len = Math.Sqrt(gx * gx + gy * gy);
					if (len == 0) len = 1;
					double nx = gx / len;
					double ny = gy / len;

					double topLeft = -(nx + ny) * invSqrt2;
					if (topLeft < 0) topLeft = 0;
					double bottomRight = (nx + ny) * invSqrt2;
					if (bottomRight < 0) bottomRight = 0;

					double i1 = 0.70 * topLeft * Falloff(depth, 5);
					double i2 = 0.25 * bottomRight * Falloff(depth, 5);
					double i3 = 0.18 * Falloff(depth, 26);

					double combined = 1 - (1 - i1) * (1 - i2) * (1 - i3);
					if (combined < 0) combined = 0;
					if (combined > 1) combined = 1;
					a = combined;
				}

				byte v = (byte)(a * 255);
				int i = (y * w + x) * 4;
				data[i + 0] = v;
				data[i + 1] = v;
				data[i + 2] = v;
				data[i + 3] = v;
			}
		}

		return CanvasBitmap.CreateFromBytes(device, data, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);
	}




	// 縁から内側へ reach の幅で 1→0 に二次減衰する falloff。光沢の各グローの届く範囲を決める。
	private static double Falloff(double depth, double reach)
	{
		if (depth >= reach)
		{
			return 0;
		}

		double k = 1 - depth / reach;
		return k * k;
	}




	private static byte Clamp(double v)
	{
		int n = (int)Math.Round(v);
		if (n < 0) n = 0;
		if (n > 255) n = 255;
		return (byte)n;
	}
}
