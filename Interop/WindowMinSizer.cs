// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Irozukume.Interop;

// リサイズ可能なウィンドウに、ユーザーのドラッグ縮小の下限となる最低サイズを課す。WinUI 3 / Windows App SDK には Window の最低サイズを直接指定する API が無いため、WM_GETMINMAXINFO を comctl32 のサブクラスで受け、MINMAXINFO.ptMinTrackSize へ下限を書き込む。
// 下限は DIP で受け取り、ハンドル先の現在 DPI を用いてメッセージごとに物理ピクセルへ換算する。これにより倍率の異なるモニタへ移しても下限が正しく追従する。
// 対象ウィンドウはアプリの生存期間を通じて存在し続ける前提のため、サブクラスの撤去は行わない。生かし続ける本インスタンスを利用側がフィールドで保持することで、ネイティブへ渡したデリゲートも合わせて生存させる。
internal sealed class WindowMinSizer
{
	private readonly IntPtr _hwnd;
	private readonly int _minWidthDip;
	private readonly int _minHeightDip;

	// ネイティブへ渡したサブクラスプロシージャ。関数ポインタが GC で回収されないよう、生存期間中フィールドで保持する。
	private readonly NativeMethods.SubclassProc _subclassProc;

	private const nuint SubclassId = 1;




	public WindowMinSizer(Window window, int minWidthDip, int minHeightDip)
	{
		_hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
		_minWidthDip = minWidthDip;
		_minHeightDip = minHeightDip;
		_subclassProc = OnSubclass;

		NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, SubclassId, IntPtr.Zero);
	}




	// サブクラスへ届くメッセージのうち WM_GETMINMAXINFO だけを処理し、最低トラックサイズを現在 DPI で物理ピクセル化して書き戻す。それ以外は既定の連鎖へ委ねる。
	private IntPtr OnSubclass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
	{
		if (uMsg == NativeMethods.WM_GETMINMAXINFO)
		{
			var info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);

			// GetDpiForWindow がまだ有効な DPI を返さない初期段階に備え、0 のときは等倍として扱う。
			uint dpi = NativeMethods.GetDpiForWindow(hWnd);
			double scale = dpi == 0 ? 1.0 : dpi / 96.0;

			info.ptMinTrackSize.X = (int)Math.Round(_minWidthDip * scale);
			info.ptMinTrackSize.Y = (int)Math.Round(_minHeightDip * scale);
			Marshal.StructureToPtr(info, lParam, false);
			return IntPtr.Zero;
		}

		return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
	}
}
