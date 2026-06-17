// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

using Irozukume.Interop;
using Irozukume.Models;

namespace Irozukume.Services;

// メインウィンドウの配置と WindowPlacement の相互変換。捕捉は現在の位置・寸法・ワークエリアを読み、寸法は現在 DPI で DIP へ落とす。適用は保存位置をアンカーに復元先ディスプレイを定め、そのモニタの DPI で DIP を物理化し、最低サイズと可視ワークエリアでクランプしてから一括で移動・リサイズする。
public static class WindowPlacementService
{
	// 現在のウィンドウ配置を WindowPlacement として取り出す。寸法は復元先 DPI で再物理化できるよう DIP で保持する。物理→DIP の換算は Apply と同じく保存位置 (左上アンカー) のモニタ DPI で行う。両方向を同一アンカーの倍率で揃えることで往復が無損失になり、ウィンドウ未表示で実効 DPI が確定しない段階でも基準がぶれない。
	public static WindowPlacement Capture(Window window)
	{
		var appWindow = window.AppWindow;

		int x = appWindow.Position.X;
		int y = appWindow.Position.Y;
		double scale = MonitorScaleAt(x, y);

		return new WindowPlacement
		{
			X = x,
			Y = y,
			WidthDip = appWindow.Size.Width / scale,
			HeightDip = appWindow.Size.Height / scale,
		};
	}




	// 保存済み配置をウィンドウへ適用する。保存位置を含むディスプレイを復元先とし、そのモニタの DPI で DIP を物理化、最低サイズ以上かつ可視ワークエリア内へクランプしてから移動・リサイズする。ディスプレイ構成が変わって元位置が画面外でも可視域へ引き戻す。最低サイズは復元寸法が下限を割らないよう DIP で受け取る。
	public static void Apply(Window window, WindowPlacement placement, int minWidthDip, int minHeightDip)
	{
		var appWindow = window.AppWindow;

		var anchor = new PointInt32(placement.X, placement.Y);
		var work = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest).WorkArea;
		double scale = MonitorScaleAt(placement.X, placement.Y);

		int width = (int)Math.Round(placement.WidthDip * scale);
		int height = (int)Math.Round(placement.HeightDip * scale);
		int minWidth = (int)Math.Round(minWidthDip * scale);
		int minHeight = (int)Math.Round(minHeightDip * scale);

		// 寸法は最低サイズ以上、かつワークエリアに収まる範囲へ収める。
		width = Clamp(width, minWidth, Math.Max(minWidth, work.Width));
		height = Clamp(height, minHeight, Math.Max(minHeight, work.Height));

		// 位置はウィンドウ全体がワークエリア内に収まる範囲へ収める。
		int x = Clamp(placement.X, work.X, work.X + work.Width - width);
		int y = Clamp(placement.Y, work.Y, work.Y + work.Height - height);

		appWindow.MoveAndResize(new RectInt32(x, y, width, height));
	}




	// 指定スクリーン座標を含むモニタの実効 DPI 倍率を返す。復元先モニタの倍率で DIP を物理化するために使う。取得できなければ等倍とする。
	private static double MonitorScaleAt(int x, int y)
	{
		var point = new NativeMethods.POINT { X = x, Y = y };
		var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);

		if (monitor != IntPtr.Zero && NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX != 0)
		{
			return dpiX / 96.0;
		}

		return 1.0;
	}




	// 上限が下限を下回る (ウィンドウがワークエリアより大きい等) ときは下限を採り、Math.Clamp の例外を避ける。
	private static int Clamp(int value, int min, int max)
	{
		if (max < min)
		{
			return min;
		}

		return Math.Clamp(value, min, max);
	}
}
