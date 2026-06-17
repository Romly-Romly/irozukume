// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Microsoft.UI.Xaml.Controls;

namespace Irozukume.Views;

// 設定オーバーレイ用 Frame の待機状態に置く透明な土台ページ。
// 設定を閉じた(GoBack した)ときの戻り先で、これ自体は何も描かない。透明なので背後の本体がそのまま見え、Frame の当たり判定を切ることで本体への操作も通る。
// Frame.Navigate は XAML 型メタデータ経由でページを生成するため、ナビゲート先のページは x:Class を持つ XAML ページにする。
public sealed partial class BackdropPage : Page
{
	public BackdropPage()
	{
		this.InitializeComponent();
	}
}
