// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.ViewModels;

// 「形式を選択してコピー」のサブメニュー用に、色1の現在の表示色を各形式の文字列へ整えて提供する。
// メニュー項目のキャプションをこれらに束縛し、開くたびに実際にコピーされる文字列が見えるようにする。
// 色1や不透明度、色制限が変わると共有モデルの通知を受けて全形式を更新する。クリック時に確定する文字列とその不透明度の有無、基にした表示色は Resolve で返す。
public sealed class CopyFormatsViewModel : INotifyPropertyChanged
{
	// 表示色と不透明度の取得元。これの変更に追従して各形式を組み直す。
	private readonly ColorEditorViewModel _editor;




	public CopyFormatsViewModel(ColorEditorViewModel editor)
	{
		_editor = editor;
		_editor.PropertyChanged += OnEditorPropertyChanged;
	}




	public string Hex => Format("hex");




	public string HexAlpha => Format("hexa");




	public string Rgb => Format("rgb");




	public string Rgba => Format("rgba");




	public string RgbModern => Format("rgb_modern");




	public string RgbaModern => Format("rgba_modern");




	public string Hsl => Format("hsl");




	public string Hsla => Format("hsla");




	public string HslModern => Format("hsl_modern");




	public string HslaModern => Format("hsla_modern");




	public string Hwb => Format("hwb");




	public string Hwba => Format("hwba");




	public string Lch => Format("lch");




	public string Lcha => Format("lcha");




	public string Oklch => Format("oklch");




	public string Oklcha => Format("oklcha");




	public string Lab => Format("lab");




	public string Laba => Format("laba");




	public string Oklab => Format("oklab");




	public string Oklaba => Format("oklaba");




	public string Packed => Format("packed");




	public string PackedAlpha => Format("packeda");




	public string NamedColor => Format("named");




	// 名前付きカラーの項目の表示・非表示。色1が CSS 名と一致しないときは項目を隠す。
	public Visibility NamedColorVisibility => NamedColor.Length > 0 ? Visibility.Visible : Visibility.Collapsed;




	// ターミナルのコピー(前景・背景 × トゥルーカラー/256/16/8)のメニューキャプション。方向と形式名のラベルに続けて、実際に書き出されるエスケープ列を「ターミナル前景(フルカラー) - …」の形で見せる。256/16/8 は現在の距離計算・参照テーマで最も近いインデックスを選び、ESC表現とリセット付与の設定を反映する。
	public string TermTrueColorFg => TerminalCaption("term_tc_fg");

	public string TermTrueColorBg => TerminalCaption("term_tc_bg");

	public string Term256Fg => TerminalCaption("term_256_fg");

	public string Term256Bg => TerminalCaption("term_256_bg");

	public string Term16Fg => TerminalCaption("term_16_fg");

	public string Term16Bg => TerminalCaption("term_16_bg");

	public string Term8Fg => TerminalCaption("term_8_fg");

	public string Term8Bg => TerminalCaption("term_8_bg");




	// 編集メニューの「コピー」項目のキャプション。設定で選んだ既定の形式で色1をコピーする文字列を「コピー - …」の形で示し、実際に書き出される値が見えるようにする。色1・不透明度・既定形式の変化に追従する。
	public string CopyMenuCaption => $"{Loc.Get("Edit_Copy_Prefix")}{Format(_editor.CopyFormatKey)}";




	// 編集メニューの2つ目の「コピー」項目のキャプション。設定で選んだ2つ目の形式で色1をコピーする文字列を「コピー - …」の形で示す。1つ目と接頭辞は同じだが付く形式が異なるため一覧上で区別できる。アルファ表記も2つ目の設定に従い、Ctrl+Shift+C で書き出す値に追従する。
	public string CopyMenuCaption2 => $"{Loc.Get("Edit_Copy_Prefix")}{Format(_editor.CopyFormatKey2, _editor.CopyAlphaUnit2)}";




