// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した白みにおける HWB の色相・黒みパッドの下地を描いた画像を生成する。
/// 横軸が色相(左 0 度→右 360 度)、縦軸が黒み(上 0→下 1)で、白みは引数で与える一定値とする。
/// 各画素を HWB→RGB へ変換し、色制限が有効なら設定の制限へ丸める。
/// 色相が横一面に渡って変わるため画像で賄う。
/// 上ほど純色(明るい)・下ほど黒とし、白み黒みの正方形(<see cref="HwbPlane"/>)や他の表色系の明度・輝度の縦軸(上ほど明るい)と向きをそろえる。
/// 白み+黒みが 1 を超える下側(白みが大きいとき)は無彩色へ退化するが、HwbToRgb がその灰を返す。
/// </summary>
/// <remarks>
/// 白みや色制限モードが変わるたびに作り直す想定。
/// HueWhitenessPlane の縦軸を白みから黒みへ替えた対の描画。
/// </remarks>
public static class HueBlacknessPlane
{
	// 指定した画素サイズ・白み・色制限設定で、色相・黒みパッドの下地を描いた WriteableBitmap を作る。横軸が色相(左 0 度→右 360 度)、縦軸が黒み(上 0→下 1)で、上ほど純色(明るい)・下ほど黒。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double whiteness, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HwbToRgb(u * 360.0, whiteness, v));
	}
}
