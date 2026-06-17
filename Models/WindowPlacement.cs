// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Text.Json.Serialization;

namespace Irozukume.Models;

// 保存済みのメインウィンドウ配置。位置 (X,Y) は物理ピクセルのスクリーン絶対座標、幅・高さは DIP (論理単位) で持つ。寸法を DIP にするのは、別 DPI のモニタへ復元したときに物理ピクセルの往復で寸法が崩れるのを避け、復元先モニタの DPI で都度物理化するため。復元先ディスプレイは保存位置 (X,Y) を座標アンカーとして同定する。DisplayId は再起動・構成変更で安定しないため使わない。
public sealed class WindowPlacement
{
	[JsonPropertyName("x")]
	public int X { get; set; }

	[JsonPropertyName("y")]
	public int Y { get; set; }

	[JsonPropertyName("width_dip")]
	public double WidthDip { get; set; }

	[JsonPropertyName("height_dip")]
	public double HeightDip { get; set; }
}