	// 指定の形式について、コピーする文字列・不透明度を伴うか・基にした表示色を返す。クリックでのコピーと履歴追加に使う。色1の現在の表示色を対象にする。alphaUnit を渡すとその表記でアルファを書き出し、null のときは既定(1つ目のコピー形式)の表記を使う。
	public (string Text, bool HasAlpha, byte R, byte G, byte B, byte A) Resolve(string key, WebAlphaUnit? alphaUnit = null)
	{
		return ResolveFor(_editor.DisplayedColor1, (byte)Math.Round(_editor.Alpha), key, alphaUnit);
	}




	// 指定の色・不透明度・形式について、コピーする文字列・不透明度を伴うか・基にした色を返す。サイドバーの色パネルの右クリックメニューが、アクティブでない色をそのままコピーするのに使う。色制限の丸めは呼び出し側で済ませた表示色を渡す。alphaUnit を渡すとその表記でアルファを書き出し、null のときは既定(1つ目のコピー形式)の表記を使う。
	public (string Text, bool HasAlpha, byte R, byte G, byte B, byte A) ResolveFor(Color color, byte alpha, string key, WebAlphaUnit? alphaUnit = null)
	{
		string text = FormatFor(color, alpha, key, alphaUnit);
		bool hasAlpha = key is "hexa" or "rgba" or "rgba_modern" or "hsla" or "hsla_modern" or "hwba" or "lcha" or "oklcha" or "laba" or "oklaba" or "packeda";
		return (text, hasAlpha, color.R, color.G, color.B, alpha);
	}




	// 指定の形式の現在の文字列。メニューのキャプション束縛が使う。色1の現在の表示色を対象にする。alphaUnit を渡すとその表記でアルファを書き出し、null のときは既定(1つ目のコピー形式)の表記を使う。
	private string Format(string key, WebAlphaUnit? alphaUnit = null)
	{
		return FormatFor(_editor.DisplayedColor1, (byte)Math.Round(_editor.Alpha), key, alphaUnit);
	}




	// 指定の色・不透明度を指定の形式の文字列にする。ターミナル形式(term_ で始まる)は不透明度を持たず、ESC表現・リセット・距離計算・参照テーマの設定を反映する。それ以外は不透明度をアルファ表記の設定で書き出す。alphaUnit を渡すとその表記でアルファを書き出し、null のときは既定(1つ目のコピー形式)の表記を使う。色制限の丸めは呼び出し側で済ませた表示色を渡す。
	public string FormatFor(Color color, byte alpha, string key, WebAlphaUnit? alphaUnit = null)
	{
		if (key.StartsWith("term_"))
		{
			SnapSettings snap = _editor.CurrentSnap;
			return TerminalColorFormatter.Format(key, color.R, color.G, color.B, _editor.EscStyle, _editor.TerminalResetSuffix, snap.Metric, snap.Theme);
		}

		return FormatColor(key, color.R, color.G, color.B, alpha, alphaUnit ?? _editor.CopyAlphaUnit);
	}




	// 指定の色・不透明度・既定形式について、「コピー」メニュー項目のキャプション「コピー - …」を作る。色パネルの右クリックメニューのコピー項目で、その色を実際に書き出す文字列を見せるのに使う。alphaUnit を渡すとその表記でアルファを書き出し、null のときは既定(1つ目のコピー形式)の表記を使う。
	public string CopyCaptionFor(Color color, byte alpha, string key, WebAlphaUnit? alphaUnit = null)
	{
		return $"{Loc.Get("Edit_Copy_Prefix")}{FormatFor(color, alpha, key, alphaUnit)}";
	}




	// 指定の色・不透明度・ターミナル形式について、メニューキャプション「ターミナル前景(フルカラー) - …」を作る。色パネルの右クリックメニューのターミナル項目で使う。方向と形式名のラベルに続けて実際に書き出されるエスケープ列を見せる。
	public string TermCaptionFor(Color color, byte alpha, string key)
	{
		return $"{TermLabel(key)} - {FormatFor(color, alpha, key)}";
	}




