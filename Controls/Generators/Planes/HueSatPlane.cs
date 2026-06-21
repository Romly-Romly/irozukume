// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した明度における HSV の色相・彩度パッドの下地を描いた画像を生成する。
/// 横軸が色相(左 0 度→右 360 度)、縦軸が彩度(下 0→上 1)で、明度は引数で与える一定値とする。
/// 各画素を HSV→RGB へ変換し、色制限が有効なら設定の制限へ丸める。
/// 色相が横一面に渡って変わるため画像で賄う。
/// </summary>
/// <remarks>
/// 明度や色制限モードが変わるたびに作り直す想定。
/// HueValuePlane の縦軸を明度から彩度へ替えた対の描画。
/// </remarks>
public static class HueSatPlane
{
	// 指定した画素サイズ・明度・色制限設定で、色相・彩度パッドの下地を描いた WriteableBitmap を作る。横軸が色相(左 0 度→右 360 度)、縦軸が彩度(上 1→下 0)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double value, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HsvToRgb(u * 360.0, 1.0 - v, value));
	}
}
