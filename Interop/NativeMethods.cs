// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Runtime.InteropServices;

namespace Irozukume.Interop;

// メインウィンドウに最低サイズを課すために必要な Win32 API の P/Invoke 宣言と、関連する定数・構造体・デリゲートをまとめる。
// WM_GETMINMAXINFO が運ぶ寸法はすべて物理ピクセル。DIP からの換算には、対象ウィンドウの現在 DPI を返す GetDpiForWindow を用いる。
internal static class NativeMethods
{
	// ウィンドウの寸法・位置の最小最大の問い合わせ。リサイズ枠のドラッグ等に際して送られ、lParam の MINMAXINFO を書き換えることで下限・上限を返す。
	public const uint WM_GETMINMAXINFO = 0x0024;

	[StructLayout(LayoutKind.Sequential)]
	public struct POINT
	{
		public int X;
		public int Y;
	}

	// WM_GETMINMAXINFO の lParam が指す構造体。ptMinTrackSize がユーザーのドラッグ縮小の下限(物理ピクセル)を表す。
	[StructLayout(LayoutKind.Sequential)]
	public struct MINMAXINFO
	{
		public POINT ptReserved;
		public POINT ptMaxSize;
		public POINT ptMaxPosition;
		public POINT ptMinTrackSize;
		public POINT ptMaxTrackSize;
	}

	// サブクラスプロシージャ。元のウィンドウプロシージャの手前で各メッセージを受け取る。処理しないものは DefSubclassProc へ委ねる。
	public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData);

	// 既存のウィンドウプロシージャを置き換えずにメッセージ列へサブクラスを差し込む。元プロシージャの保存と連鎖は comctl32 が受け持つため、自前での CallWindowProc は不要。
	[DllImport("comctl32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, IntPtr dwRefData);

	[DllImport("comctl32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

	// サブクラスで処理しなかったメッセージを、サブクラス連鎖の次(最終的には元のプロシージャ)へ渡す。
	[DllImport("comctl32.dll")]
	public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

	// 指定ウィンドウの現在の DPI を返す。物理ピクセルへの換算係数 dpi/96 を得るために使う。モニタ移動で倍率が変わるたびに読み直す。
	[DllImport("user32.dll")]
	public static extern uint GetDpiForWindow(IntPtr hWnd);

	public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
	public const int MDT_EFFECTIVE_DPI = 0;

	// 指定したスクリーン座標を含むモニタのハンドルを返す。座標が画面外でも、MONITOR_DEFAULTTONEAREST を指定すれば最も近いモニタを返す。保存位置の復元先ディスプレイを同定するために使う。
	[DllImport("user32.dll")]
	public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

	// 指定モニタの DPI を返す。dpiType に MDT_EFFECTIVE_DPI を渡すと表示倍率込みの実効 DPI が得られる。戻り値 0 (S_OK) で成功。復元先モニタの倍率で DIP を物理ピクセル化するために使う。
	[DllImport("shcore.dll")]
	public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

	// 指定ウィンドウを前面のアクティブウィンドウにする。Show/Activate だけでは抜けられない前面奪取制限を、ユーザー入力 (トレイクリック等) の直後にシステムが与える一時的な前面化許可を使って突破するために明示的に呼ぶ。
	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetForegroundWindow(IntPtr hWnd);

	// ウィンドウのシステムメニュー(元のサイズに戻す・移動・サイズ変更・最小化・最大化・閉じる)を指定スクリーン座標へ出す。Windows 自身がタイトルバーの右クリックや Shift+F10 でこのメッセージを使ってメニューを提示する。項目の有効/無効はシステムが現在のウィンドウ状態に合わせて設定する。lParam の下位ワードが X、上位ワードが Y(スクリーン座標)。
	public const uint WM_POPUPSYSTEMMENU = 0x0313;

	// メッセージを対象ウィンドウのキューへ非同期に投函する。システムメニューは自前のモーダルループを回すため、現在のハンドラを抜けてから出させる。
	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	// クライアント座標(物理ピクセル)をスクリーン座標へ変換する。XAML 要素の位置は DIP で得られるため、現在 DPI で物理ピクセル化したうえでこれに通す。
	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
}
