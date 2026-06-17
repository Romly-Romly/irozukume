// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Runtime.InteropServices;

namespace Irozukume.ScreenPicker.Interop;

// 画面カラーピッカーに必要な Win32 API の P/Invoke と定数・構造体。座標はすべて物理ピクセル(プロセスが Per-Monitor V2)。
// レイヤードウィンドウのレンズ表示、モニタ・DPI の取得、確定/中止を拾う低レベル入力フックをまとめる。
internal static class PickerNativeMethods
{
	public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
	public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
	public const int MDT_EFFECTIVE_DPI = 0;

	public const long WS_EX_TRANSPARENT = 0x00000020;
	public const long WS_EX_TOOLWINDOW = 0x00000080;
	public const long WS_EX_NOACTIVATE = 0x08000000;
	public const long WS_EX_TOPMOST = 0x00000008;
	public const long WS_EX_LAYERED = 0x00080000;

	// 自ウィンドウを画面キャプチャ対象から完全に除外するフラグ。レンズの描画が捕捉内容へ映り込む(自己フィードバック)のを防ぐ。Windows 10 2004 以降。
	public const uint WDA_NONE = 0x00000000;
	public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

	public const uint WS_POPUP = 0x80000000;
	public const int SW_SHOWNOACTIVATE = 4;

	public const uint ULW_ALPHA = 0x00000002;
	public const byte AC_SRC_OVER = 0x00;
	public const byte AC_SRC_ALPHA = 0x01;
	public const uint BI_RGB = 0;
	public const uint DIB_RGB_COLORS = 0;

	public const uint WM_TIMER = 0x0113;
	public const uint WM_CLOSE = 0x0010;
	public const uint WM_QUIT = 0x0012;
	public const uint PM_NOREMOVE = 0x0000;

	// 低レベル入力フックの種別とメッセージ。レンズの専用スレッドへ据え、確定(左クリック)・中止(右クリック/Esc)を拾って握り潰す。
	public const int WH_KEYBOARD_LL = 13;
	public const int WH_MOUSE_LL = 14;
	public const int HC_ACTION = 0;
	public const uint WM_LBUTTONDOWN = 0x0201;
	public const uint WM_RBUTTONDOWN = 0x0204;
	public const uint WM_KEYDOWN = 0x0100;
	public const uint WM_SYSKEYDOWN = 0x0104;
	public const uint VK_ESCAPE = 0x1B;

	[StructLayout(LayoutKind.Sequential)]
	public struct POINT
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;

		public int Width => Right - Left;
		public int Height => Bottom - Top;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct MONITORINFOEX
	{
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string szDevice;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct SIZE
	{
		public int cx;
		public int cy;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct BLENDFUNCTION
	{
		public byte BlendOp;
		public byte BlendFlags;
		public byte SourceConstantAlpha;
		public byte AlphaFormat;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct BITMAPINFOHEADER
	{
		public int biSize;
		public int biWidth;
		public int biHeight;
		public short biPlanes;
		public short biBitCount;
		public uint biCompression;
		public uint biSizeImage;
		public int biXPelsPerMeter;
		public int biYPelsPerMeter;
		public uint biClrUsed;
		public uint biClrImportant;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct WNDCLASSEX
	{
		public int cbSize;
		public uint style;
		public IntPtr lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
		[MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
		public IntPtr hIconSm;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MSG
	{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public POINT pt;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MSLLHOOKSTRUCT
	{
		public POINT pt;
		public uint mouseData;
		public uint flags;
		public uint time;
		public UIntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct KBDLLHOOKSTRUCT
	{
		public uint vkCode;
		public uint scanCode;
		public uint flags;
		public uint time;
		public UIntPtr dwExtraInfo;
	}

	public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);




	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll")]
	public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll")]
	public static extern IntPtr WindowFromPoint(POINT pt);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

	[DllImport("Shcore.dll")]
	public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr GetModuleHandle(string? lpModuleName);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DestroyWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll")]
	public static extern uint GetCurrentThreadId();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessage(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern void PostQuitMessage(int nExitCode);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

	[DllImport("user32.dll")]
	public static extern IntPtr GetDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

	[DllImport("user32.dll")]
	public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

	[DllImport("gdi32.dll")]
	public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DeleteDC(IntPtr hdc);

	[DllImport("gdi32.dll")]
	public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

	[DllImport("gdi32.dll")]
	public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DeleteObject(IntPtr hObject);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UnhookWindowsHookEx(IntPtr hhk);

	[DllImport("user32.dll")]
	public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
