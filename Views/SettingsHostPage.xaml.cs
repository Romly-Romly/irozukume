// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Irozukume.Views;

// 設定 UI を Frame に載せて、組み込みの NavigationThemeTransition で出し入れするための薄い宿主ページ。
// 本体側で生成した設定 UI(SettingsView)を SetContent で受け取って中身に差し込む。XAML 要素はナビゲーションの引数で WinRT 境界を越えられず渡せないため、引数では渡さずここへ差し込む。
// 背景は不透明にして、本体の上へ重ねたとき背後を隠す。閉じる(GoBack)アニメーションではこの面ごと退き、背後の本体が現れる。
public sealed partial class SettingsHostPage : Page
{
	public SettingsHostPage()
	{
		this.InitializeComponent();
	}




	// 本体側で生成した設定 UI を中身に差し込む。ナビゲート直後に呼ぶ。
	public void SetContent(UIElement? content)
	{
		Content = content;
	}
}
