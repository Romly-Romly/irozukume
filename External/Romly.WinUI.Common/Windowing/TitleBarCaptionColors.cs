using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Romly.WinUI.Common.Windowing;

/// <summary>
/// ExtendsContentIntoTitleBar 下では、最小化・最大化・閉じるのキャプションボタンはシステムが描くままで、ウィンドウ内容(ルート要素)の RequestedTheme/ActualTheme には追従しない。
/// そのため、明るい背景に白いグリフ・暗い背景に黒いグリフといった視認できない組み合わせが起きうる。これを避けるべく、AppWindow のタイトルバー越しに前景色を実効テーマへ合わせて当てる。背景は閉じるボタンの赤いホバー等のシステム既定に委ねる。
/// </summary>
public static class TitleBarCaptionColors
{
	/// <summary>
	/// ウィンドウ内容(ルート要素)の実効テーマに、キャプションボタンの前景色を追従させる。初期状態を一度当て、以後はテーマの変化(設定での切り替えや、Default 時の OS テーマ変更)を購読して貼り直す。
	/// </summary>
	/// <param name="window">前景色を追従させる対象のウィンドウ。内容(Content)が定まった後、すなわち InitializeComponent の後で渡すこと。</param>
	public static void FollowContentTheme(Window window)
	{
		if (window.Content is not FrameworkElement root)
		{
			return;
		}

		root.ActualThemeChanged += (sender, args) => Apply(window, sender.ActualTheme);
		Apply(window, root.ActualTheme);
	}




	/// <summary>
	/// キャプションボタンの前景色を、システム標準のテーマリソースから当てる。フォーカスのある状態には WindowCaptionForeground、ウィンドウが非アクティブのときには WindowCaptionForegroundDisabled を用いる。AppWindowTitleBar の Inactive 系プロパティはウィンドウ非アクティブ時のボタンに効くため、リソースの Disabled とちょうど対応する。直値を持たないため、High Contrast やシステム配色のカスタムにもそのまま追従する。
	/// </summary>
	private static void Apply(Window window, ElementTheme theme)
	{
		Color foreground = CaptionColor("WindowCaptionForeground", theme);
		Color inactiveForeground = CaptionColor("WindowCaptionForegroundDisabled", theme);

		var titleBar = window.AppWindow.TitleBar;
		titleBar.ButtonForegroundColor = foreground;
		titleBar.ButtonHoverForegroundColor = foreground;
		titleBar.ButtonPressedForegroundColor = foreground;
		titleBar.ButtonInactiveForegroundColor = inactiveForeground;
	}




	/// <summary>
	/// 指定したキャプション用テーマリソース(SolidColorBrush)の色を返す。アプリのリソースはアプリのテーマへ解決されるため、ウィンドウ内容のテーマがアプリのテーマと一致している間は実効テーマと揃う。標準リソースが取得できない場合に限り、引数のテーマで最低限見える色へ退避する。
	/// </summary>
	private static Color CaptionColor(string key, ElementTheme theme)
	{
		try
		{
			if (Application.Current.Resources[key] is SolidColorBrush brush)
			{
				return brush.Color;
			}
		}
		catch
		{
		}

		return theme == ElementTheme.Dark ? Colors.White : Colors.Black;
	}
}
