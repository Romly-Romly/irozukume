// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;
using Irozukume.Glass;

namespace Irozukume.Helpers;

// スライダーつまみのガラスレンズ(ルーペ)の効きを、設定ページから動かすための全体設定。各スライダー・色相環・パッドは、自分の基準パラメータにこの設定を掛け合わせてレンズを組む。ドラッグ開始時に読むため、設定を変えると次のドラッグから反映される。強さ・ズレは基準値に対する倍率で、1.0 で基準どおり(コントロールごとに異なる基準値の比率を保つ)。設定ページの ViewModel が値を書き込み、描画コントロール側が読む。
public static class LensTuning
{
	// 既定値。「デフォルトに戻す」で戻す基準で、いずれも現状の見た目をそのまま保つ値。
	public const bool DefaultLensEffect = true;
	public const double DefaultMagnify = 1.0;
	public const bool DefaultRefraction = true;
	public const double DefaultRefractionStrength = 1.0;
	public const double DefaultBevel = 1.0;
	public const double DefaultChromaSpread = 1.0;
	public const bool DefaultShowHighlight = true;
	public const double DefaultHighlightDesignAzim = 225.0;
	public const double DefaultHighlightLightFracX = 0.1;
	public const double DefaultHighlightLightFracY = 0.1;

	// レンズ効果の総スイッチ。オフにすると屈折も色収差も切り、ただの拡大にする。
	public static bool LensEffect = DefaultLensEffect;

	// 拡大率の係数。基準を 1.0(等倍)からの増分として扱い、実効拡大率は 1.0 + (基準 - 1.0) × この係数。0 で全コントロール等倍(拡大なし)、1.0 で基準どおり、上げるほど拡大が強まる。基準が等倍のコントロール(色相環)はこの係数に依らず等倍のまま。
	public static double Magnify = DefaultMagnify;

	// 縁の屈折を掛けるか。
	public static bool Refraction = DefaultRefraction;

	// 屈折の強さの倍率(基準の縁の屈折量に掛ける)。1.0 で基準どおり、0 で屈折なし、負で向きが反転(外向き)する。
	public static double RefractionStrength = DefaultRefractionStrength;

	// 屈折が縁から内側へ効く幅(ベベル)の倍率(基準のベベル比に掛ける)。1.0 で基準どおり。色収差も同じ屈折を使うため影響を受ける。
	public static double Bevel = DefaultBevel;

	// 色収差(縁のカラーフリンジ)の倍率(基準の色収差の広がりに掛ける)。1.0 で基準どおり、0 で色収差なし。屈折の一部のため屈折オフでは効かない。
	public static double ChromaSpread = DefaultChromaSpread;

	// つまみレンズへ鏡面ハイライト(ガラスの艶)を乗せるか。レンズ効果の総スイッチ(LensEffect)がオフのときは、これに関わらず乗せない。
	public static bool ShowHighlight = DefaultShowHighlight;

	// 鏡面ハイライトの灯。各灯のドーム・方位・仰角・鋭さ・強さ・平坦化・カーブをまとめて持ち、加算合成して艶を作る。ピッカーの GlassParams.Highlights と同じ型・同じ作法。空なら艶を乗せない。灯を足せばそのぶん立体的な艶になる。左上の主たる縁艶と、その反対側(右下)へ控えめに添える縁艶の2灯。両者は同じカーブ・膨らみのため法線場を使い回せる。
	public static List<Highlight> Highlights = new()
	{
		new Highlight { Curve = "sphere", Dome = 0.55, Azim = 225, Elev = 5, Exp = 16, Power = 1.4, Flat = 0.25 },
		new Highlight { Curve = "sphere", Dome = 0.4, Azim = 45, Elev = 14, Exp = 20, Power = 0.95, Flat = 0.7 },
	};

	// 光源追従の基準となる焼き付け方位(度)。0 が右・90 が下で時計回り、225 は左上。焼いた艶全体を、レンズ位置から光源への方位とこの基準との差ぶんだけ回す。主たる灯の Azim に合わせておくと、追従ゼロのとき艶がその向きに座る。
	public static double HighlightDesignAzim = DefaultHighlightDesignAzim;

	// 光源追従の仮想光源の位置。レンズの置き先(ウィンドウの最前面オーバーレイ)の幅・高さに対する比率で、(0.1, 0.1) は左上から1割内側。レンズがこの点へ近づくほど艶がレンズ中心側へ向く。
	public static double HighlightLightFracX = DefaultHighlightLightFracX;
	public static double HighlightLightFracY = DefaultHighlightLightFracY;
}
