// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls.Generators.Planes;

/// <summary>
/// 指定した黒みにおける HWB の色相・白みパッドの下地を描いた画像を生成する。
/// 横軸が色相(左 0 度→右 360 度)、縦軸が白み(下 0→上 1)で、黒みは引数で与える一定値とする。
/// 各画素を HWB→RGB へ変換し、色制限が有効なら設定の制限へ丸める。
/// 色相が横一面に渡って変わるため画像で賄う。
/// 白み+黒みが 1 を超える上側(黒みが大きいとき)は無彩色へ退化するが、HwbToRgb がその灰を返す。
/// </summary>
/// <remarks>
/// 黒みや色制限モードが変わるたびに作り直す想定。
/// HueLightnessPlane の HSL を HWB へ替え、縦軸を輝度から白みにした対の描画。
/// </remarks>
public static class HueWhitenessPlane
{
	// 指定した画素サイズ・黒み・色制限設定で、色相・白みパッドの下地を描いた WriteableBitmap を作る。横軸が色相(左 0 度→右 360 度)、縦軸が白み(上 1→下 0)。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double blackness, SnapSettings snap)
	{
		return PlaneRenderer.Create(pixelWidth, pixelHeight, snap, (u, v) => ColorConversion.HwbToRgb(u * 360.0, 1.0 - v, blackness));
	}
}
