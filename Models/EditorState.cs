// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Irozukume.Models;

// 保存済みの色編集状態。サイドバーの色一覧 (各色の RGB と不透明度) とアクティブな色の位置を持ち、スライダー背景を実際の色で描くか (実際の色で表示) の真偽も残す。
public sealed class EditorState
{
	// サイドバーの色の一覧。表示の並び順のまま保存する。このキーを持たない設定や1件も解釈できない設定では、既定の2色で始める。
	[JsonPropertyName("colors")]
	public List<ColorEntryState>? Colors { get; set; }

	// アクティブ (編集対象) な色の位置 (colors の添字)。範囲外の値は範囲内へ丸めて扱う。
	[JsonPropertyName("active_color")]
	public int ActiveColor { get; set; }

	// アルファ値の表示単位。"byte" (0–255) / "percent" (0–100%) / "normalized" (0.0–1.0)。未指定や未知の値は 0–255 表記として扱う。
	[JsonPropertyName("alpha_unit")]
	public string? AlphaUnit { get; set; }

	// R・G・B の表示単位。"byte" (0–255) / "hex" (00–FF) / "normalized" (0.0–1.0)。未指定や未知の値は 0–255 表記として扱う。
	[JsonPropertyName("rgb_unit")]
	public string? RgbUnit { get; set; }

	// 既定はオン。このキーを持たない古い設定ファイルでも、VM の既定 (実際の色で表示) に揃える。
	[JsonPropertyName("show_actual_color")]
	public bool ShowActualColor { get; set; } = true;

	// サイドバーをテキストモード(コントラスト確認)にするか。既定はオフ。このキーを持たない設定ファイルでは表示しない既定に揃える。
	[JsonPropertyName("show_contrast_text")]
	public bool ShowContrastText { get; set; }

	// アルファ値のスライダー類を、タブの中身の下に常駐させて表示するか。既定はオフ。オンのとき、選択中のタブに依らず不透明度スライダーと単位の組を見せる。このキーを持たない設定ファイルでは表示しない既定に揃える。表示だけの設定で、色や丸めには影響しない。
	[JsonPropertyName("show_alpha")]
	public bool ShowAlpha { get; set; }

	// コマンドバーの各ボタンにアイコンの下のキャプション(名前)を表示するか。既定はオン。オフにするとアイコンだけになり横幅を節約できる。このキーを持たない古い設定ファイルでも、VM の既定(表示する)に揃える。表示だけの設定で、色や丸めには影響しない。
	[JsonPropertyName("show_toolbar_caption")]
	public bool ShowToolbarCaption { get; set; } = true;

	// P型 (1型) での見え方の行をサイドバーの各色パネルへ表示するか。既定はオフ。表示だけの設定で、色や丸めには影響しない。
	[JsonPropertyName("show_protan")]
	public bool ShowProtan { get; set; }

	// D型 (2型) での見え方の行をサイドバーの各色パネルへ表示するか。既定はオフ。
	[JsonPropertyName("show_deutan")]
	public bool ShowDeutan { get; set; }

	// T型 (3型) での見え方の行をサイドバーの各色パネルへ表示するか。既定はオフ。
	[JsonPropertyName("show_tritan")]
	public bool ShowTritan { get; set; }

	// 1色覚での見え方の行をサイドバーの各色パネルへ表示するか。既定はオフ。
	[JsonPropertyName("show_monochromacy")]
	public bool ShowMonochromacy { get; set; }

	// 色覚シミュレーションの弱まりの強さ (severity)。0 が正常、1 が完全な2色覚で、P型・D型・T型の見え方とコントラストマトリックスのシミュレーションに共通で効く。1色覚には効かない。このキーを持たない設定では 0.6 で始める。
	[JsonPropertyName("vision_severity")]
	public double VisionSeverity { get; set; } = 0.6;

	// コントラスト確認欄に入力した文字列。このキーを持たない設定では、言語ごとの既定のサンプル文で始める。利用者が空にしたときは空文字として保存し、次回も空で開く。
	[JsonPropertyName("contrast_text")]
	public string? ContrastText { get; set; }

