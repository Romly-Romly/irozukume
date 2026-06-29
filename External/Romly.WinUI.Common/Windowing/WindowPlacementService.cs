using System;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Romly.WinUI.Common.Interop;

namespace Romly.WinUI.Common.Windowing;

/// <summary>
/// ウィンドウ位置・サイズの取得と復元。位置は物理ピクセル、サイズはDIPで扱い、復元時はモニタ構成・解像度・DPIの変化に追従する。
/// </summary>
/// <remarks>
/// 復元先モニタは DisplayId ではなく座標とワークエリアの重なりから毎回決め直す。DisplayId は再起動やモニタ抜き差しで当てにならないため。復元先モニタの実効DPIで DIP をピクセルへ戻し、最後にワークエリア内へクランプして画面外復元を防ぐ。
/// </remarks>
public static class WindowPlacementService
{
	/// <summary>
	/// 現在のウィンドウの配置を取り出す。
	/// </summary>
	/// <param name="appWindow">配置を取り出す対象のウィンドウ。</param>
	/// <returns>物理座標とDIPサイズ、および載っているモニタのワークエリアを収めた配置情報。</returns>
	public static WindowPlacement Capture(AppWindow appWindow)
	{
		int x = appWindow.Position.X;
		int y = appWindow.Position.Y;
		double scale = MonitorScaleAt(x, y);
		RectInt32 work = WorkAreaAt(x, y);

		return new WindowPlacement
		{
			X = x,
			Y = y,
			WidthDip = appWindow.Size.Width / scale,
			HeightDip = appWindow.Size.Height / scale,
			WorkX = work.X,
			WorkY = work.Y,
			WorkWidth = work.Width,
			WorkHeight = work.Height,
		};
	}




	/// <summary>
	/// 保存した配置を現在のモニタ構成へ当てはめて復元する。
	/// </summary>
	/// <param name="appWindow">配置を復元する対象のウィンドウ。</param>
	/// <param name="placement">復元する配置情報。</param>
	/// <param name="minWidthDip">復元時に保証する最小の幅(DIP)。これ以下に潰れた値が保存されていても操作できる大きさへ戻す。</param>
	/// <param name="minHeightDip">復元時に保証する最小の高さ(DIP)。これ以下に潰れた値が保存されていても操作できる大きさへ戻す。</param>
	public static void Apply(AppWindow appWindow, WindowPlacement placement, int minWidthDip = 400, int minHeightDip = 300)
	{
		RectInt32 target = ResolveTargetWorkArea(placement);

		// 復元先モニタのDPIで DIP サイズを物理ピクセルへ戻す。最小サイズもDPIに合わせて算出する。
		double scale = MonitorScaleAt(target.X + target.Width / 2, target.Y + target.Height / 2);
		int minWidth = (int)Math.Round(minWidthDip * scale);
		int minHeight = (int)Math.Round(minHeightDip * scale);
		int width = (int)Math.Round(placement.WidthDip * scale);
		int height = (int)Math.Round(placement.HeightDip * scale);
		width = Clamp(width, minWidth, Math.Max(minWidth, target.Width));
		height = Clamp(height, minHeight, Math.Max(minHeight, target.Height));

		int x;
		int y;
		if (placement.WorkWidth > 0 && placement.WorkHeight > 0)
		{
			// 保存時のモニタ内での相対位置を保ったまま、復元先モニタの原点へ移す。モニタが別位置・別原点へ動いていても同じ見え方になる。
			x = target.X + (placement.X - placement.WorkX);
			y = target.Y + (placement.Y - placement.WorkY);
		}
		else
		{
			// ワークエリア情報が無い古い保存データは、保存した絶対座標をそのまま使う。
			x = placement.X;
			y = placement.Y;
		}

		x = Clamp(x, target.X, target.X + target.Width - width);
		y = Clamp(y, target.Y, target.Y + target.Height - height);

		appWindow.MoveAndResize(new RectInt32(x, y, width, height));
	}




