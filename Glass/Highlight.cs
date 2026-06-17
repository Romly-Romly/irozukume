// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Glass;

// 鏡面ハイライト1灯ぶんのパラメータ。共有のドーム高さ場に対して各灯の鏡面を焼き、明るさを加算合成する。
public sealed class Highlight
{
	public string Curve = "smooth";
	public double Dome = 0.7;
	public double Azim = 225;
	public double Elev = 55;
	public double Exp = 24;
	public double Power = 1.4;
	public double Flat = 1.0;




	public Highlight Clone()
	{
		return new Highlight
		{
			Curve = Curve, Dome = Dome, Azim = Azim, Elev = Elev,
			Exp = Exp, Power = Power, Flat = Flat,
		};
	}
}