	// 編集メニューの「コピー」と Ctrl+C でアクティブな色を書き出す既定の形式。"hex"/"hexa"/"rgb"/"rgba"/"hsl"/"hsla"/"hwb"/"packed"/"packeda"。未指定や未知の値は 16 進 (#RRGGBB) として扱う。
	[JsonPropertyName("copy_format")]
	public string? CopyFormat { get; set; }

	// Ctrl+Shift+C でアクティブな色を書き出す2つ目の既定の形式。候補は copy_format と同じ。未指定や未知の値は rgb() として扱う。
	[JsonPropertyName("copy_format2")]
	public string? CopyFormat2 { get; set; }

	// YUV/YCbCr タブの係数規格。"bt601" / "bt709" / "bt2020"。未指定なら VM の既定 (BT.709) に揃える。
	[JsonPropertyName("yuv_standard")]
	public string? YuvStandard { get; set; }

	// YUV/YCbCr タブの量子化レンジ。既定はフルレンジ。このキーを持たない古い設定ファイルでも VM の既定に揃える。
	[JsonPropertyName("yuv_full_range")]
	public bool YuvFullRange { get; set; } = true;

	// YUV/YCbCr タブを符号付き (YUV) 表記で読むか。既定は 0–255 中心 128 の YCbCr 表記。
	[JsonPropertyName("yuv_signed")]
	public bool YuvSigned { get; set; }

	// YUV/YCbCr タブで色差 (Cb・Cr) を sRGB 色域へ制限 (色域境界でクランプ) するか。既定はオフで、色域外の色差も保ったまま、表示できない範囲を色差平面上に可視化する。このキーを持たない古い設定ファイルでも VM の既定 (制限しない) に揃える。
	[JsonPropertyName("yuv_gamut_limit")]
	public bool YuvGamutLimit { get; set; }

	// YUV/YCbCr タブの見せ方 (レイアウト)。"cbcr_plane" (Cb×Cr 平面+Y の縦バー) / "cb_luma_plane" (Cb×Y 平面+Cr の縦バー) / "cr_luma_plane" (Cr×Y 平面+Cb の縦バー)。未指定や未知の値は Cb×Cr 平面+Y の縦バーとして扱う。色1の RGB は不変で、見せ方だけが変わる。
	[JsonPropertyName("yuv_layout")]
	public string? YuvLayout { get; set; }

	// YUV/YCbCr タブの色差平面の表示枠 (スケール) の決め方。"none" (0–255 の固定枠) / "isotropic" (等方フィット) / "anisotropic" (縦横独立フィット)。フィットは固定成分 (Cb×Cr では輝度、Cb×Y では Cr、Cr×Y では Cb) ごとの色域の広がりへ枠を寄せて有効領域を広げる。未指定や未知の値は固定枠として扱う。色1の RGB は不変。
	[JsonPropertyName("yuv_scale")]
	public string? YuvScale { get; set; }

	// RGB/CMYK タブの2次元エディタの見せ方 (レイアウト)。RGB・CMYK 共通の1つのピッカーが選ぶ。"sliders" (パッド無し・線形スライダーのみ) / "rgb_gb" (G×B 平面+R) / "rgb_rb" (R×B 平面+G) / "rgb_rg" (R×G 平面+B) / "cmyk_my" (M×Y 平面+C) / "cmyk_cy" (C×Y 平面+M) / "cmyk_cm" (C×M 平面+Y)。タブに出すパッドは1枚で、RGB 系か CMYK 系のどちらか一方。未指定や未知の値はパッド無しとして扱う。色1の RGB は不変で、見せ方だけが変わる。
	[JsonPropertyName("rgbcmyk_layout")]
	public string? RgbCmykLayout { get; set; }

	// HSV/HSL タブの副モード。中央パッドと数値の表色系を "hsv" / "hsl" / "hwb" のいずれで読むか。未指定や未知の値は HSV として扱う。
	[JsonPropertyName("hsv_sub_mode")]
	public string? HsvSubMode { get; set; }

