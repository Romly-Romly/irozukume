// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Irozukume.Models;

namespace Irozukume.Helpers;

// Mix タブのポッチ位置を、指定の整列形ごとに 0–1 の正規化座標(左上原点、x 右・y 下)で計算する。自動アレンジの各プリセットと、位置が未設定のポッチの既定配置(正多角形)で共通に使う。
public static class MixArranger
{
	// 平面の縁からポッチを内側に置く余白(正規化)。グラデーションから色を拾う用途では縁の帯が死に領域になるため、ポッチを極力端へ寄せて平面いっぱいを使えるよう小さく取る。掴みやすさのためにわずかに残す程度で、小さな平面ではポッチが縁へ少し掛かることがある。
	private const double Margin = 0.03;




	// 指定の整列形で count 個のポッチ位置を返す。返り値は (x, y) の配列で、いずれも 0–1。count が 0 以下なら空配列。正多角形は平面の表示寸法(width・height)から、画面で正多角形に見えるよう頂点を割り付ける。既定の (1, 1) では正方形として扱う。四隅・縦横一列は縁や中心線に沿って並び、寸法に依らず画面でも崩れないため寸法を使わない。
	public static (double X, double Y)[] Arrange(MixArrange preset, int count, double width = 1.0, double height = 1.0)
	{
		if (count <= 0)
		{
			return Array.Empty<(double, double)>();
		}

		return preset switch
		{
			MixArrange.Corners => Corners(count),
			MixArrange.Column => Line(count, vertical: true),
			MixArrange.Row => Line(count, vertical: false),
			_ => Polygon(count, width, height),
		};
	}




	// 四隅へ。左上・右上・右下・左下の順に割り当て、5個目以降は中央へ重ねる。2個なら左上と右下、3個なら左上・右上・右下になる。
	private static (double X, double Y)[] Corners(int count)
	{
		(double X, double Y)[] slots =
		{
			(Margin, Margin),
			(1.0 - Margin, Margin),
			(1.0 - Margin, 1.0 - Margin),
			(Margin, 1.0 - Margin),
		};

		var result = new (double, double)[count];

		for (int i = 0; i < count; i++)
		{
			result[i] = i < slots.Length ? slots[i] : (0.5, 0.5);
		}

		return result;
	}




	// 中央 (0.5, 0.5) を囲む正 count 角形の頂点へ。最初の頂点を真上に置き、時計回りに配る。半径は余白を残した範囲いっぱい。count が 1 のときは中央、2 のときは上下になる。半径は短辺基準のピクセル長で取り、軸ごとの正規化半径へ割り戻すことで、非正方形の平面でも画面では正多角形に見える。寸法が不正(0 以下)なら正方形として扱う。
	private static (double X, double Y)[] Polygon(int count, double width, double height)
	{
		var result = new (double, double)[count];

		if (count == 1)
		{
			result[0] = (0.5, 0.5);
			return result;
		}

		double min = Math.Min(width, height);
		double radius = 0.5 - Margin;
		double radiusX = min > 0.0 ? radius * min / width : radius;
		double radiusY = min > 0.0 ? radius * min / height : radius;

		for (int i = 0; i < count; i++)
		{
			double angle = (-Math.PI / 2.0) + (2.0 * Math.PI * i / count);
			result[i] = (0.5 + (radiusX * Math.Cos(angle)), 0.5 + (radiusY * Math.Sin(angle)));
		}

		return result;
	}




	// 縦または横の一列に等間隔で並べる。1個なら中央。両端は余白の内側に収める。
	private static (double X, double Y)[] Line(int count, bool vertical)
	{
		var result = new (double, double)[count];

		if (count == 1)
		{
			result[0] = (0.5, 0.5);
			return result;
		}

		for (int i = 0; i < count; i++)
		{
			double t = Margin + ((1.0 - (2.0 * Margin)) * i / (count - 1));
			result[i] = vertical ? (0.5, t) : (t, 0.5);
		}

		return result;
	}
}
