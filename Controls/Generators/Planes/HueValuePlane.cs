// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した彩度における HSV の色相・明度パッドの下地を描いた画像を生成する。
/// 横軸が色相(左 0 度→右 360 度)、縦軸が明度(下 0→上 1)で、彩度は引数で与える一定値とする。
/// 各画素を HSV→RGB へ変換し、色制限が有効なら設定の制限へ丸める。
/// 色相が横一面に渡って変わり、彩度・明度の単純なグラデーションの重ねでは描けないため画像で賄う。
/// </summary>
/// <remarks>彩度や色制限モードが変わるたびに作り直す想定。</remarks>
public static class HueValuePlane
{
	// 指定した画素サイズ・彩度・色制限設定で、色相・明度パッドの下地を描いた WriteableBitmap を作る。横軸が色相(左 0 度→右 360 度)、縦軸が明度(上 1→下 0)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double saturation, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HsvToRgb(u * 360.0, saturation, 1.0 - v));
	}
}