	// HSV/HSL タブで中央の2次元パッドを色相環の位置へ追従回転させるか (連動)。既定はオフ。このキーを持たない古い設定ファイルでも追従しない既定に揃える。
	[JsonPropertyName("hsv_follow_hue")]
	public bool HsvFollowHue { get; set; }

	// HWB の白み+黒みを正規化 (合計を 100% 以内へ畳む) するか。既定はオン。このキーを持たない古い設定ファイルでも VM の既定 (正規化する) に揃える。
	[JsonPropertyName("hwb_normalize")]
	public bool HwbNormalize { get; set; } = true;

	// HSV モードの見せ方 (レイアウト)。"ring_square" (色相リング+彩度・明度の正方形パッド) / "hue_sat_wheel" (角度=色相・半径=彩度の円盤+明度の縦スライダー)。未指定や未知の値は色相リング+正方形として扱う。HSL・HWB の副モードには効かず、HSV のときだけ円盤を選べる。色1の RGB は不変で、見せ方だけが変わる。
	[JsonPropertyName("hsv_layout")]
	public string? HsvLayout { get; set; }

	// HSL モードの見せ方 (レイアウト)。"ring_square" / "hue_sat_wheel" / "hue_lightness_plane" / "hue_lightness_wheel" / "hue_sat_plane" / "sl_hue_bar" / "ring_triangle" / "triangle_hue_bar"。未指定や未知の値は色相リング+三角形として扱う。HSV・HWB の副モードには効かず、HSL のときだけ効く。色1の RGB は不変で、見せ方だけが変わる。
	[JsonPropertyName("hsl_layout")]
	public string? HslLayout { get; set; }

	// HWB モードの見せ方 (レイアウト)。"ring_square" / "hue_whiteness_wheel" / "hue_blackness_plane" / "hue_blackness_wheel" / "hue_whiteness_plane" / "wb_hue_bar" / "ring_triangle" / "triangle_hue_bar"。未指定や未知の値は色相リング+正方形として扱う。HSV・HSL の副モードには効かず、HWB のときだけ効く。色1の RGB は不変で、見せ方だけが変わる。
	[JsonPropertyName("hwb_layout")]
	public string? HwbLayout { get; set; }

	// LCH タブの副モード。明度・彩度・色相を "oklch" (OKLab 基準) / "lch" (CIELAB 基準) のどちらの表色系で読むか。未指定や未知の値は OKLCH として扱う。
	[JsonPropertyName("lch_sub_mode")]
	public string? LchSubMode { get; set; }

	// LCH タブで彩度を sRGB 色域へ制限 (色域境界でクランプ) するか。既定はオフで、色域外の彩度も保ったまま、表示できない範囲をスライダー上に可視化する。このキーを持たない古い設定ファイルでも VM の既定 (制限しない) に揃える。
	[JsonPropertyName("lch_gamut_limit")]
	public bool LchGamutLimit { get; set; }

	// LCH タブの見せ方 (レイアウト)。"ring_plane" (色相リング+彩度・明度の平面) / "cl_hue_bar" (彩度×明度の平面+色相の縦スライダー) / "hue_lightness_wheel" (角度=色相・半径=明度の円盤+彩度の縦スライダー) / "hue_lightness_plane" (色相×明度の平面+彩度の縦スライダー) / "hue_chroma_wheel" (角度=色相・半径=彩度の円盤+明度の縦スライダー) / "hue_chroma_plane" (色相×彩度の平面+明度の縦スライダー)。未指定や未知の値は色相リング+平面として扱う。OKLCH・CIE LCH の副モードで共通の見せ方を使う。色1の RGB は不変で、見せ方だけが変わる。
	[JsonPropertyName("lch_layout")]
	public string? LchLayout { get; set; }

	// LCH タブの L-C 平面(色相リング+平面・C×L 平面+色相バー)で、彩度軸を色域へ詰めて表示するか。オン時はその色相で色域が届く最大彩度(cusp)まで彩度軸を縮め、彩度方向にパッドいっぱいへ色域を広げて選びやすくする(明度は常に全域)。既定はオフ。このキーを持たない古い設定ファイルでも VM の既定(詰めない)に揃える。
	[JsonPropertyName("lch_chroma_fit")]
	public bool LchChromaFit { get; set; }