	// ターミナル形式のメニューキャプションを作る。方向(前景/背景)と形式名のラベルに続けて、実際にコピーされるエスケープ列を「ターミナル前景(フルカラー) - …」の形で並べる。コピー本体(Resolve)はラベルを含めずエスケープ列だけを書き出すため、表示と書き出しを分ける。
	private string TerminalCaption(string key)
	{
		return $"{TermLabel(key)} - {Format(key)}";
	}




	// ターミナル形式の識別子から、方向と形式名のラベル「ターミナル前景(フルカラー)」等を作る。
	private static string TermLabel(string key)
	{
		string direction = key.EndsWith("_bg") ? Loc.Get("Copy_Term_Background") : Loc.Get("Copy_Term_Foreground");
		string format = key switch
		{
			"term_tc_fg" or "term_tc_bg" => Loc.Get("Copy_Term_TrueColor"),
			"term_256_fg" or "term_256_bg" => Loc.Get("Copy_Term_256"),
			"term_16_fg" or "term_16_bg" => Loc.Get("Copy_Term_16"),
			"term_8_fg" or "term_8_bg" => Loc.Get("Copy_Term_8"),
			_ => "",
		};

		return Loc.Get("Copy_Term_Label", direction, format);
	}




	// 形式の識別子から、その形式の文字列を作る。CSS 関数のアルファ表記は alphaUnit に従う。
	private static string FormatColor(string key, byte r, byte g, byte b, byte a, WebAlphaUnit alphaUnit)
	{
		return key switch
		{
			"hex" => ColorStringFormatter.HexRgb(r, g, b),
			"hexa" => ColorStringFormatter.HexRgba(r, g, b, a),
			"rgb" => ColorStringFormatter.Rgb(r, g, b),
			"rgba" => ColorStringFormatter.Rgba(r, g, b, a, alphaUnit),
			"rgb_modern" => ColorStringFormatter.RgbModern(r, g, b),
			"rgba_modern" => ColorStringFormatter.RgbaModern(r, g, b, a, alphaUnit),
			"hsl" => ColorStringFormatter.Hsl(r, g, b),
			"hsla" => ColorStringFormatter.Hsla(r, g, b, a, alphaUnit),
			"hsl_modern" => ColorStringFormatter.HslModern(r, g, b),
			"hsla_modern" => ColorStringFormatter.HslaModern(r, g, b, a, alphaUnit),
			"hwb" => ColorStringFormatter.Hwb(r, g, b),
			"hwba" => ColorStringFormatter.Hwba(r, g, b, a, alphaUnit),
			"lch" => ColorStringFormatter.Lch(r, g, b),
			"lcha" => ColorStringFormatter.Lcha(r, g, b, a, alphaUnit),
			"oklch" => ColorStringFormatter.Oklch(r, g, b),
			"oklcha" => ColorStringFormatter.Oklcha(r, g, b, a, alphaUnit),
			"lab" => ColorStringFormatter.Lab(r, g, b),
			"laba" => ColorStringFormatter.Laba(r, g, b, a, alphaUnit),
			"oklab" => ColorStringFormatter.Oklab(r, g, b),
			"oklaba" => ColorStringFormatter.Oklaba(r, g, b, a, alphaUnit),
			"packed" => ColorStringFormatter.PackedRgb(r, g, b),
			"packeda" => ColorStringFormatter.PackedArgb(a, r, g, b),
			"named" => ColorStringFormatter.TryNamedColor(r, g, b, out string name) ? name : "",
			_ => "",
		};
	}




