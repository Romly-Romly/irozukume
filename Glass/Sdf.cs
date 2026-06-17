// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;

namespace Irozukume.Glass;

// 角丸長方形の符号付き距離など、マップ生成で共用する形状の数学。
internal static class Sdf
{
	// 中心 (cx,cy)、半幅 hx・半高 hy、角丸半径 r の角丸長方形の符号付き距離。内側が負、外側が正。
	public static double RoundedRect(double px, double py, double cx, double cy, double hx, double hy, double r)
	{
		double qx = Math.Abs(px - cx) - (hx - r);
		double qy = Math.Abs(py - cy) - (hy - r);
		double ax = Math.Max(qx, 0);
		double ay = Math.Max(qy, 0);
		double outside = Math.Sqrt(ax * ax + ay * ay);
		double inside = Math.Min(Math.Max(qx, qy), 0);

		return outside + inside - r;
	}
}
