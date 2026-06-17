// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Irozukume.ScreenPicker.Interop;

// 各モニタの SDR 白レベルを DisplayConfig API から取得する。
// HDR モードのデスクトップでは SDR コンテンツの白が scRGB 1.0 ではなく SDR 輝度設定分だけ上にスケールされるため、
// 捕捉した scRGB から正しい SDR 色を得るにはこのスケール（= SDRWhiteLevel/1000、scRGB での SDR 白の値）で割る必要がある。
internal static class DisplayInfo
{
	private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
	private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
	private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11;

	[StructLayout(LayoutKind.Sequential)]
	private struct LUID
	{
		public uint LowPart;
		public int HighPart;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_PATH_SOURCE_INFO
	{
		public LUID adapterId;
		public uint id;
		public uint modeInfoIdx;
		public uint statusFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_RATIONAL
	{
		public uint Numerator;
		public uint Denominator;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_PATH_TARGET_INFO
	{
		public LUID adapterId;
		public uint id;
		public uint modeInfoIdx;
		public uint outputTechnology;
		public uint rotation;
		public uint scaling;
		public DISPLAYCONFIG_RATIONAL refreshRate;
		public uint scanLineOrdering;
		public int targetAvailable;
		public uint statusFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_PATH_INFO
	{
		public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
		public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
		public uint flags;
	}

	// modeInfo の共用体（48バイト）は本処理で参照しないため、サイズだけ確保したブロブとして扱う。
	[StructLayout(LayoutKind.Sequential, Size = 48)]
	private struct DISPLAYCONFIG_MODE_INFO_UNION
	{
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_MODE_INFO
	{
		public uint infoType;
		public uint id;
		public LUID adapterId;
		public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
	{
		public uint type;
		public uint size;
		public LUID adapterId;
		public uint id;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string viewGdiDeviceName;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_SDR_WHITE_LEVEL
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
		public uint SDRWhiteLevel;
	}

	[DllImport("user32.dll")]
	private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

	[DllImport("user32.dll")]
	private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

	[DllImport("user32.dll")]
	private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

	[DllImport("user32.dll")]
	private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL requestPacket);




	// GDI デバイス名（例 \\.\DISPLAY1）→ SDR 白レベルの scRGB スケール（既定 1.0）の対応表を返す。取得に失敗したモニタは表に現れない。
	public static Dictionary<string, double> GetSdrWhiteScales()
	{
		var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

		if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint numPaths, out uint numModes) != 0)
		{
			return result;
		}

		var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
		var modes = new DISPLAYCONFIG_MODE_INFO[numModes];
		if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0)
		{
			return result;
		}

		for (int i = 0; i < numPaths; i++)
		{
			var name = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
			name.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
			name.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
			name.header.adapterId = paths[i].sourceInfo.adapterId;
			name.header.id = paths[i].sourceInfo.id;
			if (DisplayConfigGetDeviceInfo(ref name) != 0 || string.IsNullOrEmpty(name.viewGdiDeviceName))
			{
				continue;
			}

			var wl = new DISPLAYCONFIG_SDR_WHITE_LEVEL();
			wl.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL;
			wl.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SDR_WHITE_LEVEL>();
			wl.header.adapterId = paths[i].targetInfo.adapterId;
			wl.header.id = paths[i].targetInfo.id;

			double scale = 1.0;
			if (DisplayConfigGetDeviceInfo(ref wl) == 0 && wl.SDRWhiteLevel > 0)
			{
				scale = wl.SDRWhiteLevel / 1000.0;
			}
			result[name.viewGdiDeviceName] = scale;
		}

		return result;
	}




	// 指定 GDI デバイス名の SDR 白レベルスケールを返す。不明なら 1.0。
	public static double GetSdrWhiteScale(string deviceName)
	{
		Dictionary<string, double> map = GetSdrWhiteScales();
		return map.TryGetValue(deviceName, out double scale) ? scale : 1.0;
	}
}
