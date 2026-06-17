// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Helpers;

// 画面カラーピッカーのガラスレンズの効きの全体設定。ピッカーを開くときにこの値からガラスパラメータを組む。設定ページの上級者向け設定で調整し、設定ファイルへ保存する。
// 設定ページの ViewModel が値を書き込み、ScreenColorPickerService が読む。
public static class ScreenPickerTuning
{
	// 既定値。「デフォルトに戻す」で戻す基準。拡大率は、よくある分数スケール(125〜175%)で1ソース画素が画素精度の整数ブロック描画に乗る値にしてあり、採色点のレチクルがちょうど1画素を囲うようにする。
	public const double DefaultMagnify = 12.0;
	public const double DefaultDiameter = 300.0;
	public const bool DefaultGlassEffect = true;
	public const double DefaultRefractionStrength = 1.0;

	// 拡大率。レンズが背後(カーソル周辺)のデスクトップをどれだけ拡大して見せるか。上げるほど1画素が大きく映り採色しやすくなる。
	public static double Magnify = DefaultMagnify;

	// レンズの直径(DIP)。円形のガラス本体の大きさ。
	public static double Diameter = DefaultDiameter;

	// ガラス効果の総スイッチ。オフにすると屈折・ぼかし・彩度強調・白み・光沢・ハイライトを切り、縁と拡大だけの素のルーペにする。
	public static bool GlassEffect = DefaultGlassEffect;

	// 縁の屈折の強さの倍率(既定の屈折量に掛ける)。1.0 で既定どおり、0 で屈折なし、上げるほど縁が強く歪む。ガラス効果オフでは効かない。
	public static double RefractionStrength = DefaultRefractionStrength;
}