	/// <summary>
	/// 既定サイズ (DIP) でウィンドウを開く。物理化した寸法は、そのモニタの可視ワークエリアを超えない範囲へ収める。
	/// </summary>
	/// <param name="appWindow">サイズを変える対象のウィンドウ。</param>
	/// <param name="widthDip">開く幅(DIP)。</param>
	/// <param name="heightDip">開く高さ(DIP)。</param>
	/// <remarks>
	/// AppWindow.Resize は物理ピクセルを取るため、現在の生成位置のモニタ DPI で DIP を物理化してから渡す。素の DIP 値をそのまま渡すと高 DPI 環境で意図より小さく開き、DIP 基準で物理化される最低サイズが課されている場合はそれを下回って窮屈な窓へ切り詰められることもある。
	/// </remarks>
	public static void ResizeToDip(AppWindow appWindow, int widthDip, int heightDip)
	{
		var pos = appWindow.Position;
		double scale = MonitorScaleAt(pos.X, pos.Y);
		RectInt32 work = WorkAreaAt(pos.X, pos.Y);

		int width = Math.Min((int)Math.Round(widthDip * scale), work.Width);
		int height = Math.Min((int)Math.Round(heightDip * scale), work.Height);

		appWindow.Resize(new SizeInt32(width, height));
	}




	/// <summary>
	/// 復元先モニタのワークエリアを決める。保存時のワークエリアと最も重なる現在のモニタを選ぶ。重なるモニタが無い(その画面が消えた等)場合は保存座標の直近モニタへ退避する。
	/// </summary>
	private static RectInt32 ResolveTargetWorkArea(WindowPlacement placement)
	{
		if (placement.WorkWidth > 0 && placement.WorkHeight > 0)
		{
			DisplayArea? best = null;
			long bestOverlap = 0;

			// FindAll() が返すコレクションは foreach で列挙すると WinRT プロジェクションの不整合で InvalidCastException を投げる。インデックスで参照して列挙を避ける。
			var areas = DisplayArea.FindAll();
			for (int i = 0; i < areas.Count; i++)
			{
				DisplayArea da = areas[i];
				RectInt32 w = da.WorkArea;
				long overlap = OverlapArea(w.X, w.Y, w.Width, w.Height, placement.WorkX, placement.WorkY, placement.WorkWidth, placement.WorkHeight);
				if (overlap > bestOverlap)
				{
					bestOverlap = overlap;
					best = da;
				}
			}

			if (best is not null)
			{
				return best.WorkArea;
			}
		}

		var anchor = new PointInt32(placement.X, placement.Y);
		return DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest).WorkArea;
	}




	/// <summary>
	/// 指定したスクリーン座標が乗るモニタのワークエリアを返す。
	/// </summary>
	private static RectInt32 WorkAreaAt(int x, int y)
	{
		var point = new PointInt32(x, y);
		return DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest).WorkArea;
	}




	/// <summary>
	/// 指定したスクリーン座標が乗るモニタの実効DPI倍率(96基準)を返す。取得に失敗したら等倍として扱う。
	/// </summary>
	private static double MonitorScaleAt(int x, int y)
	{
		IntPtr monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = x, Y = y }, NativeMethods.MONITOR_DEFAULTTONEAREST);
		if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX != 0)
		{
			return dpiX / 96.0;
		}

		return 1.0;
	}




	/// <summary>
	/// 2つの矩形の重なり面積を返す。重ならなければ0。
	/// </summary>
	private static long OverlapArea(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
	{
		int left = Math.Max(ax, bx);
		int top = Math.Max(ay, by);
		int right = Math.Min(ax + aw, bx + bw);
		int bottom = Math.Min(ay + ah, by + bh);
		int w = right - left;
		int h = bottom - top;
		if (w <= 0 || h <= 0)
		{
			return 0;
		}

		return (long)w * h;
	}




	private static int Clamp(int value, int min, int max)
	{
		if (max < min)
		{
			return min;
		}

		if (value < min)
		{
			return min;
		}

		if (value > max)
		{
			return max;
		}

		return value;
	}
}
