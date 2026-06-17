// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// YCbCr の係数規格。輝度の重み(Kr・Kb)が変わり、色差平面の形と RGB との対応が変わる。
public enum YCbCrStandard
{
	Bt601,
	Bt709,
	Bt2020,
}




// YCbCr の符号化形式。係数規格と量子化レンジ(フル/スタジオ)の組で、RGB との相互変換式が一意に定まる。Cb・Cr は常に 0–255 のデジタルコードで無彩色を 128 とし、輝度 Y はフルレンジで 0–255、スタジオレンジで 16–235 を取る。
public readonly struct YCbCrFormat
{
	public YCbCrFormat(YCbCrStandard standard, bool fullRange)
	{
		Standard = standard;
		FullRange = fullRange;
	}




	public YCbCrStandard Standard { get; }




	public bool FullRange { get; }




	// 赤の輝度重み。
	public double Kr => Standard switch
	{
		YCbCrStandard.Bt601 => 0.299,
		YCbCrStandard.Bt2020 => 0.2627,
		_ => 0.2126,
	};




	// 青の輝度重み。
	public double Kb => Standard switch
	{
		YCbCrStandard.Bt601 => 0.114,
		YCbCrStandard.Bt2020 => 0.0593,
		_ => 0.0722,
	};




	// 緑の輝度重み。三つの重みの総和は 1。
	public double Kg => 1.0 - Kr - Kb;
}
