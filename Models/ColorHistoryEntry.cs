// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// 色の履歴に積む1件。取り込んだ色 (R・G・B) と不透明度 A、その不透明度が元の文字列に明示されていたか (HasAlpha)、出所、元の文字列を持つ。HasAlpha が偽のとき A は 255 で、この履歴を色1へ戻すときも不透明度には触れない判断に使う。
public sealed class ColorHistoryEntry
{
	public byte R { get; }

	public byte G { get; }

	public byte B { get; }

	public byte A { get; }

	public bool HasAlpha { get; }

	public HistoryKind Kind { get; }

	public string Source { get; }




	public ColorHistoryEntry(byte r, byte g, byte b, byte a, bool hasAlpha, HistoryKind kind, string source)
	{
		R = r;
		G = g;
		B = b;
		A = a;
		HasAlpha = hasAlpha;
		Kind = kind;
		Source = source;
	}




	// 色・不透明度・出所が同じ履歴かどうか。元の文字列 (Source) は比べない。同じ色を別の表記で貼っても1件にまとめ、最新の表記で先頭へ繰り上げるために使う。
	public bool SameColor(ColorHistoryEntry other)
	{
		return Kind == other.Kind
			&& R == other.R
			&& G == other.G
			&& B == other.B
			&& A == other.A
			&& HasAlpha == other.HasAlpha;
	}
}
