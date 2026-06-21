// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.Foundation;

namespace Irozukume.Controls.Geometry;

// 環状スライダー(RingSlider)と色相環画像(HueWheel)が共有する幾何計算。中心・内外半径やつまみ位置の算出を一箇所にまとめ、つまみの当たり判定・描画と画像の輪が食い違わないようにする。角度は度で扱い、値 0 を真上(12時)とし、時計回りに増える。画面座標は下方向が正のため、真上は y が負になる。
public static class RingGeometry
{
	// 領域の大きさとリング太さ・つまみ径から、中心点と内側・外側・中央(つまみ中心が乗る)の各半径を求める。つまみは中央半径(帯の中心線)上に置かれ、帯の太さの半分より大きい径のときだけ帯の外へはみ出す。ドラッグ中の拡大つまみ(ルーペ)は最前面オーバーレイへ描かれ領域に縛られないため、静的なつまみが帯の外へはみ出す分だけを外側半径から控え、残りは帯を領域いっぱいへ広げる。
	public static RingMetrics Compute(double width, double height, double thickness, double thumbDiameter)
	{
		var center = new Point(width / 2.0, height / 2.0);
		double half = Math.Min(width, height) / 2.0;
		double thumbRadius = thumbDiameter / 2.0;

		double thumbOverflow = Math.Max(0.0, thumbRadius - (thickness / 2.0));
		double outer = half - thumbOverflow - 1.0;

		if (outer < 0.0)
		{
			outer = 0.0;
		}

		double inner = outer - thickness;

		if (inner < 0.0)
		{
			inner = 0.0;
		}

		double mid = (inner + outer) / 2.0;
		return new RingMetrics(center, inner, outer, mid);
	}




	// 値(度)に対応する、中心からのオフセット位置を返す。値 0 を真上とし時計回りに進む。radius には通常つまみが乗る中央半径を渡す。
	public static Point OffsetForValue(double radius, double valueDegrees)
	{
		double radians = (valueDegrees - 90.0) * Math.PI / 180.0;
		return new Point(radius * Math.Cos(radians), radius * Math.Sin(radians));
	}




	// 中央のパッド(リングと同心で、padRotation だけ回って表示される)の局所座標を、リングの局所座標へ写す。パッドの外側をレンズで覗いたとき、その点の背後にあるリングの色を引くのに使う。パッド中心はリング中心に一致する前提で、パッド中心からのずれを表示と同じだけ回し、リング中心へ加える。
	public static Point PadPointToRing(double padWidth, double padHeight, double ringWidth, double ringHeight, double padRotationDegrees, double x, double y)
	{
		double offsetX = x - (padWidth / 2.0);
		double offsetY = y - (padHeight / 2.0);
		double radians = padRotationDegrees * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		double rotatedX = (offsetX * cos) - (offsetY * sin);
		double rotatedY = (offsetX * sin) + (offsetY * cos);
		return new Point((ringWidth / 2.0) + rotatedX, (ringHeight / 2.0) + rotatedY);
	}




	// PadPointToRing の逆。リングの局所座標を、中央のパッド(リングと同心で padRotation だけ回って表示される)の局所座標へ写す。色相環のレンズが内側のパッドを覗くとき、リング座標の点の背後にあるパッドの色を引くのに使う。リング中心からのずれを表示と逆向きに回し、パッド中心へ加える。
	public static Point RingPointToPad(double padWidth, double padHeight, double ringWidth, double ringHeight, double padRotationDegrees, double x, double y)
	{
		double offsetX = x - (ringWidth / 2.0);
		double offsetY = y - (ringHeight / 2.0);
		double radians = -padRotationDegrees * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		double rotatedX = (offsetX * cos) - (offsetY * sin);
		double rotatedY = (offsetX * sin) + (offsetY * cos);
		return new Point((padWidth / 2.0) + rotatedX, (padHeight / 2.0) + rotatedY);
	}




	// 中心からの相対位置(dx, dy)が指す角度を度で返す。値 0 を真上とし時計回りに増え、戻り値は [0, 360) に収める。
	public static double ValueFromPoint(double dx, double dy)
	{
		double degrees = (Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90.0;

		if (degrees < 0.0)
		{
			degrees += 360.0;
		}

		return degrees % 360.0;
	}
}




// RingGeometry.Compute が返すリングの寸法一式。中心点と内側・外側・中央(つまみ中心が乗る)半径を持つ。
public readonly struct RingMetrics
{
	public RingMetrics(Point center, double innerRadius, double outerRadius, double midRadius)
	{
		Center = center;
		InnerRadius = innerRadius;
		OuterRadius = outerRadius;
		MidRadius = midRadius;
	}




	public Point Center { get; }




	public double InnerRadius { get; }




	public double OuterRadius { get; }




	public double MidRadius { get; }
}
