// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した色相における HSV の彩度・明度パッドの下地を、色を現在の色制限モードに従って丸めて段階的に描いた画像を生成する。
/// 横軸が彩度(左 0→右 1)、縦軸が明度(下 0→上 1)で、各画素を HSV→RGB へ変換してから丸める。
/// 丸めない滑らかな下地は XAML のグラデーションで賄うため、これは色制限が有効なときだけ使う。
/// </summary>
/// <remarks>色相や色制限モードが変わるたびに作り直す想定。</remarks>
public static class SvPlane
{
	// 指定した画素サイズ・色相・色制限設定で、彩度・明度パッドの下地を描いた WriteableBitmap を作る。横軸が彩度(左 0→右 1)、縦軸が明度(上 1→下 0)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double hue, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HsvToRgb(hue, u, 1.0 - v));
	}
}