	// Lab タブの副モード。明度・a 軸・b 軸を "oklab" (OKLab 基準) / "lab" (CIELAB 基準) のどちらの表色系で読むか。未指定や未知の値は OKLab として扱う。
	[JsonPropertyName("lab_sub_mode")]
	public string? LabSubMode { get; set; }

	// Lab タブの見せ方 (レイアウト)。"ab_plane" (a×b 平面+明度の縦バー) / "a_lightness_plane" (a×L 平面+b の縦バー) / "b_lightness_plane" (b×L 平面+a の縦バー)。未指定や未知の値は a×b 平面+明度の縦バーとして扱う。OKLab・CIE Lab の副モードで共通の見せ方を使う。色1の RGB は不変で、見せ方だけが変わる。
	[JsonPropertyName("lab_layout")]
	public string? LabLayout { get; set; }

	// Lab タブの a×b 平面の表示枠(スケール)の決め方。"none" (±AbMax の固定枠) / "isotropic" (等方フィット) / "anisotropic" (縦横独立フィット)。フィットは明度ごとの色域の広がりへ枠を寄せて有効領域を広げる。未指定や未知の値は固定枠として扱う。a×b 平面の見せ方のときだけ効く。色1の RGB は不変。
	[JsonPropertyName("lab_ab_scale")]
	public string? LabAbScale { get; set; }

	// Lab タブで a・b を sRGB 色域へ制限 (色域境界でクランプ) するか。既定はオフで、色域外の値も保ったまま、表示できない範囲をスライダー・パッド上に可視化する。このキーを持たない古い設定ファイルでも VM の既定 (制限しない) に揃える。
	[JsonPropertyName("lab_gamut_limit")]
	public bool LabGamutLimit { get; set; }

	// 2次元スライダー(LCH の L-C 平面、Lab の a-b 平面)で色域外をどう見せるか。"fill_boundary" (クランプ色塗り+境界線) / "fill_boundary_hatch" (それに斜線も) / "white_hatch" (白塗り+斜線)。未指定や未知の値は VM の既定 (クランプ色塗り+境界線+斜線) として扱う。
	[JsonPropertyName("gamut_oog_style")]
	public string? GamutOutOfRangeStyle { get; set; }

	// 作れる色を表示上どの制約へ丸めるか。"none"/"web_safe"/"rgb565"/"rgb555"/"rgb444"/"rgb332"。未指定や未知の値は制限なしとして扱う。各色の RGB は常にフルカラーで保持し、これは表示の丸めだけを切り替える。
	[JsonPropertyName("color_limit_mode")]
	public string? ColorLimitMode { get; set; }

	// 最も近いパレット色を選ぶ距離計算。"lab" / "redmean" / "rgb"。未指定や未知の値は知覚的(lab)として扱う。格子モード・ターミナルモードの最近傍探索で共通に使う。
	[JsonPropertyName("snap_metric")]
	public string? SnapMetric { get; set; }

	// ターミナルモードで基本16色を解決する参照テーマ。"campbell" / "vga" / "xterm"。未指定や未知の値は Campbell として扱う。
	[JsonPropertyName("terminal_theme")]
	public string? TerminalTheme { get; set; }

	// ターミナルのコピーで ESC をどの表記で書き出すか。"literal" / "backslash_e" / "hex" / "octal" / "unicode" / "caret"。未指定や未知の値は hex(\x1b)として扱う。
	[JsonPropertyName("terminal_esc")]
	public string? TerminalEsc { get; set; }

	// ターミナルのコピーで末尾にリセットを付けるか。既定は付けない。このキーを持たない古い設定ファイルでも付けない既定に揃える。
	[JsonPropertyName("terminal_reset_suffix")]
	public bool TerminalResetSuffix { get; set; }

