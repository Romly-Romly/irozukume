// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;

namespace Irozukume.Glass;

// 鏡面ハイライトを本体サイズの1枚へ焼く。各灯は自分の膨らみ・カーブのドーム法線に対して鏡面反射 (N・H)^exp を計算し、明るさを加算合成する。
// 重い法線場の生成(BuildNormalSet)と鏡面の焼成(Bake)を分ける。法線は形状と膨らみだけで決まり光の角度に依存しないので、仰角を変えて毎フレーム焼き直す場合も法線は使い回し、鏡面ループだけを回せばよい。
// 出力は乗算済みアルファの白(rgb=v, a=v)で、加算合成すれば screen と近い明るみになる。
// 焼く対象は幅 w・高さ h・角丸半径 radius の角丸長方形のドーム。円形のレンズは半径=幅÷2 の特殊形として同じ経路で焼ける。
internal static class HighlightBaker
{
	internal readonly struct Normals
	{
		public Normals(float[] nx, float[] ny, float[] nz)
		{
			Nx = nx;
			Ny = ny;
			Nz = nz;
		}

		public float[] Nx { get; }
		public float[] Ny { get; }
		public float[] Nz { get; }
	}




	// 焼き直しの間で使い回す法線場の束。法線は形状と膨らみだけで決まり光の角度に依存しないので、仰角を変えて焼き直すときも作り直さずに済む。
	internal sealed class NormalSet
	{
		private readonly Dictionary<string, Normals> _map;

		internal NormalSet(Dictionary<string, Normals> map)
		{
			_map = map;
		}




		internal bool TryGet(string key, out Normals n)
		{
			return _map.TryGetValue(key, out n);
		}
	}




	// 灯に現れる全ての膨らみ・カーブの組について法線場を求めてキャッシュする。仰角追従で毎フレーム焼き直す際、この重い計算を一度きりにするために使う。
	internal static NormalSet BuildNormalSet(int w, int h, double radius, IReadOnlyList<Highlight> lights)
	{
		var map = new Dictionary<string, Normals>();

		foreach (Highlight l in lights)
		{
			string key = l.Curve + "|" + l.Dome;
			if (!map.ContainsKey(key))
			{
				map[key] = BuildNormals(w, h, radius, l.Dome, l.Curve);
			}
		}

		return new NormalSet(map);
	}




	// 鏡面ハイライトを本体サイズの1枚へ焼く。各灯は渡された法線場に対して鏡面反射 (N・H)^exp を計算し、明るさを加算合成する。
	// elevOffsetDeg は全ての灯の仰角へ加える角度で、光源との距離から艶を中央⇔リムへ寄せる仰角追従に使う。法線は使い回されるので重い法線計算は走らない。
	internal static CanvasBitmap Bake(CanvasDevice device, int w, int h, double radius, NormalSet normals, IReadOnlyList<Highlight> lights, double elevOffsetDeg)
	{
		int total = w * h;

		var acc = new float[total];

		foreach (Highlight l in lights)
		{
			string key = l.Curve + "|" + l.Dome;
			if (!normals.TryGet(key, out Normals nrm))
			{
				nrm = BuildNormals(w, h, radius, l.Dome, l.Curve);
			}

			float[] nx = nrm.Nx;
			float[] ny = nrm.Ny;
			float[] nz = nrm.Nz;

			double elevDeg = l.Elev + elevOffsetDeg;
			if (elevDeg < 0) elevDeg = 0;
			if (elevDeg > 90) elevDeg = 90;

			double az = l.Azim * Math.PI / 180;
			double el = elevDeg * Math.PI / 180;
			double ce = Math.Cos(el);
			double hvx = Math.Cos(az) * ce;
			double hvy = Math.Sin(az) * ce;
			double hvz = Math.Sin(el) + 1;
			double hlen = Math.Sqrt(hvx * hvx + hvy * hvy + hvz * hvz);
			if (hlen == 0) hlen = 1;
			hvx /= hlen;
			hvy /= hlen;
			hvz /= hlen;

			double baseV = l.Flat * hvz;
			double denom = (1 - baseV) > 1e-3 ? (1 - baseV) : 1e-3;
			double exp = l.Exp;
			double power = l.Power;

			for (int p = 0; p < total; p++)
			{
				double d = nx[p] * hvx + ny[p] * hvy + nz[p] * hvz;

				double dd = (d - baseV) / denom;
				if (dd < 0) dd = 0;
				if (dd > 1) dd = 1;

				acc[p] += (float)(power * Math.Pow(dd, exp));
			}
		}

		var data = new byte[total * 4];
		for (int p = 0; p < total; p++)
		{
			double a = acc[p];
			if (a > 1) a = 1;

			byte v = (byte)(a * 255);
			int i = p * 4;
			data[i + 0] = v;
			data[i + 1] = v;
			data[i + 2] = v;
			data[i + 3] = v;
		}

		return CanvasBitmap.CreateFromBytes(device, data, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);
	}




	// 指定した膨らみ・カーブのドーム高さ場から、各画素の法線(nx, ny, nz)を float で求める。
	// 微分は float の高さ場に対して行い、量子化した値を微分しないので等高線状のバンディングが出ない。
	private static Normals BuildNormals(int w, int h, double radius, double dome, string curve)
	{
		double cx = w / 2.0;
		double cy = h / 2.0;
		double hx = w / 2.0;
		double hy = h / 2.0;
		double maxDepth = Math.Min(hx, hy);
		const double surfaceScale = 36;

		var ht = new float[w * h];

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				double depth = -Sdf.RoundedRect(x + 0.5, y + 0.5, cx, cy, hx, hy, radius);

				double u = depth / maxDepth;
				if (u < 0) u = 0;
				if (u > 1) u = 1;

				double domeH;

				if (curve == "sphere")
				{
					double a = 2 * u - u * u;
					domeH = Math.Sqrt(a < 0 ? 0 : a);
				}
				else if (curve == "convex")
				{
					domeH = 2 * u - u * u;
				}
				else
				{
					domeH = u * u * u * (u * (u * 6 - 15) + 10);
				}

				ht[y * w + x] = (float)(dome * domeH * surfaceScale);
			}
		}

		var nx = new float[w * h];
		var ny = new float[w * h];
		var nz = new float[w * h];

		for (int y = 0; y < h; y++)
		{
			int ym = (y > 0) ? y - 1 : y;
			int yp = (y < h - 1) ? y + 1 : y;

			for (int x = 0; x < w; x++)
			{
				int xm = (x > 0) ? x - 1 : x;
				int xp = (x < w - 1) ? x + 1 : x;

				double dzx = (ht[y * w + xp] - ht[y * w + xm]) / (double)(xp - xm);
				double dzy = (ht[yp * w + x] - ht[ym * w + x]) / (double)(yp - ym);

				double vx = -dzx;
				double vy = -dzy;
				double vz = 1;
				double len = Math.Sqrt(vx * vx + vy * vy + vz * vz);
				if (len == 0) len = 1;

				int i = y * w + x;
				nx[i] = (float)(vx / len);
				ny[i] = (float)(vy / len);
				nz[i] = (float)(vz / len);
			}
		}

		return new Normals(nx, ny, nz);
	}
}
