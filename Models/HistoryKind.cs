// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// 色の履歴1件の出所。履歴のリストでは、この出所に応じた左側のアイコンで取り込み方を見分ける。出所フィルタは貼り付けとコピーの2区分で、画面ピックは取り込み方として貼り付けと同じ側に含める。
public enum HistoryKind
{
	// クリップボードからの貼り付けで色を取り込んだ。
	Paste,

	// 形式を選んでクリップボードへ色をコピーした。
	Copy,

	// 画面カラーピッカー(アイドロッパー)で画面から色を拾った。
	Pick,
}