	// CSS のカラー関数のコピーでアルファをどの表記で書き出すか。"number" (0–1) / "percent" (0–100%)。未指定や未知の値は 0–1 の数値として扱う。rgba()/hsla()/hwb() 等のアルファに効き、色や丸めには影響しない。1つ目のコピー形式 (Ctrl+C) と、形式を選択してコピーのアルファ表記を司る。
	[JsonPropertyName("web_alpha_unit")]
	public string? WebAlphaUnit { get; set; }

	// 2つ目のコピー形式 (Ctrl+Shift+C) のアルファをどの表記で書き出すか。"number" / "percent"。1つ目 (web_alpha_unit) と独立して持つ。このキーを持たない古い設定ファイルでは web_alpha_unit と同値に揃える。
	[JsonPropertyName("web_alpha_unit2")]
	public string? WebAlphaUnit2 { get; set; }

	// 起動時に開くタブの表示名。"RGB/CMYK" などのタブ見出しをそのまま持つ。このキーを持たない設定や、一致する見出しが無いときは XAML 既定の RGB/CMYK タブで開く。
	[JsonPropertyName("active_tab")]
	public string? ActiveTab { get; set; }

	// タブバーの右クリックメニューで隠したタブの識別子(Tag)の一覧。ここに挙がらないタブは表示する。最低1枚は表示するため、すべてのタブを隠すことはできない。このキーを持たない設定ではすべてのタブを表示する。
	[JsonPropertyName("hidden_tabs")]
	public List<string>? HiddenTabs { get; set; }

	// 貼り付けた色の書式に合わせて、対応するタブ(と HSV/HSL の副モード)へ自動で切り替えるか。既定はオン。このキーを持たない古い設定ファイルでも、VM の既定(切り替える)に揃える。
	[JsonPropertyName("switch_tab_on_paste")]
	public bool SwitchTabOnPaste { get; set; } = true;

	// コントラスト確認欄で不透明度(アルファ)を反映するか。既定はオン。オンのとき、サンプル文字を文字色の不透明度で背景色へ透かし、コントラスト比もその透けた実効色で測る。このキーを持たない古い設定ファイルでも、VM の既定(反映する)に揃える。
	[JsonPropertyName("contrast_include_alpha")]
	public bool ContrastIncludeAlpha { get; set; } = true;

	// テキストモードで文字色の役に選んだ色の位置 (colors の添字)。範囲外は範囲内へ丸めて扱う。
	[JsonPropertyName("contrast_text_index")]
	public int ContrastTextIndex { get; set; }

	// テキストモードで背景色の役に選んだ色の位置 (colors の添字)。既定は2番目。範囲外は範囲内へ丸めて扱う。
	[JsonPropertyName("contrast_background_index")]
	public int ContrastBackgroundIndex { get; set; } = 1;

	// コントラストマトリックスで AA を満たさない組み合わせに斜線を重ねるか。既定はオフ。表示だけの設定で、色や丸めには影響しない。
	[JsonPropertyName("matrix_hatch_fails")]
	public bool MatrixHatchFails { get; set; }

	// コントラストマトリックスの色覚シミュレーションの型。"full" / "protan" / "deutan" / "tritan" / "monochromacy"。未指定や未知の値はフルカラー(シミュレーションなし)として扱う。弱まりの強さは vision_severity と共通。セルの文字と背景の見せ方だけに効き、コントラスト比や AA/AAA の判定には影響しない。
	[JsonPropertyName("matrix_vision")]
	public string? MatrixVision { get; set; }

	// 左の色プレビュー列(サイドバー)の幅(DIP)。利用者がスプリッターでドラッグした幅を覚えておき、次回起動で復元する。このキーを持たない設定では XAML 既定の比率(2:3)で開く。
	[JsonPropertyName("sidebar_width")]
	public double? SidebarWidth { get; set; }

	// 設定ページの上級者向け設定を表示するか。既定はオフ。オンのとき、スライダーつまみレンズの効きの調整など、普段は隠している設定を見せる。
	[JsonPropertyName("advanced_settings")]
	public bool AdvancedSettings { get; set; }

	// スライダーつまみのレンズ効果(屈折・色収差)を掛けるか。既定はオン。オフにするとただの拡大になる。表示だけの設定で、色や丸めには影響しない。
	[JsonPropertyName("lens_effect")]
	public bool LensEffect { get; set; } = true;

