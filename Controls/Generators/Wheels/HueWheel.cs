// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.Controls.Generators.Wheels;

/// <summary>
/// 色相環(リングの帯)の画像を生成する。
/// 各画素の中心からの角度を色相に、半径で帯の内外を決め、帯の縁は不透明度を滑らかに落として縁取りをなじませる。
/// 角度の取り方は <see cref="RingGeometry"/> と揃えるため、つまみの位置と環の色がずれない。
/// </summary>
/// <remarks>生成はサイズ変更時など必要なときだけ行う想定で、描画中の頻繁な再生成は呼び出し側が避ける。</remarks>
public static class HueWheel
{
	// 指定した画素サイズで色相環を描いた WriteableBitmap を作る。innerRadius・outerRadius は画素単位の帯の内外半径で、リングの幾何(RingGeometry)から得た値に表示倍率を掛けたものを渡す。帯の外と内側の穴は透明にし、中央へ別のコントロールを透かせる。各画素は彩度・明度 1 の HSV から作り、色制限が有効なら設定の制限へ丸める。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, double innerRadius, double outerRadius, SnapSettings snap)
	{
		return RadialRenderer.Create(pixelWidth, pixelHeight, snap,
			radius => RadialRenderer.BandCoverage(radius, innerRadius, outerRadius),
			(dx, dy, radius) => ColorConversion.HsvToRgb(RingGeometry.ValueFromPoint(dx, dy), 1.0, 1.0));
	}
}
