// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した輝度における HSL の色相・彩度パッドの下地を描いた画像を生成する。
/// 横軸が色相(左 0 度→右 360 度)、縦軸が彩度(下 0→上 1)で、輝度は引数で与える一定値とする。
/// 各画素を HSL→RGB へ変換し、色制限が有効なら設定の制限へ丸める。
/// 色相が横一面に渡って変わるため画像で賄う。
/// </summary>
/// <remarks>
/// 輝度や色制限モードが変わるたびに作り直す想定。
/// HueLightnessPlane の縦軸を輝度から彩度へ替えた対の描画。
/// </remarks>
public static class HslHueSatPlane
{
	// 指定した画素サイズ・輝度・色制限設定で、色相・彩度パッドの下地を描いた WriteableBitmap を作る。横軸が色相(左 0 度→右 360 度)、縦軸が彩度(上 1→下 0)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double lightness, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HslToRgb(u * 360.0, 1.0 - v, lightness));
	}
}