	// レンズの拡大率の係数。基準の等倍からの増分に掛け、0 で等倍(拡大なし)、1.0 で基準どおり。既定は 1.0。
	[JsonPropertyName("lens_magnify")]
	public double LensMagnify { get; set; } = 1.0;

	// レンズの縁の屈折を掛けるか。既定はオン。
	[JsonPropertyName("lens_refraction")]
	public bool LensRefraction { get; set; } = true;

	// 屈折の強さの倍率(基準値に掛ける)。既定は 1.0(基準どおり)。負で向きが反転する。
	[JsonPropertyName("lens_refraction_strength")]
	public double LensRefractionStrength { get; set; } = 1.0;

	// 屈折のベベル(縁から内側へ効く幅)の倍率(基準値に掛ける)。既定は 1.0(基準どおり)。
	[JsonPropertyName("lens_bevel")]
	public double LensBevel { get; set; } = 1.0;

	// 色収差(縁のカラーフリンジ)の倍率(基準値に掛ける)。既定は 1.0(基準どおり)。0 で色収差なし。屈折の一部のため屈折オフでは効かない。
	[JsonPropertyName("lens_chroma_spread")]
	public double LensChromaSpread { get; set; } = 1.0;

	// 画面カラーピッカーのレンズの拡大率。上げるほど1画素が大きく映り採色しやすくなる。既定は 12.0。
	[JsonPropertyName("screen_picker_magnify")]
	public double ScreenPickerMagnify { get; set; } = 12.0;

	// 画面カラーピッカーのレンズの直径(DIP)。円形のガラス本体の大きさ。既定は 300。
	[JsonPropertyName("screen_picker_diameter")]
	public double ScreenPickerDiameter { get; set; } = 300.0;

	// 画面カラーピッカーのガラス効果を掛けるか。既定はオン。オフにすると縁と拡大だけの素のルーペになる。
	[JsonPropertyName("screen_picker_glass_effect")]
	public bool ScreenPickerGlassEffect { get; set; } = true;

	// 画面カラーピッカーの縁の屈折の強さの倍率(既定値に掛ける)。既定は 1.0。0 で屈折なし。ガラス効果オフでは効かない。
	[JsonPropertyName("screen_picker_refraction_strength")]
	public double ScreenPickerRefractionStrength { get; set; } = 1.0;

	// 画面カラーピッカーで最後に使った実拡大率 bp(1ソース画素あたりの物理px)。採色中にホイールで変えた値を次回へ引き継ぐ。0 は未設定で、その場合は拡大率設定から初期 bp を導く。
	[JsonPropertyName("screen_picker_block_px")]
	public int ScreenPickerBlockPx { get; set; }

	// 画面カラーピッカーで最後に使った取得範囲の半径(ソース画素)。0 で単一画素。採色中に Ctrl+ホイールで変えた値を次回へ引き継ぐ。
	[JsonPropertyName("screen_picker_sample_radius")]
	public int ScreenPickerSampleRadius { get; set; }

	// Mix タブで色を混ぜる色空間。"oklch" / "oklab" / "hsl" / "rgb"。未指定や未知の値は知覚的に最も忠実な OKLCH として扱う。
	[JsonPropertyName("mix_space")]
	public string? MixSpace { get; set; }

	// Mix タブの平面の塗り広げ方(空間補間)。"inverse_distance" / "gaussian" / "nearest"。未指定や未知の値は逆距離加重として扱う。
	[JsonPropertyName("mix_method")]
	public string? MixMethod { get; set; }

	// Mix タブで色相を持つ色空間の混色の回り方。"shorter" / "longer"。未指定や未知の値は近い側として扱う。
	[JsonPropertyName("mix_hue_dir")]
	public string? MixHueDir { get; set; }

	// Mix タブの重みの効き具合(とろける⇔くっきり)。0–1 で、0 がやわらか・1 がくっきり。このキーを持たない設定では中庸(0.5)で始める。
	[JsonPropertyName("mix_sharpness")]
	public double MixSharpness { get; set; } = 0.5;

