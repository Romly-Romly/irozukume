// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.Foundation;

namespace Irozukume.Controls;

// 三角形パッド(TrianglePad)と HSL 三角形画像(HslTriangle)が共有する幾何計算。三角形を HSL の双錐(バイコーン)の色相断面として扱い、純色・白・黒を頂点に取る。彩度はその輝度での三角形の幅に対する割合、輝度は白の頂点へ近いほど大きい、という対応を一箇所にまとめ、つまみの位置・当たり判定と画像の色がずれないようにする。未回転では純色の頂点を真上、黒を左下、白を右下に置く。
public static class TriangleGeometry
{
	// 与えられた領域に内接する正三角形の3頂点を求める。中心に外接円(半径 = 短辺の半分)を取り、純色を真上、黒を左下、白を右下に置く。回転しても領域からはみ出さないよう外接円を領域に収める。
	public static TriangleVertices ComputeVertices(double width, double height)
	{
		double centerX = width / 2.0;
		double centerY = height / 2.0;
		double radius = Math.Min(width, height) / 2.0;
		double halfBase = radius * Math.Sqrt(3.0) / 2.0;

		var hue = new Point(centerX, centerY - radius);
		var black = new Point(centerX - halfBase, centerY + (radius / 2.0));
		var white = new Point(centerX + halfBase, centerY + (radius / 2.0));
		return new TriangleVertices(hue, black, white);
	}




	// 彩度・輝度(各 0–1)を重心座標(純色・黒・白の重み)へ変換する。純色の重みがクロマ(= S かつ輝度での幅割合)に、白の重みが輝度を担う。
	public static (double Hue, double Black, double White) SlToBarycentric(double saturation, double lightness)
	{
		double chroma = saturation * (1.0 - Math.Abs((2.0 * lightness) - 1.0));
		double white = lightness - (0.5 * chroma);
		double black = 1.0 - chroma - white;
		return (chroma, black, white);
	}




	// 重心座標(純色・黒・白の重み)を彩度・輝度(各 0–1)へ戻す。輝度は白と純色の重みから、彩度はクロマをその輝度での最大クロマで割って求める。最大クロマが 0(黒・白の極)なら彩度は 0 とする。
	public static (double Saturation, double Lightness) BarycentricToSl(double hue, double black, double white)
	{
		double lightness = white + (0.5 * hue);
		double maxChroma = 1.0 - Math.Abs((2.0 * lightness) - 1.0);
		double saturation = maxChroma <= 0.0 ? 0.0 : hue / maxChroma;
		return (Math.Clamp(saturation, 0.0, 1.0), Math.Clamp(lightness, 0.0, 1.0));
	}




	// 重心座標(純色・黒・白の重み)を、与えた頂点での位置へ変換する。
	public static Point BarycentricToPoint(double hue, double black, double white, TriangleVertices vertices)
	{
		double x = (hue * vertices.Hue.X) + (black * vertices.Black.X) + (white * vertices.White.X);
		double y = (hue * vertices.Hue.Y) + (black * vertices.Black.Y) + (white * vertices.White.Y);
		return new Point(x, y);
	}




	// 位置を、与えた頂点に対する重心座標(純色・黒・白の重み)へ変換する。三角形が退化していなければ重みの和は 1 になる。
	public static (double Hue, double Black, double White) PointToBarycentric(Point point, TriangleVertices vertices)
	{
		double x1 = vertices.Hue.X;
		double y1 = vertices.Hue.Y;
		double x2 = vertices.Black.X;
		double y2 = vertices.Black.Y;
		double x3 = vertices.White.X;
		double y3 = vertices.White.Y;

		double denominator = ((y2 - y3) * (x1 - x3)) + ((x3 - x2) * (y1 - y3));

		if (denominator == 0.0)
		{
			return (1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);
		}

		double hue = (((y2 - y3) * (point.X - x3)) + ((x3 - x2) * (point.Y - y3))) / denominator;
		double black = (((y3 - y1) * (point.X - x3)) + ((x1 - x3) * (point.Y - y3))) / denominator;
		double white = 1.0 - hue - black;
		return (hue, black, white);
	}




	// 重心座標を三角形の内側へ収める。負の重みを 0 にして総和で正規化することで、外側の点を最寄りの辺・頂点へ寄せる。すべて 0 になった場合は重心へ落とす。
	public static (double Hue, double Black, double White) ClampBarycentric(double hue, double black, double white)
	{
		double h = Math.Max(hue, 0.0);
		double b = Math.Max(black, 0.0);
		double w = Math.Max(white, 0.0);
		double sum = h + b + w;

		if (sum <= 0.0)
		{
			return (1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);
		}

		return (h / sum, b / sum, w / sum);
	}




