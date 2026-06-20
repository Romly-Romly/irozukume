// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Runtime.InteropServices;
using Irozukume.ScreenPicker.Interop;

namespace Irozukume.ScreenPicker;

// HMONITOR から、レンズ上端へ出す「Monitor 番号: フレンドリ名」表記を組む。番号は GDI デバイス名(\\.\DISPLAYn)の末尾の数、名前は DisplayConfig のターゲットフレンドリ名(例: LG HDR 4K)を使う。
// EnumDisplayDevices では多くのモニタで "Generic PnP Monitor" しか返らないため、製品名は DisplayConfig 経由で引く。取得に失敗しても番号だけで表記が成り立つよう、各段で空文字へ退避する。
internal static class MonitorNaming
{
	public static string GetLabel(IntPtr hMonitor)
	{
		string device = GetGdiDeviceName(hMonitor);
		int number = ParseDisplayNumber(device);
		string friendly = GetFriendlyName(device);

		string head = number > 0 ? $"Monitor {number}" : "Monitor";
		return string.IsNullOrEmpty(friendly) ? head : $"{head}: {friendly}";
	}




	// HMONITOR の GDI デバイス名(\\.\DISPLAYn)を得る。番号の算出と DisplayConfig のソース名照合の双方で使う。
	private static string GetGdiDeviceName(IntPtr hMonitor)
	{
		var info = new PickerNativeMethods.MONITORINFOEX
		{
			cbSize = Marshal.SizeOf<PickerNativeMethods.MONITORINFOEX>(),
		};

		if (PickerNativeMethods.GetMonitorInfo(hMonitor, ref info))
		{
			return info.szDevice ?? string.Empty;
		}

		return string.Empty;
	}




	// \\.\DISPLAY1 の末尾の連続する数字を取り出す。取れなければ 0。
	private static int ParseDisplayNumber(string device)
	{
		int end = device.Length;
		int start = end;
		while (start > 0 && char.IsDigit(device[start - 1]))
		{
			start--;
		}

		if (start < end && int.TryParse(device.Substring(start), out int n))
		{
			return n;
		}

		return 0;
	}




	// GDI デバイス名に対応するモニタのフレンドリ名を DisplayConfig から引く。アクティブ経路を全て問い合わせ、ソース名が一致した経路のターゲット名を返す。
	private static string GetFriendlyName(string device)
	{
		if (string.IsNullOrEmpty(device))
		{
			return string.Empty;
		}

		try
		{
			int r = PickerNativeMethods.GetDisplayConfigBufferSizes(PickerNativeMethods.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
			if (r != PickerNativeMethods.ERROR_SUCCESS || pathCount == 0)
			{
				return string.Empty;
			}

			var paths = new PickerNativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
			var modes = new PickerNativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
			r = PickerNativeMethods.QueryDisplayConfig(PickerNativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
			if (r != PickerNativeMethods.ERROR_SUCCESS)
			{
				return string.Empty;
			}

			for (uint i = 0; i < pathCount; i++)
			{
				var src = new PickerNativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
				src.header.type = PickerNativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
				src.header.size = (uint)Marshal.SizeOf<PickerNativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
				src.header.adapterId = paths[i].sourceInfo.adapterId;
				src.header.id = paths[i].sourceInfo.id;

				if (PickerNativeMethods.DisplayConfigGetDeviceInfo(ref src) != PickerNativeMethods.ERROR_SUCCESS)
				{
					continue;
				}

				if (!string.Equals(src.viewGdiDeviceName, device, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var tgt = new PickerNativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME();
				tgt.header.type = PickerNativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
				tgt.header.size = (uint)Marshal.SizeOf<PickerNativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>();
				tgt.header.adapterId = paths[i].targetInfo.adapterId;
				tgt.header.id = paths[i].targetInfo.id;

				if (PickerNativeMethods.DisplayConfigGetDeviceInfo(ref tgt) == PickerNativeMethods.ERROR_SUCCESS)
				{
					return (tgt.monitorFriendlyDeviceName ?? string.Empty).Trim();
				}
			}
		}
		catch
		{
			// 取得に失敗してもラベルは番号だけで成立するため握り潰す。
		}

		return string.Empty;
	}
}
