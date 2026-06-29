// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls.Generators.Disks;

/// <summary>
/// 角度=色相・中心からの半径=白みの円盤の下地画像を生成する。
/// 各画素の中心からの角度を色相に、半径を白み(中心 0→縁 1)に写し、黒みは引数で与える一定値とする。
/// 角度の取り方は <see cref="RingGeometry"/> と揃えるため、つまみの位置と円盤の色がずれない。
/// 円の縁は不透明度を滑らかに落として背景になじませ、円の外は透明にする。
/// 白み+黒みが 1 を超える領域(黒みが大きいときの外周側)は無彩色へ退化するが、HwbToRgb がその灰を返すため特別な処理は要らない。
/// </summary>
/// <remarks>
/// 色相は円盤の全方位に現れるため色相を引数に取らず、黒みまたは色制限が変わったときだけ作り直す想定。
/// HslHueSatDisk の HSL を HWB へ替え、半径=白み・固定成分=黒みにした対の描画。
/// </remarks>
public static class HwbWhitenessDisk
{
	// 指定した画素サイズ・黒み・色制限設定で、色相・白みの円盤を描いた WriteableBitmap を作る。円の半径は画素サイズの短辺の半分。各画素を HWB→RGB へ変換し、色制限が有効なら設定の制限へ丸める。円の外は透明にする。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double blackness, SnapSettings snap)
	{
		double maxRadius = Math.Min(pixelWidth, pixelHeight) / 2.0;
		return RadialRenderer.Create(pixelWidth, pixelHeight, snap,
			radius => RadialRenderer.DiskCoverage(radius, maxRadius),
			(dx, dy, radius) => ColorConversion.HwbToRgb(RingGeometry.ValueFromPoint(dx, dy), Math.Clamp(radius / maxRadius, 0.0, 1.0), blackness));
	}
}
