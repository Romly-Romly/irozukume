// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls.Generators.Disks;

/// <summary>
/// 角度=色相・中心からの半径=明度の円盤の下地画像を生成する。
/// 各画素の中心からの角度を色相に、半径を明度(中心 0=黒→縁 1)に写し、彩度は引数で与える一定値とする。
/// 角度の取り方は RingGeometry と揃えるため、つまみの位置と円盤の色がずれない。
/// 円の縁は不透明度を滑らかに落として背景になじませ、円の外は透明にする。
/// </summary>
/// <remarks>
/// 彩度が変わったとき、または色制限が変わったときだけ作り直す想定。
/// HueSatDisk の半径を彩度から明度へ替えた対の描画。
/// </remarks>
public static class HueValueDisk
{
	// 指定した画素サイズ・彩度・色制限設定で、色相・明度の円盤を描いた WriteableBitmap を作る。円の半径は画素サイズの短辺の半分。各画素を HSV→RGB へ変換し、色制限が有効なら設定の制限へ丸める。円の外は透明にする。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double saturation, SnapSettings snap)
	{
		double maxRadius = Math.Min(pixelWidth, pixelHeight) / 2.0;
		return RadialRenderer.Create(pixelWidth, pixelHeight, snap,
			radius => RadialRenderer.DiskCoverage(radius, maxRadius),
			(dx, dy, radius) => ColorConversion.HsvToRgb(RingGeometry.ValueFromPoint(dx, dy), saturation, Math.Clamp(radius / maxRadius, 0.0, 1.0)));
	}
}
