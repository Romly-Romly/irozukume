// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace Irozukume.ScreenPicker.Capture;

// GraphicsCaptureItem を HMONITOR から生成するための WinRT interop。
// GraphicsCaptureItem のアクティベーションファクトリは IGraphicsCaptureItemInterop(IUnknown ベース)を実装しており、CsWinRT の As<T>() で QueryInterface して CreateForMonitor を呼ぶ。
internal static class CaptureInterop
{
	private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

	[ComImport]
	[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IGraphicsCaptureItemInterop
	{
		IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
		IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
	}




	public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon)
	{
		var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
		Guid iid = GraphicsCaptureItemGuid;
		IntPtr itemPtr = interop.CreateForMonitor(hmon, ref iid);

		try
		{
			return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
		}
		finally
		{
			Marshal.Release(itemPtr);
		}
	}
}