	// 点から三角形までの符号付き距離(内側が負)。各辺の線分への最短距離の最小を取り、内外で符号を付ける。角丸三角形の被覆判定に使う。
	public static double SignedDistanceToTriangle(Point p, TriangleVertices v)
	{
		double p0x = v.Hue.X, p0y = v.Hue.Y;
		double p1x = v.Black.X, p1y = v.Black.Y;
		double p2x = v.White.X, p2y = v.White.Y;

		double e0x = p1x - p0x, e0y = p1y - p0y;
		double e1x = p2x - p1x, e1y = p2y - p1y;
		double e2x = p0x - p2x, e2y = p0y - p2y;

		double v0x = p.X - p0x, v0y = p.Y - p0y;
		double v1x = p.X - p1x, v1y = p.Y - p1y;
		double v2x = p.X - p2x, v2y = p.Y - p2y;

		double pq0x = v0x - (e0x * Clamp01Dot(v0x, v0y, e0x, e0y)), pq0y = v0y - (e0y * Clamp01Dot(v0x, v0y, e0x, e0y));
		double pq1x = v1x - (e1x * Clamp01Dot(v1x, v1y, e1x, e1y)), pq1y = v1y - (e1y * Clamp01Dot(v1x, v1y, e1x, e1y));
		double pq2x = v2x - (e2x * Clamp01Dot(v2x, v2y, e2x, e2y)), pq2y = v2y - (e2y * Clamp01Dot(v2x, v2y, e2x, e2y));

		// 三角形の向き(時計回り/反時計回り)に合わせ、内側で各辺の外積が負になるよう符号を取る。
		double s = Math.Sign((e0x * e2y) - (e0y * e2x));

		double d0Dist = (pq0x * pq0x) + (pq0y * pq0y), d0Side = s * ((v0x * e0y) - (v0y * e0x));
		double d1Dist = (pq1x * pq1x) + (pq1y * pq1y), d1Side = s * ((v1x * e1y) - (v1y * e1x));
		double d2Dist = (pq2x * pq2x) + (pq2y * pq2y), d2Side = s * ((v2x * e2y) - (v2y * e2x));

		double dist = Math.Min(Math.Min(d0Dist, d1Dist), d2Dist);
		double side = Math.Min(Math.Min(d0Side, d1Side), d2Side);
		return -Math.Sqrt(dist) * Math.Sign(side);
	}




	// 各辺を内側へ radius ぶん寄せた三角形の頂点を返す。角丸三角形は「この内寄せ三角形を radius ぶん膨らませた形」として描く(膨張で角が丸まり、辺は元の位置へ戻る)。radius が 0 以下なら元の頂点をそのまま返す。
	public static TriangleVertices InsetVertices(TriangleVertices v, double radius)
	{
		if (radius <= 0.0)
		{
			return v;
		}

		(double nx, double ny, double c) hueBlack = InsetLine(v.Hue, v.Black, v.White, radius);
		(double nx, double ny, double c) blackWhite = InsetLine(v.Black, v.White, v.Hue, radius);
		(double nx, double ny, double c) whiteHue = InsetLine(v.White, v.Hue, v.Black, radius);

		return new TriangleVertices(
			LineIntersect(whiteHue, hueBlack),
			LineIntersect(hueBlack, blackWhite),
			LineIntersect(blackWhite, whiteHue));
	}




	// 線分方向への射影係数を [0,1] へ収めた値。点から線分までの最短点を求めるのに使う。
	private static double Clamp01Dot(double vx, double vy, double ex, double ey)
	{
		double denominator = (ex * ex) + (ey * ey);

		if (denominator <= 0.0)
		{
			return 0.0;
		}

		return Math.Clamp(((vx * ex) + (vy * ey)) / denominator, 0.0, 1.0);
	}




	// 辺(a→b)を内側(third のある側)へ radius ぶん寄せた直線を、単位法線(nx,ny)と切片 c(n·x = c)で返す。
	private static (double nx, double ny, double c) InsetLine(Point a, Point b, Point third, double radius)
	{
		double dx = b.X - a.X, dy = b.Y - a.Y;
		double length = Math.Sqrt((dx * dx) + (dy * dy));

		if (length <= 0.0)
		{
			return (0.0, 0.0, 0.0);
		}

		double nx = -dy / length, ny = dx / length;

		// third のある側を内向きとする。
		if (((nx * (third.X - a.X)) + (ny * (third.Y - a.Y))) < 0.0)
		{
			nx = -nx;
			ny = -ny;
		}

		return (nx, ny, (nx * a.X) + (ny * a.Y) + radius);
	}




	// 2直線(n·x = c)の交点を返す。三角形は退化しない前提のため、平行に近いときは原点を返す。
	private static Point LineIntersect((double nx, double ny, double c) l1, (double nx, double ny, double c) l2)
	{
		double det = (l1.nx * l2.ny) - (l1.ny * l2.nx);

		if (Math.Abs(det) < 1e-9)
		{
			return new Point(0.0, 0.0);
		}

		double x = ((l1.c * l2.ny) - (l2.c * l1.ny)) / det;
		double y = ((l1.nx * l2.c) - (l2.nx * l1.c)) / det;
		return new Point(x, y);
	}
}




// TriangleGeometry.ComputeVertices が返す三角形の3頂点。純色・黒・白の位置を持つ。
public readonly struct TriangleVertices
{
	public TriangleVertices(Point hue, Point black, Point white)
	{
		Hue = hue;
		Black = black;
		White = white;
	}




	public Point Hue { get; }




	public Point Black { get; }




	public Point White { get; }
}
