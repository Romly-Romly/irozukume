// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using Irozukume.Glass;

namespace Irozukume.ScreenPicker.Glass;

// ガラス効果の全パラメータ。レンズ窓を組むときに採色セッションの設定から実効値を写し込む。
public sealed class GlassParams
{
	public string Shape = "circle";

	// 円形シェイプの直径(DIP)。本体の幅・高さと角丸半径をこの値から決める。
	public double CircleSize = 300;

	public bool Displace = true;
	public double Scale = -99;

	public bool Chroma = true;
	public double Spread = 0.14;

	public bool Blur = true;
	public double BlurAmt = 3;
	public bool VBlur = true;
	public double VBlurW = 102;

	public bool Enhance = true;
	public bool VEnh = true;
	public double VEnhW = 102;

	public bool Tint = true;
	public double TintAmt = -0.30;
	public bool VTint = true;
	public double VTintW = 70;

	public bool Border = true;
	public double BorderW = 1;

	public bool Gloss = true;

	public bool Light = true;

	// ハイライトの向きをカーソル位置から動的に決める。仮想光源(画面左上寄り)から採色点へ向かう方位へ、焼いたハイライト全体を中心まわりに回して追従させる。円形シェイプでは法線場が中心対称なので回転だけで厳密に成り立つ。
	public bool LightFollow = true;

	// 仮想光源との距離からハイライトの仰角(中央⇔リムの寄り)を擬似する。距離が遠いほどリム寄り(かすめる光)、近いほど中央寄り(見上げる光)になるよう、焼いたハイライトの仰角を距離で補間する。採色を妨げないよう帯を狭くする。
	public bool LightFollowDist = true;

	public double Radius = 25;
	public double Bevel = 50;

	// 拡大率。レンズが背後(=カーソル周辺)のデスクトップをどれだけ拡大して見せるか。1 ソース画素を round(Magnify/scale) 物理ピクセルの整数ブロックへ拡大する。レンズ中心がカラーピッカーの採色点になる。
	public double Magnify = 3;

	public List<Highlight> Highlights = new()
	{
		new Highlight { Curve = "sphere", Dome = 0.7, Azim = 225, Elev = 55, Exp = 30, Power = 1.4, Flat = 0.85 },
		new Highlight { Curve = "convex", Dome = 0.7, Azim = 45,  Elev = 51, Exp = 14, Power = 1.0, Flat = 1.0 },
	};
}




// 形状から決まる寸法一式。ガラス本体(Card)と、屈折のはみ出しを吸収する余白を足したマップ(Map)の大きさを持つ。
internal readonly struct GlassGeometry
{
	public int CardW { get; init; }
	public int CardH { get; init; }
	public int MapW { get; init; }
	public int MapH { get; init; }
	public int Margin { get; init; }
	public double Radius { get; init; }




	// 屈折・色収差を最大に振っても変位先がマップ外へ出ない余白。変位の最大ずれ 0.5×|scale|max×(1+spreadmax) に安全余裕を足す。
	public static int ComputeMargin()
	{
		return (int)Math.Ceiling(0.5 * 160 * (1 + 0.6)) + 12;
	}




	public static GlassGeometry From(GlassParams p)
	{
		int margin = ComputeMargin();
		int w, h;
		double radius;

		if (p.Shape == "circle")
		{
			int d = (int)Math.Round(p.CircleSize);
			if (d < 80)
			{
				d = 80;
			}

			w = d;
			h = d;
			radius = d / 2.0;
		}
		else
		{
			w = 360;
			h = 260;
			radius = p.Radius;
		}

		return new GlassGeometry
		{
			CardW = w,
			CardH = h,
			MapW = w + margin * 2,
			MapH = h + margin * 2,
			Margin = margin,
			Radius = radius,
		};
	}
}
