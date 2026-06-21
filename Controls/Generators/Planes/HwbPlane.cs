// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した色相における HWB の白み・黒みパッドの下地を描いた画像を生成する。
/// 横軸が白み(左 0→右 1)、縦軸が黒み(上 0→下 1)で、純色を左上・白を右上・黒を左下・灰を右下に取る。
/// 各画素を HWB→RGB へ変換し、白み+黒みが 1 を超える右下の三角形は色相を失った灰へ退化する。
/// HWB は純色・白・黒の3頂点を線形に混ぜる加法的な配色のため、HSV のような重ねたグラデーションでは正しく描けない。
/// よって滑らかな下地も含めて常にこの画像で賄う。
/// 色制限が有効なら各画素の色をその制限へ丸めて段階的にする。
/// </summary>
/// <remarks>色相や色制限モードが変わるたびに作り直す想定。</remarks>
public static class HwbPlane
{
	// 指定した画素サイズ・色相・色制限設定で、白み・黒みパッドの下地を描いた WriteableBitmap を作る。横軸が白み(左 0→右 1)、縦軸が黒み(上 0→下 1)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double hue, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HwbToRgb(hue, u, v));
	}
}
