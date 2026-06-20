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
	public const uint WM_MOUSEWHEEL = 0x020A;

	// WM_MOUSEWHEEL の1ノッチぶんの回転量。mouseData の上位ワード(符号付き)がこの倍数で届く。
	public const int WHEEL_DELTA = 120;
	public const uint WM_KEYDOWN = 0x0100;
	public const uint WM_SYSKEYDOWN = 0x0104;
	public const uint VK_ESCAPE = 0x1B;

	// Ctrl キー(左右どちらでも立つ仮想キー)。採色中の Ctrl+ホイールで取得範囲を増減するための押下判定に使う。
	public const int VK_CONTROL = 0x11;

	// 矢印キーの仮想キーコード。採色中にカーソルを1物理ピクセルずつ動かす微調整に使う。
	public const uint VK_LEFT = 0x25;
	public const uint VK_UP = 0x26;
	public const uint VK_RIGHT = 0x27;
	public const uint VK_DOWN = 0x28;

	// DisplayConfig。アクティブな表示経路だけを問い合わせ、経路からモニタの GDI デバイス名(ソース名)とフレンドリ名(ターゲット名)を引く。
	public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
	public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
	public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
	public const int ERROR_SUCCESS = 0;

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

	[StructLayout(LayoutKind.Sequential)]
	public struct LUID
	{
		public uint LowPart;
		public int HighPart;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_RATIONAL
	{
		public uint Numerator;
		public uint Denominator;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_PATH_SOURCE_INFO
	{
		public LUID adapterId;
		public uint id;
		public uint modeInfoIdx;
		public uint statusFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_PATH_TARGET_INFO
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
	public struct DISPLAYCONFIG_PATH_INFO
	{
		public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
		public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
		public uint flags;
	}

	// ユニオン部の中身は使わないため、構造体の総サイズ(64バイト)だけ確保して配列の刻みを合わせる。先頭の型・id・アダプタIDだけ明示する。
	[StructLayout(LayoutKind.Sequential, Size = 64)]
	public struct DISPLAYCONFIG_MODE_INFO
	{
		public uint infoType;
		public uint id;
		public LUID adapterId;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
	{
		public uint type;
		public uint size;
		public LUID adapterId;
		public uint id;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string viewGdiDeviceName;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
		public uint flags;
		public uint outputTechnology;
		public ushort edidManufactureId;
		public ushort edidProductCodeId;
		public uint connectorInstance;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string monitorFriendlyDeviceName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string monitorDevicePath;
	}

	public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);




	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetCursorPos(int X, int Y);

	[DllImport("user32.dll")]
	public static extern short GetAsyncKeyState(int vKey);

	[DllImport("user32.dll")]
	public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll")]
	public static extern IntPtr WindowFromPoint(POINT pt);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

	[DllImport("user32.dll")]
	public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

	[DllImport("user32.dll")]
	public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

	[DllImport("user32.dll")]
	public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

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
