// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Windows.UI;

namespace Irozukume.Controls;

// スライダー類のつまみ(中空の二重リング)を、レンズの色サンプリングの中へ描き込むための共有部品。レンズは背後のライブピクセルを掴めず色を作り直すため、別コントロールのつまみがレンズに入ったときは、ここでその意匠を再現して重ねる。白(線幅2)を半透明の黒(線幅4)の上へ置き中央は中空、という意匠は各コントロールのテンプレート(RingSlider.xaml・PlanarPad.xaml・TrianglePad.xaml の PART_Thumb)と合わせる。つまみの中心と径は呼び出し側が渡す。
internal static class ThumbGlyph
{
	// つまみの輪を baseColor へ重ねた色を返す。点 (x, y) と中心 (centerX, centerY) の距離が、径から決まる輪の帯に入れば白または半透明の黒を、中空(中央)と輪の外は baseColor をそのまま返す。baseColor が透明な箇所(色面・帯の外)には描かない。
	public static Color Overlay(Color baseColor, double x, double y, double centerX, double centerY, double diameter)
	{
		if (baseColor.A == 0)
		{
			return baseColor;
		}

		double dx = x - centerX;
		double dy = y - centerY;
		double dist = Math.Sqrt((dx * dx) + (dy * dy));
		double radius = diameter / 2.0;

		// 白(線幅2)は中央半径の内外1px、半透明の黒(線幅4)は内外2px。白を黒の上へ重ねる。中央(穴)は透かす。
		if (dist >= radius - 1.0 && dist <= radius + 1.0)
		{
			return Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
		}

		if (dist >= radius - 2.0 && dist <= radius + 2.0)
		{
			return CompositeOverOpaque(baseColor, Color.FromArgb(0x80, 0x00, 0x00, 0x00));
		}

		return baseColor;
	}




	// 半透明の上塗りを不透明な下地へ重ねた色を返す。つまみの暗いリング(半透明の黒)を色面や帯の色へ乗せるのに使う。結果は不透明。
	private static Color CompositeOverOpaque(Color baseColor, Color overlay)
	{
		double alpha = overlay.A / 255.0;
		byte Mix(byte under, byte over) => (byte)Math.Round((under * (1.0 - alpha)) + (over * alpha));
		return Color.FromArgb(0xFF, Mix(baseColor.R, overlay.R), Mix(baseColor.G, overlay.G), Mix(baseColor.B, overlay.B));
	}
}
