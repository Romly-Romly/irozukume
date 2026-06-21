// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した色相における HSL の彩度・輝度パッドの下地を描いた画像を生成する。
/// 横軸が彩度(左 0→右 1)、縦軸が輝度(下 0→上 1)で、各画素を HSL→RGB へ変換し、色制限が有効なら設定の制限へ丸める。
/// 上辺(輝度 1)は全白、下辺(輝度 0)は全黒へ退化し、純色は中段(輝度 0.5・彩度 1)に現れる。
/// 色相が横一面に渡らず縦横の単純なグラデーションの重ねでも近似できるが、色制限の段階化を含めて画像で賄う。
/// </summary>
/// <remarks>
/// 色相や色制限モードが変わるたびに作り直す想定。
/// SvPlane の縦軸を明度から輝度へ替えた対の描画。
/// </remarks>
public static class SlPlane
{
	// 指定した画素サイズ・色相・色制限設定で、彩度・輝度パッドの下地を描いた WriteableBitmap を作る。横軸が彩度(左 0→右 1)、縦軸が輝度(上 1→下 0)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double hue, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HslToRgb(hue, u, 1.0 - v));
	}
}
