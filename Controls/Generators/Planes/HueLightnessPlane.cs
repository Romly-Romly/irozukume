// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した彩度における HSL の色相・輝度パッドの下地を描いた画像を生成する。
/// 横軸が色相(左 0 度→右 360 度)、縦軸が輝度(下 0=黒→上 1=白)で、彩度は引数で与える一定値とする。
/// 各画素を HSL→RGB へ変換し、色制限が有効なら設定の制限へ丸める。
/// 色相が横一面に渡って変わるため画像で賄う。
/// </summary>
/// <remarks>
/// 彩度や色制限モードが変わるたびに作り直す想定。
/// HueValuePlane の HSV を HSL へ替え、縦軸を明度から輝度へ替えた対の描画。
/// </remarks>
public static class HueLightnessPlane
{
	// 指定した画素サイズ・彩度・色制限設定で、色相・輝度パッドの下地を描いた WriteableBitmap を作る。横軸が色相(左 0 度→右 360 度)、縦軸が輝度(上 1=白→下 0=黒)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double saturation, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HslToRgb(u * 360.0, saturation, 1.0 - v));
	}
}