	private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// 色1 (Color1HexText が代表) と不透明度 (Alpha) の変化で全形式が変わりうる。色制限の切替も Color1HexText の通知を伴う。1つ目のアルファ表記 (WebAlphaUnitIndex) の切替は、1つ目のコピーとサブメニュー・右クリックの個別形式が使う CSS 関数のアルファを変えるため、まとめて作り直す。
		if (e.PropertyName == nameof(ColorEditorViewModel.Color1HexText)
			|| e.PropertyName == nameof(ColorEditorViewModel.Alpha)
			|| e.PropertyName == nameof(ColorEditorViewModel.WebAlphaUnitIndex))
		{
			NotifyAll();
		}
		else if (e.PropertyName == nameof(ColorEditorViewModel.WebAlphaUnit2Index))
		{
			// 2つ目のアルファ表記が変わると2つ目の「コピー」項目のキャプションだけが変わる。1つ目とサブメニューは1つ目の表記に従うため変わらない。
			OnPropertyChanged(nameof(CopyMenuCaption2));
		}
		else if (e.PropertyName == nameof(ColorEditorViewModel.CopyFormatIndex))
		{
			// 既定の形式が変わると「コピー」項目のキャプションだけが変わる。個別形式の表示は色1・不透明度に依るため変わらない。
			OnPropertyChanged(nameof(CopyMenuCaption));
		}
		else if (e.PropertyName == nameof(ColorEditorViewModel.CopyFormatIndex2))
		{
			// 2つ目の既定の形式が変わると2つ目の「コピー」項目のキャプションだけが変わる。
			OnPropertyChanged(nameof(CopyMenuCaption2));
		}
		else if (e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap)
			|| e.PropertyName == nameof(ColorEditorViewModel.TerminalEscIndex)
			|| e.PropertyName == nameof(ColorEditorViewModel.TerminalResetSuffix))
		{
			// 距離計算・参照テーマ (CurrentSnap)・ESC表現・リセット付与が変わると、ターミナル形式のコピー文字列だけが変わる。色1・不透明度に依る他形式は変わらない。
			NotifyTerminal();
		}
	}




	private void NotifyAll()
	{
		OnPropertyChanged(nameof(Hex));
		OnPropertyChanged(nameof(HexAlpha));
		OnPropertyChanged(nameof(Rgb));
		OnPropertyChanged(nameof(Rgba));
		OnPropertyChanged(nameof(RgbModern));
		OnPropertyChanged(nameof(RgbaModern));
		OnPropertyChanged(nameof(Hsl));
		OnPropertyChanged(nameof(Hsla));
		OnPropertyChanged(nameof(HslModern));
		OnPropertyChanged(nameof(HslaModern));
		OnPropertyChanged(nameof(Hwb));
		OnPropertyChanged(nameof(Hwba));
		OnPropertyChanged(nameof(Lch));
		OnPropertyChanged(nameof(Lcha));
		OnPropertyChanged(nameof(Oklch));
		OnPropertyChanged(nameof(Oklcha));
		OnPropertyChanged(nameof(Lab));
		OnPropertyChanged(nameof(Laba));
		OnPropertyChanged(nameof(Oklab));
		OnPropertyChanged(nameof(Oklaba));
		OnPropertyChanged(nameof(Packed));
		OnPropertyChanged(nameof(PackedAlpha));
		OnPropertyChanged(nameof(NamedColor));
		OnPropertyChanged(nameof(NamedColorVisibility));
		NotifyTerminal();
		OnPropertyChanged(nameof(CopyMenuCaption));
		OnPropertyChanged(nameof(CopyMenuCaption2));
	}




	// ターミナル形式(前景・背景 × トゥルーカラー/256/16/8)のキャプションをまとめて通知する。
	private void NotifyTerminal()
	{
		OnPropertyChanged(nameof(TermTrueColorFg));
		OnPropertyChanged(nameof(TermTrueColorBg));
		OnPropertyChanged(nameof(Term256Fg));
		OnPropertyChanged(nameof(Term256Bg));
		OnPropertyChanged(nameof(Term16Fg));
		OnPropertyChanged(nameof(Term16Bg));
		OnPropertyChanged(nameof(Term8Fg));
		OnPropertyChanged(nameof(Term8Bg));
	}




	public event PropertyChangedEventHandler? PropertyChanged;




	private void OnPropertyChanged(string name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