	// Mix タブのつまみ(編集色のサンプル位置)の正規化座標(0–1・左上原点)。このキーを持たない設定では中央(0.5)で始める。
	[JsonPropertyName("mix_thumb_x")]
	public double MixThumbX { get; set; } = 0.5;

	[JsonPropertyName("mix_thumb_y")]
	public double MixThumbY { get; set; } = 0.5;

	// 配色タブの色の関係づけ。角度系の "complementary" / "diad" / "analogous" / "triadic" / "split_complementary" / "tetradic" / "square" / "pentad" と、拘束系の "monochromatic"(色相を揃える) / "dominant_tone"(トーンを揃える) / "tonal"(トーンを中間色域に揃える)。未指定や未知の値は補色として扱う。
	[JsonPropertyName("harmony_scheme")]
	public string? HarmonyScheme { get; set; }

	// 配色タブで配色を逆周り(色相オフセットの左右反転)にするか。既定はオフ。非対称な配色(類似色・テトラード・ダイアード)にだけ効く。
	[JsonPropertyName("harmony_reverse")]
	public bool HarmonyReverse { get; set; }

	// 配色タブの円盤の種類。"full_range"(中心白・縁黒) / "full_range_reversed"(中心黒・縁白) / "white_to_cusp"(中心白・縁 cusp) / "black_to_cusp"(中心黒・縁 cusp) / "hsv"(HSV の色相環、半径=彩度)。未指定や未知の値はフルレンジとして扱う。
	[JsonPropertyName("lightness_disc_pattern")]
	public string? LightnessDiscPattern { get; set; }

	// 貼り付けなどで取り込んだ色の履歴。新しいものが先頭。このキーを持たない設定では履歴なしで始める。
	[JsonPropertyName("history")]
	public List<ColorHistoryItemState>? History { get; set; }

	// 利用者が取っておいたお気に入りパレットの一覧。並びは登録順。このキーを持たない設定ではお気に入りなしで始める。各パレットは色の並びだけを持ち、配色の作り方は持たない。
	[JsonPropertyName("saved_palettes")]
	public List<SavedPaletteState>? SavedPalettes { get; set; }

	// 画像タブの焼きなまし法の反復回数。多いほど煮詰まるが重くなる。このキーを持たない設定では既定の 18000 で始める。
	[JsonPropertyName("sa_iterations")]
	public int SaIterations { get; set; } = 18000;

	// 画像タブの焼きなまし法の1手あたりの代表色の移動幅(Lab)。このキーを持たない設定では既定の 18 で始める。
	[JsonPropertyName("sa_move")]
	public double SaMove { get; set; } = 18.0;

	// 画像タブの焼きなまし法の冷却率(1手ごとに温度へ掛ける係数、1未満)。このキーを持たない設定では既定の 0.9997 で始める。
	[JsonPropertyName("sa_alpha")]
	public double SaAlpha { get; set; } = 0.9997;

	// 画像タブの焼きなまし法の初期温度係数(試行手の平均悪化量へ掛けて初期温度を決める)。このキーを持たない設定では既定の 0.5 で始める。
	[JsonPropertyName("sa_initial_temp_factor")]
	public double SaInitialTempFactor { get; set; } = 0.5;

	// 画像タブの焼きなまし法で、種を中央値分割法の結果から起こすか。偽なら学習標本から無作為に種を採る。このキーを持たない設定では真で始める。
	[JsonPropertyName("sa_seed_median_cut")]
	public bool SaSeedFromMedianCut { get; set; } = true;

	// 画像タブの焼きなまし法の彩度重み(0–8)。高彩度の色を学習標本へ多めに採る度合い。このキーを持たない設定では 0(均等)で始める。
	[JsonPropertyName("sa_saturation_weight")]
	public int SaSaturationWeight { get; set; }

	// 画像タブの焼きなまし法の希少度重み(0–8)。色空間で疎(希少)な色を学習標本へ多めに採る度合い。このキーを持たない設定では 0(均等)で始める。
	[JsonPropertyName("sa_rarity_weight")]
	public int SaRarityWeight { get; set; }
}
