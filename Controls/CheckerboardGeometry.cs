// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Irozukume.Controls;

// 透明度表現の市松模様で、暗色の升を敷き詰めるジオメトリと、その見た目を表す定数を集約する。明色の下地は呼び出し側が用意し、その上にこのジオメトリで暗色の升を重ねる。升は一辺 CellSize の正方形で、(行+列)が奇数の升だけを暗色にして市松にする。スライダーと色プレビューの双方が同じ見た目になるよう、市松の寸法・色をここに一本化する。
public static class CheckerboardGeometry
{
	// 市松の升の一辺(DIP)。
	public const double CellSize = 6.0;

	// 市松の明色(下地)。
	public static readonly Color LightColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

	// 市松の暗色(升)。
	public static readonly Color DarkColor = Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC);




	// 指定の幅・高さの領域に、一辺 cell の市松の暗色升をまとめた1つのジオメトリを返す。寸法や升が無効なときは空のジオメトリを返す。
	public static Geometry BuildDarkCells(double width, double height, double cell)
	{
		var darkCells = new GeometryGroup();

		if (width <= 0.0 || height <= 0.0 || cell <= 0.0)
		{
			return darkCells;
		}

		for (int row = 0; row * cell < height; row++)
		{
			for (int column = 0; column * cell < width; column++)
			{
				if ((row + column) % 2 == 1)
				{
					darkCells.Children.Add(new RectangleGeometry
					{
						Rect = new Rect(column * cell, row * cell, cell, cell),
					});
				}
			}
		}

		return darkCells;
	}
}
