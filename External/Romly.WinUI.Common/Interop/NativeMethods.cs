using System;
using System.Runtime.InteropServices;

namespace Romly.WinUI.Common.Interop;

/// <summary>
/// ウィンドウ配置の復元で、座標が乗るモニタの実効DPIを引くために使う Win32 宣言。
/// </summary>
internal static class NativeMethods
{
	public const uint MONITOR_DEFAULTTONEAREST = 2;
	public const int MDT_EFFECTIVE_DPI = 0;

	[StructLayout(LayoutKind.Sequential)]
	public struct POINT
	{
		public int X;
		public int Y;
	}

	[DllImport("user32.dll")]
	public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("shcore.dll")]
	public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
