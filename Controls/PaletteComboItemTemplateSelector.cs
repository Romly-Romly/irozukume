// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Irozukume.ViewModels;

namespace Irozukume.Controls;

// パレットの種類コンボの項目テンプレートを、通常項目と区切りで切り替える。通常項目は素の文字だけにして標準のコンボと同じ見た目を保ち、区切りだけ専用の線テンプレートにする。
public sealed class PaletteComboItemTemplateSelector : DataTemplateSelector
{
	// 通常項目(表示名)のテンプレート。
	public DataTemplate? Item { get; set; }

	// 区切り項目(線)のテンプレート。
	public DataTemplate? Separator { get; set; }




	protected override DataTemplate? SelectTemplateCore(object item)
	{
		return item is PaletteComboItem combo && combo.IsSeparator ? Separator : Item;
	}




	protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
	{
		return SelectTemplateCore(item);
	}
}
