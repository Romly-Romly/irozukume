// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;

namespace Irozukume.Models;

// 色を「最も近いパレット色」へ丸めるときの設定一式。色制限モード(格子・ターミナル)、距離計算、ターミナルの参照テーマをまとめて、Snap の呼び出しと各描画コントロールへ一括して渡す。表示の再描画要否をキャッシュ比較で判定するため、値同型として等価比較を備える。
public readonly struct SnapSettings : IEquatable<SnapSettings>
{
	public SnapSettings(ColorLimitMode mode, SnapMetric metric, TerminalTheme theme)
	{
		Mode = mode;
		Metric = metric;
		Theme = theme;
	}




	// 表示上どの制約へ丸めるか。None なら丸めない。
	public ColorLimitMode Mode { get; }

	// 最近傍を選ぶ距離計算。格子モードでは Rgb のときチャンネル独立丸めの速い経路を通る。
	public SnapMetric Metric { get; }

	// ターミナルモードで基本16色(0-15)を解決する参照テーマ。格子モードでは使わない。
	public TerminalTheme Theme { get; }




	public bool Equals(SnapSettings other)
	{
		return Mode == other.Mode && Metric == other.Metric && Theme == other.Theme;
	}




	public override bool Equals(object? obj)
	{
		return obj is SnapSettings other && Equals(other);
	}




	public override int GetHashCode()
	{
		return HashCode.Combine(Mode, Metric, Theme);
	}




	public static bool operator ==(SnapSettings left, SnapSettings right)
	{
		return left.Equals(right);
	}




	public static bool operator !=(SnapSettings left, SnapSettings right)
	{
		return !left.Equals(right);
	}
}
