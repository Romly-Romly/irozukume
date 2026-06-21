// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.ViewModels;

// 色編集の状態を一手に持つモデル。
// サイドバーの色リスト(最大 MaxColors 色)を持ち、アクティブな1色を編集対象として R・G・B(0–255)の作業値で保持する。
// コメント中の「色1」はこのアクティブな色を指す。各スライダー背景のグラデーション、各色のプレビュー、16進表記を提供し、スライダー操作やアクティブの切り替えで R・G・B が変わると、それを束縛するスライダーのつまみも追従する。
public sealed class ColorEditorViewModel : INotifyPropertyChanged
{
	// サイドバーに置ける色の数の上限。
	public const int MaxColors = 5;

	// サイドバーに置ける色の数の下限。文字色と背景色へ別々の色を割り当てられるよう、テキストモードのコントラスト確認が成り立つ最低数を保つ。
	public const int MinColors = 2;

	// アクティブ(編集対象)な色の作業値。色リストのアクティブ項目と常に同期し、各タブのスライダー・数値入力はこれを束縛する。
	private double _r = 255.0;
	private double _g = 110.0;
	private double _b = 64.0;

	// サイドバーの色の一覧。各色が素の RGB と不透明度を持つ。最低 MinColors 色・最大 MaxColors 色。
	private readonly List<SidebarColorViewModel> _colors = new();

	// アクティブ(編集対象)な色の位置。
	private int _activeIndex;

	private bool _showActualColor = true;
	private bool _showContrastText;

	// アルファ値のスライダー類を、タブの中身の下に常駐させて表示するか。既定はオフ。ツールバーと表示メニューのトグルが切り替え、設定に永続化する。表示だけの設定で、色や丸めには影響しない。
	private bool _showAlpha;

	// コマンドバーの各ボタンにキャプション(名前)を表示するか。既定はオン。表示メニューのトグルが切り替え、設定に永続化する。オフのときアイコンだけになり横幅を節約する。表示だけの設定で、色や丸めには影響しない。
	private bool _showToolbarCaption = true;

	// コントラスト確認でアルファ(不透明度)を反映するか。真のとき、サンプル文字を文字色の不透明度で背景色へ透かして見せ、コントラスト比もその透けた実効色で測る。偽のときは不透明として扱う。表示メニューのトグルから切り替え、設定に永続化する。
	private bool _contrastIncludeAlpha = true;

	// 色覚シミュレーション(P型・D型・T型・1色覚)の見え方の行をサイドバーの各色パネルへ表示するか。型ごとに独立したトグルで、複数を同時に重ねられる。表示メニューから切り替え、設定に永続化する。
	private bool _showProtan;
	private bool _showDeutan;
	private bool _showTritan;
	private bool _showMonochromacy;

	// 色覚行を一括オフにする直前に出していた型の組み合わせ。コマンドバーの分割ボタンで一括オン/オフを切り替えるとき、オンへ戻した先を直前の組み合わせへ復元するために覚えておく。
	private bool _savedShowProtan;
	private bool _savedShowDeutan;
	private bool _savedShowTritan;
	private bool _savedShowMonochromacy;

	// 色覚シミュレーションの弱まりの強さ (severity)。0 が正常、1 が完全な2色覚。P型・D型・T型の見え方とコントラストマトリックスのシミュレーションに共通で効く。1色覚には効かない。設定に永続化する。
	private double _visionSeverity = 0.6;

	// テキストモードで文字色の役に就いている色の位置。背景色の役と合わせて、コントラスト確認に使う2色を色リストから選ぶ。
	private int _textColorIndex;

	// テキストモードで背景色の役に就いている色の位置。文字色と同じ位置を選んだときは SelectContrastRole が相手の役を隣へずらし、同じ色になるのを避ける。
	private int _bgColorIndex = 1;

	// テキストモードの編集フォーカスが文字色の役にあるか。偽なら背景色の役。最後にクリックした役を指し、コントラストスライダーはこの役の色をもう片方を基準に動かす。
	private bool _contrastFocusIsText = true;

	// コントラストマトリックスで AA を満たさない組み合わせに斜線を重ねるか。表示だけの設定で、色や丸めには影響しない。設定に永続化する。
	private bool _matrixHatchFails;

	// コントラストマトリックスの色覚シミュレーション。null はフルカラー(シミュレーションなし)。セルの文字と背景の見せ方だけに効き、コントラスト比や AA/AAA の判定・ヘッダーの色には影響しない。設定に永続化する。
	private ColorVisionType? _matrixVision;

	// 直前に選んでいたマトリックスの色覚シミュレーションの型。トグルでフルカラーへ戻した後、再度オンにしたときの復帰先。一度も選んでいなければ有病率が最も高い D型で始める。
	private ColorVisionType _lastMatrixVision = ColorVisionType.Deutan;

	private string _contrastSampleText = string.Empty;

	// Ctrl+C と編集メニューの「コピー」で選べる既定の形式の識別子。設定ページの ComboBox の項目順と一致させる。CSS 形式に続けてターミナル形式を「形式を選択してコピー」サブメニューと同じ前景4種・背景4種の並びで載せる。名前付きカラーは現在色が CSS 名に一致したときしか文字列にならず、既定にすると一致しない色で何もコピーされないため含めない。
	private static readonly string[] CopyFormatKeys = { "hex", "hexa", "rgb", "rgba", "rgb_modern", "rgba_modern", "hsl", "hsla", "hsl_modern", "hsla_modern", "hwb", "hwba", "packed", "packeda", "term_tc_fg", "term_256_fg", "term_16_fg", "term_8_fg", "term_tc_bg", "term_256_bg", "term_16_bg", "term_8_bg" };

	// Ctrl+C と編集メニューの「コピー」で色1を書き出す既定の形式の選択 (CopyFormatKeys の番号)。既定は 16 進。各色の RGB と独立した書き出し形式の設定で、これを変えても色そのものは変わらない。
	private int _copyFormatIndex;

	// Ctrl+Shift+C で色1を書き出す2つ目の既定の形式の選択 (CopyFormatKeys の番号)。既定は rgb()。1つ目 (CopyFormatIndex) と独立して持ち、これも色そのものは変えない。
	private int _copyFormatIndex2 = Array.IndexOf(CopyFormatKeys, "rgb");

	// アクティブな色の不透明度(アルファ, 0–255)の作業値。既定は不透明。RGB と独立して編集し、色リストのアクティブ項目と常に同期する。不透明度は各色が自分の値を持ち、入れ替えや並べ替えでも色に付いて動く。
	private double _alpha = 255.0;

	// アルファ値の表示単位。スライダーの値(0–255)はこの設定に依らず、数値表示の見せ方だけを切り替える。
	private AlphaUnit _alphaUnit = AlphaUnit.Byte;

	// R・G・B の表示単位。スライダーの値(0–255)はこの設定に依らず、数値表示と入力解釈の見せ方だけを切り替える。
	private RgbUnit _rgbUnit = RgbUnit.Byte;

	// 作れる色を表示上どの制約へ丸めるか。各色の RGB(真実の値)は常にフルカラーで保ち、これは表示・グラデーションの丸めだけを切り替える純粋な表示レンズ。制限なし(None)にすれば下地の色がそのまま現れる。
	private ColorLimitMode _limitMode;

	// ToggleLimit が None から復帰する際の戻り先となる、直前に選ばれた非None のモード。制限モードへ切り替わるたびに更新する。一度も制限を選んでいないときは WebSafe を戻り先にする。
	private ColorLimitMode _lastNonNoneLimitMode = ColorLimitMode.WebSafe;

	// 最も近いパレット色を選ぶ距離計算。色制限の格子モード・ターミナルモードの最近傍探索で使う。既定は知覚的(Lab)。
	private SnapMetric _snapMetric = SnapMetric.Lab;

	// ターミナルモードで基本16色を解決する参照テーマ。既定は Campbell。
	private TerminalTheme _terminalTheme = TerminalTheme.Campbell;

	// ターミナルのコピーで ESC をどの表記で書き出すか。既定は16進表記。色そのものや丸めには影響せず、コピー文字列の見た目だけを変える。
	private TerminalEscStyle _terminalEsc = TerminalEscStyle.Hex;

	// ターミナルのコピーで末尾にリセットを付けるか。既定は付けない。色や丸めには影響しない。
	private bool _terminalResetSuffix;

	// CSS のカラー関数のコピーでアルファをどの表記で書き出すか。既定は 0–1 の数値。rgba()/hsla()/hwb() 等のアルファに効き、色や丸めには影響しない。1つ目のコピー形式 (Ctrl+C) と、形式を選択してコピーのアルファ表記の既定として使う。
	private WebAlphaUnit _webAlphaUnit = WebAlphaUnit.Number;

	// 2つ目のコピー形式 (Ctrl+Shift+C) のアルファ表記。1つ目 (_webAlphaUnit) と独立して持つ。既定は 0–1 の数値。
	private WebAlphaUnit _webAlphaUnit2 = WebAlphaUnit.Number;

	// 貼り付けた色の書式に合わせて、対応するタブ(と HSV/HSL の副モード)へ自動で切り替えるか。既定はオン。色や丸めには影響せず、貼り付けたときにどのタブを前面にするかだけを変える。
	private bool _switchTabOnPaste = true;

	// Mix タブで色を混ぜる色空間。既定は知覚的に最も忠実な OKLCH。各色の RGB は変えず、平面の塗りだけに効く。
	private MixColorSpace _mixSpace = MixColorSpace.Oklch;

	// Mix タブの平面の塗り広げ方(空間補間)。既定は逆距離加重。各色の RGB は変えず、平面の塗りだけに効く。
	private MixInterpolation _mixMethod = MixInterpolation.InverseDistance;

	// Mix タブのつまみ(編集色のサンプル位置)の正規化座標(0–1・左上原点)。既定は中央。ポッチとは別物で、ここで拾った混色を編集中の色へ反映する。
	private double _mixThumbX = 0.5;
	private double _mixThumbY = 0.5;

	// Mix タブで色相を持つ色空間の混色をどちら回りにするか。既定は近い側。色相を持たない色空間では効かない。
	private MixHueDirection _mixHueDir = MixHueDirection.Shorter;

	// Mix タブの重みの効き具合(とろける⇔くっきり)。0–1 で、0 がやわらか・1 がくっきり。既定は中庸。逆距離加重の指数とガウスの広がりに効く。
	private double _mixSharpness = 0.5;

	// Mix の色空間・補間方式・色相の回り方の選択肢の並び。設定の解決と保存に使う。色空間の並びは Mix タブの ComboBox の項目順と一致させる。
	private static readonly MixColorSpace[] MixSpaceByIndex = { MixColorSpace.Oklch, MixColorSpace.Oklab, MixColorSpace.Lch, MixColorSpace.Lab, MixColorSpace.Hsl, MixColorSpace.LinearRgb, MixColorSpace.Rgb };
	private static readonly MixInterpolation[] MixMethodByIndex = { MixInterpolation.InverseDistance, MixInterpolation.Gaussian, MixInterpolation.Nearest };
	private static readonly MixHueDirection[] MixHueDirByIndex = { MixHueDirection.Shorter, MixHueDirection.Longer };

	// 配色タブの色相の関係づけ。既定は基準と反対の2色(補色)。各色の明度・彩度は配色に含めず、色相の角度関係だけを定める。
	private ColorHarmonyScheme _harmonyScheme = ColorHarmonyScheme.Complementary;

	// 配色を逆周り(色相オフセットの左右反転)にするか。既定はオフ。非対称な配色(類似色・テトラード・ダイアード)にだけ効く。
	private bool _harmonyReverse;

	// 配色タブの明度ディスクの半径の取り方。既定は中心白・縁黒で明度の全域を覆うフルレンジ。
	private LightnessDiscPattern _lightnessDiscPattern = LightnessDiscPattern.FullRange;

	// 2次元スライダーで色域外をどう見せるか。既定はクランプ色塗り+境界線+斜線。色域内の表示や各色の RGB には影響せず、色域外の描き方だけを変える。
	private GamutOutOfRangeStyle _oogStyle = GamutOutOfRangeStyle.FillBoundaryHatch;

	// 設定ページで上級者向け設定を表示するか。既定はオフ。表示の出し分けだけで、色や丸めには影響しない。
	private bool _advancedSettings;

	// スライダーつまみのレンズの効きの全体設定。設定ページの上級者向け設定で調整する。値は静的な LensTuning へ反映し、各描画コントロールがドラッグ開始時に読む。拡大率・強さ・ズレ・ベベルは基準値への係数(拡大率は等倍からの増分への係数)。
	private bool _lensEffect = Helpers.LensTuning.DefaultLensEffect;
	private double _lensMagnify = Helpers.LensTuning.DefaultMagnify;
	private bool _lensRefraction = Helpers.LensTuning.DefaultRefraction;
	private double _lensRefractionStrength = Helpers.LensTuning.DefaultRefractionStrength;
	private double _lensBevel = Helpers.LensTuning.DefaultBevel;
	private double _lensChromaSpread = Helpers.LensTuning.DefaultChromaSpread;

	// 画面カラーピッカーのガラスレンズの効きの全体設定。設定ページの上級者向け設定で調整する。値は静的な ScreenPickerTuning へ反映し、ピッカーを開くときに読む。
	private double _screenPickerMagnify = Helpers.ScreenPickerTuning.DefaultMagnify;
	private double _screenPickerDiameter = Helpers.ScreenPickerTuning.DefaultDiameter;
	private bool _screenPickerGlassEffect = Helpers.ScreenPickerTuning.DefaultGlassEffect;
	private double _screenPickerRefractionStrength = Helpers.ScreenPickerTuning.DefaultRefractionStrength;

	// 距離計算・参照テーマ・ESC表現・アルファ表記の設定の選択肢の並び。設定ページの ComboBox の項目順と一致させる。
	private static readonly GamutOutOfRangeStyle[] OogStyleByIndex = { GamutOutOfRangeStyle.FillBoundary, GamutOutOfRangeStyle.FillBoundaryHatch, GamutOutOfRangeStyle.WhiteHatch };
	private static readonly SnapMetric[] SnapMetricByIndex = { SnapMetric.Lab, SnapMetric.Redmean, SnapMetric.Rgb };
	private static readonly TerminalTheme[] TerminalThemeByIndex = { TerminalTheme.Campbell, TerminalTheme.Vga, TerminalTheme.Xterm };
	private static readonly TerminalEscStyle[] TerminalEscByIndex = { TerminalEscStyle.Literal, TerminalEscStyle.BackslashE, TerminalEscStyle.Hex, TerminalEscStyle.Octal, TerminalEscStyle.Unicode, TerminalEscStyle.Caret };
	private static readonly WebAlphaUnit[] WebAlphaUnitByIndex = { WebAlphaUnit.Number, WebAlphaUnit.Percent };

	// HSV・HSL 編集で利用者が選んだ色相・彩度を保持する。色1が無彩色(灰・黒)になり RGB から色相・彩度を復元できない間も、これらを保って色相環や彩度の操作を破綻させない。色相は HSV・HSL で共通だが、彩度は両表色系で定義が異なるため別々に持つ。明度・輝度は色1の RGB から常に復元できるため保持しない。
	private double _cachedHue;
	private double _cachedSaturation;
	private double _cachedHslSaturation;

	// HWB の白み・黒みを保持する。正規化オフのときは利用者が入れた値(白み+黒みが 1 を超える退化域も)をそのまま覚え、色とは独立に保つ。正規化オンのときは色1の RGB から導いた和≤1 の正準形に保つ。表示・パッド・スライダーはこのキャッシュを読む。
	private double _cachedWhiteness;
	private double _cachedBlackness;

	// HWB の白み+黒みを正規化(和を 1 以内へ畳む)するか。既定は正規化する。オフにすると退化域(灰)でも入れた白み・黒みをそのまま保てる。
	private bool _normalizeHwb = true;

	// HWB の編集を反映している最中か。真の間は NotifyHwbDerived が RGB からのキャッシュ取り直しをせず、編集で入れた白み・黒み(退化域を含む)を保つ。
	private bool _hwbEditing;

	// 中央の2次元パッドを色相環へ追従させて回すかどうか。
	private bool _followHue;

	// HSV/HSL タブの副モードの位置 (0=HSV, 1=HSL, 2=HWB)。タブのラジオの選択を保ち、保存して次回起動へ引き継ぐ。
	private int _hsvSubModeIndex;

	// HSV モードの見せ方 (0=色相リング+正方形パッド, 1=角度=色相・半径=彩度の円盤+明度の縦スライダー)。レイアウトのセレクタの選択を保ち、保存して次回起動へ引き継ぐ。HSL・HWB の副モードでは色相リング+パッドに固定で、この値は HSV のときだけ効く。
	private int _hsvLayoutIndex;

	// HSL モードの見せ方の位置 (0=色相リング+正方形, 1=色相・彩度の円盤+輝度の縦スライダー, 2=色相×輝度の直交パッド+彩度の縦スライダー, 3=色相・輝度の円盤+彩度の縦スライダー, 4=色相×彩度の直交パッド+輝度の縦スライダー, 5=彩度×輝度の正方形+色相の縦スライダー, 6=色相リング+三角形, 7=三角形+色相の縦スライダー)。HSV とは別に保ち、保存して次回起動へ引き継ぐ。既定は HSL らしい色相リング+三角形(6)。HSL のときだけ効く。
	private int _hslLayoutIndex = 6;

	// HWB モードの見せ方の位置 (0=色相リング+正方形, 1=色相・白みの円盤+黒みの縦スライダー, 2=色相×黒みの直交パッド+白みの縦スライダー, 3=色相・黒みの円盤+白みの縦スライダー, 4=色相×白みの直交パッド+黒みの縦スライダー, 5=白み×黒みの正方形+色相の縦スライダー, 6=色相リング+三角形, 7=三角形+色相の縦スライダー)。HSV・HSL とは別に保ち、保存して次回起動へ引き継ぐ。既定は現行の色相リング+正方形(0)。HWB のときだけ効く。
	private int _hwbLayoutIndex;

	// LCH タブの副モード (0=OKLCH, 1=CIE LCH)。明度・彩度・色相をどちらの表色系で読むかを保ち、保存して次回起動へ引き継ぐ。
	private int _lchSpaceIndex;

	// LCH タブの見せ方の位置 (0=色相リング+彩度明度の平面, 1=彩度×明度の平面+色相の縦スライダー, 2=角度=色相・半径=明度の円盤+彩度の縦スライダー, 3=色相×明度の平面+彩度の縦スライダー, 4=角度=色相・半径=彩度の円盤+明度の縦スライダー, 5=色相×彩度の平面+明度の縦スライダー)。レイアウトのセレクタの選択を保ち、保存して次回起動へ引き継ぐ。OKLCH・CIE LCH の副モードで共通に効く。既定は色相リング+平面(0)。
	private int _lchLayoutIndex;

	// LCH 編集で利用者が入れた明度・彩度・色相を、現在の副モード(_lchSpaceIndex)の素の尺度で保持する。色1が無彩色で色相が定まらない間や、色域外の彩度を制限せず保つ間も、これらを保って操作を破綻させない。
	private double _cachedLchL;
	private double _cachedLchChroma;
	private double _cachedLchHue;

	// LCH の彩度を sRGB 色域へ制限(色域境界でクランプ)するか。既定はオフで、色域外の彩度もそのまま保ち、表示できない範囲をスライダー上に可視化する。
	private bool _lchGamutLimit;

	// LCH の L-C 平面(色相リング+平面・C×L 平面+色相バー)で、彩度軸を色域へ詰めて表示するか。オン時はその色相で色域が届く最大彩度(cusp)まで彩度軸を縮め、彩度方向にパッドいっぱいへ色域を広げて選びやすくする。明度は常に全域を使う。色1の RGB は不変で、彩度軸の見せ方だけが変わる。保存して次回起動へ引き継ぐ。
	private bool _lchChromaFit;

	// LCH の編集を反映している最中か。真の間は NotifyLchDerived がキャッシュの RGB からの取り直しをせず、編集で入れた明度・彩度・色相(色域外を含む)を保つ。
	private bool _lchEditing;

	// Lab タブの副モード (0=OKLab, 1=CIE Lab)。明度・a 軸・b 軸をどちらの表色系で読むかを保ち、保存して次回起動へ引き継ぐ。
	private int _labSpaceIndex;

	// Lab タブの見せ方の位置 (0=a×b 平面+明度の縦バー, 1=a×L 平面+b の縦バー, 2=b×L 平面+a の縦バー, 3=a-b 円盤(角度=色相・半径=彩度)+明度の縦バー)。レイアウトのセレクタの選択を保ち、保存して次回起動へ引き継ぐ。OKLab・CIE Lab の副モードで共通に効く。既定は a×b 平面+明度の縦バー(0)。
	private int _labLayoutIndex;

	// Lab タブの a×b 平面の表示枠(スケール)の決め方の位置 (0=None=固定枠, 1=Isotropic=等方フィット, 2=Anisotropic=縦横独立フィット)。AbFitMode と同じ並び。フィットは明度ごとの色域の広がりへ枠を寄せて有効領域を広げる。セレクタの選択を保ち、保存して次回起動へ引き継ぐ。a×b 平面の見せ方のときだけ効く。
	private int _labAbScaleIndex;

	// Lab 編集で利用者が入れた明度・a 軸・b 軸を、現在の副モード(_labSpaceIndex)の素の尺度で保持する。色域外の値を制限せず保つ間も、これらを保って操作を破綻させない。
	private double _cachedLabL;
	private double _cachedLabA;
	private double _cachedLabB;

	// Lab の a・b を sRGB 色域へ制限(色域境界でクランプ)するか。既定はオフで、色域外の値もそのまま保ち、表示できない範囲をスライダー・パッド上に可視化する。
	private bool _labGamutLimit;

	// Lab の編集を反映している最中か。真の間は NotifyLabDerived がキャッシュの RGB からの取り直しをせず、編集で入れた明度・a・b(色域外を含む)を保つ。
	private bool _labEditing;

	// YCbCr の符号化形式と表示。規格(係数)・量子化レンジ(フル/スタジオ)・符号付き(YUV)表記を保持する。色1の RGB は不変で、これらは数値の読み方とガモットの形だけを変える。
	private YCbCrStandard _yuvStandard = YCbCrStandard.Bt709;
	private bool _yuvFullRange = true;
	private bool _yuvSignedMode;

	// YCbCr 編集で利用者が入れた輝度・色差(Cb・Cr)を、現在の符号化形式の素の尺度で保持する。色域外の色差を制限せず保つ間も、これらを保って操作を破綻させない。
	private double _cachedY = 128.0;
	private double _cachedCb = 128.0;
	private double _cachedCr = 128.0;

	// YCbCr の色差を sRGB 色域へ制限(色域境界でクランプ)するか。既定はオフで、色域外の色差もそのまま保ち、表示できない範囲を色差平面上に可視化する。
	private bool _yuvGamutLimit;

	// YUV/YCbCr タブの見せ方(レイアウト)の位置 (0=Cb×Cr 平面+Y の縦バー, 1=Cb×Y 平面+Cr の縦バー, 2=Cr×Y 平面+Cb の縦バー)。色1の RGB は不変で、見せ方だけが変わる。
	private int _yuvLayoutIndex;

	// RGB/CMYK タブの2次元エディタの見せ方(レイアウト)の位置。RGB・CMYK 共通の1つのピッカーが選ぶ。0=パッド無し(線形スライダーのみ)、1〜3=RGB の平面、4〜6=CMYK の平面。タブに出すパッドは1枚で、RGB 系か CMYK 系のどちらか一方。色1の RGB は不変で、見せ方だけが変わる。線形スライダーは常に残る。
	private int _rgbCmykLayoutIndex;

	// CMYK の作業用の値(各 0–1)。CMYK は4成分で3次元の色に対して1つ冗長なため、RGB から正準形(K=1−max)で導出するだけでは、ある成分を編集した際に他成分(特に K)が再計算で動いてしまう。これを避けて2次元パッドや各スライダーで成分を保てるよう、YCbCr と同じく作業値をここへ保持し、編集中はこの値を真とみなして RGB へ反映する。外部(他タブ・貼り付け等)で色が変わったときは正準形へ取り直す。
	private double _cmykC;
	private double _cmykM;
	private double _cmykY;
	private double _cmykK;

	// CMYK の編集を反映している最中か。真の間は NotifyCmykDerived がキャッシュの RGB からの取り直しをせず、編集で入れた成分(冗長な K を含む)を保つ。
	private bool _cmykEditing;

	// YUV/YCbCr タブの色差平面の表示枠(スケール)の決め方の位置 (0=固定枠, 1=等方フィット, 2=縦横独立フィット)。AbFitMode と同じ並びで、フィットは固定成分(Cb×Cr では輝度、Cb×Y では Cr、Cr×Y では Cb)ごとの色域の広がりへ枠を寄せて有効領域を広げる(枠は固定成分で縮尺が変わる)。色1の RGB は不変。
	private int _yuvScaleIndex;

	// YCbCr の編集を反映している最中か。真の間は NotifyYuvDerived がキャッシュの RGB からの取り直しをせず、編集で入れた輝度・色差(色域外を含む)を保つ。
	private bool _yuvEditing;

	// 元に戻す/やり直しのための色状態スナップショットの列。色リスト全体とアクティブ位置を1件として持ち、行き来する。表示設定(色制限・テーマ等)は色の変化ではないため含めない。永続化はせず、その場限りで保つ。
	private readonly List<ColorSnapshot> _undoStack = new();
	private readonly List<ColorSnapshot> _redoStack = new();

	// 戻せる段数の上限。1件は数バイトと軽いが、際限なく溜めない。古いものから落とす。
	private const int MaxUndoDepth = 100;

	// スライダー・パッドの連続編集をひとまとまりへ畳む無操作時間(秒)。最後の変化からこの時間が過ぎたら、その一連を1段として確定する。確定操作(入れ替え・貼り付け等)は時間に依らず即座にひと区切りにする。
	private const double GestureCoalesceSeconds = 5.0;

	// 連続編集のまとまりが開いているか。開いている間は新たな段を積まず、同じ段へ畳む。
	private bool _gestureActive;

	// 確定操作の入れ子の深さ。最外の操作だけが直前の状態を1件退避し、内側の入れ子では退避しない。複合操作(黒白・貼り付け等)を1段にまとめるために使う。
	private int _transactionDepth;

	// 戻す/やり直しでスナップショットを適用している最中か。適用そのものを新たな履歴として積まないために立てる。
	private bool _restoring;

	// 連続編集のまとまりを閉じる無操作タイマー。連続編集の最初の変化で起動し、変化のたびに測り直す。初回の連続編集まで生成しない。
	private DispatcherTimer? _gestureTimer;

	// コントラストスライダーが基準にする色1の色相と彩度(OKLCh)。スライダーで端へ寄せて彩度が落ちても元の鮮やかさへ戻せるよう、スライダー操作では取り直さず、色1がスライダー以外で変わったときだけ取り直す。
	private double _contrastHue;
	private double _contrastChroma;

	// コントラストスライダーが最後に作った色1。これと現在の色1が食い違えば、色1がスライダー以外で編集されたと判断して色相・彩度の基準を取り直す。
	private Color _contrastLocusColor;

	// コントラストスライダーの位置(-1–+1)。0 は色2と輝度が一致する点(コントラスト1:1)、正は色2より明るく、負は色2より暗くした側を表す。
	private double _contrastSliderValue;

	// コントラストスライダーで色1を動かしている最中か。立っている間は、スライダー位置の取り直しを抑えてつまみが操作と競合しないようにする。
	private bool _applyingContrastSlider;




	// 既定では起動時の見本色で始める。保存済みの色編集状態があれば、それで初期化する。構築段階のため変更通知は不要で、フィールドへ直接入れる。
	public ColorEditorViewModel(EditorState? state = null)
	{
		if (state is not null)
		{
			_showActualColor = state.ShowActualColor;
			_showContrastText = state.ShowContrastText;
			_showAlpha = state.ShowAlpha;
			_showToolbarCaption = state.ShowToolbarCaption;
			_contrastIncludeAlpha = state.ContrastIncludeAlpha;
			_showProtan = state.ShowProtan;
			_showDeutan = state.ShowDeutan;
			_showTritan = state.ShowTritan;
			_showMonochromacy = state.ShowMonochromacy;
			_visionSeverity = Math.Clamp(state.VisionSeverity, 0.0, 1.0);
			_textColorIndex = state.ContrastTextIndex;
			_bgColorIndex = state.ContrastBackgroundIndex;
			_matrixHatchFails = state.MatrixHatchFails;
			_matrixVision = ResolveMatrixVision(state.MatrixVision);

			if (_matrixVision is ColorVisionType savedVision)
			{
				_lastMatrixVision = savedVision;
			}
			_yuvStandard = ParseStandard(state.YuvStandard);
			_yuvFullRange = state.YuvFullRange;
			_yuvSignedMode = state.YuvSigned;
			_yuvGamutLimit = state.YuvGamutLimit;
			_yuvLayoutIndex = ResolveYuvLayout(state.YuvLayout);
			_yuvScaleIndex = ResolveYuvScale(state.YuvScale);
			_rgbCmykLayoutIndex = ResolveRgbCmykLayout(state.RgbCmykLayout);
			_hsvSubModeIndex = ResolveHsvSubMode(state.HsvSubMode);
			_hsvLayoutIndex = ResolveHsvLayout(state.HsvLayout);
			_hslLayoutIndex = ResolveHslLayout(state.HslLayout);
			_hwbLayoutIndex = ResolveHwbLayout(state.HwbLayout);
			_lchSpaceIndex = ResolveLchSpace(state.LchSubMode);
			_lchLayoutIndex = ResolveLchLayout(state.LchLayout);
			_lchGamutLimit = state.LchGamutLimit;
			_lchChromaFit = state.LchChromaFit;
			_labSpaceIndex = ResolveLabSpace(state.LabSubMode);
			_labLayoutIndex = ResolveLabLayout(state.LabLayout);
			_labAbScaleIndex = ResolveLabAbScale(state.LabAbScale);
			_labGamutLimit = state.LabGamutLimit;
			_followHue = state.HsvFollowHue;
			_normalizeHwb = state.HwbNormalize;
			_limitMode = ResolveLimitMode(state);

			if (_limitMode != ColorLimitMode.None)
			{
				_lastNonNoneLimitMode = _limitMode;
			}

			_alphaUnit = ParseAlphaUnit(state.AlphaUnit);
			_rgbUnit = ParseRgbUnit(state.RgbUnit);
			_copyFormatIndex = ResolveCopyFormatIndex(state.CopyFormat);
			_copyFormatIndex2 = ResolveCopyFormatIndex2(state.CopyFormat2);
			_snapMetric = ResolveSnapMetric(state.SnapMetric);
			_terminalTheme = ResolveTerminalTheme(state.TerminalTheme);
			_terminalEsc = ResolveTerminalEsc(state.TerminalEsc);
			_terminalResetSuffix = state.TerminalResetSuffix;
			_webAlphaUnit = ResolveWebAlphaUnit(state.WebAlphaUnit);
			_webAlphaUnit2 = state.WebAlphaUnit2 is null ? _webAlphaUnit : ResolveWebAlphaUnit(state.WebAlphaUnit2);
			_switchTabOnPaste = state.SwitchTabOnPaste;
			_mixSpace = ResolveMixSpace(state.MixSpace);
			_mixMethod = ResolveMixMethod(state.MixMethod);
			_mixHueDir = ResolveMixHueDir(state.MixHueDir);
			_mixSharpness = Math.Clamp(state.MixSharpness, 0.0, 1.0);
			_mixThumbX = Math.Clamp(state.MixThumbX, 0.0, 1.0);
			_mixThumbY = Math.Clamp(state.MixThumbY, 0.0, 1.0);
			_harmonyScheme = ResolveHarmonyScheme(state.HarmonyScheme);
			_harmonyReverse = state.HarmonyReverse;
			_lightnessDiscPattern = ResolveLightnessDiscPattern(state.LightnessDiscPattern);
			_oogStyle = ResolveOogStyle(state.GamutOutOfRangeStyle);
			_advancedSettings = state.AdvancedSettings;
			_lensEffect = state.LensEffect;
			_lensMagnify = state.LensMagnify;
			_lensRefraction = state.LensRefraction;
			_lensRefractionStrength = state.LensRefractionStrength;
			_lensBevel = state.LensBevel;
			_lensChromaSpread = state.LensChromaSpread;
			_screenPickerMagnify = state.ScreenPickerMagnify;
			_screenPickerDiameter = state.ScreenPickerDiameter;
			_screenPickerGlassEffect = state.ScreenPickerGlassEffect;
			_screenPickerRefractionStrength = state.ScreenPickerRefractionStrength;

			// 採色中にホイールで最後に使った拡大率・取得範囲を復元する。設定スライダーで編集する値ではなくセッションのライブ値のため、ViewModel のフィールドを介さず静的な保持先へ直接戻す。
			Helpers.ScreenPickerTuning.LastBlockPx = state.ScreenPickerBlockPx;
			Helpers.ScreenPickerTuning.LastSampleRadius = state.ScreenPickerSampleRadius;
		}

		// レンズの効きの全体設定を、描画コントロールおよび画面ピッカーが読む静的な保持先へ反映する。以後の変更は各プロパティの setter が反映する。
		SyncLensTuning();
		SyncScreenPickerTuning();

		// 保存済みの入力文字列があればそれで始める。キー自体が無い古い設定や設定ファイルが無い初回は、言語ごとの既定のサンプル文で始める。空文字として保存されていれば空のまま開く。
		_contrastSampleText = state?.ContrastText ?? Loc.Get("ContrastSampleDefault");

		// 色制限などの表示設定を読み終えてから色リストを組み立てる。各パネルの表示ブラシは丸めの設定に依存する。役の位置は保存値が件数と食い違うことがあるため、組み立て後に検証する。
		BuildColorList(state);
		EnsureContrastRoles();
		InitColorCaches();

		(double _, double initC, double initH) = OklabColor.ToOklch(Color1Raw);
		_contrastChroma = initC;
		_contrastHue = initH;
		_contrastLocusColor = Color1Raw;
		_contrastSliderValue = ComputeContrastSliderValue();
	}




	// 現在の色編集状態を取り出す。色リストは各色を "#RRGGBB" 形式+不透明度で、アクティブ位置とともに書き出す。設定の保存に使う。各色は表示の丸めを焼き込まずに済むよう、色制限モードに依らず素のフルカラーで書き出す。制限の状態は別に持つため、次回起動でも同じ見え方を再現できる。
	public EditorState CaptureState()
	{
		return new EditorState
		{
			Colors = CaptureColorEntries(),
			ActiveColor = _activeIndex,
			ShowActualColor = _showActualColor,
			ShowContrastText = _showContrastText,
			ShowAlpha = _showAlpha,
			ShowToolbarCaption = _showToolbarCaption,
			ContrastIncludeAlpha = _contrastIncludeAlpha,
			ShowProtan = _showProtan,
			ShowDeutan = _showDeutan,
			ShowTritan = _showTritan,
			ShowMonochromacy = _showMonochromacy,
			VisionSeverity = _visionSeverity,
			ContrastTextIndex = _textColorIndex,
			ContrastBackgroundIndex = _bgColorIndex,
			MatrixHatchFails = _matrixHatchFails,
			MatrixVision = MatrixVisionToString(_matrixVision),
			ContrastText = _contrastSampleText,
			YuvStandard = StandardToString(_yuvStandard),
			YuvFullRange = _yuvFullRange,
			YuvSigned = _yuvSignedMode,
			YuvGamutLimit = _yuvGamutLimit,
			YuvLayout = YuvLayoutToString(_yuvLayoutIndex),
			YuvScale = YuvScaleToString(_yuvScaleIndex),
			RgbCmykLayout = RgbCmykLayoutToString(_rgbCmykLayoutIndex),
			HsvSubMode = HsvSubModeToString(_hsvSubModeIndex),
			HsvLayout = HsvLayoutToString(_hsvLayoutIndex),
			HslLayout = HslLayoutToString(_hslLayoutIndex),
			HwbLayout = HwbLayoutToString(_hwbLayoutIndex),
			LchSubMode = LchSpaceToString(_lchSpaceIndex),
			LchLayout = LchLayoutToString(_lchLayoutIndex),
			LchGamutLimit = _lchGamutLimit,
			LchChromaFit = _lchChromaFit,
			LabSubMode = LabSpaceToString(_labSpaceIndex),
			LabLayout = LabLayoutToString(_labLayoutIndex),
			LabAbScale = LabAbScaleToString(_labAbScaleIndex),
			LabGamutLimit = _labGamutLimit,
			HsvFollowHue = _followHue,
			HwbNormalize = _normalizeHwb,
			ColorLimitMode = LimitModeToString(_limitMode),
			AlphaUnit = AlphaUnitToString(_alphaUnit),
			RgbUnit = RgbUnitToString(_rgbUnit),
			CopyFormat = CopyFormatKeys[_copyFormatIndex],
			CopyFormat2 = CopyFormatKeys[_copyFormatIndex2],
			SnapMetric = SnapMetricToString(_snapMetric),
			TerminalTheme = TerminalThemeToString(_terminalTheme),
			TerminalEsc = TerminalEscToString(_terminalEsc),
			TerminalResetSuffix = _terminalResetSuffix,
			WebAlphaUnit = WebAlphaUnitToString(_webAlphaUnit),
			WebAlphaUnit2 = WebAlphaUnitToString(_webAlphaUnit2),
			SwitchTabOnPaste = _switchTabOnPaste,
			MixSpace = MixSpaceToString(_mixSpace),
			MixMethod = MixMethodToString(_mixMethod),
			MixHueDir = MixHueDirToString(_mixHueDir),
			MixSharpness = _mixSharpness,
			MixThumbX = _mixThumbX,
			MixThumbY = _mixThumbY,
			HarmonyScheme = HarmonySchemeToString(_harmonyScheme),
			HarmonyReverse = _harmonyReverse,
			LightnessDiscPattern = LightnessDiscPatternToString(_lightnessDiscPattern),
			GamutOutOfRangeStyle = OogStyleToString(_oogStyle),
			AdvancedSettings = _advancedSettings,
			LensEffect = _lensEffect,
			LensMagnify = _lensMagnify,
			LensRefraction = _lensRefraction,
			LensRefractionStrength = _lensRefractionStrength,
			LensBevel = _lensBevel,
			LensChromaSpread = _lensChromaSpread,
			ScreenPickerMagnify = _screenPickerMagnify,
			ScreenPickerDiameter = _screenPickerDiameter,
			ScreenPickerGlassEffect = _screenPickerGlassEffect,
			ScreenPickerRefractionStrength = _screenPickerRefractionStrength,
			ScreenPickerBlockPx = Helpers.ScreenPickerTuning.LastBlockPx,
			ScreenPickerSampleRadius = Helpers.ScreenPickerTuning.LastSampleRadius,
		};
	}




	// 保存済みの色一覧からサイドバーの色を組み立てる。解釈できた色を上限まで取り込み、1件も無ければ既定の2色(オレンジ・青)で始め、1件だけなら2色目を補って最低 MinColors 色を保つ。アクティブ位置は範囲内へ丸める。構築段階のため変更通知はせず、作業値もアクティブ色から直接入れる。
	private void BuildColorList(EditorState? state)
	{
		if (state?.Colors is not null)
		{
			foreach (ColorEntryState entry in state.Colors)
			{
				if (_colors.Count >= MaxColors)
				{
					break;
				}

				if (TryParseRgb(entry.Rgb, out byte r, out byte g, out byte b))
				{
					_colors.Add(new SidebarColorViewModel
					{
						Rgb = Color.FromArgb(0xFF, r, g, b),
						Alpha = Math.Clamp(entry.Alpha ?? 255, 0, 255),
						MixX = entry.MixX ?? double.NaN,
						MixY = entry.MixY ?? double.NaN,
					});
				}
			}
		}

		if (_colors.Count == 0)
		{
			if (state?.Colors is { Count: > 0 })
			{
				// 保存済みの色があったのに1件も解釈できなかった。既定パレットで起動するが、原因を追えるよう crash.log へ残す。
				Services.CrashLog.Write($"保存済みの色 {state.Colors.Count} 件をいずれも解釈できなかったため、既定パレットで起動します。");
			}

			_colors.Add(new SidebarColorViewModel { Rgb = Color.FromArgb(0xFF, 0xFF, 0x6E, 0x40) });
			_colors.Add(new SidebarColorViewModel { Rgb = Color.FromArgb(0xFF, 0x3D, 0x5A, 0xFE) });
		}
		else
		{
			// 保存済みの色が1件だけだと文字色と背景色を別々に選べないため、2色目(青)を補って最低 MinColors 色を満たす。
			while (_colors.Count < MinColors)
			{
				_colors.Add(new SidebarColorViewModel { Rgb = Color.FromArgb(0xFF, 0x3D, 0x5A, 0xFE) });
			}
		}

		_activeIndex = Math.Clamp(state?.ActiveColor ?? 0, 0, _colors.Count - 1);

		SidebarColorViewModel active = _colors[_activeIndex];
		active.IsActive = true;
		_r = active.Rgb.R;
		_g = active.Rgb.G;
		_b = active.Rgb.B;
		_alpha = active.Alpha;

		UpdateListCapabilities();
		RefreshAllColorDisplays();
	}




	// 色リストを保存用の形へ写す。各色は素の RGB と不透明度をそのまま書き出す。
	private List<ColorEntryState> CaptureColorEntries()
	{
		var list = new List<ColorEntryState>(_colors.Count);

		foreach (SidebarColorViewModel item in _colors)
		{
			list.Add(new ColorEntryState
			{
				Rgb = HexText(item.Rgb),
				Alpha = (int)Math.Round(item.Alpha),
				MixX = double.IsNaN(item.MixX) ? null : item.MixX,
				MixY = double.IsNaN(item.MixY) ? null : item.MixY,
			});
		}

		return list;
	}




	// サイドバーの色の一覧。並び・件数・アクティブの変化は ColorListChanged で別途通知し、各項目の表示の変化は項目自身が通知する。
	public IReadOnlyList<SidebarColorViewModel> Colors => _colors;




	// アクティブ(編集対象)な色の位置。
	public int ActiveColorIndex => _activeIndex;




	// 色リストの並び・件数・アクティブが変わったときの通知。サイドバーの色パネル一覧の組み直しに使う。
	public event EventHandler? ColorListChanged;




	// いずれかの色の表示が塗り直されたときの通知。値の変化(編集・ランダム・貼り付け・黒白)を一律に拾う。Mix タブが平面の塗り直しに使う。
	public event EventHandler? ColorsChanged;




	// Mix タブで色を混ぜる色空間。設定ページや Mix タブの選択が束縛する。各色の RGB は変えず、平面の塗りだけが変わるため、変更時は再描画通知として ColorsChanged を流す。
	public MixColorSpace MixSpace
	{
		get => _mixSpace;
		set
		{
			if (_mixSpace == value)
			{
				return;
			}

			_mixSpace = value;
			OnPropertyChanged(nameof(MixSpace));
			ColorsChanged?.Invoke(this, EventArgs.Empty);
		}
	}




	// Mix タブの平面の塗り広げ方(空間補間)。Mix タブの選択が束縛する。変更時は再描画通知として ColorsChanged を流す。
	public MixInterpolation MixMethod
	{
		get => _mixMethod;
		set
		{
			if (_mixMethod == value)
			{
				return;
			}

			_mixMethod = value;
			OnPropertyChanged(nameof(MixMethod));
			ColorsChanged?.Invoke(this, EventArgs.Empty);
		}
	}




	// Mix タブで色空間の選択(設定の並びの番号)を読み書きする束縛用。範囲外は OKLCH へ丸める。
	public int MixSpaceIndex
	{
		get => Math.Max(0, Array.IndexOf(MixSpaceByIndex, _mixSpace));
		set => MixSpace = MixSpaceByIndex[Math.Clamp(value, 0, MixSpaceByIndex.Length - 1)];
	}




	// Mix タブで補間方式の選択(設定の並びの番号)を読み書きする束縛用。範囲外は逆距離加重へ丸める。
	public int MixMethodIndex
	{
		get => Math.Max(0, Array.IndexOf(MixMethodByIndex, _mixMethod));
		set => MixMethod = MixMethodByIndex[Math.Clamp(value, 0, MixMethodByIndex.Length - 1)];
	}




	// Mix タブで色相を持つ色空間の混色の回り方。Mix タブの選択が束縛する。変更時は再描画通知として ColorsChanged を流す。
	public MixHueDirection MixHueDirection
	{
		get => _mixHueDir;
		set
		{
			if (_mixHueDir == value)
			{
				return;
			}

			_mixHueDir = value;
			OnPropertyChanged(nameof(MixHueDirection));
			ColorsChanged?.Invoke(this, EventArgs.Empty);
		}
	}




	// Mix タブで色相の回り方の選択(設定の並びの番号)を読み書きする束縛用。範囲外は近い側へ丸める。
	public int MixHueDirIndex
	{
		get => Math.Max(0, Array.IndexOf(MixHueDirByIndex, _mixHueDir));
		set => MixHueDirection = MixHueDirByIndex[Math.Clamp(value, 0, MixHueDirByIndex.Length - 1)];
	}




	// 配色タブの色相の関係づけ。配色タブの ComboBox が束縛し、設定に保存される。各色の RGB はここでは変えず、配色タブが色相の角度を組み立てるのに使う。
	public ColorHarmonyScheme HarmonyScheme
	{
		get => _harmonyScheme;
		set
		{
			if (_harmonyScheme == value)
			{
				return;
			}

			_harmonyScheme = value;
			OnPropertyChanged(nameof(HarmonyScheme));
			OnPropertyChanged(nameof(HarmonySchemeKey));
			OnPropertyChanged(nameof(HarmonyReverseApplicable));
		}
	}




	// 配色を逆周り(鏡像)にするか。配色タブの反転トグルが束縛し、設定に保存される。非対称な配色にだけ効き、配色タブが色相の角度を反転して組み立てるのに使う。
	public bool HarmonyReverse
	{
		get => _harmonyReverse;
		set
		{
			if (_harmonyReverse == value)
			{
				return;
			}

			_harmonyReverse = value;
			OnPropertyChanged(nameof(HarmonyReverse));
		}
	}




	// 現在の配色で反転が効くか。非対称な配色のときだけ真。配色タブの反転トグルの有効・無効の束縛に使う。
	public bool HarmonyReverseApplicable => ColorHarmony.IsDirectional(_harmonyScheme);




	// 配色の種類の保存キー(Tag)を読み書きする束縛用。配色タブの ComboBox が SelectedValuePath="Tag" で束縛する。文字列⇔種類の読み替えは保存と同じ HarmonySchemeToString / ResolveHarmonyScheme を使い、未知の値は補色へ丸める。
	public string HarmonySchemeKey
	{
		get => HarmonySchemeToString(_harmonyScheme);
		set => HarmonyScheme = ResolveHarmonyScheme(value);
	}




	// 保存文字列を配色の種類へ読み替える。未知の値は補色として扱う。
	private static ColorHarmonyScheme ResolveHarmonyScheme(string? value)
	{
		return value switch
		{
			"diad" => ColorHarmonyScheme.Diad,
			"analogous" => ColorHarmonyScheme.Analogous,
			"triadic" => ColorHarmonyScheme.Triadic,
			"split_complementary" => ColorHarmonyScheme.SplitComplementary,
			"tetradic" => ColorHarmonyScheme.Tetradic,
			"square" => ColorHarmonyScheme.Square,
			"pentad" => ColorHarmonyScheme.Pentad,
			"monochromatic" => ColorHarmonyScheme.Monochromatic,
			"dominant_tone" => ColorHarmonyScheme.DominantTone,
			"tonal" => ColorHarmonyScheme.Tonal,
			"monotone" => ColorHarmonyScheme.Monotone,
			_ => ColorHarmonyScheme.Complementary,
		};
	}




	// 配色の種類を保存文字列へ書き出す。
	private static string HarmonySchemeToString(ColorHarmonyScheme scheme)
	{
		return scheme switch
		{
			ColorHarmonyScheme.Diad => "diad",
			ColorHarmonyScheme.Analogous => "analogous",
			ColorHarmonyScheme.Triadic => "triadic",
			ColorHarmonyScheme.SplitComplementary => "split_complementary",
			ColorHarmonyScheme.Tetradic => "tetradic",
			ColorHarmonyScheme.Square => "square",
			ColorHarmonyScheme.Pentad => "pentad",
			ColorHarmonyScheme.Monochromatic => "monochromatic",
			ColorHarmonyScheme.DominantTone => "dominant_tone",
			ColorHarmonyScheme.Tonal => "tonal",
			ColorHarmonyScheme.Monotone => "monotone",
			_ => "complementary",
		};
	}




	// 配色タブの明度ディスクの型。配色タブの ComboBox が束縛し、設定に保存される。半径から明度への写し方を決め、円盤の下地とマーカーの配置に効く。
	public LightnessDiscPattern LightnessDiscPattern
	{
		get => _lightnessDiscPattern;
		set
		{
			if (_lightnessDiscPattern == value)
			{
				return;
			}

			_lightnessDiscPattern = value;
			OnPropertyChanged(nameof(LightnessDiscPattern));
			OnPropertyChanged(nameof(LightnessDiscPatternKey));
		}
	}




	// 明度ディスクの型の保存キー(Tag)を読み書きする束縛用。配色タブの ComboBox が SelectedValuePath="Tag" で束縛する。文字列⇔型の読み替えは保存と同じ LightnessDiscPatternToString / ResolveLightnessDiscPattern を使い、未知の値はフルレンジへ丸める。
	public string LightnessDiscPatternKey
	{
		get => LightnessDiscPatternToString(_lightnessDiscPattern);
		set => LightnessDiscPattern = ResolveLightnessDiscPattern(value);
	}




	// 保存文字列を明度ディスクの型へ読み替える。未知の値はフルレンジとして扱う。
	private static LightnessDiscPattern ResolveLightnessDiscPattern(string? value)
	{
		return value switch
		{
			"full_range_reversed" => LightnessDiscPattern.FullRangeReversed,
			"white_to_cusp" => LightnessDiscPattern.WhiteToCusp,
			"black_to_cusp" => LightnessDiscPattern.BlackToCusp,
			"hsv" => LightnessDiscPattern.Hsv,
			_ => LightnessDiscPattern.FullRange,
		};
	}




	// 明度ディスクの型を保存文字列へ書き出す。
	private static string LightnessDiscPatternToString(LightnessDiscPattern pattern)
	{
		return pattern switch
		{
			LightnessDiscPattern.FullRangeReversed => "full_range_reversed",
			LightnessDiscPattern.WhiteToCusp => "white_to_cusp",
			LightnessDiscPattern.BlackToCusp => "black_to_cusp",
			LightnessDiscPattern.Hsv => "hsv",
			_ => "full_range",
		};
	}




	// Mix タブの重みの効き具合(とろける⇔くっきり)。0–1 で、0 がやわらか・1 がくっきり。Mix タブのスライダーが束縛する。変更時は再描画通知として ColorsChanged を流す。
	public double MixSharpness
	{
		get => _mixSharpness;
		set
		{
			double clamped = Math.Clamp(value, 0.0, 1.0);

			if (clamped == _mixSharpness)
			{
				return;
			}

			_mixSharpness = clamped;
			OnPropertyChanged(nameof(MixSharpness));
			ColorsChanged?.Invoke(this, EventArgs.Empty);
		}
	}




	// Mix タブのつまみ(編集色のサンプル位置)の正規化座標。Mix タブが束縛・保存に使う。
	public double MixThumbX => _mixThumbX;
	public double MixThumbY => _mixThumbY;




	// Mix タブのつまみの位置を覚える。Mix タブのつまみドラッグが呼ぶ。色の反映は別途 SetActiveRgbFromMix が担い、ここは位置の保存だけを行う。
	public void SetMixThumb(double x, double y)
	{
		_mixThumbX = Math.Clamp(x, 0.0, 1.0);
		_mixThumbY = Math.Clamp(y, 0.0, 1.0);
	}




	// 指定位置の色のポッチ位置(正規化 0–1)を設定する。Mix タブのドラッグが呼ぶ。色の値は変えないため、元に戻す履歴の段にはしない。位置は色項目に付いて保存される。
	public void SetMixPosition(int index, double x, double y)
	{
		if (index < 0 || index >= _colors.Count)
		{
			return;
		}

		_colors[index].MixX = Math.Clamp(x, 0.0, 1.0);
		_colors[index].MixY = Math.Clamp(y, 0.0, 1.0);
	}




	// 全色のポッチ位置を指定の整列形へ並べ直す。Mix タブの自動アレンジが呼ぶ。色の値は変えないため、元に戻す履歴の段にはしない。正多角形を画面で正多角形に見せるため、平面の表示寸法(width・height)を受け取って整列計算へ渡す。
	public void AutoArrangeMix(MixArrange preset, double width, double height)
	{
		(double X, double Y)[] positions = MixArranger.Arrange(preset, _colors.Count, width, height);

		for (int i = 0; i < _colors.Count && i < positions.Length; i++)
		{
			_colors[i].MixX = positions[i].X;
			_colors[i].MixY = positions[i].Y;
		}
	}




	// 位置が未設定(NaN)のポッチへ、正多角形の既定位置を当てる。Mix タブが描画前に呼び、初回や色の追加で位置が無いポッチも掴める場所に置く。1つでも当て直したら真を返す。平面の表示寸法(width・height)で正多角形の頂点を画面に合わせる。寸法が未確定(既定の 1, 1)のときは正方形として配り、確定後の自動アレンジで整え直せる。
	public bool EnsureMixPositions(double width = 1.0, double height = 1.0)
	{
		(double X, double Y)[] defaults = MixArranger.Arrange(MixArrange.Polygon, _colors.Count, width, height);
		bool changed = false;

		for (int i = 0; i < _colors.Count && i < defaults.Length; i++)
		{
			if (double.IsNaN(_colors[i].MixX) || double.IsNaN(_colors[i].MixY))
			{
				_colors[i].MixX = defaults[i].X;
				_colors[i].MixY = defaults[i].Y;
				changed = true;
			}
		}

		return changed;
	}




	// 色が2つ以上あるか。入れ替え・黒白・テキストモードといった2色を前提とする機能の有効判定に使う。
	public bool HasMultipleColors => _colors.Count >= 2;




	// 指定位置の色をアクティブ(編集対象)にする。進行中の連続編集は前の色のまとまりとして締めてから切り替え、作業値を新しい色から読み直して全タブの表示を追従させる。アクティブ化そのものは色の値を変えないため、元に戻す履歴の段にはしない。
	public void ActivateColor(int index)
	{
		if (index < 0 || index >= _colors.Count || index == _activeIndex)
		{
			return;
		}

		SealGesture();
		_colors[_activeIndex].IsActive = false;
		_activeIndex = index;
		_colors[_activeIndex].IsActive = true;
		LoadActiveColor();
		RaiseColorListChanged();
	}




	// 色を追加するとき、新しい色の色を外部に決めさせる委譲。差し込み先の位置(0 始まり)を受け、その色(不透明)を返すか、既定の色選びに委ねるなら null を返す。配色タブが表示中だけ預け、配色上の色で追加できるようにする。設定しなければ既定どおり調和規則と距離選別で選ぶ。
	public Func<int, Color?>? NextColorProvider { get; set; }




	// 指定位置の色を元に新しい色を作り、その直下へ追加してアクティブにする。新しい色は、色を決める委譲(NextColorProvider)が預けられていればそれを優先し、無ければ元の色と調和しつつ既存のどの色とも紛れないものを選ぶ。不透明度は元の色から引き継ぐ。色が上限に達している間は何もしない。追加は1段として元に戻せる。
	public void AddColorBelow(int index)
	{
		if (_colors.Count >= MaxColors || index < 0 || index >= _colors.Count)
		{
			return;
		}

		BeginDiscrete();

		try
		{
			SidebarColorViewModel source = _colors[index];
			Color newColor = NextColorProvider?.Invoke(index + 1) ?? PickNewColor(source.Rgb);
			var item = new SidebarColorViewModel { Rgb = newColor, Alpha = source.Alpha };
			_colors.Insert(index + 1, item);

			// 挿入位置より後ろの役は1つずれる。
			if (_textColorIndex > index)
			{
				_textColorIndex++;
			}

			if (_bgColorIndex > index)
			{
				_bgColorIndex++;
			}

			_colors[_activeIndex].IsActive = false;
			_activeIndex = index + 1;
			item.IsActive = true;
			NotifyColorCountChanged();
			LoadActiveColor();
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// 追加する色を決める。元の色の OKLCH を基に、調和規則(類似・三色・分裂補色・補色・濃淡)の候補を作り、その中から既存の全色との OKLab 距離の最小値が最も大きい(=どの色とも一番紛れない)候補を選ぶ。調和規則が配色のまとまりを、距離選別が見分けやすさを担う。無彩色に近い色からは色相が定まらないため、中庸の彩度を与えた色相環一周と濃淡を候補にする。
	private Color PickNewColor(Color source)
	{
		(double l, double c, double h) = OklabColor.ToOklch(source);
		var candidates = new List<Color>();

		if (c >= 0.02)
		{
			foreach (double offset in new[] { 30.0, -30.0, 120.0, -120.0, 150.0, -150.0, 180.0 })
			{
				candidates.Add(LchColor.ToRgb(LchSpace.Oklch, l, c, NormalizeHueDegrees(h + offset)));
			}

			candidates.Add(LchColor.ToRgb(LchSpace.Oklch, Math.Clamp(l + 0.25, 0.15, 0.9), c, h));
			candidates.Add(LchColor.ToRgb(LchSpace.Oklch, Math.Clamp(l - 0.25, 0.15, 0.9), c, h));
		}
		else
		{
			double baseLightness = Math.Clamp(l, 0.3, 0.8);

			for (double hue = 0.0; hue < 360.0; hue += 60.0)
			{
				candidates.Add(LchColor.ToRgb(LchSpace.Oklch, baseLightness, 0.12, hue));
			}

			candidates.Add(LchColor.ToRgb(LchSpace.Oklch, Math.Clamp(l + 0.3, 0.1, 0.95), c, h));
			candidates.Add(LchColor.ToRgb(LchSpace.Oklch, Math.Clamp(l - 0.3, 0.1, 0.95), c, h));
		}

		Color best = candidates[0];
		double bestScore = double.MinValue;

		foreach (Color candidate in candidates)
		{
			double score = MinDistanceToExistingColors(candidate);

			if (score > bestScore)
			{
				bestScore = score;
				best = candidate;
			}
		}

		return best;
	}




	// 候補色と既存の全色との OKLab 距離(の2乗)の最小値。大きいほど既存のどの色とも紛れない。
	private double MinDistanceToExistingColors(Color candidate)
	{
		(double l1, double a1, double b1) = LabColor.FromRgb(LchSpace.Oklch, candidate.R, candidate.G, candidate.B);
		double min = double.MaxValue;

		foreach (SidebarColorViewModel item in _colors)
		{
			(double l2, double a2, double b2) = LabColor.FromRgb(LchSpace.Oklch, item.Rgb.R, item.Rgb.G, item.Rgb.B);
			double dl = l1 - l2;
			double da = a1 - a2;
			double db = b1 - b2;
			double distance = (dl * dl) + (da * da) + (db * db);

			if (distance < min)
			{
				min = distance;
			}
		}

		return min;
	}




	// 角度を 0–360 度へ正規化する。
	private static double NormalizeHueDegrees(double hue)
	{
		return ((hue % 360.0) + 360.0) % 360.0;
	}




	// 指定位置の色を削除する。色が下限(MinColors)の間は何もしない。アクティブな色を削除したときは直下の色(最後尾なら直上)を新しいアクティブにする。役(文字色・背景色)も同じ規則で位置を保つ。削除は1段として元に戻せる。
	public void RemoveColor(int index)
	{
		if (_colors.Count <= MinColors || index < 0 || index >= _colors.Count)
		{
			return;
		}

		BeginDiscrete();

		try
		{
			bool wasActive = index == _activeIndex;
			_colors.RemoveAt(index);

			// 削除位置より後ろの役は1つ詰まり、削除された役は同じ位置(末尾なら直上)の色へ就け直す。
			if (index < _textColorIndex)
			{
				_textColorIndex--;
			}
			else if (index == _textColorIndex)
			{
				_textColorIndex = Math.Min(_textColorIndex, _colors.Count - 1);
			}

			if (index < _bgColorIndex)
			{
				_bgColorIndex--;
			}
			else if (index == _bgColorIndex)
			{
				_bgColorIndex = Math.Min(_bgColorIndex, _colors.Count - 1);
			}

			if (wasActive)
			{
				_activeIndex = Math.Min(index, _colors.Count - 1);
				_colors[_activeIndex].IsActive = true;
				NotifyColorCountChanged();
				LoadActiveColor();
			}
			else
			{
				if (index < _activeIndex)
				{
					_activeIndex--;
				}

				NotifyColorCountChanged();
				NotifyContrastRolesChanged();
			}
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// 色を並べ替える。from の色を抜き、to の位置へ差し込む。アクティブな色と役(文字色・背景色)は、どこへ動いてもその色に付いて追従する。並びが変わらなければ何もしない。並べ替えは1段として元に戻せる。
	public void MoveColor(int from, int to)
	{
		if (from < 0 || from >= _colors.Count)
		{
			return;
		}

		to = Math.Clamp(to, 0, _colors.Count - 1);

		if (from == to)
		{
			return;
		}

		BeginDiscrete();

		try
		{
			// Mix タブのポッチ位置は色ではなくスロット(番号)に紐づける。並べ替え前のスロット順の位置を控え、並べ替え後に番号へ書き戻すことで、位置は据え置いたまま色だけがスロット間を移る。これで並べ替えが Mix の平面へ反映される(位置が色に付いて回ると、同じ位置に同じ色のままで平面が変わらない)。
			double[] mixX = new double[_colors.Count];
			double[] mixY = new double[_colors.Count];

			for (int i = 0; i < _colors.Count; i++)
			{
				mixX[i] = _colors[i].MixX;
				mixY[i] = _colors[i].MixY;
			}

			SidebarColorViewModel textItem = _colors[_textColorIndex];
			SidebarColorViewModel bgItem = _colors[_bgColorIndex];
			SidebarColorViewModel item = _colors[from];
			_colors.RemoveAt(from);
			_colors.Insert(to, item);
			_activeIndex = _colors.FindIndex(c => c.IsActive);
			_textColorIndex = _colors.IndexOf(textItem);
			_bgColorIndex = _colors.IndexOf(bgItem);

			for (int i = 0; i < _colors.Count; i++)
			{
				_colors[i].MixX = mixX[i];
				_colors[i].MixY = mixY[i];
			}

			NotifyContrastRolesChanged();
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// アクティブ色の値を作業値(_r・_g・_b・_alpha)へ読み込み、全表色系の表示をまとめて更新する。アクティブの切り替え・入れ替え・復元など、編集対象の色が丸ごと差し替わったときに使う。
	private void LoadActiveColor()
	{
		SidebarColorViewModel active = _colors[_activeIndex];
		_r = active.Rgb.R;
		_g = active.Rgb.G;
		_b = active.Rgb.B;
		_alpha = active.Alpha;
		InitColorCaches();

		OnPropertyChanged(nameof(R));
		OnPropertyChanged(nameof(G));
		OnPropertyChanged(nameof(B));
		OnPropertyChanged(nameof(Alpha));
		NotifyColor1Derived();
		NotifyCmykDerived();
		NotifyHsvDerived();
		NotifyHslDerived();
		NotifyHwbDerived();
		NotifyYuvDerived();
		NotifyLchDerived();
		NotifyLabDerived();
		NotifyContrastRolesChanged();
	}




	// アクティブ色の項目へ作業値(_r・_g・_b・_alpha)を写し、サイドバーのパネル表示を追従させる。色1由来の表示通知(NotifyColor1Derived)と不透明度の変更のたびに呼ばれる。
	private void SyncActiveColorItem()
	{
		if (_colors.Count == 0)
		{
			return;
		}

		SidebarColorViewModel active = _colors[_activeIndex];
		active.Rgb = Color1Raw;
		active.Alpha = _alpha;
		RefreshColorDisplay(active);
	}




	// 16進表記が重なる透過プレビューの代表背景。市松の明色と暗色の中間で、現在のアルファの色を合成した見え方に対する文字色のコントラスト判定に使う。
	private static readonly Color CheckerMidColor = Color.FromArgb(
		0xFF,
		(byte)((CheckerboardGeometry.LightColor.R + CheckerboardGeometry.DarkColor.R) / 2),
		(byte)((CheckerboardGeometry.LightColor.G + CheckerboardGeometry.DarkColor.G) / 2),
		(byte)((CheckerboardGeometry.LightColor.B + CheckerboardGeometry.DarkColor.B) / 2));




	// 色項目の表示物(スウォッチ背景・透過プレビュー・文字色・16進表記)を、現在の色制限の丸めを掛けて流し込む。16進表記は透過プレビュー(市松+現在のアルファの色)の上に重なるため、文字色はその合成後の見え方に対して選ぶ。あわせて色覚シミュレーションの各行も、表示中の色から作って流し込む。
	private void RefreshColorDisplay(SidebarColorViewModel item)
	{
		Color display = ToDisplay(item.Rgb);
		Color alphaOverlay = Color.FromArgb((byte)Math.Round(item.Alpha), display.R, display.G, display.B);
		Color hexBackground = ColorMetrics.AlphaComposite(alphaOverlay, CheckerMidColor);
		item.UpdateDisplay(
			new SolidColorBrush(display),
			new SolidColorBrush(alphaOverlay),
			ContrastBrush(hexBackground),
			HexText(display));

		RefreshVisionRow(item.Protan, _showProtan, ColorVisionType.Protan, _visionSeverity, display, item.Alpha);
		RefreshVisionRow(item.Deutan, _showDeutan, ColorVisionType.Deutan, _visionSeverity, display, item.Alpha);
		RefreshVisionRow(item.Tritan, _showTritan, ColorVisionType.Tritan, _visionSeverity, display, item.Alpha);
		RefreshVisionRow(item.Monochromacy, _showMonochromacy, ColorVisionType.Monochromacy, _visionSeverity, display, item.Alpha);

		// 色の値が変わると Mix タブの平面の塗りも変わるため、再描画の機会を知らせる。表示の丸めだけで実色が変わらない場合も塗り直しは安価で、過剰な描画にはならない。
		ColorsChanged?.Invoke(this, EventArgs.Empty);
	}




	// 色覚シミュレーションの1行分の表示物を流し込む。不透明の塗りとキャプションの文字色は、丸め反映済みの表示色を指定の色覚種別での見え方へ変換して作る。透過プレビューは、市松の明色・暗色それぞれへ表示色を現在の不透明度で合成してから見え方へ変換した2色を作り、行側で不透明な市松として描く。画面に合成された結果へその色覚で見るという順序を保つため、合成が先でフィルタが後になる。表示トグルがオフの行は隠すだけにして、変換とブラシ生成を省く。
	private static void RefreshVisionRow(ColorVisionRowViewModel row, bool show, ColorVisionType type, double severity, Color display, double alpha)
	{
		if (!show)
		{
			row.Hide();
			return;
		}

		Color simulated = ColorVisionSimulation.Simulate(type, severity, display);
		Color overlay = Color.FromArgb((byte)Math.Round(alpha), display.R, display.G, display.B);
		Color lightBlend = ColorVisionSimulation.Simulate(type, severity, ColorMetrics.AlphaComposite(overlay, CheckerboardGeometry.LightColor));
		Color darkBlend = ColorVisionSimulation.Simulate(type, severity, ColorMetrics.AlphaComposite(overlay, CheckerboardGeometry.DarkColor));
		row.Show(
			new SolidColorBrush(simulated),
			new SolidColorBrush(lightBlend),
			new SolidColorBrush(darkBlend),
			ContrastBrush(simulated));
	}




	// 全色項目の表示物を流し込み直す。色制限の設定が変わったときと復元のときに呼ぶ。
	private void RefreshAllColorDisplays()
	{
		foreach (SidebarColorViewModel item in _colors)
		{
			RefreshColorDisplay(item);
		}
	}




	// 各項目の削除・追加ボタンの可否を件数に合わせて更新する。削除は色が下限(MinColors)の間、追加は上限に達している間押せない。
	private void UpdateListCapabilities()
	{
		bool canDelete = _colors.Count > MinColors;
		bool canAdd = _colors.Count < MaxColors;

		foreach (SidebarColorViewModel item in _colors)
		{
			item.CanDelete = canDelete;
			item.CanAdd = canAdd;
		}
	}




	// 色の件数が変わったときの通知一式。ボタンの可否と、2色前提の機能(入れ替え・黒白・テキストモード)の有効状態を更新する。
	private void NotifyColorCountChanged()
	{
		UpdateListCapabilities();
		OnPropertyChanged(nameof(HasMultipleColors));
		OnPropertyChanged(nameof(EffectiveContrastTextMode));
		OnPropertyChanged(nameof(ContrastTextVisibility));
		OnPropertyChanged(nameof(SidebarVisionSeverityVisibility));
	}




	// テキストモードの役(文字色・背景色)由来の表示(背景ブラシ・16進表記・前景色・文字色)とコントラスト比をまとめて通知する。役の差し替え・編集フォーカスの移動・役の色の変化で呼ぶ。
	private void NotifyContrastRolesChanged()
	{
		OnPropertyChanged(nameof(Color2Brush));
		OnPropertyChanged(nameof(Color2HexText));
		OnPropertyChanged(nameof(Color2ForegroundBrush));
		OnPropertyChanged(nameof(ContrastTextBrush));
		NotifyContrastChanged();
	}




	// 色リストの並び・件数・アクティブの変化を通知する。サイドバーがパネル一覧を組み直す。
	private void RaiseColorListChanged()
	{
		ColorListChanged?.Invoke(this, EventArgs.Empty);
	}




	// 文字色の役に就いている色の位置。テキストモードの役選択チップが読む。
	public int ContrastTextColorIndex => _textColorIndex;




	// 背景色の役に就いている色の位置。テキストモードの役選択チップが読む。
	public int ContrastBgColorIndex => _bgColorIndex;




	// 編集フォーカスが文字色の役にあるか。チップの編集枠の表示と、スライダー・透かしの基準の切り替えに使う。
	public bool ContrastFocusIsText => _contrastFocusIsText;




	// コントラストマトリックスで AA を満たさない組み合わせに斜線を重ねるか。マトリックスウィンドウのツールバーのトグルが束縛する。
	public bool MatrixHatchFails
	{
		get => _matrixHatchFails;
		set
		{
			if (_matrixHatchFails == value)
			{
				return;
			}

			_matrixHatchFails = value;
			OnPropertyChanged(nameof(MatrixHatchFails));
		}
	}




	// コントラストマトリックスの色覚シミュレーション。null はフルカラー(シミュレーションなし)。マトリックスウィンドウのトグルとメニューが書き込み、変更の通知で同ウィンドウがセルを書き換える。種別を選んだときはトグルの復帰先(直前の種別)も覚え直す。
	public ColorVisionType? MatrixVision
	{
		get => _matrixVision;
		set
		{
			if (_matrixVision == value)
			{
				return;
			}

			_matrixVision = value;

			if (value is ColorVisionType vision)
			{
				_lastMatrixVision = vision;
			}

			OnPropertyChanged(nameof(MatrixVision));
			OnPropertyChanged(nameof(MatrixVisionSeverityVisibility));
		}
	}




	// マトリックスの色覚シミュレーションをオン/オフで切り替える。オンは直前に選んでいた種別へ復帰する。マトリックスウィンドウのトグルボタンのボタン部が呼ぶ。
	public void ToggleMatrixVision(bool on)
	{
		MatrixVision = on ? _lastMatrixVision : null;
	}




	// 保存値からマトリックスの色覚シミュレーションを解決する。未指定や未知の値はフルカラー(null)として扱う。
	private static ColorVisionType? ResolveMatrixVision(string? value)
	{
		return value switch
		{
			"protan" => ColorVisionType.Protan,
			"deutan" => ColorVisionType.Deutan,
			"tritan" => ColorVisionType.Tritan,
			"monochromacy" => ColorVisionType.Monochromacy,
			_ => null,
		};
	}




	// マトリックスの色覚シミュレーションを保存値の文字列にする。
	private static string MatrixVisionToString(ColorVisionType? vision)
	{
		return vision switch
		{
			ColorVisionType.Protan => "protan",
			ColorVisionType.Deutan => "deutan",
			ColorVisionType.Tritan => "tritan",
			ColorVisionType.Monochromacy => "monochromacy",
			_ => "full",
		};
	}




	// 役(文字色/背景色)へ色を就ける。別の色をクリックしたときは役の色を差し替えた上で編集対象もその色へ移し、既に就いている色をクリックしたときは編集フォーカスだけをその役へ移す。テキストモードの役選択チップのクリックが呼ぶ。
	public void SelectContrastRole(bool isTextRole, int index)
	{
		if (index < 0 || index >= _colors.Count)
		{
			return;
		}

		if (isTextRole)
		{
			_textColorIndex = index;

			// 文字色に背景色と同じ位置を選んだら確認の意味が無くなるため、背景色の役を隣(末尾なら直前)へずらして別の色にする。色は最低 MinColors 色あるため必ず別の位置へ動ける。
			if (_bgColorIndex == index)
			{
				_bgColorIndex = index >= _colors.Count - 1 ? index - 1 : index + 1;
			}
		}
		else
		{
			_bgColorIndex = index;

			// 背景色に文字色と同じ位置を選んだときも同様に、文字色の役を隣へずらす。
			if (_textColorIndex == index)
			{
				_textColorIndex = index >= _colors.Count - 1 ? index - 1 : index + 1;
			}
		}

		_contrastFocusIsText = isTextRole;

		if (_activeIndex != index)
		{
			ActivateColor(index);
		}
		else
		{
			NotifyContrastRolesChanged();
			RaiseColorListChanged();
		}
	}




	// 文字色と背景色の役を入れ替える。色リストの並びや各色の値は変えず、どの色がどちらの役に就いているかだけを差し替える。テキストモードが表示されている間だけ働く。編集フォーカスは同じ役に留め、その役へ新しく就いた色を編集対象(アクティブ)にする。役の編集を続けながら中身の色だけが入れ替わるので、スライダーのつまみは新しい色の値へ追従する。色の値を変えない操作のため、元に戻す履歴の段にはしない。役選択パネルのボタンと X キーが呼ぶ。
	public void SwapContrastRoles()
	{
		if (!EffectiveContrastTextMode)
		{
			return;
		}

		(_textColorIndex, _bgColorIndex) = (_bgColorIndex, _textColorIndex);
		int target = _contrastFocusIsText ? _textColorIndex : _bgColorIndex;

		if (_activeIndex != target)
		{
			ActivateColor(target);
		}
		else
		{
			NotifyContrastRolesChanged();
			RaiseColorListChanged();
		}
	}




	// 役の位置を色リストの範囲へ収め、アクティブな色がどちらの役でもなければ文字色の役へ就ける。編集フォーカスはアクティブが就いている役へ合わせる。構築・復元・テキストモードへ入るときに呼ぶ。
	private void EnsureContrastRoles()
	{
		_textColorIndex = Math.Clamp(_textColorIndex, 0, _colors.Count - 1);
		_bgColorIndex = Math.Clamp(_bgColorIndex, 0, _colors.Count - 1);

		if (_activeIndex != _textColorIndex && _activeIndex != _bgColorIndex)
		{
			_textColorIndex = _activeIndex;
		}

		_contrastFocusIsText = _activeIndex == _textColorIndex;
	}




	// テキストモード(コントラスト確認)で背景色の役に就いている色(素の値)。
	private Color ContrastBackgroundColor => _colors[_bgColorIndex].Rgb;




	// コントラストスライダーの基準色(素の値)。スライダーは編集フォーカスの役の色を動かすため、基準にはもう片方の役の色を使う。
	private Color ContrastReferenceColor => _contrastFocusIsText ? _colors[_bgColorIndex].Rgb : _colors[_textColorIndex].Rgb;




	public double R
	{
		get => _r;
		set => SetChannel(ref _r, value);
	}




	public double G
	{
		get => _g;
		set => SetChannel(ref _g, value);
	}




	public double B
	{
		get => _b;
		set => SetChannel(ref _b, value);
	}




	// 無彩色(灰)としての明るさ(0–255)。get は色1の R・G・B を Rec.601 加重輝度(0.299R+0.587G+0.114B)で1次元へ落とし、バイト粒度の整数へ丸めて返す。有彩色でも代表的な明るさを示す。set は R・G・B を同値へ一括で揃えて無彩色にする(= 脱色)。RGB タブの無彩色スライダー・数値入力欄が束縛する。get は R・G・B から導く投影で set の逆関数ではないため、R・G・B 変更に伴う追従をスライダーへ流し込むときの書き戻し抑制はビュー側の同期フラグが担う。
	public double Gray
	{
		get => Math.Round((0.299 * _r) + (0.587 * _g) + (0.114 * _b));
		set => SetGray(value);
	}




	// CMYK は作業用キャッシュ(_cmykC など)を真として持つ。各成分はパーセント(0–100)で扱い、設定時は他成分を保ったまま RGB へ変換し直して色1へ反映する。外部で色が変わると正準形へ取り直す(NotifyCmykDerived)。
	public double C
	{
		get => _cmykC * 100.0;
		set => SetCmykChannel(0, value);
	}




	public double M
	{
		get => _cmykM * 100.0;
		set => SetCmykChannel(1, value);
	}




	public double Y
	{
		get => _cmykY * 100.0;
		set => SetCmykChannel(2, value);
	}




	public double K
	{
		get => _cmykK * 100.0;
		set => SetCmykChannel(3, value);
	}




	// 色相(度, 0–360)。リング上のスライダーが束縛する。色1が無彩色(灰・黒)で色相が定まらない間も、利用者が選んだ色相を保つため内部に保持した値を返す。
	public double H
	{
		get => _cachedHue;
		set => SetHue(value);
	}




	// 色相を 0–1 で扱う束縛用。色相×明度の直交パッドの横方向が束縛する。0 を色相 0 度、1 を 360 度とし、設定時は度へ直して色相の本体(H)へ渡す。
	public double HueFraction01
	{
		get => _cachedHue / 360.0;
		set => SetHue(Math.Clamp(value, 0.0, 1.0) * 360.0);
	}




	// 彩度(0–100%)。表示と数値入力に使う。設定時は 0–1 へ直して彩度の本体(Saturation01)へ渡す。
	public double S
	{
		get => _cachedSaturation * 100.0;
		set => Saturation01 = value / 100.0;
	}




	// 明度(0–100%)。表示と数値入力に使う。設定時は 0–1 へ直して明度の本体(Value01)へ渡す。
	public double V
	{
		get => CurrentValue() * 100.0;
		set => Value01 = value / 100.0;
	}




	// 彩度を 0–1 で扱う束縛用。中央の2次元パッドの横方向が束縛する。
	public double Saturation01
	{
		get => _cachedSaturation;
		set => SetSaturation(value);
	}




	// 明度を 0–1 で扱う束縛用。中央の2次元パッドの縦方向が束縛する。
	public double Value01
	{
		get => CurrentValue();
		set => SetValueComponent(value);
	}




	// 中央の2次元パッドの下地。現在の色相を最大彩度・最大明度で塗った色で、彩度・明度が0でも色相に応じて変わる。
	public Brush SvBaseColorBrush => new SolidColorBrush(SvBaseColor());




	// HSV/HSL タブの中央パッドと数値表示をどの表色系で読むか (0=HSV, 1=HSL, 2=HWB)。タブのラジオが束縛し、保存して次回起動へ引き継ぐ。色1の RGB は不変で、見せ方だけが変わる。
	public int HsvSubModeIndex
	{
		get => _hsvSubModeIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 2);

			if (_hsvSubModeIndex == clamped)
			{
				return;
			}

			_hsvSubModeIndex = clamped;
			OnPropertyChanged(nameof(HsvSubModeIndex));
		}
	}




	// HSV モードの見せ方 (0=色相リング+正方形パッド, 1=角度=色相・半径=彩度の円盤+明度の縦スライダー, 2=色相×明度の直交パッド+彩度の縦スライダー, 3=角度=色相・半径=明度の円盤+彩度の縦スライダー, 4=色相×彩度の直交パッド+明度の縦スライダー, 5=彩度×明度の正方形+色相の縦スライダー)。レイアウトのセレクタが束縛し、保存して次回起動へ引き継ぐ。色1の RGB は不変で、見せ方だけが変わる。HSL・HWB の副モードでは色相リング+パッドに固定で、この値はそのとき表示に反映されない。
	public int HsvLayoutIndex
	{
		get => _hsvLayoutIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 5);

			if (_hsvLayoutIndex == clamped)
			{
				return;
			}

			_hsvLayoutIndex = clamped;
			OnPropertyChanged(nameof(HsvLayoutIndex));
		}
	}




	// HSL モードの見せ方 (0=色相リング+正方形パッド, 1=角度=色相・半径=彩度の円盤+輝度の縦スライダー, 2=色相×輝度の直交パッド+彩度の縦スライダー, 3=角度=色相・半径=輝度の円盤+彩度の縦スライダー, 4=色相×彩度の直交パッド+輝度の縦スライダー, 5=彩度×輝度の正方形+色相の縦スライダー, 6=色相リング+三角形, 7=三角形+色相の縦スライダー)。レイアウトのセレクタが束縛し、保存して次回起動へ引き継ぐ。色1の RGB は不変で、見せ方だけが変わる。HSV・HWB の副モードではこの値は表示に反映されない。
	public int HslLayoutIndex
	{
		get => _hslLayoutIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 7);

			if (_hslLayoutIndex == clamped)
			{
				return;
			}

			_hslLayoutIndex = clamped;
			OnPropertyChanged(nameof(HslLayoutIndex));
		}
	}




	// HWB モードの見せ方 (0=色相リング+正方形パッド, 1=角度=色相・半径=白みの円盤+黒みの縦スライダー, 2=色相×黒みの直交パッド+白みの縦スライダー, 3=角度=色相・半径=黒みの円盤+白みの縦スライダー, 4=色相×白みの直交パッド+黒みの縦スライダー, 5=白み×黒みの正方形+色相の縦スライダー, 6=色相リング+三角形, 7=三角形+色相の縦スライダー)。レイアウトのセレクタが束縛し、保存して次回起動へ引き継ぐ。色1の RGB は不変で、見せ方だけが変わる。HSV・HSL の副モードではこの値は表示に反映されない。
	public int HwbLayoutIndex
	{
		get => _hwbLayoutIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 7);

			if (_hwbLayoutIndex == clamped)
			{
				return;
			}

			_hwbLayoutIndex = clamped;
			OnPropertyChanged(nameof(HwbLayoutIndex));
		}
	}




	// LCH モードの見せ方 (0=色相リング+彩度明度の平面, 1=彩度×明度の平面+色相の縦スライダー, 2=角度=色相・半径=明度の円盤+彩度の縦スライダー, 3=色相×明度の平面+彩度の縦スライダー, 4=角度=色相・半径=彩度の円盤+明度の縦スライダー, 5=色相×彩度の平面+明度の縦スライダー)。レイアウトのセレクタが束縛し、保存して次回起動へ引き継ぐ。OKLCH・CIE LCH の副モードで共通の見せ方を使う。色1の RGB は不変で、見せ方だけが変わる。
	public int LchLayoutIndex
	{
		get => _lchLayoutIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 5);

			if (_lchLayoutIndex == clamped)
			{
				return;
			}

			_lchLayoutIndex = clamped;
			OnPropertyChanged(nameof(LchLayoutIndex));
		}
	}




	// Lab モードの見せ方 (0=a×b 平面+明度の縦バー, 1=a×L 平面+b の縦バー, 2=b×L 平面+a の縦バー)。レイアウトのセレクタが束縛し、保存して次回起動へ引き継ぐ。OKLab・CIE Lab の副モードで共通の見せ方を使う。色1の RGB は不変で、見せ方だけが変わる。
	public int LabLayoutIndex
	{
		get => _labLayoutIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 2);

			if (_labLayoutIndex == clamped)
			{
				return;
			}

			_labLayoutIndex = clamped;
			OnPropertyChanged(nameof(LabLayoutIndex));
		}
	}




	// YUV/YCbCr タブの見せ方(レイアウト)の位置 (0=Cb×Cr 平面+Y の縦バー, 1=Cb×Y 平面+Cr の縦バー, 2=Cr×Y 平面+Cb の縦バー)。見せ方を変えても色1の RGB は変わらない。保存して次回起動へ引き継ぐ。
	public int YuvLayoutIndex
	{
		get => _yuvLayoutIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 2);

			if (_yuvLayoutIndex == clamped)
			{
				return;
			}

			_yuvLayoutIndex = clamped;
			OnPropertyChanged(nameof(YuvLayoutIndex));
		}
	}




	// RGB/CMYK タブの2次元エディタの見せ方(レイアウト)の位置。RGB・CMYK 共通の1つのピッカーが選ぶ。0=2次元パッド無し(線形スライダーのみ)、1〜3=RGB の平面(1=G×B+R, 2=R×B+G, 3=R×G+B)、4〜6=CMYK の平面(4=M×Y+C, 5=C×Y+M, 6=C×M+Y)。タブに出すパッドは1枚で、RGB 系か CMYK 系のどちらか一方。見せ方を変えても色1の RGB は変わらない。保存して次回起動へ引き継ぐ。
	public int RgbCmykLayoutIndex
	{
		get => _rgbCmykLayoutIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 6);

			if (_rgbCmykLayoutIndex == clamped)
			{
				return;
			}

			_rgbCmykLayoutIndex = clamped;
			OnPropertyChanged(nameof(RgbCmykLayoutIndex));
		}
	}




	// YUV/YCbCr タブの色差平面の表示枠(スケール)の決め方の位置 (0=固定枠, 1=等方フィット, 2=縦横独立フィット)。AbFitMode と同じ並びで、フィットは固定成分ごとの色域の広がりへ枠を寄せて有効領域を広げる(枠は固定成分で縮尺が変わる)。三つの見せ方すべてに効く。スケールが変わると下地・つまみ位置の座標と値の読み替え方(枠)が変わるため、既定 Cb×Cr パッドのつまみ位置の正規化値を通知し直す(直交パッドはコードビハインドが UpdateCartThumb で合わせ直す)。保存して次回起動へ引き継ぐ。色1の RGB は不変。
	public int YuvScaleIndex
	{
		get => _yuvScaleIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 2);

			if (_yuvScaleIndex == clamped)
			{
				return;
			}

			_yuvScaleIndex = clamped;
			OnPropertyChanged(nameof(YuvScaleIndex));
			OnPropertyChanged(nameof(YuvCbCrPadCbNorm));
			OnPropertyChanged(nameof(YuvCbCrPadCrNorm));
		}
	}




	// 現在のスケールの決め方。YuvScaleIndex を AbFitMode へ読み替える。表示枠の算出に使う。
	private AbFitMode YuvFit => (AbFitMode)_yuvScaleIndex;




	// 現在の符号化形式・輝度・スケールの決め方で求めた Cb×Cr 平面の表示枠の覚え書き。下地の生成・つまみ位置の正規化(YuvCbCrPadCbNorm/YuvCbCrPadCrNorm)・パッド操作の値の読み替え(SetYuvPad)で同じ枠を使うため、輝度ドラッグ中に色域の外接枠の走査を毎回やり直さずに済むよう、鍵(形式・輝度・スケール)が変わらない限り使い回す。
	private YCbCrFormat _yuvCbCrExtentFormat;
	private double _yuvCbCrExtentLuma = double.NaN;
	private AbFitMode _yuvCbCrExtentFit = (AbFitMode)(-1);
	private bool _yuvCbCrExtentValid;
	private PlaneExtent _yuvCbCrExtentCache;




	// 現在の符号化形式・輝度・スケールの決め方に対応する Cb×Cr 平面の表示枠を返す。鍵が変わらなければ覚えた枠をそのまま返し、変わったときだけ作り直す。
	private PlaneExtent CurrentYuvCbCrExtent()
	{
		AbFitMode fit = YuvFit;
		YCbCrFormat format = Format;

		if (_yuvCbCrExtentValid && format.Standard == _yuvCbCrExtentFormat.Standard && format.FullRange == _yuvCbCrExtentFormat.FullRange && _cachedY == _yuvCbCrExtentLuma && fit == _yuvCbCrExtentFit)
		{
			return _yuvCbCrExtentCache;
		}

		_yuvCbCrExtentCache = YuvColor.CbCrExtentFor(format, _cachedY, fit);
		_yuvCbCrExtentFormat = format;
		_yuvCbCrExtentLuma = _cachedY;
		_yuvCbCrExtentFit = fit;
		_yuvCbCrExtentValid = true;
		return _yuvCbCrExtentCache;
	}




	// 現在の符号化形式・固定色差・スケールの決め方で求めた Cb×Y・Cr×Y 平面の表示枠の覚え書き。下地の生成・つまみ位置の正規化・パッド操作の値の読み替えで同じ枠を使うため、固定色差ドラッグ中に二次元の外接枠の走査を毎回やり直さずに済むよう、鍵が変わらない限り使い回す。Cb×Y と Cr×Y は同時には活性にならないため一枠だけ覚える。
	private YCbCrFormat _yuvLumaExtentFormat;
	private bool _yuvLumaExtentHorizontalIsCb;
	private double _yuvLumaExtentFixed = double.NaN;
	private AbFitMode _yuvLumaExtentFit = (AbFitMode)(-1);
	private bool _yuvLumaExtentValid;
	private PlaneExtent _yuvLumaExtentCache;




	// 現在の符号化形式・固定色差・スケールの決め方に対応する Cb×Y・Cr×Y 平面の表示枠を返す。horizontalIsCb が真なら横軸が Cb で固定成分は Cr(Cb×Y)、偽なら横軸が Cr で固定成分は Cb(Cr×Y)。鍵が変わらなければ覚えた枠をそのまま返し、変わったときだけ作り直す。
	private PlaneExtent YuvLumaExtent(bool horizontalIsCb)
	{
		double fixedChroma = horizontalIsCb ? _cachedCr : _cachedCb;
		AbFitMode fit = YuvFit;
		YCbCrFormat format = Format;

		if (_yuvLumaExtentValid && format.Standard == _yuvLumaExtentFormat.Standard && format.FullRange == _yuvLumaExtentFormat.FullRange && horizontalIsCb == _yuvLumaExtentHorizontalIsCb && fixedChroma == _yuvLumaExtentFixed && fit == _yuvLumaExtentFit)
		{
			return _yuvLumaExtentCache;
		}

		_yuvLumaExtentCache = horizontalIsCb
			? YuvColor.CbLumaExtentFor(format, fixedChroma, fit)
			: YuvColor.CrLumaExtentFor(format, fixedChroma, fit);
		_yuvLumaExtentFormat = format;
		_yuvLumaExtentHorizontalIsCb = horizontalIsCb;
		_yuvLumaExtentFixed = fixedChroma;
		_yuvLumaExtentFit = fit;
		_yuvLumaExtentValid = true;
		return _yuvLumaExtentCache;
	}




	// 既定 Cb×Cr パッドのつまみの横位置(0–1)。横軸は Cb。下段の Cb スライダー(常に 0–255 の固定尺度)と違い、パッドは表示枠(CurrentYuvCbCrExtent)を介して読む。固定枠のときは Cb01 と一致し、フィットのときは枠の縮尺・中心に追従する。枠は輝度で変わるため、輝度が変わるとつまみ位置も移る。読み取り専用(操作の反映は SetYuvPad)。
	public double YuvCbCrPadCbNorm
	{
		get
		{
			PlaneExtent extent = CurrentYuvCbCrExtent();
			return extent.XWidth <= 0.0 ? 0.5 : Math.Clamp((_cachedCb - extent.XMin) / extent.XWidth, 0.0, 1.0);
		}
	}




	// 既定 Cb×Cr パッドのつまみの縦位置(0–1)。0 が枠の下端(Cr の下限)、1 が上端(上限)。横位置(YuvCbCrPadCbNorm)と同じく表示枠を介して読む。
	public double YuvCbCrPadCrNorm
	{
		get
		{
			PlaneExtent extent = CurrentYuvCbCrExtent();
			return extent.YHeight <= 0.0 ? 0.5 : Math.Clamp((_cachedCr - extent.YMin) / extent.YHeight, 0.0, 1.0);
		}
	}




	// Cb×Y パッドのつまみの横位置(0–1)。横軸は Cb。固定成分は Cr。表示枠(YuvLumaExtent)を介して読む。読み取り専用(操作の反映は SetYuvCbLumaPad)。
	public double YuvCbLumaPadCbNorm
	{
		get
		{
			PlaneExtent extent = YuvLumaExtent(true);
			return extent.XWidth <= 0.0 ? 0.5 : Math.Clamp((_cachedCb - extent.XMin) / extent.XWidth, 0.0, 1.0);
		}
	}




	// Cb×Y パッドのつまみの縦位置(0–1)。縦軸は輝度 Y。0 が枠の下端、1 が上端。横位置(YuvCbLumaPadCbNorm)と同じく表示枠を介して読む。フィットのときは色域に入る輝度の帯に詰まる。
	public double YuvCbLumaPadYNorm
	{
		get
		{
			PlaneExtent extent = YuvLumaExtent(true);
			return extent.YHeight <= 0.0 ? 0.5 : Math.Clamp((_cachedY - extent.YMin) / extent.YHeight, 0.0, 1.0);
		}
	}




	// Cr×Y パッドのつまみの横位置(0–1)。横軸は Cr。固定成分は Cb。表示枠(YuvLumaExtent)を介して読む。読み取り専用(操作の反映は SetYuvCrLumaPad)。
	public double YuvCrLumaPadCrNorm
	{
		get
		{
			PlaneExtent extent = YuvLumaExtent(false);
			return extent.XWidth <= 0.0 ? 0.5 : Math.Clamp((_cachedCr - extent.XMin) / extent.XWidth, 0.0, 1.0);
		}
	}




	// Cr×Y パッドのつまみの縦位置(0–1)。縦軸は輝度 Y。0 が枠の下端、1 が上端。横位置(YuvCrLumaPadCrNorm)と同じく表示枠を介して読む。フィットのときは色域に入る輝度の帯に詰まる。
	public double YuvCrLumaPadYNorm
	{
		get
		{
			PlaneExtent extent = YuvLumaExtent(false);
			return extent.YHeight <= 0.0 ? 0.5 : Math.Clamp((_cachedY - extent.YMin) / extent.YHeight, 0.0, 1.0);
		}
	}




	// Lab タブの a×b 平面の表示枠(スケール)の決め方の位置 (0=固定枠, 1=等方フィット, 2=縦横独立フィット)。AbFitMode と同じ並びで、フィットは明度ごとの色域の広がりへ枠を寄せて有効領域を広げる(枠は明度で縮尺が変わる)。a×b 平面の見せ方のときだけ効く。スケールが変わると下地・つまみ位置の座標と値の読み替え方(枠)が変わるため、パッドのつまみ位置の正規化値を通知し直す。保存して次回起動へ引き継ぐ。色1の RGB は不変。
	public int LabAbScaleIndex
	{
		get => _labAbScaleIndex;
		set
		{
			int clamped = Math.Clamp(value, 0, 2);

			if (_labAbScaleIndex == clamped)
			{
				return;
			}

			_labAbScaleIndex = clamped;
			OnPropertyChanged(nameof(LabAbScaleIndex));
			OnPropertyChanged(nameof(LabPadANorm));
			OnPropertyChanged(nameof(LabPadBNorm));
		}
	}




	// 現在のスケールの決め方。LabAbScaleIndex を AbFitMode へ読み替える。表示枠の算出に使う。
	private AbFitMode LabAbFit => (AbFitMode)_labAbScaleIndex;




	// 現在の副モード・明度・スケールの決め方で求めた a×b 平面の表示枠の覚え書き。下地の生成・つまみ位置の正規化(LabPadANorm/LabPadBNorm)・パッド操作の値の読み替え(SetLabPad)で同じ枠を使うため、明度ドラッグ中に色相を一周しての境界探索を毎回やり直さずに済むよう、鍵(副モード・明度・スケール)が変わらない限り使い回す。
	private LchSpace _abExtentSpace = (LchSpace)(-1);
	private double _abExtentL = double.NaN;
	private AbFitMode _abExtentFit = (AbFitMode)(-1);
	private PlaneExtent _abExtentCache;




	// 現在の副モード・明度・スケールの決め方に対応する a×b 平面の表示枠を返す。鍵が変わらなければ覚えた枠をそのまま返し、変わったときだけ作り直す。
	private PlaneExtent CurrentAbExtent()
	{
		AbFitMode fit = LabAbFit;
		LchSpace space = LabColorSpace;

		if (space == _abExtentSpace && _cachedLabL == _abExtentL && fit == _abExtentFit)
		{
			return _abExtentCache;
		}

		_abExtentCache = LabColor.AbExtentFor(space, _cachedLabL, fit);
		_abExtentSpace = space;
		_abExtentL = _cachedLabL;
		_abExtentFit = fit;
		return _abExtentCache;
	}




	// 現在の副モード・固定成分・スケールの決め方で求めた a×L・b×L 平面の表示枠の覚え書き。下地の生成・つまみ位置の正規化・パッド操作の値の読み替えで同じ枠を使うため、固定成分ドラッグ中に二次元の境界走査を毎回やり直さずに済むよう、鍵が変わらない限り使い回す。a×L と b×L は同時には活性にならないため一枠だけ覚える。
	private LchSpace _cartExtentSpace = (LchSpace)(-1);
	private bool _cartExtentHorizontalIsA;
	private double _cartExtentFixed = double.NaN;
	private AbFitMode _cartExtentFit = (AbFitMode)(-1);
	private bool _cartExtentValid;
	private PlaneExtent _cartExtentCache;




	// 現在の副モード・固定成分・スケールの決め方に対応する a×L・b×L 平面の表示枠を返す。horizontalIsA が真なら横軸が a で固定成分は b(a×L)、偽なら横軸が b で固定成分は a(b×L)。鍵が変わらなければ覚えた枠をそのまま返し、変わったときだけ作り直す。
	private PlaneExtent CartExtent(bool horizontalIsA)
	{
		double fixedValue = horizontalIsA ? _cachedLabB : _cachedLabA;
		AbFitMode fit = LabAbFit;
		LchSpace space = LabColorSpace;

		if (_cartExtentValid && space == _cartExtentSpace && horizontalIsA == _cartExtentHorizontalIsA && fixedValue == _cartExtentFixed && fit == _cartExtentFit)
		{
			return _cartExtentCache;
		}

		_cartExtentCache = LabColor.CartExtentFor(space, fixedValue, horizontalIsA, fit);
		_cartExtentSpace = space;
		_cartExtentHorizontalIsA = horizontalIsA;
		_cartExtentFixed = fixedValue;
		_cartExtentFit = fit;
		_cartExtentValid = true;
		return _cartExtentCache;
	}




	// a×L パッドのつまみの横位置(0–1)。横軸は a。下段の a スライダー(常に ±AbMax の固定尺度)と違い、a×L パッドは表示枠(CartExtent)を介して読む。固定枠のときは LabANorm と一致し、フィットのときは枠の縮尺・中心に追従する。枠は固定成分 b で変わるため、b が変わるとつまみ位置も移る。読み取り専用(操作の反映は SetLabALPad)。
	public double LabAlPadXNorm
	{
		get
		{
			PlaneExtent extent = CartExtent(true);
			return extent.XWidth <= 0.0 ? 0.5 : Math.Clamp((_cachedLabA - extent.XMin) / extent.XWidth, 0.0, 1.0);
		}
	}




	// a×L パッドのつまみの縦位置(0–1)。縦軸は明度 L。0 が枠の下端、1 が上端。横位置(LabAlPadXNorm)と同じく表示枠を介して読む。フィットのときは色域に入る明度の帯に詰まる。
	public double LabAlPadYNorm
	{
		get
		{
			PlaneExtent extent = CartExtent(true);
			return extent.YHeight <= 0.0 ? 0.5 : Math.Clamp((_cachedLabL - extent.YMin) / extent.YHeight, 0.0, 1.0);
		}
	}




	// b×L パッドのつまみの横位置(0–1)。横軸は b。固定成分は a。表示枠(CartExtent)を介して読む。
	public double LabBlPadXNorm
	{
		get
		{
			PlaneExtent extent = CartExtent(false);
			return extent.XWidth <= 0.0 ? 0.5 : Math.Clamp((_cachedLabB - extent.XMin) / extent.XWidth, 0.0, 1.0);
		}
	}




	// b×L パッドのつまみの縦位置(0–1)。縦軸は明度 L。0 が枠の下端、1 が上端。横位置(LabBlPadXNorm)と同じく表示枠を介して読む。
	public double LabBlPadYNorm
	{
		get
		{
			PlaneExtent extent = CartExtent(false);
			return extent.YHeight <= 0.0 ? 0.5 : Math.Clamp((_cachedLabL - extent.YMin) / extent.YHeight, 0.0, 1.0);
		}
	}




	// 中央の2次元パッドを色相環の現在位置へ追従させて回すかどうか。真のとき、最大彩度・最大明度の角が色相環のつまみの方向を向くようパッドを回す。
	public bool FollowHue
	{
		get => _followHue;
		set
		{
			if (_followHue == value)
			{
				return;
			}

			_followHue = value;
			OnPropertyChanged(nameof(FollowHue));
			OnPropertyChanged(nameof(SvPadRotation));
			OnPropertyChanged(nameof(SlSquarePadRotation));
			OnPropertyChanged(nameof(SlPadRotation));
			OnPropertyChanged(nameof(HwbPadRotation));
			OnPropertyChanged(nameof(HwbTrianglePadRotation));
		}
	}




	// 中央の2次元パッドの回転角(度)。追従が有効なときは、最大彩度・最大明度の角(未回転では真上から時計回りに45度の位置)が色相環のつまみ(色相の角度)を向くよう、色相から45度引いた角度にする。無効なときは0。
	public double SvPadRotation => _followHue ? _cachedHue - 45.0 : 0.0;




	// HSL の彩度・輝度の正方形パッド(リング内)の回転角(度)。追従が有効なときは、純色(彩度1・輝度0.5、未回転では右辺中央=真上から時計回りに90度の位置)が色相環のつまみ(色相の角度)を向くよう、色相から90度引いた角度にする。無効なときは0。
	public double SlSquarePadRotation => _followHue ? _cachedHue - 90.0 : 0.0;




	// 色1(R・G・B)から現在の明度(0–1)を求める。
	private double CurrentValue()
	{
		return Math.Max(_r, Math.Max(_g, _b)) / 255.0;
	}




	// 色相を設定する。保持した色相を更新し、色1へ反映する。無彩色で RGB が変わらない場合も、下地の色や追従回転は色相で変わるため通知する。
	private void SetHue(double hue)
	{
		double normalized = ((hue % 360.0) + 360.0) % 360.0;

		if (normalized == _cachedHue)
		{
			return;
		}

		_cachedHue = normalized;
		OnPropertyChanged(nameof(H));
		OnPropertyChanged(nameof(HueFraction01));
		OnPropertyChanged(nameof(SvBaseColorBrush));
		OnPropertyChanged(nameof(SvPadRotation));
		OnPropertyChanged(nameof(SlSquarePadRotation));
		OnPropertyChanged(nameof(SlPadRotation));
		OnPropertyChanged(nameof(HwbPadRotation));
		OnPropertyChanged(nameof(HwbTrianglePadRotation));
		NotifySliderTrackBrushes();
		ApplyHsv();
	}




	// 彩度を設定する。保持した彩度を更新し、色1へ反映する。
	private void SetSaturation(double saturation01)
	{
		double clamped = Math.Clamp(saturation01, 0.0, 1.0);

		if (clamped == _cachedSaturation)
		{
			return;
		}

		_cachedSaturation = clamped;
		OnPropertyChanged(nameof(Saturation01));
		OnPropertyChanged(nameof(S));
		ApplyHsv();
	}




	// 明度を設定する。明度は色1の RGB から導くため、目標の明度で HSV を RGB へ変換し直して反映する。
	private void SetValueComponent(double value01)
	{
		double clamped = Math.Clamp(value01, 0.0, 1.0);

		if (clamped == CurrentValue())
		{
			return;
		}

		ApplyHsv(clamped);
		OnPropertyChanged(nameof(Value01));
		OnPropertyChanged(nameof(V));
	}




	// 保持している色相・彩度と、現在(または指定)の明度から色1の RGB を作り直して反映する。RGB が変わらなければ通知しない。
	private void ApplyHsv(double? valueOverride = null)
	{
		double value = valueOverride ?? CurrentValue();
		(byte r, byte g, byte b) = ColorConversion.HsvToRgb(_cachedHue, _cachedSaturation, value);

		if (r == (byte)_r && g == (byte)_g && b == (byte)_b)
		{
			return;
		}

		RecordContinuousChange();
		_r = r;
		_g = g;
		_b = b;

		OnPropertyChanged(nameof(R));
		OnPropertyChanged(nameof(G));
		OnPropertyChanged(nameof(B));
		NotifyColor1Derived();
		NotifyCmykDerived();
		SyncHslCacheFromRgb();
		NotifyHslDerived();
		NotifyHwbDerived();
		NotifyYuvDerived();
		NotifyLchDerived();
		NotifyLabDerived();
	}




	// 中央の2次元パッドの下地色。現在の色相を最大彩度・最大明度で塗った色。
	private Color SvBaseColor()
	{
		(byte r, byte g, byte b) = ColorConversion.HsvToRgb(_cachedHue, 1.0, 1.0);
		return Color.FromArgb(0xFF, r, g, b);
	}




	// 色1の初期値から、保持する色相・彩度(HSV・HSL)を求める。構築時に一度だけ呼ぶ。
	private void InitColorCaches()
	{
		(double hue, double saturation, double value) = ColorConversion.RgbToHsv((byte)_r, (byte)_g, (byte)_b);
		_cachedHue = hue;
		_cachedSaturation = saturation;

		(double hslHue, double hslSaturation, double lightness) = ColorConversion.RgbToHsl((byte)_r, (byte)_g, (byte)_b);
		_cachedHslSaturation = hslSaturation;

		_cachedWhiteness = Math.Min(_r, Math.Min(_g, _b)) / 255.0;
		_cachedBlackness = 1.0 - (Math.Max(_r, Math.Max(_g, _b)) / 255.0);

		(double lchL, double lchC, double lchH) = LchColor.FromRgb(LchColorSpace, (byte)_r, (byte)_g, (byte)_b);
		_cachedLchL = lchL;
		_cachedLchChroma = lchC;
		_cachedLchHue = lchH;

		(double labL, double labA, double labB) = LabColor.FromRgb(LabColorSpace, (byte)_r, (byte)_g, (byte)_b);
		_cachedLabL = labL;
		_cachedLabA = labA;
		_cachedLabB = labB;

		(double yuvY, double yuvCb, double yuvCr) = ColorConversion.RgbToYCbCr((byte)_r, (byte)_g, (byte)_b, Format);
		_cachedY = yuvY;
		_cachedCb = yuvCb;
		_cachedCr = yuvCr;

		SyncCmykCacheFromRgb();
	}




	// 色1の RGB が外部から変わったとき、保持している色相・彩度を同期する。色相・彩度が定まる有彩色なら更新し、灰なら彩度を0にして色相は保ち、黒なら双方を保つ。色相が変わらないはずの操作(HSL の彩度・輝度編集など)からは updateHue=false で呼び、共通の色相を RGB のバイト丸め由来で揺らさないようにする。
	private void SyncHsvCacheFromRgb(bool updateHue = true)
	{
		(double hue, double saturation, double value) = ColorConversion.RgbToHsv((byte)_r, (byte)_g, (byte)_b);

		if (value > 0.0 && saturation > 0.0)
		{
			if (updateHue)
			{
				_cachedHue = hue;
			}

			_cachedSaturation = saturation;
		}
		else if (value > 0.0)
		{
			_cachedSaturation = 0.0;
		}
	}




	// 色1から導く HSV 系の表示物をまとめて通知する。色1が変わると色相・彩度・明度とその表示、下地色、追従回転が変わりうる。
	private void NotifyHsvDerived()
	{
		OnPropertyChanged(nameof(H));
		OnPropertyChanged(nameof(S));
		OnPropertyChanged(nameof(V));
		OnPropertyChanged(nameof(Saturation01));
		OnPropertyChanged(nameof(Value01));
		OnPropertyChanged(nameof(SvBaseColorBrush));
		OnPropertyChanged(nameof(SvPadRotation));
		OnPropertyChanged(nameof(SlSquarePadRotation));
	}




	// HSL の彩度を 0–1 で扱う束縛用。中央の三角形パッドの彩度軸が束縛する。色1が無彩色で彩度が定まらない間も、利用者が選んだ彩度を保つため内部に保持した値を返す。
	public double HslSaturation01
	{
		get => _cachedHslSaturation;
		set => SetHslSaturation(value);
	}




	// HSL の輝度を 0–1 で扱う束縛用。中央の三角形パッドの輝度軸が束縛する。
	public double Lightness01
	{
		get => CurrentLightness();
		set => SetLightness(value);
	}




	// HSL の彩度(0–100%)。表示と数値入力に使う。設定時は 0–1 へ直して彩度の本体(HslSaturation01)へ渡す。
	public double HslS
	{
		get => _cachedHslSaturation * 100.0;
		set => HslSaturation01 = value / 100.0;
	}




	// HSL の輝度(0–100%)。表示と数値入力に使う。設定時は 0–1 へ直して輝度の本体(Lightness01)へ渡す。
	public double HslL
	{
		get => CurrentLightness() * 100.0;
		set => Lightness01 = value / 100.0;
	}




	// 中央の三角形パッドの回転角(度)。追従が有効なときは、純色の頂点(未回転では真上)が色相環のつまみ(色相の角度)を向くよう色相と同じ角度にする。無効なときは0。
	public double SlPadRotation => _followHue ? _cachedHue : 0.0;




	// HWB の白み・黒みの三角形パッド(リング内)の回転角(度)。HSL の三角形と同じく純色の頂点が未回転で真上にあるため、追従時は色相と同じ角度にする。無効なときは0。SlPadRotation と同値だが、表色系ごとに束縛先を分けて意味を明確にする。
	public double HwbTrianglePadRotation => _followHue ? _cachedHue : 0.0;




	// 色1(R・G・B)から現在の HSL 輝度(0–1)を求める。最大と最小の平均で、灰でも黒でも白でも定まる。
	private double CurrentLightness()
	{
		double max = Math.Max(_r, Math.Max(_g, _b));
		double min = Math.Min(_r, Math.Min(_g, _b));
		return (max + min) / 2.0 / 255.0;
	}




	// HSL の彩度を設定する。保持した彩度を更新し、色1へ反映する。
	private void SetHslSaturation(double saturation01)
	{
		double clamped = Math.Clamp(saturation01, 0.0, 1.0);

		if (clamped == _cachedHslSaturation)
		{
			return;
		}

		_cachedHslSaturation = clamped;
		OnPropertyChanged(nameof(HslSaturation01));
		OnPropertyChanged(nameof(HslS));
		ApplyHsl();
	}




	// HSL の輝度を設定する。輝度は色1の RGB から導くため、目標の輝度で HSL を RGB へ変換し直して反映する。
	private void SetLightness(double lightness01)
	{
		double clamped = Math.Clamp(lightness01, 0.0, 1.0);

		if (clamped == CurrentLightness())
		{
			return;
		}

		ApplyHsl(clamped);
		OnPropertyChanged(nameof(Lightness01));
		OnPropertyChanged(nameof(HslL));
	}




	// 保持している色相・彩度と、現在(または指定)の輝度から色1の RGB を作り直して反映する。RGB が変わらなければ通知しない。HSL の操作で色1が変わると HSV 側の表示も変わるため、HSV のキャッシュを同期して通知する。
	private void ApplyHsl(double? lightnessOverride = null)
	{
		double lightness = lightnessOverride ?? CurrentLightness();
		(byte r, byte g, byte b) = ColorConversion.HslToRgb(_cachedHue, _cachedHslSaturation, lightness);

		if (r == (byte)_r && g == (byte)_g && b == (byte)_b)
		{
			return;
		}

		RecordContinuousChange();
		_r = r;
		_g = g;
		_b = b;

		OnPropertyChanged(nameof(R));
		OnPropertyChanged(nameof(G));
		OnPropertyChanged(nameof(B));
		NotifyColor1Derived();
		NotifyCmykDerived();
		SyncHsvCacheFromRgb(updateHue: false);
		NotifyHsvDerived();
		NotifyHwbDerived();
		NotifyYuvDerived();
		NotifyLchDerived();
		NotifyLabDerived();
	}




	// 色1の RGB が外部から変わったとき、保持している HSL の彩度を同期する。輝度が中間で彩度が定まる範囲なら更新し、黒・白の極では彩度が定まらないため保つ。色相は HSV 側の同期で共通に扱う。
	private void SyncHslCacheFromRgb()
	{
		(double hue, double saturation, double lightness) = ColorConversion.RgbToHsl((byte)_r, (byte)_g, (byte)_b);
		double denominator = 1.0 - Math.Abs((2.0 * lightness) - 1.0);

		if (denominator > 0.0)
		{
			_cachedHslSaturation = saturation;
		}
	}




	// 色1から導く HSL 系の表示物をまとめて通知する。色1が変わると彩度・輝度とその表示、追従回転が変わりうる。
	private void NotifyHslDerived()
	{
		OnPropertyChanged(nameof(HslSaturation01));
		OnPropertyChanged(nameof(Lightness01));
		OnPropertyChanged(nameof(HslS));
		OnPropertyChanged(nameof(HslL));
		OnPropertyChanged(nameof(SlPadRotation));
	}




	// 白みを 0–1 で扱う束縛用。中央の白み・黒み正方形パッドの横方向が束縛する。白みは色1の最小チャンネルから常に復元できるため保持しない。
	public double Whiteness01
	{
		get => CurrentWhiteness();
		set => SetWhiteness(value);
	}




	// 黒みを 0–1 で扱う束縛用。中央の白み・黒み正方形パッドの縦方向が束縛する。
	public double Blackness01
	{
		get => CurrentBlackness();
		set => SetBlackness(value);
	}




	// 白み(0–100%)。表示と数値入力に使う。設定時は 0–1 へ直して白みの本体(Whiteness01)へ渡す。Blue の B と名前が衝突しないよう接頭辞を付ける。
	public double HwbW
	{
		get => CurrentWhiteness() * 100.0;
		set => Whiteness01 = value / 100.0;
	}




	// 黒み(0–100%)。表示と数値入力に使う。設定時は 0–1 へ直して黒みの本体(Blackness01)へ渡す。
	public double HwbB
	{
		get => CurrentBlackness() * 100.0;
		set => Blackness01 = value / 100.0;
	}




	// 白み・黒みパッドの縦位置(束縛用)。PlanarPad は下端 0・上端 1 のため、黒みが下ほど大きくなるよう「1−黒み」を渡す。上端=黒み0(明るい側)、下端=黒み1(黒)。
	public double HwbPadY
	{
		get => 1.0 - CurrentBlackness();
		set => SetBlackness(1.0 - value);
	}




	// 中央の白み・黒みパッドの回転角(度)。追従が有効なときは、純色の角(白み0・黒み0、未回転では左上=真上から時計回りに 315 度の位置)が色相環のつまみ(色相の角度)を向くよう、色相から 315 度引いた角度にする。無効なときは 0。
	public double HwbPadRotation => _followHue ? _cachedHue - 315.0 : 0.0;




	// HWB の白み+黒みを正規化するか。オン(既定)では和を 1 以内へ畳み、退化域へ入れても正準形(灰)に丸める。オフでは入れた白み・黒みをそのまま保ち、白み+黒み>1 も許す(色は同じ灰だが数値とつまみ位置が忠実)。HWB モードのチェックボックスが束縛する。色や他の表色系には影響しない。
	public bool NormalizeHwb
	{
		get => _normalizeHwb;
		set
		{
			if (_normalizeHwb == value)
			{
				return;
			}

			_normalizeHwb = value;
			OnPropertyChanged(nameof(NormalizeHwb));

			// オンへ切り替えたら、いま退化域に入れている白み・黒みを正準形へ畳んで見せ直す。NotifyHwbDerived は編集中でないため RGB から取り直して和≤1 にする。オフは以後の編集で和>1 を許すだけで、今の表示はそのまま。
			if (_normalizeHwb)
			{
				NotifyHwbDerived();
			}
		}
	}




	// 現在の白み(0–1)。表示・パッド・スライダーが読む。キャッシュを真実とし、正規化オフでは退化域(白み+黒み>1)の値も保つ。
	private double CurrentWhiteness()
	{
		return _cachedWhiteness;
	}




	// 現在の黒み(0–1)。表示・パッド・スライダーが読む。キャッシュを真実とし、正規化オフでは退化域(白み+黒み>1)の値も保つ。
	private double CurrentBlackness()
	{
		return _cachedBlackness;
	}




	// 色1の RGB から白み・黒みのキャッシュを取り直す。白みは最小チャンネル、黒みは最大チャンネルの補数で、和は必ず 1 以内。外部で色が変わったとき(NotifyHwbDerived)と、正規化オンの編集後に正準形へ畳むときに呼ぶ。
	private void SyncHwbCacheFromRgb()
	{
		_cachedWhiteness = Math.Min(_r, Math.Min(_g, _b)) / 255.0;
		_cachedBlackness = 1.0 - (Math.Max(_r, Math.Max(_g, _b)) / 255.0);
	}




	// 白みを設定する。黒みは現在のキャッシュを保ったまま、目標の白みをキャッシュへ入れて色1へ反映する。正規化オフなら和>1 の値も保たれ、オンなら反映後に正準形へ畳まれる。
	private void SetWhiteness(double whiteness01)
	{
		double clamped = Math.Clamp(whiteness01, 0.0, 1.0);

		if (clamped == _cachedWhiteness)
		{
			return;
		}

		_cachedWhiteness = clamped;
		ApplyHwbFromCache();
	}




	// 黒みを設定する。白みは現在のキャッシュを保ったまま、目標の黒みをキャッシュへ入れて色1へ反映する。正規化オフなら和>1 の値も保たれ、オンなら反映後に正準形へ畳まれる。
	private void SetBlackness(double blackness01)
	{
		double clamped = Math.Clamp(blackness01, 0.0, 1.0);

		if (clamped == _cachedBlackness)
		{
			return;
		}

		_cachedBlackness = clamped;
		ApplyHwbFromCache();
	}




	// キャッシュの色相・白み・黒みから色1の RGB を作り直して反映する。編集中フラグを立て、NotifyHwbDerived がキャッシュを RGB から取り直さないようにして、入れた白み・黒み(退化域を含む)を保つ。正規化オンのときは反映後にキャッシュを RGB 由来の和≤1 の正準形へ畳む。白み+黒みが 1 を超える指定は無彩色(灰)になる。HWB の操作で色1が変わると HSV・HSL・YCbCr 側の表示も変わるため、各キャッシュを同期して通知する。色相は HWB では独立に保つため、RGB のバイト丸めで揺らさないよう更新しない。
	private void ApplyHwbFromCache()
	{
		_hwbEditing = true;

		try
		{
			(byte r, byte g, byte b) = ColorConversion.HwbToRgb(_cachedHue, _cachedWhiteness, _cachedBlackness);
			bool changed = !(r == (byte)_r && g == (byte)_g && b == (byte)_b);

			if (changed)
			{
				RecordContinuousChange();
				_r = r;
				_g = g;
				_b = b;
			}

			// 正規化オンのときは、退化域へ入れた白み・黒みを RGB 由来の和≤1 の正準形へ畳む。下流のスライダー背景もこの正準形で描けるよう、色由来の通知より先に行う。
			if (_normalizeHwb)
			{
				SyncHwbCacheFromRgb();
			}

			if (changed)
			{
				OnPropertyChanged(nameof(R));
				OnPropertyChanged(nameof(G));
				OnPropertyChanged(nameof(B));
				NotifyColor1Derived();
				NotifyCmykDerived();
				SyncHsvCacheFromRgb(updateHue: false);
				NotifyHsvDerived();
				SyncHslCacheFromRgb();
				NotifyHslDerived();
				NotifyYuvDerived();
				NotifyLchDerived();
				NotifyLabDerived();
			}

			NotifyHwbDerived();
		}
		finally
		{
			_hwbEditing = false;
		}
	}




	// 色1から導く HWB 系の表示物をまとめて通知する。色1が変わると白み・黒みとその表示、追従回転が変わりうる。HWB 自身の編集中(_hwbEditing)でなければ、外部(他タブ・貼り付け等)で色が変わった通知とみなしてキャッシュを RGB 由来(和≤1)へ取り直す。編集中は入れた白み・黒み(退化域を含む)を保つため取り直さない。
	private void NotifyHwbDerived()
	{
		if (!_hwbEditing)
		{
			SyncHwbCacheFromRgb();
		}

		OnPropertyChanged(nameof(Whiteness01));
		OnPropertyChanged(nameof(Blackness01));
		OnPropertyChanged(nameof(HwbPadY));
		OnPropertyChanged(nameof(HwbW));
		OnPropertyChanged(nameof(HwbB));
		OnPropertyChanged(nameof(HwbPadRotation));
		OnPropertyChanged(nameof(HwbTrianglePadRotation));
	}




	// 現在の LCH の表色系。副モードの位置(_lchSpaceIndex)から決める。各変換・色域判定はこの種別で切り替える。
	private LchSpace LchColorSpace => _lchSpaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;




	// LCH タブの副モードの位置 (0=OKLCH, 1=CIE LCH)。タブのラジオが束縛し、保存して次回起動へ引き継ぐ。色1の RGB は不変で、明度・彩度・色相の読み方(尺度と知覚均等性)だけが変わる。副モードを変えたらキャッシュを RGB から新しい表色系で取り直す。
	public int LchSpaceIndex
	{
		get => _lchSpaceIndex;
		set
		{
			int clamped = value == 1 ? 1 : 0;

			if (_lchSpaceIndex == clamped)
			{
				return;
			}

			_lchSpaceIndex = clamped;
			SyncLchCacheFromRgb();
			OnPropertyChanged(nameof(LchSpaceIndex));
			OnPropertyChanged(nameof(LchChromaFormatter));
			OnPropertyChanged(nameof(LchChromaStep));
			OnPropertyChanged(nameof(LchChromaNormStep));
			OnPropertyChanged(nameof(LchChromaNormLargeStep));
			NotifyLchComponents();
			NotifyLchTrackBrushes();
		}
	}




	// LCH の彩度を sRGB 色域へ制限するか。オフ(既定)では色域外の彩度もそのまま保ち、表示できない範囲をスライダー上にハッチで可視化する。オンでは彩度を色域境界へクランプする。色1の RGB は表示できる色しか持てないため、この設定はキャッシュが色域外の値を保つかどうかだけを変える。
	public bool GamutLimit
	{
		get => _lchGamutLimit;
		set
		{
			if (_lchGamutLimit == value)
			{
				return;
			}

			_lchGamutLimit = value;
			OnPropertyChanged(nameof(GamutLimit));

			// オンへ切り替えたら、いま色域外に入れている彩度を境界へクランプして見せ直す。NotifyLchDerived は編集中でないため RGB から取り直して色域内へ収める。オフは以後の編集で色域外を許すだけで、今の表示はそのまま。
			if (_lchGamutLimit)
			{
				NotifyLchDerived();
			}
		}
	}




	// 明度 L。0–100 の共通尺度で表示・数値入力に使う。OKLCH の素の L(0–1)・CIE LCH の素の L(0–100)を、どちらも 0–100 へそろえて見せる。設定時は素の尺度へ戻して反映する。
	public double LchL
	{
		get => _cachedLchL * 100.0 / LchColor.LMax(LchColorSpace);
		set => SetLchLightness(value);
	}




	// 彩度 C。表色系の素の尺度(OKLCH は 0–0.4 程度、CIE LCH は 0–150 程度)で表示・数値入力に使う。
	public double LchC
	{
		get => _cachedLchChroma;
		set => SetLchChroma(value);
	}




	// 色相 H(度, 0–360)。色1が無彩色で色相が定まらない間も、利用者が入れた色相を保つため内部に保持した値を返す。
	public double LchH
	{
		get => _cachedLchHue;
		set => SetLchHue(value);
	}




	// 明度を 0–1 で扱う束縛用。スライダーは表色系で上限が変わらないよう正規化値で束縛し、ガモット可視化の区間(0–1)・トラック背景(0–1)と尺度をそろえる。素の上限が表色系で変わる彩度スライダーで、上限の双方向束縛が値を強制クランプして壊すのを避けるため、明度・色相も同じく正規化で束縛する。
	public double LchLNorm
	{
		get => _cachedLchL / LchColor.LMax(LchColorSpace);
		set => SetLchLightness(Math.Clamp(value, 0.0, 1.0) * 100.0);
	}




	// 彩度を 0–1 で扱う束縛用。1 は現在の表色系の表示上限(CMax)に対応する。下段の彩度スライダーが束縛する全域の尺度で、L-C パッドのつまみ(LchPadCNorm)とは別に常に CMax を基準にする。
	public double LchCNorm
	{
		get => _cachedLchChroma / LchColor.CMax(LchColorSpace);
		set => SetLchChroma(Math.Clamp(value, 0.0, 1.0) * LchColor.CMax(LchColorSpace));
	}




	// L-C 平面(色相リング+平面・C×L 平面+色相バー)で彩度軸を色域へ詰めて表示するか。オン時はその色相の cusp 彩度まで彩度軸を縮め、色域を彩度方向へパッドいっぱいに広げる。L-C 平面の下地・つまみ位置(LchPadCNorm)・パッド操作(SetLchPad)が彩度軸の上限(CurrentChromaAxisMax)を介して座標と値を読み替える。スケールが変わるとつまみ位置の正規化も変わるため通知し直す。色1の RGB は不変。
	public bool LchChromaFit
	{
		get => _lchChromaFit;
		set
		{
			if (_lchChromaFit == value)
			{
				return;
			}

			_lchChromaFit = value;
			OnPropertyChanged(nameof(LchChromaFit));
			OnPropertyChanged(nameof(LchPadCNorm));
			OnPropertyChanged(nameof(LchHueChromaCNorm));
		}
	}




	// 現在の副モード・色相・彩度フィットの有無で求めた L-C 平面の彩度軸の表示上限の覚え書き。下地の生成・つまみ位置の正規化(LchPadCNorm)・パッド操作の値の読み替え(SetLchPad)で同じ上限を使うため、色相ドラッグ中に cusp 探索を毎回やり直さずに済むよう、鍵(副モード・色相・フィット)が変わらない限り使い回す。
	private LchSpace _chromaAxisSpace = (LchSpace)(-1);
	private double _chromaAxisHue = double.NaN;
	private bool _chromaAxisFit;
	private bool _chromaAxisValid;
	private double _chromaAxisCache;




	// 現在の副モード・色相・彩度フィットの有無に対応する L-C 平面の彩度軸の表示上限を返す。鍵が変わらなければ覚えた値をそのまま返し、変わったときだけ求め直す。
	private double CurrentChromaAxisMax()
	{
		LchSpace space = LchColorSpace;

		if (_chromaAxisValid && space == _chromaAxisSpace && _cachedLchHue == _chromaAxisHue && _lchChromaFit == _chromaAxisFit)
		{
			return _chromaAxisCache;
		}

		_chromaAxisCache = LchColor.ChromaAxisMax(space, _cachedLchHue, _lchChromaFit);
		_chromaAxisSpace = space;
		_chromaAxisHue = _cachedLchHue;
		_chromaAxisFit = _lchChromaFit;
		_chromaAxisValid = true;
		return _chromaAxisCache;
	}




	// L-C パッドのつまみの彩度位置(0–1)。下段の彩度スライダー(常に CMax 基準の LchCNorm)と違い、パッドは彩度軸の表示上限(CurrentChromaAxisMax)を介して読む。フィット無効のときは LchCNorm と一致し、有効のときはその色相の cusp を 1 とする尺度に追従する。彩度軸の上限は色相で変わるため、色相が変わるとつまみ位置も移る。読み取り専用(操作の反映は SetLchPad)。
	public double LchPadCNorm
	{
		get
		{
			double axisMax = CurrentChromaAxisMax();
			return axisMax <= 0.0 ? 0.0 : Math.Clamp(_cachedLchChroma / axisMax, 0.0, 1.0);
		}
	}




	// 現在の副モード・明度・彩度フィットの有無で求めた色相×彩度の平面・円盤の彩度軸(円盤では半径)の表示上限の覚え書き。下地の生成・つまみ位置の正規化(LchHueChromaCNorm)・パッド/円盤操作の値の読み替え(SetLchHueChroma)で同じ上限を使うため、明度ドラッグ中に全色相の最大彩度探索を毎回やり直さずに済むよう、鍵(副モード・明度・フィット)が変わらない限り使い回す。固定成分が明度のため、固定成分が色相の L-C 平面(CurrentChromaAxisMax)とは別に持つ。
	private LchSpace _chromaAtLSpace = (LchSpace)(-1);
	private double _chromaAtLLightness = double.NaN;
	private bool _chromaAtLFit;
	private bool _chromaAtLValid;
	private double _chromaAtLCache;




	// 現在の副モード・明度・彩度フィットの有無に対応する色相×彩度の彩度軸の表示上限を返す。鍵が変わらなければ覚えた値をそのまま返し、変わったときだけ求め直す。
	private double CurrentChromaAxisMaxAtL()
	{
		LchSpace space = LchColorSpace;

		if (_chromaAtLValid && space == _chromaAtLSpace && _cachedLchL == _chromaAtLLightness && _lchChromaFit == _chromaAtLFit)
		{
			return _chromaAtLCache;
		}

		_chromaAtLCache = LchColor.ChromaAxisMaxAtLightness(space, _cachedLchL, _lchChromaFit);
		_chromaAtLSpace = space;
		_chromaAtLLightness = _cachedLchL;
		_chromaAtLFit = _lchChromaFit;
		_chromaAtLValid = true;
		return _chromaAtLCache;
	}




	// 色相×彩度の平面・円盤のつまみの彩度位置(平面は縦、円盤は半径)を 0–1 で扱う束縛・表示用。下段の彩度スライダー(常に CMax 基準の LchCNorm)と違い、彩度軸の表示上限(CurrentChromaAxisMaxAtL)を介して読む。フィット無効のときは LchCNorm と一致し、有効のときはその明度で全色相を通じた最大彩度を 1 とする尺度に追従する。彩度軸の上限は明度で変わるため、明度が変わるとつまみ位置も移る。読み取り専用(操作の反映は SetLchHueChroma)。
	public double LchHueChromaCNorm
	{
		get
		{
			double axisMax = CurrentChromaAxisMaxAtL();
			return axisMax <= 0.0 ? 0.0 : Math.Clamp(_cachedLchChroma / axisMax, 0.0, 1.0);
		}
	}




	// 色相を 0–1 で扱う束縛用。1 は 360 度に対応する。
	public double LchHNorm
	{
		get => _cachedLchHue / 360.0;
		set => SetLchHue(Math.Clamp(value, 0.0, 1.0) * 360.0);
	}




	// 彩度の数値入力欄の表示書式。OKLCH の小さな値は3桁、CIE LCH の大きな値は2桁の小数で見せる。
	public Windows.Globalization.NumberFormatting.DecimalFormatter LchChromaFormatter => LchColorSpace == LchSpace.Oklch ? NumberFormatters.ThreeDecimal : NumberFormatters.TwoDecimal;




	// 彩度の数値入力欄のスピンボタンの刻み。素の尺度が桁違いに異なるため表色系で変える(OKLCH は 0.01、CIE LCH は 1)。値の範囲そのものではないため、双方向束縛で値を壊すことはない。
	public double LchChromaStep => LchColorSpace == LchSpace.Oklch ? 0.01 : 1.0;




	// 彩度スライダーの矢印キーの刻み。スライダーは正規化値(0–1)で束縛するため、素の尺度の刻み(OKLCH は 0.005、CIE LCH は 1)を表示上限(CMax)で割って正規化して渡す。
	public double LchChromaNormStep => (LchColorSpace == LchSpace.Oklch ? 0.005 : 1.0) / LchColor.CMax(LchColorSpace);




	// 彩度スライダーの Page 移動量。素の尺度の移動量(OKLCH は 0.04、CIE LCH は 10)を表示上限(CMax)で割って正規化して渡す。
	public double LchChromaNormLargeStep => (LchColorSpace == LchSpace.Oklch ? 0.04 : 10.0) / LchColor.CMax(LchColorSpace);




	// 明度スライダーの背景。明度を 0→最大に振った色変化を示す。ShowActualColor が真なら現在の彩度・色相のもとで描き、偽なら彩度を 0 に固定して現在色には依らない黒→白のグレースケールにする。色域外の明度は色域内へ収めた色で描き、表示できない範囲は LchLightnessGamut が示す。明度に対して RGB は非線形のため複数の刻みで標本化する。
	public Brush LchLightnessTrackBrush
	{
		get
		{
			LchSpace space = LchColorSpace;
			double lMax = LchColor.LMax(space);
			double chroma = _showActualColor ? _cachedLchChroma : 0.0;
			double hue = _showActualColor ? _cachedLchHue : 0.0;
			return MakeTrackBrush(t => LchColor.ToRgb(space, t * lMax, chroma, hue), new Point(0.0, 0.5), new Point(1.0, 0.5), 16);
		}
	}




	// 彩度スライダーの背景。現在の色相を保ったまま彩度を 0→最大に振った色変化(無彩色→鮮やか)を示す。ShowActualColor が真なら現在の明度のもとで描き、偽なら明度を中間(最大の半分)に固定して現在の明度には依らない基準にする。彩度は中間明度のとき最も伸びる。色域を外れた先は色域内へ収めた色になり、その範囲は LchChromaGamut が示す。
	public Brush LchChromaTrackBrush
	{
		get
		{
			LchSpace space = LchColorSpace;
			double cMax = LchColor.CMax(space);
			double lightness = _showActualColor ? _cachedLchL : LchColor.LMax(space) * 0.5;
			return MakeTrackBrush(t => LchColor.ToRgb(space, lightness, t * cMax, _cachedLchHue), new Point(0.0, 0.5), new Point(1.0, 0.5), 16);
		}
	}




	// 色相スライダーで現在色に依らない基準を描くときの明度と彩度。明度は中間(最大の半分)に置き、彩度はその明度で全色相が sRGB 色域に収まる最大値を取って色相環の全域を色域内に収める。色域外区間の判定(ComputeGamutSegments)は色相を 64 分割で標本化するため、その間の色相でわずかに色域を外れてハッチが出ないよう、最大値に小さな余裕を見て少し内側へ寄せる。
	private (double Lightness, double Chroma) LchHueBaseline()
	{
		LchSpace space = LchColorSpace;
		double lightness = LchColor.LMax(space) * 0.5;
		double chroma = LchColor.MaxChromaForAllHues(space, lightness) * 0.98;
		return (lightness, chroma);
	}




	// 色相スライダーの背景。色相を 0→360 に振った色変化を示す。ShowActualColor が真なら現在の明度・彩度のもとで描き、明度・彩度が高いと多くの色相で色域を外れるため表示できない範囲は LchHueGamut が示す。偽なら中間明度で全色相が色域内に収まる基準彩度に固定し、現在色には依らず色域外も出ない鮮やかな色相環にする。色相一周は非線形のため細かく標本化する。
	public Brush LchHueTrackBrush
	{
		get
		{
			LchSpace space = LchColorSpace;
			double lightness = _cachedLchL;
			double chroma = _cachedLchChroma;

			if (!_showActualColor)
			{
				(lightness, chroma) = LchHueBaseline();
			}

			return MakeTrackBrush(t => LchColor.ToRgb(space, lightness, chroma, t * 360.0), new Point(0.0, 0.5), new Point(1.0, 0.5), 48);
		}
	}




	// 明度スライダー(縦向き)の背景。LchLightnessTrackBrush と同じ色変化を、下端=明度 0・上端=最大の縦のグラデーションで描く。見せ方が円盤・直交パッドのとき、切り出した明度の縦スライダーが使う。
	public Brush LchLightnessTrackBrushVertical
	{
		get
		{
			LchSpace space = LchColorSpace;
			double lMax = LchColor.LMax(space);
			double chroma = _showActualColor ? _cachedLchChroma : 0.0;
			double hue = _showActualColor ? _cachedLchHue : 0.0;
			return MakeTrackBrush(t => LchColor.ToRgb(space, t * lMax, chroma, hue), new Point(0.5, 1.0), new Point(0.5, 0.0), 16);
		}
	}




	// 彩度スライダー(縦向き)の背景。LchChromaTrackBrush と同じ色変化を、下端=彩度 0・上端=表示上限の縦のグラデーションで描く。見せ方が円盤・直交パッドのとき、切り出した彩度の縦スライダーが使う。
	public Brush LchChromaTrackBrushVertical
	{
		get
		{
			LchSpace space = LchColorSpace;
			double cMax = LchColor.CMax(space);
			double lightness = _showActualColor ? _cachedLchL : LchColor.LMax(space) * 0.5;
			return MakeTrackBrush(t => LchColor.ToRgb(space, lightness, t * cMax, _cachedLchHue), new Point(0.5, 1.0), new Point(0.5, 0.0), 16);
		}
	}




	// 色相スライダー(縦向き)の背景。LchHueTrackBrush と同じ色変化を、下端=色相 0 度・上端=360 度の縦のグラデーションで描く。見せ方が彩度×明度の平面+色相の縦スライダーのとき、切り出した色相の縦スライダーが使う。
	public Brush LchHueTrackBrushVertical
	{
		get
		{
			LchSpace space = LchColorSpace;
			double lightness = _cachedLchL;
			double chroma = _cachedLchChroma;

			if (!_showActualColor)
			{
				(lightness, chroma) = LchHueBaseline();
			}

			return MakeTrackBrush(t => LchColor.ToRgb(space, lightness, chroma, t * 360.0), new Point(0.5, 1.0), new Point(0.5, 0.0), 48);
		}
	}




	// 明度スライダーで sRGB 色域を外れる区間。背景と同じ基準のもとで、明度を端から端まで振ったときの色域外の範囲を示す。ShowActualColor が偽のときは背景が彩度 0 のグレースケールで全域色域内のため、区間は出ない。
	public IReadOnlyList<GamutSegment> LchLightnessGamut
	{
		get
		{
			LchSpace space = LchColorSpace;
			double lMax = LchColor.LMax(space);
			double chroma = _showActualColor ? _cachedLchChroma : 0.0;
			double hue = _showActualColor ? _cachedLchHue : 0.0;
			return ComputeGamutSegments(t => LchColor.InGamut(space, t * lMax, chroma, hue));
		}
	}




	// 彩度スライダーで sRGB 色域を外れる区間。背景と同じ基準のもとで、彩度を 0 から上限まで振ったときの色域外の範囲を示す。ShowActualColor が偽のときは明度を中間に固定して判定する。
	public IReadOnlyList<GamutSegment> LchChromaGamut
	{
		get
		{
			LchSpace space = LchColorSpace;
			double cMax = LchColor.CMax(space);
			double lightness = _showActualColor ? _cachedLchL : LchColor.LMax(space) * 0.5;
			return ComputeGamutSegments(t => LchColor.InGamut(space, lightness, t * cMax, _cachedLchHue));
		}
	}




	// 色相スライダーで sRGB 色域を外れる区間。背景と同じ基準のもとで、色相を一周させたときの色域外の範囲を示す。ShowActualColor が偽のときは中間明度で全色相が色域内に収まる基準彩度を使うため、区間は出ない。
	public IReadOnlyList<GamutSegment> LchHueGamut
	{
		get
		{
			LchSpace space = LchColorSpace;
			double lightness = _cachedLchL;
			double chroma = _cachedLchChroma;

			if (!_showActualColor)
			{
				(lightness, chroma) = LchHueBaseline();
			}

			return ComputeGamutSegments(t => LchColor.InGamut(space, lightness, chroma, t * 360.0));
		}
	}




	// 明度を設定する。現在の彩度・色相を保ったまま、目標の明度をキャッシュへ入れて色1へ反映する。表示尺度(0–100)を素の尺度へ戻す。適用中(ApplyLchFromCache)の通知がスライダー・パッドの双方向束縛を介して再入したときは、正規化値の往復で生じる微小な誤差で再適用が連鎖しないよう無視する。
	private void SetLchLightness(double displayValue)
	{
		if (_lchEditing)
		{
			return;
		}

		double native = Math.Clamp(displayValue, 0.0, 100.0) * LchColor.LMax(LchColorSpace) / 100.0;

		if (native == _cachedLchL)
		{
			return;
		}

		_cachedLchL = native;
		ApplyLchFromCache();
	}




	// 彩度を設定する。現在の明度・色相を保ったまま、目標の彩度をキャッシュへ入れて色1へ反映する。色域制限オンなら反映後に色域境界へ畳まれ、オフなら色域外の値も保たれる。適用中(ApplyLchFromCache)の通知がスライダー・パッドの双方向束縛を介して再入したときは、正規化値の往復で生じる微小な誤差で再適用が連鎖しないよう無視する。これがないと色域制限オンで色域境界へ畳むたびに書き戻りが再適用を呼び、無限再帰でスタックを食い潰す。
	private void SetLchChroma(double value)
	{
		if (_lchEditing)
		{
			return;
		}

		double clamped = Math.Clamp(value, 0.0, LchColor.CMax(LchColorSpace));

		if (clamped == _cachedLchChroma)
		{
			return;
		}

		_cachedLchChroma = clamped;
		ApplyLchFromCache();
	}




	// 色相を設定する。現在の明度・彩度を保ったまま、目標の色相をキャッシュへ入れて色1へ反映する。適用中(ApplyLchFromCache)の通知がリング・スライダーの双方向束縛を介して再入したときは、正規化値の往復で生じる微小な誤差で再適用が連鎖しないよう無視する。
	private void SetLchHue(double value)
	{
		if (_lchEditing)
		{
			return;
		}

		double normalized = ((value % 360.0) + 360.0) % 360.0;

		if (normalized == _cachedLchHue)
		{
			return;
		}

		_cachedLchHue = normalized;
		ApplyLchFromCache();
	}




	// L-C パッドの操作で明度と彩度を同時に設定する。色域制限オンのときは、明度・色相を保って彩度だけを詰めるのではなく、カーソル位置(明度・彩度)に最も近い色域内の点へ二次元で寄せて、つまみを色域の縁へ滑らかに沿わせる。横方向に押し込んでもつまみが境界に貼り付いて止まる感触を和らげる。オフのときは色域外もそのまま受け、つまみをカーソルへ追従させる。色相はパッドでは変えないため保つ。値は正規化(0–1)で受け取る。
	public void SetLchPad(double chromaNorm, double lightnessNorm)
	{
		if (_lchEditing)
		{
			return;
		}

		LchSpace space = LchColorSpace;
		double l = Math.Clamp(lightnessNorm, 0.0, 1.0) * LchColor.LMax(space);

		// 彩度は彩度軸の表示上限(CurrentChromaAxisMax)を介して素の値へ戻す。フィット無効のときは CMax、有効のときはその色相の cusp 彩度を 1 とする尺度になる。
		double c = Math.Clamp(chromaNorm, 0.0, 1.0) * CurrentChromaAxisMax();

		if (_lchGamutLimit)
		{
			(l, c) = LchColor.NearestInGamut(space, l, c, _cachedLchHue);
		}

		if (l == _cachedLchL && c == _cachedLchChroma)
		{
			return;
		}

		_cachedLchL = l;
		_cachedLchChroma = c;
		ApplyLchFromCache();
	}




	// 色相×明度の2次元コントロール(色相×明度の平面、半径=明度の円盤)の操作で、色相と明度を同時に設定する。彩度は固定成分(縦スライダー)が司るため現在値を保つ。色域制限オンのときは、その明度・色相で sRGB 色域に収まる最大彩度まで彩度を詰めて色域内へ収める(明度・色相は動かさない)。オフのときは色域外もそのまま受け、つまみをカーソルへ追従させる。値は正規化(0–1)で受け取る。
	public void SetLchHueLightness(double hueNorm, double lightnessNorm)
	{
		if (_lchEditing)
		{
			return;
		}

		LchSpace space = LchColorSpace;
		double h = Math.Clamp(hueNorm, 0.0, 1.0) * 360.0;
		double l = Math.Clamp(lightnessNorm, 0.0, 1.0) * LchColor.LMax(space);
		double c = _cachedLchChroma;

		if (_lchGamutLimit)
		{
			c = Math.Min(c, LchColor.MaxChroma(space, l, h));
		}

		if (l == _cachedLchL && c == _cachedLchChroma && h == _cachedLchHue)
		{
			return;
		}

		_cachedLchL = l;
		_cachedLchChroma = c;
		_cachedLchHue = h;
		ApplyLchFromCache();
	}




	// 色相×彩度の2次元コントロール(色相×彩度の平面、半径=彩度の円盤)の操作で、色相と彩度を同時に設定する。明度は固定成分(縦スライダー)が司るため現在値を保つ。色域制限オンのときは、その明度・色相で sRGB 色域に収まる最大彩度まで彩度を詰めて色域内へ収める。オフのときは色域外もそのまま受け、つまみをカーソルへ追従させる。値は正規化(0–1)で受け取る。
	public void SetLchHueChroma(double hueNorm, double chromaNorm)
	{
		if (_lchEditing)
		{
			return;
		}

		LchSpace space = LchColorSpace;
		double h = Math.Clamp(hueNorm, 0.0, 1.0) * 360.0;

		// 彩度は彩度軸の表示上限(CurrentChromaAxisMaxAtL)を介して素の値へ戻す。フィット無効のときは CMax、有効のときはその明度で全色相を通じた最大彩度を 1 とする尺度になる。
		double c = Math.Clamp(chromaNorm, 0.0, 1.0) * CurrentChromaAxisMaxAtL();
		double l = _cachedLchL;

		if (_lchGamutLimit)
		{
			c = Math.Min(c, LchColor.MaxChroma(space, l, h));
		}

		if (l == _cachedLchL && c == _cachedLchChroma && h == _cachedLchHue)
		{
			return;
		}

		_cachedLchL = l;
		_cachedLchChroma = c;
		_cachedLchHue = h;
		ApplyLchFromCache();
	}




	// キャッシュの明度・彩度・色相から色1の RGB を作り直して反映する。色域外の指定は明度・色相を保ったまま彩度を詰めて色域内へ収める。編集中フラグを立て、NotifyLchDerived がキャッシュを RGB から取り直さないようにして、入れた値(色域外を含む)を保つ。色域制限オンのときは反映後にキャッシュを RGB 由来の色域内の値へ畳む。LCH の操作で色1が変わると HSV・HSL・HWB・YCbCr 側の表示も変わるため、各キャッシュを同期して通知する。
	private void ApplyLchFromCache()
	{
		_lchEditing = true;

		try
		{
			Color color = LchColor.ToRgb(LchColorSpace, _cachedLchL, _cachedLchChroma, _cachedLchHue);
			bool changed = !(color.R == (byte)_r && color.G == (byte)_g && color.B == (byte)_b);

			if (changed)
			{
				RecordContinuousChange();
				_r = color.R;
				_g = color.G;
				_b = color.B;
			}

			// 色域制限オンのときは、色域外へ入れた彩度を RGB 由来の色域内の値へ畳む。下流のスライダー背景もこの値で描けるよう、色由来の通知より先に行う。
			if (_lchGamutLimit)
			{
				SyncLchCacheFromRgb();
			}

			if (changed)
			{
				OnPropertyChanged(nameof(R));
				OnPropertyChanged(nameof(G));
				OnPropertyChanged(nameof(B));
				NotifyColor1Derived();
				NotifyCmykDerived();
				SyncHsvCacheFromRgb();
				NotifyHsvDerived();
				SyncHslCacheFromRgb();
				NotifyHslDerived();
				NotifyHwbDerived();
				NotifyYuvDerived();
				NotifyLabDerived();
			}

			NotifyLchDerived();
		}
		finally
		{
			_lchEditing = false;
		}
	}




	// 色1の RGB からキャッシュの明度・彩度・色相を取り直す。色相は彩度が 0(無彩色)のとき定まらないため保つ。副モードの切り替え・色域制限のオン・外部での色変更(NotifyLchDerived)で呼ぶ。
	private void SyncLchCacheFromRgb()
	{
		(double l, double c, double h) = LchColor.FromRgb(LchColorSpace, (byte)_r, (byte)_g, (byte)_b);
		_cachedLchL = l;
		_cachedLchChroma = c;

		if (c > 1e-6)
		{
			_cachedLchHue = h;
		}
	}




	// 色1から導く LCH 系の表示物をまとめて通知する。LCH 自身の編集中(_lchEditing)でなければ、外部(他タブ・貼り付け等)で色が変わった通知とみなしてキャッシュを RGB 由来へ取り直す。編集中は入れた値(色域外を含む)を保つため取り直さない。
	private void NotifyLchDerived()
	{
		if (!_lchEditing)
		{
			SyncLchCacheFromRgb();
		}

		NotifyLchComponents();
		NotifyLchTrackBrushes();
	}




	// 明度・彩度・色相の数値表示と、スライダーが束縛する正規化値をまとめて通知する。
	private void NotifyLchComponents()
	{
		OnPropertyChanged(nameof(LchL));
		OnPropertyChanged(nameof(LchC));
		OnPropertyChanged(nameof(LchH));
		OnPropertyChanged(nameof(LchLNorm));
		OnPropertyChanged(nameof(LchCNorm));
		OnPropertyChanged(nameof(LchHNorm));

		// L-C パッドのつまみの彩度位置は彩度軸の表示上限を介して読むため、彩度・色相のいずれが変わっても通知し直す(色相が変わると cusp が変わり、彩度が同じでもつまみが移る)。色相×彩度のつまみは明度ごとの最大彩度を介すため、明度が変わってもつまみが移る。
		OnPropertyChanged(nameof(LchPadCNorm));
		OnPropertyChanged(nameof(LchHueChromaCNorm));
	}




	// LCH スライダーの背景と色域外区間をまとめて通知する。背景は現在の明度・彩度・色相から、色域外区間は同じ基準から導かれるため、色1が変わるたびに作り直す。
	private void NotifyLchTrackBrushes()
	{
		OnPropertyChanged(nameof(LchLightnessTrackBrush));
		OnPropertyChanged(nameof(LchChromaTrackBrush));
		OnPropertyChanged(nameof(LchHueTrackBrush));
		OnPropertyChanged(nameof(LchLightnessTrackBrushVertical));
		OnPropertyChanged(nameof(LchChromaTrackBrushVertical));
		OnPropertyChanged(nameof(LchHueTrackBrushVertical));
		OnPropertyChanged(nameof(LchLightnessGamut));
		OnPropertyChanged(nameof(LchChromaGamut));
		OnPropertyChanged(nameof(LchHueGamut));
	}




	// 現在の Lab の表色系。副モードの位置(_labSpaceIndex)から決める。各変換・色域判定はこの種別で切り替える。表色系の種別は Lab 平面を共有する LchSpace で表す。
	private LchSpace LabColorSpace => _labSpaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;




	// Lab タブの副モードの位置 (0=OKLab, 1=CIE Lab)。タブのラジオが束縛し、保存して次回起動へ引き継ぐ。色1の RGB は不変で、明度・a・b の読み方(尺度と知覚均等性)だけが変わる。副モードを変えたらキャッシュを RGB から新しい表色系で取り直す。
	public int LabSpaceIndex
	{
		get => _labSpaceIndex;
		set
		{
			int clamped = value == 1 ? 1 : 0;

			if (_labSpaceIndex == clamped)
			{
				return;
			}

			_labSpaceIndex = clamped;
			SyncLabCacheFromRgb();
			OnPropertyChanged(nameof(LabSpaceIndex));
			OnPropertyChanged(nameof(LabAbFormatter));
			OnPropertyChanged(nameof(LabAbStep));
			OnPropertyChanged(nameof(LabAbNormStep));
			OnPropertyChanged(nameof(LabAbNormLargeStep));
			NotifyLabComponents();
			NotifyLabTrackBrushes();
		}
	}




	// Lab の a・b を sRGB 色域へ制限するか。オフ(既定)では色域外の値もそのまま保ち、表示できない範囲をスライダー・パッド上にハッチで可視化する。オンでは a・b を色相(a:b の比)を保ったまま色域境界へクランプする。色1の RGB は表示できる色しか持てないため、この設定はキャッシュが色域外の値を保つかどうかだけを変える。
	public bool LabGamutLimit
	{
		get => _labGamutLimit;
		set
		{
			if (_labGamutLimit == value)
			{
				return;
			}

			_labGamutLimit = value;
			OnPropertyChanged(nameof(LabGamutLimit));

			// オンへ切り替えたら、いま色域外に入れている a・b を境界へクランプして見せ直す。NotifyLabDerived は編集中でないため RGB から取り直して色域内へ収める。オフは以後の編集で色域外を許すだけで、今の表示はそのまま。
			if (_labGamutLimit)
			{
				NotifyLabDerived();
			}
		}
	}




	// 明度 L。0–100 の共通尺度で表示・数値入力に使う。OKLab の素の L(0–1)・CIE Lab の素の L(0–100)を、どちらも 0–100 へそろえて見せる。設定時は素の尺度へ戻して反映する。
	public double LabL
	{
		get => _cachedLabL * 100.0 / LabColor.LMax(LabColorSpace);
		set => SetLabLightness(value);
	}




	// a 軸。表色系の素の尺度(OKLab は ±0.4 程度、CIE Lab は ±125 程度)で表示・数値入力に使う。正が赤寄り、負が緑寄り。
	public double LabA
	{
		get => _cachedLabA;
		set => SetLabA(value);
	}




	// b 軸。表色系の素の尺度で表示・数値入力に使う。正が黄寄り、負が青寄り。
	public double LabB
	{
		get => _cachedLabB;
		set => SetLabB(value);
	}




	// 明度を 0–1 で扱う束縛用。スライダーは表色系で上限が変わらないよう正規化値で束縛し、ガモット可視化の区間(0–1)・トラック背景(0–1)と尺度をそろえる。
	public double LabLNorm
	{
		get => _cachedLabL / LabColor.LMax(LabColorSpace);
		set => SetLabLightness(Math.Clamp(value, 0.0, 1.0) * 100.0);
	}




	// a 軸を 0–1 で扱う束縛用。0 が −上限、0.5 が 0、1 が +上限に対応する。
	public double LabANorm
	{
		get => (_cachedLabA / LabColor.AbMax(LabColorSpace) / 2.0) + 0.5;
		set => SetLabA(((Math.Clamp(value, 0.0, 1.0) * 2.0) - 1.0) * LabColor.AbMax(LabColorSpace));
	}




	// b 軸を 0–1 で扱う束縛用。0 が −上限、0.5 が 0、1 が +上限に対応する。
	public double LabBNorm
	{
		get => (_cachedLabB / LabColor.AbMax(LabColorSpace) / 2.0) + 0.5;
		set => SetLabB(((Math.Clamp(value, 0.0, 1.0) * 2.0) - 1.0) * LabColor.AbMax(LabColorSpace));
	}




	// a×b パッドのつまみの横位置(0–1)。下段の a スライダー(常に ±AbMax の固定尺度)と違い、パッドは表示枠(CurrentAbExtent)を介して読む。固定枠のときは LabANorm と一致し、フィットのときは枠の縮尺・中心に追従する。枠は明度で変わるため、明度が変わるとつまみ位置も移る。読み取り専用(操作の反映は SetLabPad)。
	public double LabPadANorm
	{
		get
		{
			PlaneExtent extent = CurrentAbExtent();
			return extent.XWidth <= 0.0 ? 0.5 : Math.Clamp((_cachedLabA - extent.XMin) / extent.XWidth, 0.0, 1.0);
		}
	}




	// a×b パッドのつまみの縦位置(0–1)。0 が枠の下端(b の下限)、1 が上端(上限)。横位置(LabPadANorm)と同じく表示枠を介して読む。
	public double LabPadBNorm
	{
		get
		{
			PlaneExtent extent = CurrentAbExtent();
			return extent.YHeight <= 0.0 ? 0.5 : Math.Clamp((_cachedLabB - extent.YMin) / extent.YHeight, 0.0, 1.0);
		}
	}




	// a・b の数値入力欄の表示書式。OKLab の小さな値は3桁、CIE Lab の大きな値は2桁の小数で見せる。
	public Windows.Globalization.NumberFormatting.DecimalFormatter LabAbFormatter => LabColorSpace == LchSpace.Oklch ? NumberFormatters.ThreeDecimal : NumberFormatters.TwoDecimal;




	// a・b の数値入力欄のスピンボタンの刻み。素の尺度が桁違いに異なるため表色系で変える(OKLab は 0.01、CIE Lab は 1)。値の範囲そのものではないため、双方向束縛で値を壊すことはない。
	public double LabAbStep => LabColorSpace == LchSpace.Oklch ? 0.01 : 1.0;




	// a・b スライダーの矢印キーの刻み。スライダーは正規化値(0–1、0.5 が 0)で束縛するため、素の尺度の刻み(OKLab は 0.005、CIE Lab は 1)を a・b の全幅(±上限ぶんで 2×AbMax)で割って正規化して渡す。a・b で尺度が同じため共用する。
	public double LabAbNormStep => (LabColorSpace == LchSpace.Oklch ? 0.005 : 1.0) / (2.0 * LabColor.AbMax(LabColorSpace));




	// a・b スライダーの Page 移動量。素の尺度の移動量(OKLab は 0.04、CIE Lab は 10)を a・b の全幅(2×AbMax)で割って正規化して渡す。
	public double LabAbNormLargeStep => (LabColorSpace == LchSpace.Oklch ? 0.04 : 10.0) / (2.0 * LabColor.AbMax(LabColorSpace));




	// 明度スライダーの背景。明度を 0→最大に振った色変化を示す。ShowActualColor が真なら現在の a・b のもとで描き、偽なら a・b を 0 に固定して現在色には依らない黒→白のグレースケールにする。色域外の明度は色域内へ収めた色で描き、表示できない範囲は LabLightnessGamut が示す。明度に対して RGB は非線形のため複数の刻みで標本化する。下段の水平スライダー用に左→右のグラデーションで作る。
	public Brush LabLightnessTrackBrush
	{
		get
		{
			LchSpace space = LabColorSpace;
			double lMax = LabColor.LMax(space);
			double aAxis = _showActualColor ? _cachedLabA : 0.0;
			double bAxis = _showActualColor ? _cachedLabB : 0.0;
			return MakeTrackBrush(t => LabColor.ToRgb(space, t * lMax, aAxis, bAxis), new Point(0.0, 0.5), new Point(1.0, 0.5), 16);
		}
	}




	// 明度レール(縦スライダー)の背景。LabLightnessTrackBrush と同じ色変化を、下端=明度 0・上端=最大の縦のグラデーションで描く。上段のパッド右の明度レールが使う。
	public Brush LabLightnessTrackBrushVertical
	{
		get
		{
			LchSpace space = LabColorSpace;
			double lMax = LabColor.LMax(space);
			double aAxis = _showActualColor ? _cachedLabA : 0.0;
			double bAxis = _showActualColor ? _cachedLabB : 0.0;
			return MakeTrackBrush(t => LabColor.ToRgb(space, t * lMax, aAxis, bAxis), new Point(0.5, 1.0), new Point(0.5, 0.0), 16);
		}
	}




	// a スライダーの背景。a を −上限→+上限(緑寄り→赤寄り)に振った色変化を示す。ShowActualColor が真なら現在の明度・b のもとで描き、偽なら明度を中間(最大の半分)・b を 0 に固定して現在色には依らない基準にする。色域を外れた先は色域内へ収めた色になり、その範囲は LabAGamut が示す。
	public Brush LabATrackBrush
	{
		get
		{
			LchSpace space = LabColorSpace;
			double abMax = LabColor.AbMax(space);
			double lightness = _showActualColor ? _cachedLabL : LabColor.LMax(space) * 0.5;
			double bAxis = _showActualColor ? _cachedLabB : 0.0;
			return MakeTrackBrush(t => LabColor.ToRgb(space, lightness, ((t * 2.0) - 1.0) * abMax, bAxis), new Point(0.0, 0.5), new Point(1.0, 0.5), 16);
		}
	}




	// b スライダーの背景。b を −上限→+上限(青寄り→黄寄り)に振った色変化を示す。ShowActualColor が真なら現在の明度・a のもとで描き、偽なら明度を中間(最大の半分)・a を 0 に固定して現在色には依らない基準にする。色域を外れた先は色域内へ収めた色になり、その範囲は LabBGamut が示す。
	public Brush LabBTrackBrush
	{
		get
		{
			LchSpace space = LabColorSpace;
			double abMax = LabColor.AbMax(space);
			double lightness = _showActualColor ? _cachedLabL : LabColor.LMax(space) * 0.5;
			double aAxis = _showActualColor ? _cachedLabA : 0.0;
			return MakeTrackBrush(t => LabColor.ToRgb(space, lightness, aAxis, ((t * 2.0) - 1.0) * abMax), new Point(0.0, 0.5), new Point(1.0, 0.5), 16);
		}
	}




	// a スライダー(縦向き)の背景。LabATrackBrush と同じ色変化を、下端=a の −上限・上端=+上限の縦のグラデーションで描く。見せ方が b×L 平面+a の縦バーのとき、切り出した a の縦スライダーが使う。
	public Brush LabATrackBrushVertical
	{
		get
		{
			LchSpace space = LabColorSpace;
			double abMax = LabColor.AbMax(space);
			double lightness = _showActualColor ? _cachedLabL : LabColor.LMax(space) * 0.5;
			double bAxis = _showActualColor ? _cachedLabB : 0.0;
			return MakeTrackBrush(t => LabColor.ToRgb(space, lightness, ((t * 2.0) - 1.0) * abMax, bAxis), new Point(0.5, 1.0), new Point(0.5, 0.0), 16);
		}
	}




	// b スライダー(縦向き)の背景。LabBTrackBrush と同じ色変化を、下端=b の −上限・上端=+上限の縦のグラデーションで描く。見せ方が a×L 平面+b の縦バーのとき、切り出した b の縦スライダーが使う。
	public Brush LabBTrackBrushVertical
	{
		get
		{
			LchSpace space = LabColorSpace;
			double abMax = LabColor.AbMax(space);
			double lightness = _showActualColor ? _cachedLabL : LabColor.LMax(space) * 0.5;
			double aAxis = _showActualColor ? _cachedLabA : 0.0;
			return MakeTrackBrush(t => LabColor.ToRgb(space, lightness, aAxis, ((t * 2.0) - 1.0) * abMax), new Point(0.5, 1.0), new Point(0.5, 0.0), 16);
		}
	}




	// 明度レールで sRGB 色域を外れる区間。背景と同じ基準のもとで、明度を端から端まで振ったときの色域外の範囲を示す。ShowActualColor が偽のときは背景が a・b 0 のグレースケールで全域色域内のため、区間は出ない。
	public IReadOnlyList<GamutSegment> LabLightnessGamut
	{
		get
		{
			LchSpace space = LabColorSpace;
			double lMax = LabColor.LMax(space);
			double aAxis = _showActualColor ? _cachedLabA : 0.0;
			double bAxis = _showActualColor ? _cachedLabB : 0.0;
			return ComputeGamutSegments(t => LabColor.InGamut(space, t * lMax, aAxis, bAxis));
		}
	}




	// a スライダーで sRGB 色域を外れる区間。背景と同じ基準のもとで、a を −上限から +上限まで振ったときの色域外の範囲を示す。ShowActualColor が偽のときは明度を中間・b を 0 に固定して判定する。
	public IReadOnlyList<GamutSegment> LabAGamut
	{
		get
		{
			LchSpace space = LabColorSpace;
			double abMax = LabColor.AbMax(space);
			double lightness = _showActualColor ? _cachedLabL : LabColor.LMax(space) * 0.5;
			double bAxis = _showActualColor ? _cachedLabB : 0.0;
			return ComputeGamutSegments(t => LabColor.InGamut(space, lightness, ((t * 2.0) - 1.0) * abMax, bAxis));
		}
	}




	// b スライダーで sRGB 色域を外れる区間。背景と同じ基準のもとで、b を −上限から +上限まで振ったときの色域外の範囲を示す。ShowActualColor が偽のときは明度を中間・a を 0 に固定して判定する。
	public IReadOnlyList<GamutSegment> LabBGamut
	{
		get
		{
			LchSpace space = LabColorSpace;
			double abMax = LabColor.AbMax(space);
			double lightness = _showActualColor ? _cachedLabL : LabColor.LMax(space) * 0.5;
			double aAxis = _showActualColor ? _cachedLabA : 0.0;
			return ComputeGamutSegments(t => LabColor.InGamut(space, lightness, aAxis, ((t * 2.0) - 1.0) * abMax));
		}
	}




	// 明度を設定する。現在の a・b を保ったまま、目標の明度をキャッシュへ入れて色1へ反映する。表示尺度(0–100)を素の尺度へ戻す。適用中(ApplyLabFromCache)の通知がスライダー・パッドの双方向束縛を介して再入したときは、正規化値の往復で生じる微小な誤差で再適用が連鎖しないよう無視する。
	private void SetLabLightness(double displayValue)
	{
		if (_labEditing)
		{
			return;
		}

		double native = Math.Clamp(displayValue, 0.0, 100.0) * LabColor.LMax(LabColorSpace) / 100.0;

		if (native == _cachedLabL)
		{
			return;
		}

		_cachedLabL = native;
		ApplyLabFromCache();
	}




	// a 軸を設定する。現在の明度・b を保ったまま、目標の a をキャッシュへ入れて色1へ反映する。色域制限オンなら反映後に色域境界へ畳まれ、オフなら色域外の値も保たれる。適用中(ApplyLabFromCache)の通知が双方向束縛を介して再入したときは、正規化値の往復で生じる微小な誤差で再適用が連鎖しないよう無視する。
	private void SetLabA(double value)
	{
		if (_labEditing)
		{
			return;
		}

		double abMax = LabColor.AbMax(LabColorSpace);
		double clamped = Math.Clamp(value, -abMax, abMax);

		if (clamped == _cachedLabA)
		{
			return;
		}

		_cachedLabA = clamped;
		ApplyLabFromCache();
	}




	// b 軸を設定する。現在の明度・a を保ったまま、目標の b をキャッシュへ入れて色1へ反映する。色域制限オンなら反映後に色域境界へ畳まれ、オフなら色域外の値も保たれる。適用中(ApplyLabFromCache)の通知が双方向束縛を介して再入したときは、正規化値の往復で生じる微小な誤差で再適用が連鎖しないよう無視する。
	private void SetLabB(double value)
	{
		if (_labEditing)
		{
			return;
		}

		double abMax = LabColor.AbMax(LabColorSpace);
		double clamped = Math.Clamp(value, -abMax, abMax);

		if (clamped == _cachedLabB)
		{
			return;
		}

		_cachedLabB = clamped;
		ApplyLabFromCache();
	}




	// a・b パッドの操作で両軸を同時に設定する。色域制限オンのときは、色相(a:b の比)を保ったままカーソル位置を色域境界の縁へ半径方向で寄せて、つまみを色域の縁へ滑らかに沿わせる。オフのときは色域外もそのまま受け、つまみをカーソルへ追従させる。明度はパッドでは変えないため保つ。値は正規化(0–1、0.5 が 0)で受け取る。
	public void SetLabPad(double aNorm, double bNorm)
	{
		if (_labEditing)
		{
			return;
		}

		// 正規化値(0–1)を表示枠(CurrentAbExtent)を介して素の a・b へ戻す。横は左端 XMin→右端 XMax、縦は下端 YMin→上端 YMax。固定枠のときは ±AbMax の対称、フィットのときは色域の広がりに合わせた枠になる。
		PlaneExtent extent = CurrentAbExtent();
		double aAxis = extent.XMin + Math.Clamp(aNorm, 0.0, 1.0) * extent.XWidth;
		double bAxis = extent.YMin + Math.Clamp(bNorm, 0.0, 1.0) * extent.YHeight;

		if (_labGamutLimit)
		{
			(aAxis, bAxis) = LabColor.NearestInGamut(LabColorSpace, _cachedLabL, aAxis, bAxis);
		}

		if (aAxis == _cachedLabA && bAxis == _cachedLabB)
		{
			return;
		}

		_cachedLabA = aAxis;
		_cachedLabB = bAxis;
		ApplyLabFromCache();
	}




	// a×L 平面の操作で a と明度を同時に設定する。b は固定して保つ。色域制限オンのときは、b を保ったまま a・明度を色域内へ寄せる(色相が a:b の比で変わるため、色域寄せは a×b パッドの半径方向の寄せと異なり、a・b 平面ではなく a を境界の最大彩度に応じて詰める)。値は正規化(0–1、a は 0.5 が 0、明度は下端 0・上端 最大)で受け取る。
	public void SetLabALPad(double aNorm, double lNorm)
	{
		if (_labEditing)
		{
			return;
		}

		// 正規化値(0–1)を表示枠(CartExtent)を介して素の a・明度へ戻す。横は左端 XMin→右端 XMax、縦は下端 YMin→上端 YMax。固定枠のときは a が ±AbMax・明度が 0–LMax、フィットのときは色域の広がりに合わせた枠になる。b は固定して保つ。
		PlaneExtent extent = CartExtent(true);
		double aAxis = extent.XMin + Math.Clamp(aNorm, 0.0, 1.0) * extent.XWidth;
		double lightness = extent.YMin + Math.Clamp(lNorm, 0.0, 1.0) * extent.YHeight;
		double bAxis = _cachedLabB;

		if (_labGamutLimit)
		{
			(aAxis, bAxis) = LabColor.NearestInGamut(LabColorSpace, lightness, aAxis, bAxis);
		}

		if (aAxis == _cachedLabA && bAxis == _cachedLabB && lightness == _cachedLabL)
		{
			return;
		}

		_cachedLabA = aAxis;
		_cachedLabB = bAxis;
		_cachedLabL = lightness;
		ApplyLabFromCache();
	}




	// b×L 平面の操作で b と明度を同時に設定する。a は固定して保つ。色域制限オンのときは、a を保ったまま b・明度を色域内へ寄せる。値は正規化(0–1、b は 0.5 が 0、明度は下端 0・上端 最大)で受け取る。
	public void SetLabBLPad(double bNorm, double lNorm)
	{
		if (_labEditing)
		{
			return;
		}

		// 正規化値(0–1)を表示枠(CartExtent)を介して素の b・明度へ戻す。横は左端 XMin→右端 XMax、縦は下端 YMin→上端 YMax。固定枠のときは b が ±AbMax・明度が 0–LMax、フィットのときは色域の広がりに合わせた枠になる。a は固定して保つ。
		PlaneExtent extent = CartExtent(false);
		double bAxis = extent.XMin + Math.Clamp(bNorm, 0.0, 1.0) * extent.XWidth;
		double lightness = extent.YMin + Math.Clamp(lNorm, 0.0, 1.0) * extent.YHeight;
		double aAxis = _cachedLabA;

		if (_labGamutLimit)
		{
			(aAxis, bAxis) = LabColor.NearestInGamut(LabColorSpace, lightness, aAxis, bAxis);
		}

		if (aAxis == _cachedLabA && bAxis == _cachedLabB && lightness == _cachedLabL)
		{
			return;
		}

		_cachedLabA = aAxis;
		_cachedLabB = bAxis;
		_cachedLabL = lightness;
		ApplyLabFromCache();
	}




	// キャッシュの明度・a・b から色1の RGB を作り直して反映する。色域外の指定は明度と色相(a:b の比)を保ったまま彩度を詰めて色域内へ収める。編集中フラグを立て、NotifyLabDerived がキャッシュを RGB から取り直さないようにして、入れた値(色域外を含む)を保つ。色域制限オンのときは反映後にキャッシュを RGB 由来の色域内の値へ畳む。Lab の操作で色1が変わると HSV・HSL・HWB・YCbCr・LCH 側の表示も変わるため、各キャッシュを同期して通知する。
	private void ApplyLabFromCache()
	{
		_labEditing = true;

		try
		{
			Color color = LabColor.ToRgb(LabColorSpace, _cachedLabL, _cachedLabA, _cachedLabB);
			bool changed = !(color.R == (byte)_r && color.G == (byte)_g && color.B == (byte)_b);

			if (changed)
			{
				RecordContinuousChange();
				_r = color.R;
				_g = color.G;
				_b = color.B;
			}

			// 色域制限オンのときは、色域外へ入れた a・b を RGB 由来の色域内の値へ畳む。下流のスライダー背景もこの値で描けるよう、色由来の通知より先に行う。
			if (_labGamutLimit)
			{
				SyncLabCacheFromRgb();
			}

			if (changed)
			{
				OnPropertyChanged(nameof(R));
				OnPropertyChanged(nameof(G));
				OnPropertyChanged(nameof(B));
				NotifyColor1Derived();
				NotifyCmykDerived();
				SyncHsvCacheFromRgb();
				NotifyHsvDerived();
				SyncHslCacheFromRgb();
				NotifyHslDerived();
				NotifyHwbDerived();
				NotifyYuvDerived();
				NotifyLchDerived();
			}

			NotifyLabDerived();
		}
		finally
		{
			_labEditing = false;
		}
	}




	// 色1の RGB からキャッシュの明度・a・b を取り直す。a・b は無彩色でも 0 として定まるため、LCH の色相と違い常に取り直してよい。副モードの切り替え・色域制限のオン・外部での色変更(NotifyLabDerived)で呼ぶ。
	private void SyncLabCacheFromRgb()
	{
		(double l, double a, double b) = LabColor.FromRgb(LabColorSpace, (byte)_r, (byte)_g, (byte)_b);
		_cachedLabL = l;
		_cachedLabA = a;
		_cachedLabB = b;
	}




	// 色1から導く Lab 系の表示物をまとめて通知する。Lab 自身の編集中(_labEditing)でなければ、外部(他タブ・貼り付け等)で色が変わった通知とみなしてキャッシュを RGB 由来へ取り直す。編集中は入れた値(色域外を含む)を保つため取り直さない。
	private void NotifyLabDerived()
	{
		if (!_labEditing)
		{
			SyncLabCacheFromRgb();
		}

		NotifyLabComponents();
		NotifyLabTrackBrushes();
	}




	// 明度・a・b の数値表示と、スライダー・パッドが束縛する正規化値をまとめて通知する。
	private void NotifyLabComponents()
	{
		OnPropertyChanged(nameof(LabL));
		OnPropertyChanged(nameof(LabA));
		OnPropertyChanged(nameof(LabB));
		OnPropertyChanged(nameof(LabLNorm));
		OnPropertyChanged(nameof(LabANorm));
		OnPropertyChanged(nameof(LabBNorm));

		// a×b パッドのつまみ位置は表示枠を介して読むため、明度・a・b のいずれが変わっても通知し直す(明度が変わるとフィット枠の縮尺が変わり、a・b が同じでもつまみが移る)。
		OnPropertyChanged(nameof(LabPadANorm));
		OnPropertyChanged(nameof(LabPadBNorm));
	}




	// Lab スライダーの背景と色域外区間をまとめて通知する。背景は現在の明度・a・b から、色域外区間は同じ基準から導かれるため、色1が変わるたびに作り直す。
	private void NotifyLabTrackBrushes()
	{
		OnPropertyChanged(nameof(LabLightnessTrackBrush));
		OnPropertyChanged(nameof(LabLightnessTrackBrushVertical));
		OnPropertyChanged(nameof(LabATrackBrush));
		OnPropertyChanged(nameof(LabATrackBrushVertical));
		OnPropertyChanged(nameof(LabBTrackBrush));
		OnPropertyChanged(nameof(LabBTrackBrushVertical));
		OnPropertyChanged(nameof(LabLightnessGamut));
		OnPropertyChanged(nameof(LabAGamut));
		OnPropertyChanged(nameof(LabBGamut));
	}




	// 位置 t(0–1)で色域内かを返す関数から、色域を外れる連続区間の一覧を作る。十分細かく標本化して、色域外が始まる位置と終わる位置を拾う。スライダーのガモット可視化が使う。
	private static IReadOnlyList<GamutSegment> ComputeGamutSegments(Func<double, bool> inGamutAt)
	{
		const int samples = 64;
		var list = new List<GamutSegment>();
		int start = -1;

		for (int i = 0; i <= samples; i++)
		{
			double t = (double)i / samples;
			bool outside = !inGamutAt(t);

			if (outside && start < 0)
			{
				start = i;
			}
			else if (!outside && start >= 0)
			{
				list.Add(new GamutSegment((double)start / samples, (double)i / samples));
				start = -1;
			}
		}

		if (start >= 0)
		{
			list.Add(new GamutSegment((double)start / samples, 1.0));
		}

		return list;
	}




	// 輝度 Y。フルレンジで 0–255、スタジオレンジで 16–235。縦の輝度スライダーが束縛する。利用者が入れた値をキャッシュで保ち、設定時は現在の Cb・Cr を保ったまま RGB へ変換し直して色1へ反映する。
	public double Luma
	{
		get => _cachedY;
		set => SetLuma(value);
	}




	// 色差 Cb(0–255)。表示の読み取りに使う。色域制限オフでは色域外の値も保つ。
	public double Cb => _cachedCb;




	// 色差 Cr(0–255)。表示の読み取りに使う。色域制限オフでは色域外の値も保つ。
	public double Cr => _cachedCr;




	// Cb を 0–1 で扱う束縛用。Cb-Cr パッドの横方向が束縛する。
	public double Cb01
	{
		get => Math.Clamp(_cachedCb / 255.0, 0.0, 1.0);
		set => SetCb01(value);
	}




	// Cr を 0–1 で扱う束縛用。Cb-Cr パッドの縦方向が束縛する。下端を 0、上端を 1 とするパッドに合わせ、上ほど Cr が大きくなる。
	public double Cr01
	{
		get => Math.Clamp(_cachedCr / 255.0, 0.0, 1.0);
		set => SetCr01(value);
	}




	// Cb・Cr スライダーの矢印キーの刻み。スライダーは正規化値(0–1)で束縛し、表示は YCbCr/YUV のどちらも 0–255 のコード尺度(YUV は中心 128 を 0 へずらすだけ)のため、矢印で 1 段(1/255)動かす。
	public double YuvChromaStep => 1.0 / 255.0;




	// Cb・Cr スライダーの Page 移動量。0–255 のコード尺度で RGB と同じく 16 段(16/255)動かす。
	public double YuvChromaLargeStep => 16.0 / 255.0;




	// 輝度スライダーの背景。輝度を 0→255 に振ったときの色変化を示す。ShowActualColor が真なら現在の Cb・Cr のもとで描き、偽なら Cb・Cr を無彩色の 128 に固定して現在色には依らない黒→白のグレースケールにする。クランプの非線形を拾うため複数の刻みで標本化する。下段の水平スライダー用に左→右のグラデーションで作る。
	public Brush LumaTrackBrush
	{
		get
		{
			(double _, double cb, double cr) = _showActualColor ? (_cachedY, _cachedCb, _cachedCr) : (0.0, 128.0, 128.0);
			YCbCrFormat format = Format;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.YCbCrToRgb(t * 255.0, cb, cr, format)), new Point(0.0, 0.5), new Point(1.0, 0.5), 8);
		}
	}




	// 輝度レール(縦スライダー)の背景。LumaTrackBrush と同じ色変化を、下端=輝度 0・上端=255 の縦のグラデーションで描く。上段のパッド右の輝度レールが使う。
	public Brush LumaTrackBrushVertical
	{
		get
		{
			(double _, double cb, double cr) = _showActualColor ? (_cachedY, _cachedCb, _cachedCr) : (0.0, 128.0, 128.0);
			YCbCrFormat format = Format;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.YCbCrToRgb(t * 255.0, cb, cr, format)), new Point(0.5, 1.0), new Point(0.5, 0.0), 8);
		}
	}




	// Cb スライダーの背景。Cb を 0→255 に振った色変化を示す。ShowActualColor が真なら現在の輝度・Cr のもとで描き、偽なら輝度を中間の 128・Cr を無彩色の 128 に固定して現在色には依らない基準にする。色差は中間輝度のとき最もよく見える。ガモット外のクランプを拾うため複数の刻みで標本化する。
	public Brush CbTrackBrush
	{
		get
		{
			(double y, double _, double cr) = _showActualColor ? (_cachedY, _cachedCb, _cachedCr) : (128.0, 0.0, 128.0);
			YCbCrFormat format = Format;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.YCbCrToRgb(y, t * 255.0, cr, format)), new Point(0.0, 0.5), new Point(1.0, 0.5), 8);
		}
	}




	// Cb スライダー(縦向き)の背景。CbTrackBrush と同じ色変化を、下端=Cb 0・上端=255 の縦のグラデーションで描く。見せ方が Cr×Y 平面+Cb の縦バーのとき、切り出した Cb の縦スライダーが使う。
	public Brush CbTrackBrushVertical
	{
		get
		{
			(double y, double _, double cr) = _showActualColor ? (_cachedY, _cachedCb, _cachedCr) : (128.0, 0.0, 128.0);
			YCbCrFormat format = Format;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.YCbCrToRgb(y, t * 255.0, cr, format)), new Point(0.5, 1.0), new Point(0.5, 0.0), 8);
		}
	}




	// Cr スライダーの背景。Cr を 0→255 に振った色変化を示す。ShowActualColor が真なら現在の輝度・Cb のもとで描き、偽なら輝度を中間の 128・Cb を無彩色の 128 に固定して現在色には依らない基準にする。色差は中間輝度のとき最もよく見える。ガモット外のクランプを拾うため複数の刻みで標本化する。
	public Brush CrTrackBrush
	{
		get
		{
			(double y, double cb, double _) = _showActualColor ? (_cachedY, _cachedCb, _cachedCr) : (128.0, 128.0, 0.0);
			YCbCrFormat format = Format;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.YCbCrToRgb(y, cb, t * 255.0, format)), new Point(0.0, 0.5), new Point(1.0, 0.5), 8);
		}
	}




	// Cr スライダー(縦向き)の背景。CrTrackBrush と同じ色変化を、下端=Cr 0・上端=255 の縦のグラデーションで描く。見せ方が Cb×Y 平面+Cr の縦バーのとき、切り出した Cr の縦スライダーが使う。
	public Brush CrTrackBrushVertical
	{
		get
		{
			(double y, double cb, double _) = _showActualColor ? (_cachedY, _cachedCb, _cachedCr) : (128.0, 128.0, 0.0);
			YCbCrFormat format = Format;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.YCbCrToRgb(y, cb, t * 255.0, format)), new Point(0.5, 1.0), new Point(0.5, 0.0), 8);
		}
	}




	// 輝度レールで sRGB 色域を外れる区間。背景と同じ基準のもとで、輝度を 0→255 まで振ったときの色域外の範囲を示す。ShowActualColor が偽のときは色差を無彩色 128 に固定するため、全域が無彩色で色域内となり区間は出ない。
	public IReadOnlyList<GamutSegment> LumaGamut
	{
		get
		{
			YCbCrFormat format = Format;
			double cb = _showActualColor ? _cachedCb : 128.0;
			double cr = _showActualColor ? _cachedCr : 128.0;
			return ComputeGamutSegments(t => YuvColor.InGamut(format, t * 255.0, cb, cr));
		}
	}




	// Cb スライダーで sRGB 色域を外れる区間。背景と同じ基準のもとで、Cb を 0→255 まで振ったときの色域外の範囲を示す。ShowActualColor が偽のときは輝度を中間の 128・Cr を無彩色の 128 に固定して判定する。
	public IReadOnlyList<GamutSegment> CbGamut
	{
		get
		{
			YCbCrFormat format = Format;
			double y = _showActualColor ? _cachedY : 128.0;
			double cr = _showActualColor ? _cachedCr : 128.0;
			return ComputeGamutSegments(t => YuvColor.InGamut(format, y, t * 255.0, cr));
		}
	}




	// Cr スライダーで sRGB 色域を外れる区間。背景と同じ基準のもとで、Cr を 0→255 まで振ったときの色域外の範囲を示す。ShowActualColor が偽のときは輝度を中間の 128・Cb を無彩色の 128 に固定して判定する。
	public IReadOnlyList<GamutSegment> CrGamut
	{
		get
		{
			YCbCrFormat format = Format;
			double y = _showActualColor ? _cachedY : 128.0;
			double cb = _showActualColor ? _cachedCb : 128.0;
			return ComputeGamutSegments(t => YuvColor.InGamut(format, y, cb, t * 255.0));
		}
	}




	// YUV(符号付き)表記かどうか。真のとき色差を中心 0 の符号付き(U・V)で読み、偽のとき 0–255 中心 128(Cb・Cr)で読む。色1・パッド・色差平面は変わらず、数値の読み方だけが変わる。
	public bool IsSignedMode
	{
		get => _yuvSignedMode;
		set
		{
			if (_yuvSignedMode == value)
			{
				return;
			}

			_yuvSignedMode = value;
			OnPropertyChanged(nameof(IsSignedMode));
		}
	}




	// スタジオレンジ(Y 16–235、Cb・Cr 16–240)で扱うかどうか。偽のときフルレンジ(各 0–255)。色1は不変で、レンジを変えると数値とガモットの形が変わる。
	public bool UseStudioRange
	{
		get => !_yuvFullRange;
		set
		{
			if ((!_yuvFullRange) == value)
			{
				return;
			}

			_yuvFullRange = !value;
			OnPropertyChanged(nameof(UseStudioRange));
			NotifyYuvDerived();
		}
	}




	// YCbCr の色差を sRGB 色域へ制限するか。オフ(既定)では色域外の色差もそのまま保ち、表示できない範囲を色差平面上にハッチで可視化する。オンでは無彩色 128 から見た方向(Cb:Cr の比)を保ったまま色差を色域境界へクランプする。色1の RGB は表示できる色しか持てないため、この設定はキャッシュが色域外の値を保つかどうかだけを変える。
	public bool YuvGamutLimit
	{
		get => _yuvGamutLimit;
		set
		{
			if (_yuvGamutLimit == value)
			{
				return;
			}

			_yuvGamutLimit = value;
			OnPropertyChanged(nameof(YuvGamutLimit));

			// オンへ切り替えたら、いま色域外に入れている色差を境界へクランプして見せ直す。NotifyYuvDerived は編集中でないためキャッシュを RGB から取り直して色域内へ収める。オフは以後の編集で色域外を許すだけで、今の表示はそのまま。
			if (_yuvGamutLimit)
			{
				NotifyYuvDerived();
			}
		}
	}




	// 係数規格の選択(0=BT.601, 1=BT.709, 2=BT.2020)。ドロップダウンが束縛する。色1は不変で、規格を変えると数値とガモットの形が変わる。
	public int StandardIndex
	{
		get => _yuvStandard switch
		{
			YCbCrStandard.Bt601 => 0,
			YCbCrStandard.Bt2020 => 2,
			_ => 1,
		};
		set
		{
			YCbCrStandard standard = value switch
			{
				0 => YCbCrStandard.Bt601,
				2 => YCbCrStandard.Bt2020,
				_ => YCbCrStandard.Bt709,
			};

			if (_yuvStandard == standard)
			{
				return;
			}

			_yuvStandard = standard;
			OnPropertyChanged(nameof(StandardIndex));
			NotifyYuvDerived();
		}
	}




	// 現在の YCbCr 符号化形式(規格とレンジ)。色差平面の生成と各変換に使う。
	public YCbCrFormat Format => new(_yuvStandard, _yuvFullRange);




	// 輝度を設定する。目標の輝度をキャッシュへ入れ、現在の Cb・Cr を保ったまま色1へ反映する。色域制限オンなら反映後に色域境界へ畳まれ、オフなら色域外の色差も保たれる。適用中(ApplyYuvFromCache)の通知が輝度スライダーの双方向束縛を介して再入したときは、丸めの往復で再適用が連鎖しないよう無視する。
	private void SetLuma(double luma)
	{
		if (_yuvEditing)
		{
			return;
		}

		double clamped = Math.Clamp(luma, 0.0, 255.0);

		if (clamped == _cachedY)
		{
			return;
		}

		_cachedY = clamped;
		ApplyYuvFromCache();
	}




	// Cb を設定する。目標の Cb をキャッシュへ入れ、現在の輝度・Cr を保ったまま色1へ反映する。適用中の再入は無視する。
	private void SetCb01(double cb01)
	{
		if (_yuvEditing)
		{
			return;
		}

		double cb = Math.Clamp(cb01, 0.0, 1.0) * 255.0;

		if (cb == _cachedCb)
		{
			return;
		}

		_cachedCb = cb;
		ApplyYuvFromCache();
	}




	// Cr を設定する。目標の Cr をキャッシュへ入れ、現在の輝度・Cb を保ったまま色1へ反映する。適用中の再入は無視する。
	private void SetCr01(double cr01)
	{
		if (_yuvEditing)
		{
			return;
		}

		double cr = Math.Clamp(cr01, 0.0, 1.0) * 255.0;

		if (cr == _cachedCr)
		{
			return;
		}

		_cachedCr = cr;
		ApplyYuvFromCache();
	}




	// Cb-Cr パッドの操作で両軸を同時に設定する。色域制限オンのときは、無彩色 128 から見た方向(Cb:Cr の比)を保ったままカーソル位置を色域境界の縁へ半径方向で寄せて、つまみを色域の縁へ滑らかに沿わせる。横や縦に押し込んでもつまみが境界に貼り付いて止まる感触を和らげる。オフのときは色域外もそのまま受け、つまみをカーソルへ追従させる。輝度はパッドでは変えないため保つ。値は正規化(0–1)で受け取る。正規化値は表示枠(CurrentYuvCbCrExtent)を介して素の Cb・Cr のコード値へ戻す。横は左端 XMin→右端 XMax、縦は下端 YMin→上端 YMax。固定枠のときは 0–255 の全域、フィットのときは色域の広がりに合わせた枠になる。
	public void SetYuvPad(double cb01, double cr01)
	{
		if (_yuvEditing)
		{
			return;
		}

		PlaneExtent extent = CurrentYuvCbCrExtent();
		double cb = extent.XMin + Math.Clamp(cb01, 0.0, 1.0) * extent.XWidth;
		double cr = extent.YMin + Math.Clamp(cr01, 0.0, 1.0) * extent.YHeight;

		if (_yuvGamutLimit)
		{
			(cb, cr) = YuvColor.NearestInGamut(Format, _cachedY, cb, cr);
		}

		if (cb == _cachedCb && cr == _cachedCr)
		{
			return;
		}

		_cachedCb = cb;
		_cachedCr = cr;
		ApplyYuvFromCache();
	}




	// Cb×Y パッドの操作で Cb と輝度 Y を同時に設定する。Cr は固定して保つ。色域制限オンのときは、新しい輝度のもとで Cr を保ったまま Cb を無彩色 128 から見た方向で色域境界へ寄せる(SetYuvPad と同じ半径方向の寄せ)。Cb・輝度はともに正規化(0–1)で受け取る。正規化値は表示枠(YuvLumaExtent)を介して素の Cb・輝度のコード値へ戻す。横は左端 XMin→右端 XMax、縦は下端 YMin→上端 YMax。固定枠のときは横 0–255・縦 0–255、フィットのときは色域の広がりに合わせた枠になる。Cr は固定して保つ。
	public void SetYuvCbLumaPad(double cb01, double lumaNorm)
	{
		if (_yuvEditing)
		{
			return;
		}

		PlaneExtent extent = YuvLumaExtent(true);
		double cb = extent.XMin + Math.Clamp(cb01, 0.0, 1.0) * extent.XWidth;
		double luma = extent.YMin + Math.Clamp(lumaNorm, 0.0, 1.0) * extent.YHeight;
		double cr = _cachedCr;

		if (_yuvGamutLimit)
		{
			(cb, cr) = YuvColor.NearestInGamut(Format, luma, cb, cr);
		}

		if (cb == _cachedCb && cr == _cachedCr && luma == _cachedY)
		{
			return;
		}

		_cachedY = luma;
		_cachedCb = cb;
		_cachedCr = cr;
		ApplyYuvFromCache();
	}




	// Cr×Y パッドの操作で Cr と輝度 Y を同時に設定する。Cb は固定して保つ。色域制限オンのときは、新しい輝度のもとで Cb を保ったまま Cr を無彩色 128 から見た方向で色域境界へ寄せる(SetYuvPad と同じ半径方向の寄せ)。Cr・輝度はともに正規化(0–1)で受け取る。正規化値は表示枠(YuvLumaExtent)を介して素の Cr・輝度のコード値へ戻す。横は左端 XMin→右端 XMax、縦は下端 YMin→上端 YMax。固定枠のときは横 0–255・縦 0–255、フィットのときは色域の広がりに合わせた枠になる。Cb は固定して保つ。
	public void SetYuvCrLumaPad(double cr01, double lumaNorm)
	{
		if (_yuvEditing)
		{
			return;
		}

		PlaneExtent extent = YuvLumaExtent(false);
		double cr = extent.XMin + Math.Clamp(cr01, 0.0, 1.0) * extent.XWidth;
		double luma = extent.YMin + Math.Clamp(lumaNorm, 0.0, 1.0) * extent.YHeight;
		double cb = _cachedCb;

		if (_yuvGamutLimit)
		{
			(cb, cr) = YuvColor.NearestInGamut(Format, luma, cb, cr);
		}

		if (cb == _cachedCb && cr == _cachedCr && luma == _cachedY)
		{
			return;
		}

		_cachedY = luma;
		_cachedCb = cb;
		_cachedCr = cr;
		ApplyYuvFromCache();
	}




	// キャッシュの輝度・色差から色1の RGB を作り直して反映する。色域外の指定は各 RGB 成分を 0–255 へクランプして色域内へ収める。編集中フラグを立て、NotifyYuvDerived がキャッシュを RGB から取り直さないようにして、入れた値(色域外を含む)を保つ。色域制限オンのときは反映後にキャッシュを RGB 由来の色域内の値へ畳む。YCbCr の操作で色1が変わると HSV・HSL・HWB・LCH・Lab 側の表示も変わるため、各キャッシュを同期して通知する。
	private void ApplyYuvFromCache()
	{
		_yuvEditing = true;

		try
		{
			(byte r, byte g, byte b) = ColorConversion.YCbCrToRgb(_cachedY, _cachedCb, _cachedCr, Format);
			bool changed = !(r == (byte)_r && g == (byte)_g && b == (byte)_b);

			if (changed)
			{
				RecordContinuousChange();
				_r = r;
				_g = g;
				_b = b;
			}

			// 色域制限オンのときは、色域外へ入れた色差を RGB 由来の色域内の値へ畳む。下流のスライダー背景もこの値で描けるよう、色由来の通知より先に行う。
			if (_yuvGamutLimit)
			{
				SyncYuvCacheFromRgb();
			}

			if (changed)
			{
				OnPropertyChanged(nameof(R));
				OnPropertyChanged(nameof(G));
				OnPropertyChanged(nameof(B));
				NotifyColor1Derived();
				NotifyCmykDerived();
				SyncHsvCacheFromRgb();
				NotifyHsvDerived();
				SyncHslCacheFromRgb();
				NotifyHslDerived();
				NotifyHwbDerived();
				NotifyLchDerived();
				NotifyLabDerived();
			}

			NotifyYuvDerived();
		}
		finally
		{
			_yuvEditing = false;
		}
	}




	// 色1の RGB からキャッシュの輝度・色差を現在の符号化形式で取り直す。副モード(規格・レンジ)の切り替え・色域制限のオン・外部での色変更(NotifyYuvDerived)で呼ぶ。
	private void SyncYuvCacheFromRgb()
	{
		(double y, double cb, double cr) = ColorConversion.RgbToYCbCr((byte)_r, (byte)_g, (byte)_b, Format);
		_cachedY = y;
		_cachedCb = cb;
		_cachedCr = cr;
	}




	// 色1から導く YCbCr 系の表示物をまとめて通知する。YCbCr 自身の編集中(_yuvEditing)でなければ、外部(他タブ・貼り付け等)で色が変わった通知とみなしてキャッシュを RGB 由来へ取り直す。編集中は入れた値(色域外を含む)を保つため取り直さない。色1が変わると輝度・色差とその表示、輝度スライダーの背景が変わりうる。
	private void NotifyYuvDerived()
	{
		if (!_yuvEditing)
		{
			SyncYuvCacheFromRgb();
		}

		OnPropertyChanged(nameof(Luma));
		OnPropertyChanged(nameof(Cb));
		OnPropertyChanged(nameof(Cr));
		OnPropertyChanged(nameof(Cb01));
		OnPropertyChanged(nameof(Cr01));

		// 既定 Cb×Cr パッドのつまみ位置は表示枠を介して読むため、輝度・Cb・Cr のいずれが変わっても通知し直す(輝度が変わるとフィット枠の縮尺が変わり、Cb・Cr が同じでもつまみが移る)。
		OnPropertyChanged(nameof(YuvCbCrPadCbNorm));
		OnPropertyChanged(nameof(YuvCbCrPadCrNorm));
		OnPropertyChanged(nameof(LumaTrackBrush));
		OnPropertyChanged(nameof(LumaTrackBrushVertical));
		OnPropertyChanged(nameof(CbTrackBrush));
		OnPropertyChanged(nameof(CbTrackBrushVertical));
		OnPropertyChanged(nameof(CrTrackBrush));
		OnPropertyChanged(nameof(CrTrackBrushVertical));
		OnPropertyChanged(nameof(LumaGamut));
		OnPropertyChanged(nameof(CbGamut));
		OnPropertyChanged(nameof(CrGamut));
	}




	// 色相スライダーの背景。ShowActualColor が真なら現在の彩度・明度を保ったまま色相を 0→360 に振った色変化を示し、暗い色・くすんだ色のときはその見え方をそのまま反映する。偽なら現在の色に依らず最大彩度・最大明度の虹を示す。色相に対する RGB は 60 度ごとに折れる区分線形のため、各区間の端を捉える刻みで標本化する。
	public Brush HueTrackBrush
	{
		get
		{
			double saturation = _showActualColor ? _cachedSaturation : 1.0;
			double value = _showActualColor ? CurrentValue() : 1.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HsvToRgb(t * 360.0, saturation, value)), new Point(0.0, 0.5), new Point(1.0, 0.5), 6);
		}
	}




	// 縦置きの色相スライダーの背景。色変化は HueTrackBrush と同じだが、グラデーションを縦方向(下端=色相0度・上端=色相360度)に流す。彩度×明度の正方形+色相の縦スライダーのレイアウトが使う。
	public Brush HueTrackBrushVertical
	{
		get
		{
			double saturation = _showActualColor ? _cachedSaturation : 1.0;
			double value = _showActualColor ? CurrentValue() : 1.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HsvToRgb(t * 360.0, saturation, value)), new Point(0.5, 1.0), new Point(0.5, 0.0), 6);
		}
	}




	// HSV の彩度スライダーの背景。現在の色相を保ったまま彩度を 0→1 に振った色変化(無彩色→純色)を示す。ShowActualColor が真なら現在の明度のもとで描き、偽なら明度を最大に固定して現在の明度には依らない基準にする。
	public Brush SaturationTrackBrush
	{
		get
		{
			double value = _showActualColor ? CurrentValue() : 1.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HsvToRgb(_cachedHue, t, value)), new Point(0.0, 0.5), new Point(1.0, 0.5));
		}
	}




	// 縦置きの彩度スライダーの背景。色変化は SaturationTrackBrush と同じだが、グラデーションを縦方向(下端=彩度0・上端=彩度1)に流す。色相×明度の直交パッドの彩度の縦スライダーが使う。
	public Brush SaturationTrackBrushVertical
	{
		get
		{
			double value = _showActualColor ? CurrentValue() : 1.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HsvToRgb(_cachedHue, t, value)), new Point(0.5, 1.0), new Point(0.5, 0.0));
		}
	}




	// HSV の明度スライダーの背景。現在の色相を保ったまま明度を 0→1 に振った色変化(黒→純色)を示す。ShowActualColor が真なら現在の彩度のもとで描き、偽なら彩度を最大に固定して現在の彩度には依らない基準にする。
	public Brush ValueTrackBrush
	{
		get
		{
			double saturation = _showActualColor ? _cachedSaturation : 1.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HsvToRgb(_cachedHue, saturation, t)), new Point(0.0, 0.5), new Point(1.0, 0.5));
		}
	}




	// 縦置きの明度スライダーの背景。色変化は ValueTrackBrush と同じだが、グラデーションを縦方向(下端=明度0・上端=明度1)に流す。円盤レイアウトの明度の縦スライダーが使う。
	public Brush ValueTrackBrushVertical
	{
		get
		{
			double saturation = _showActualColor ? _cachedSaturation : 1.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HsvToRgb(_cachedHue, saturation, t)), new Point(0.5, 1.0), new Point(0.5, 0.0));
		}
	}




	// HSL の彩度スライダーの背景。現在の色相を保ったまま彩度を 0→1 に振った色変化を示す。ShowActualColor が真なら現在の輝度のもとで描き、偽なら輝度を 0.5 に固定して現在の輝度には依らない基準にする。輝度を最大(1.0)にすると彩度に依らず白一色になり彩度差が見えないため、彩度が最も映える中間の輝度を基準にする。
	public Brush HslSaturationTrackBrush
	{
		get
		{
			double lightness = _showActualColor ? CurrentLightness() : 0.5;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HslToRgb(_cachedHue, t, lightness)), new Point(0.0, 0.5), new Point(1.0, 0.5));
		}
	}




	// HSL の輝度スライダーの背景。現在の色相を保ったまま輝度を 0→0.5→1 に振った色変化(黒→純色→白)を示す。ShowActualColor が真なら現在の彩度のもとで描き、偽なら彩度を最大に固定して現在の彩度には依らない基準にする。輝度に対する RGB は 0.5 で折れる区分線形のため、中央に標本点を置く。
	public Brush LightnessTrackBrush
	{
		get
		{
			double saturation = _showActualColor ? _cachedHslSaturation : 1.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HslToRgb(_cachedHue, saturation, t)), new Point(0.0, 0.5), new Point(1.0, 0.5), 2);
		}
	}




	// 縦置きの HSL 彩度スライダーの背景。色変化は HslSaturationTrackBrush と同じだが、グラデーションを縦方向(下端=彩度0・上端=彩度1)に流す。色相×輝度の直交パッドや輝度の円盤の彩度の縦スライダーが使う。
	public Brush HslSaturationTrackBrushVertical
	{
		get
		{
			double lightness = _showActualColor ? CurrentLightness() : 0.5;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HslToRgb(_cachedHue, t, lightness)), new Point(0.5, 1.0), new Point(0.5, 0.0));
		}
	}




	// 縦置きの HSL 輝度スライダーの背景。色変化は LightnessTrackBrush と同じだが、グラデーションを縦方向(下端=輝度0・上端=輝度1)に流す。彩度の円盤や色相×彩度の直交パッドの輝度の縦スライダーが使う。輝度に対する RGB は 0.5 で折れる区分線形のため、中央に標本点を置く。
	public Brush LightnessTrackBrushVertical
	{
		get
		{
			double saturation = _showActualColor ? _cachedHslSaturation : 1.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HslToRgb(_cachedHue, saturation, t)), new Point(0.5, 1.0), new Point(0.5, 0.0), 2);
		}
	}




	// HWB の白みスライダーの背景。現在の色相を保ったまま白みを 0→1 に振った色変化(純色→白)を示す。ShowActualColor が真なら現在の黒みのもとで描き、偽なら黒みを 0 に固定して現在の黒みには依らない基準にする。黒みを最大にすると白み+黒みが常に 1 を超えて無彩色へ退化するため、純色を起点に白みだけを伸ばせる 0 を基準にする。白み+黒みが 1 を超える範囲は無彩色へ退化し、白み÷(白み+黒み)の灰になる。直線部とこの曲線部の折れを拾うため複数の刻みで標本化する。
	public Brush WhitenessTrackBrush
	{
		get
		{
			double blackness = _showActualColor ? CurrentBlackness() : 0.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HwbToRgb(_cachedHue, t, blackness)), new Point(0.0, 0.5), new Point(1.0, 0.5), 8);
		}
	}




	// HWB の黒みスライダーの背景。現在の色相を保ったまま黒みを 0→1 に振った色変化(純色→黒)を示す。ShowActualColor が真なら現在の白みのもとで描き、偽なら白みを 0 に固定して現在の白みには依らない基準にする。白みを最大にすると白み+黒みが常に 1 を超えて無彩色へ退化するため、純色を起点に黒みだけを伸ばせる 0 を基準にする。白み+黒みが 1 を超える範囲は無彩色へ退化する。折れを拾うため複数の刻みで標本化する。
	public Brush BlacknessTrackBrush
	{
		get
		{
			double whiteness = _showActualColor ? CurrentWhiteness() : 0.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HwbToRgb(_cachedHue, whiteness, t)), new Point(0.0, 0.5), new Point(1.0, 0.5), 8);
		}
	}




	// 縦置きの HWB 白みスライダーの背景。色変化は WhitenessTrackBrush と同じだが、グラデーションを縦方向(下端=白み0・上端=白み1)に流す。色相×黒みの直交パッドや黒みの円盤の白みの縦スライダーが使う。
	public Brush WhitenessTrackBrushVertical
	{
		get
		{
			double blackness = _showActualColor ? CurrentBlackness() : 0.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HwbToRgb(_cachedHue, t, blackness)), new Point(0.5, 1.0), new Point(0.5, 0.0), 8);
		}
	}




	// 縦置きの HWB 黒みスライダーの背景。色変化は BlacknessTrackBrush と同じだが、グラデーションを縦方向(下端=黒み0・上端=黒み1)に流す。色相×白みの直交パッドや白みの円盤の黒みの縦スライダーが使う。
	public Brush BlacknessTrackBrushVertical
	{
		get
		{
			double whiteness = _showActualColor ? CurrentWhiteness() : 0.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HwbToRgb(_cachedHue, whiteness, t)), new Point(0.5, 1.0), new Point(0.5, 0.0), 8);
		}
	}




	// 縦置きの HWB 黒みスライダーの背景(上ほど純色・下ほど黒の向き)。黒みを上下逆(下端=黒み1・上端=黒み0)に流し、白み黒みの正方形や明度・輝度の縦軸と同じ「上ほど明るい」向きにそろえる。色相×白みの直交パッドや黒みを担う縦スライダーが使う。
	public Brush HwbPadYTrackBrushVertical
	{
		get
		{
			double whiteness = _showActualColor ? CurrentWhiteness() : 0.0;
			return MakeTrackBrush(t => OpaqueColor(ColorConversion.HwbToRgb(_cachedHue, whiteness, 1.0 - t)), new Point(0.5, 1.0), new Point(0.5, 0.0), 8);
		}
	}




	// HSV・HSL・HWB スライダーの背景は現在の色1から導かれるため、色1が変わるたびにまとめて通知する。色相スライダーは ShowActualColor が真のとき現在の彩度・明度を反映するため、これも併せて通知する。
	private void NotifySliderTrackBrushes()
	{
		OnPropertyChanged(nameof(HueTrackBrush));
		OnPropertyChanged(nameof(HueTrackBrushVertical));
		OnPropertyChanged(nameof(SaturationTrackBrush));
		OnPropertyChanged(nameof(SaturationTrackBrushVertical));
		OnPropertyChanged(nameof(ValueTrackBrush));
		OnPropertyChanged(nameof(ValueTrackBrushVertical));
		OnPropertyChanged(nameof(HslSaturationTrackBrush));
		OnPropertyChanged(nameof(HslSaturationTrackBrushVertical));
		OnPropertyChanged(nameof(LightnessTrackBrush));
		OnPropertyChanged(nameof(LightnessTrackBrushVertical));
		OnPropertyChanged(nameof(WhitenessTrackBrush));
		OnPropertyChanged(nameof(WhitenessTrackBrushVertical));
		OnPropertyChanged(nameof(BlacknessTrackBrush));
		OnPropertyChanged(nameof(BlacknessTrackBrushVertical));
		OnPropertyChanged(nameof(HwbPadYTrackBrushVertical));
	}




	// R を 0→255 に動かしたときの色変化を示す。G・B は現在値で固定する。
	public Brush RedTrackBrush => MakeChannelBrush(0);




	// G を 0→255 に動かしたときの色変化を示す。R・B は現在値で固定する。
	public Brush GreenTrackBrush => MakeChannelBrush(1);




	// B を 0→255 に動かしたときの色変化を示す。R・G は現在値で固定する。
	public Brush BlueTrackBrush => MakeChannelBrush(2);




	// R を 0→255 に動かしたときの色変化を、下端=0・上端=255 の縦のグラデーションで示す。見せ方が G×B 平面+R の縦バーのとき、切り出した R の縦スライダーが使う。
	public Brush RedTrackBrushVertical => MakeChannelBrush(0, true);




	// G を 0→255 に動かしたときの色変化を、下端=0・上端=255 の縦のグラデーションで示す。見せ方が R×B 平面+G の縦バーのとき、切り出した G の縦スライダーが使う。
	public Brush GreenTrackBrushVertical => MakeChannelBrush(1, true);




	// B を 0→255 に動かしたときの色変化を、下端=0・上端=255 の縦のグラデーションで示す。見せ方が R×G 平面+B の縦バーのとき、切り出した B の縦スライダーが使う。
	public Brush BlueTrackBrushVertical => MakeChannelBrush(2, true);




	// 無彩色スライダーの背景。黒(左)→白(右)のグレーランプ(R=G=B を 0→255)。色制限が有効なら各位置が丸められ段差になる。現在の色1や ShowActualColor には依らず、グレーの並びそのものを示す。
	public Brush GrayTrackBrush => MakeGrayBrush();




	// C を 0→100% に動かしたときの色変化を示す。M・Y・K は現在値で固定する。
	public Brush CyanTrackBrush => MakeCmykChannelBrush(0);




	// M を 0→100% に動かしたときの色変化を示す。C・Y・K は現在値で固定する。
	public Brush MagentaTrackBrush => MakeCmykChannelBrush(1);




	// Y を 0→100% に動かしたときの色変化を示す。C・M・K は現在値で固定する。
	public Brush YellowTrackBrush => MakeCmykChannelBrush(2);




	// K を 0→100% に動かしたときの色変化を示す。C・M・Y は現在値で固定する。
	public Brush KeyTrackBrush => MakeCmykChannelBrush(3);




	// C を 0→100% に動かしたときの色変化を、下端=0・上端=100% の縦のグラデーションで示す。見せ方が M×Y 平面+C の縦バーのとき、切り出した C の縦スライダーが使う。
	public Brush CyanTrackBrushVertical => MakeCmykChannelBrush(0, true);




	// M を 0→100% に動かしたときの色変化を、下端=0・上端=100% の縦のグラデーションで示す。見せ方が C×Y 平面+M の縦バーのとき、切り出した M の縦スライダーが使う。
	public Brush MagentaTrackBrushVertical => MakeCmykChannelBrush(1, true);




	// Y を 0→100% に動かしたときの色変化を、下端=0・上端=100% の縦のグラデーションで示す。見せ方が C×M 平面+Y の縦バーのとき、切り出した Y の縦スライダーが使う。
	public Brush YellowTrackBrushVertical => MakeCmykChannelBrush(2, true);




	// スライダー背景のグラデーションを、現在の色1を反映した実際の色で描くかどうか。真のときは各要素を動かした際の実際の色変化を示し、偽のときは各要素を単独で 0→100% へ振った基準のグラデーションを色1に依らず一定で示す。
	public bool ShowActualColor
	{
		get => _showActualColor;
		set
		{
			if (_showActualColor == value)
			{
				return;
			}

			_showActualColor = value;
			OnPropertyChanged(nameof(ShowActualColor));
			OnPropertyChanged(nameof(HueTrackBrush));
			OnPropertyChanged(nameof(HueTrackBrushVertical));
			OnPropertyChanged(nameof(SaturationTrackBrush));
			OnPropertyChanged(nameof(SaturationTrackBrushVertical));
			OnPropertyChanged(nameof(ValueTrackBrush));
			OnPropertyChanged(nameof(ValueTrackBrushVertical));
			OnPropertyChanged(nameof(HslSaturationTrackBrush));
			OnPropertyChanged(nameof(HslSaturationTrackBrushVertical));
			OnPropertyChanged(nameof(LightnessTrackBrush));
			OnPropertyChanged(nameof(LightnessTrackBrushVertical));
			OnPropertyChanged(nameof(WhitenessTrackBrush));
			OnPropertyChanged(nameof(WhitenessTrackBrushVertical));
			OnPropertyChanged(nameof(BlacknessTrackBrush));
			OnPropertyChanged(nameof(BlacknessTrackBrushVertical));
			OnPropertyChanged(nameof(HwbPadYTrackBrushVertical));
			OnPropertyChanged(nameof(RedTrackBrush));
			OnPropertyChanged(nameof(GreenTrackBrush));
			OnPropertyChanged(nameof(BlueTrackBrush));
			OnPropertyChanged(nameof(RedTrackBrushVertical));
			OnPropertyChanged(nameof(GreenTrackBrushVertical));
			OnPropertyChanged(nameof(BlueTrackBrushVertical));
			OnPropertyChanged(nameof(CyanTrackBrush));
			OnPropertyChanged(nameof(MagentaTrackBrush));
			OnPropertyChanged(nameof(YellowTrackBrush));
			OnPropertyChanged(nameof(KeyTrackBrush));
			OnPropertyChanged(nameof(CyanTrackBrushVertical));
			OnPropertyChanged(nameof(MagentaTrackBrushVertical));
			OnPropertyChanged(nameof(YellowTrackBrushVertical));
			OnPropertyChanged(nameof(LumaTrackBrush));
			OnPropertyChanged(nameof(LumaTrackBrushVertical));
			OnPropertyChanged(nameof(CbTrackBrush));
			OnPropertyChanged(nameof(CbTrackBrushVertical));
			OnPropertyChanged(nameof(CrTrackBrush));
			OnPropertyChanged(nameof(CrTrackBrushVertical));
			OnPropertyChanged(nameof(LumaGamut));
			OnPropertyChanged(nameof(CbGamut));
			OnPropertyChanged(nameof(CrGamut));
			OnPropertyChanged(nameof(LchLightnessTrackBrush));
			OnPropertyChanged(nameof(LchChromaTrackBrush));
			OnPropertyChanged(nameof(LchHueTrackBrush));
			OnPropertyChanged(nameof(LchLightnessGamut));
			OnPropertyChanged(nameof(LchChromaGamut));
			OnPropertyChanged(nameof(LchHueGamut));
			OnPropertyChanged(nameof(LabLightnessTrackBrush));
			OnPropertyChanged(nameof(LabLightnessTrackBrushVertical));
			OnPropertyChanged(nameof(LabATrackBrush));
			OnPropertyChanged(nameof(LabATrackBrushVertical));
			OnPropertyChanged(nameof(LabBTrackBrush));
			OnPropertyChanged(nameof(LabBTrackBrushVertical));
			OnPropertyChanged(nameof(LabLightnessGamut));
			OnPropertyChanged(nameof(LabAGamut));
			OnPropertyChanged(nameof(LabBGamut));
		}
	}




	// サイドバーをテキストモード(コントラスト確認)にするか。真のときは色リストを隠して背景色をサイドバー全体に敷き、アクティブ色の文字のテキスト欄とコントラストパネルを載せる。偽のときは色リストのパネルを縦に積んで見せる。表示メニューとツールバーのトグルから切り替え、設定に永続化する。
	public bool ShowContrastText
	{
		get => _showContrastText;
		set
		{
			if (_showContrastText == value)
			{
				return;
			}

			_showContrastText = value;

			// テキストモードへ入るときは役の位置を検証し、アクティブな色をどちらかの役に就けてから見せる。
			if (value)
			{
				EnsureContrastRoles();
			}

			OnPropertyChanged(nameof(ShowContrastText));
			OnPropertyChanged(nameof(EffectiveContrastTextMode));
			OnPropertyChanged(nameof(ContrastTextVisibility));
			OnPropertyChanged(nameof(SidebarVisionSeverityVisibility));
			NotifyContrastRolesChanged();
			RaiseColorListChanged();
		}
	}




	// テキストモードを実際に表示するか。トグルがオンでも、背景に使う2色目が無い(色が1つしか無い)間は通常の色リスト表示に留める。サイドバーの組み替え(MainWindow)がこれを見る。
	public bool EffectiveContrastTextMode => _showContrastText && HasMultipleColors;




	// テキストモードで見せる要素(テキスト欄とコントラストパネル)の可視を Visibility で返す。EffectiveContrastTextMode への束縛に使う。
	public Visibility ContrastTextVisibility => EffectiveContrastTextMode ? Visibility.Visible : Visibility.Collapsed;




	// コントラスト確認で不透明度(アルファ)を反映するか。真のとき、文字は文字色の不透明度で背景色へ透け、コントラスト比もその透けた実効色で測る。偽のときは不透明として扱う。切り替えで文字色とコントラストの表示を取り直す。設定に永続化する。
	public bool ContrastIncludeAlpha
	{
		get => _contrastIncludeAlpha;
		set
		{
			if (_contrastIncludeAlpha == value)
			{
				return;
			}

			_contrastIncludeAlpha = value;
			OnPropertyChanged(nameof(ContrastIncludeAlpha));
			OnPropertyChanged(nameof(ContrastTextBrush));
			NotifyContrastChanged();
		}
	}




	// P型 (1型) での見え方の行をサイドバーの各色パネルへ表示するか。表示メニューのトグルと双方向で束縛し、設定に永続化する。切り替えで全パネルの表示物を流し込み直し、行の可視と塗りを追従させる。あわせて、色相系(P型・D型・T型)のいずれかが表示中のときだけ出す強さスライダーの可視も更新する。
	public bool ShowProtan
	{
		get => _showProtan;
		set
		{
			if (_showProtan == value)
			{
				return;
			}

			_showProtan = value;
			OnPropertyChanged(nameof(ShowProtan));
			OnPropertyChanged(nameof(AnyVisionShown));
			OnPropertyChanged(nameof(SidebarVisionSeverityVisibility));
			RefreshAllColorDisplays();
		}
	}




	// D型 (2型) での見え方の行をサイドバーの各色パネルへ表示するか。挙動は ShowProtan と同じ。
	public bool ShowDeutan
	{
		get => _showDeutan;
		set
		{
			if (_showDeutan == value)
			{
				return;
			}

			_showDeutan = value;
			OnPropertyChanged(nameof(ShowDeutan));
			OnPropertyChanged(nameof(AnyVisionShown));
			OnPropertyChanged(nameof(SidebarVisionSeverityVisibility));
			RefreshAllColorDisplays();
		}
	}




	// T型 (3型) での見え方の行をサイドバーの各色パネルへ表示するか。挙動は ShowProtan と同じ。
	public bool ShowTritan
	{
		get => _showTritan;
		set
		{
			if (_showTritan == value)
			{
				return;
			}

			_showTritan = value;
			OnPropertyChanged(nameof(ShowTritan));
			OnPropertyChanged(nameof(AnyVisionShown));
			OnPropertyChanged(nameof(SidebarVisionSeverityVisibility));
			RefreshAllColorDisplays();
		}
	}




	// 1色覚での見え方の行をサイドバーの各色パネルへ表示するか。挙動は ShowProtan と同じだが、1色覚は強さ(severity)を持たないため強さスライダーの可視には関与しない。
	public bool ShowMonochromacy
	{
		get => _showMonochromacy;
		set
		{
			if (_showMonochromacy == value)
			{
				return;
			}

			_showMonochromacy = value;
			OnPropertyChanged(nameof(ShowMonochromacy));
			OnPropertyChanged(nameof(AnyVisionShown));
			RefreshAllColorDisplays();
		}
	}




	// 色覚行をどれか1つでも表示しているか。コマンドバーの分割ボタンの点灯(オン/オフ)が双方向で束縛し、ボタン部の押下で一括オン/オフを切り替える。各型のトグルが変わると getter の値も変わるため、4つのトグルの setter から通知する。
	public bool AnyVisionShown
	{
		get => _showProtan || _showDeutan || _showTritan || _showMonochromacy;
		set => ToggleVisionOverlay(value);
	}




	// 色覚行の表示を一括でオン/オフする。オフにするときは現在の型の組み合わせを覚えてから全て消し、オンへ戻すときはその組み合わせを復元する。一度も表示していなければ、有病率が最も高い D型を出す。コマンドバーの分割ボタンのボタン部が呼ぶ。
	public void ToggleVisionOverlay(bool on)
	{
		if (on == AnyVisionShown)
		{
			return;
		}

		if (on)
		{
			if (!(_savedShowProtan || _savedShowDeutan || _savedShowTritan || _savedShowMonochromacy))
			{
				_savedShowDeutan = true;
			}

			ShowProtan = _savedShowProtan;
			ShowDeutan = _savedShowDeutan;
			ShowTritan = _savedShowTritan;
			ShowMonochromacy = _savedShowMonochromacy;
		}
		else
		{
			_savedShowProtan = _showProtan;
			_savedShowDeutan = _showDeutan;
			_savedShowTritan = _showTritan;
			_savedShowMonochromacy = _showMonochromacy;
			ShowProtan = false;
			ShowDeutan = false;
			ShowTritan = false;
			ShowMonochromacy = false;
		}
	}




	// 色覚シミュレーションの弱まりの強さ (severity, 0–1)。サイドバーとマトリックス窓の両スライダーが双方向で束縛し、両方の見え方へ共通で効く。値を変えると全パネルの色覚行を作り直す。マトリックス窓はこの通知を受けて行列のセルを書き換える。設定に永続化する。
	public double VisionSeverity
	{
		get => _visionSeverity;
		set
		{
			double clamped = Math.Clamp(value, 0.0, 1.0);

			if (_visionSeverity == clamped)
			{
				return;
			}

			_visionSeverity = clamped;
			OnPropertyChanged(nameof(VisionSeverity));
			OnPropertyChanged(nameof(VisionSeverityText));
			RefreshAllColorDisplays();
		}
	}




	// 強さスライダーの脇に出す読み値。0–1 を百分率で見せる。
	public string VisionSeverityText => string.Format(CultureInfo.CurrentCulture, "{0:P0}", _visionSeverity);




	// サイドバーの強さスライダーの可視。色相系(P型・D型・T型)のいずれかが表示中で、かつテキストモード(コントラスト確認)でないときだけ見せる。テキストモード中は色リストごと色覚行が隠れてスライダーが何も動かさないため隠す。1色覚だけ、またはどれも表示していないときも隠す。
	public Visibility SidebarVisionSeverityVisibility => (_showProtan || _showDeutan || _showTritan) && !EffectiveContrastTextMode ? Visibility.Visible : Visibility.Collapsed;




	// マトリックス窓の強さスライダーの可視。色覚シミュレーションが色相系(P型・D型・T型)のときだけ見せる。フルカラー・1色覚のときは隠す。
	public Visibility MatrixVisionSeverityVisibility => _matrixVision is ColorVisionType.Protan or ColorVisionType.Deutan or ColorVisionType.Tritan ? Visibility.Visible : Visibility.Collapsed;




	// コントラスト確認欄に入力した文字列。テキスト欄と双方向で束縛し、設定に永続化する。文字色は色1なので、内容を変えてもコントラストの判定には影響しない。
	public string ContrastSampleText
	{
		get => _contrastSampleText;
		set
		{
			string text = value ?? string.Empty;

			if (_contrastSampleText == text)
			{
				return;
			}

			_contrastSampleText = text;
			OnPropertyChanged(nameof(ContrastSampleText));
		}
	}




	// Ctrl+C と編集メニューの「コピー」が色1を書き出す既定の形式の選択 (0=16進, 1=16進+α, 2=rgb, 3=rgba, 4=hsl, 5=hsla, 6=hwb, 7=パック, 8=パック+α, 9-12=ターミナル前景のトゥルーカラー/256/16/8, 13-16=ターミナル背景のトゥルーカラー/256/16/8)。番号は CopyFormatKeys の並びに対応し、範囲外は 16 進 (0) に丸める。設定ページの ComboBox は識別子 (CopyFormatKey) を SelectedValue で束縛するため、こちらは内部の選択番号として保つ。各色の RGB は変えず、コピー時の書き出し形式だけを切り替える。
	public int CopyFormatIndex
	{
		get => _copyFormatIndex;
		set
		{
			int index = (value >= 0 && value < CopyFormatKeys.Length) ? value : 0;

			if (_copyFormatIndex == index)
			{
				return;
			}

			_copyFormatIndex = index;
			OnPropertyChanged(nameof(CopyFormatIndex));
			OnPropertyChanged(nameof(CopyFormatKey));
		}
	}




	// 既定のコピー形式の識別子。設定ページの ComboBox が各項目の Tag を SelectedValue として TwoWay で束縛する。CopyFormatsViewModel.Resolve へ渡して、Ctrl+C と「コピー」が書き出す文字列と「コピー」項目のキャプションを決める。未知の識別子は 16 進にフォールバックする。
	public string CopyFormatKey
	{
		get => CopyFormatKeys[_copyFormatIndex];
		set
		{
			int index = Array.IndexOf(CopyFormatKeys, value);
			CopyFormatIndex = index >= 0 ? index : 0;
		}
	}




	// Ctrl+Shift+C で色1を書き出す2つ目の既定の形式の選択番号 (CopyFormatKeys の番号)。1つ目 (CopyFormatIndex) と独立。設定ページの2つ目の ComboBox が CopyFormatKey2 を介して束縛する。
	public int CopyFormatIndex2
	{
		get => _copyFormatIndex2;
		set
		{
			int index = (value >= 0 && value < CopyFormatKeys.Length) ? value : 0;

			if (_copyFormatIndex2 == index)
			{
				return;
			}

			_copyFormatIndex2 = index;
			OnPropertyChanged(nameof(CopyFormatIndex2));
			OnPropertyChanged(nameof(CopyFormatKey2));
		}
	}




	// 2つ目のコピー形式の識別子。設定ページの2つ目の ComboBox が各項目の Tag を SelectedValue として TwoWay で束縛する。CopyFormatsViewModel.Resolve へ渡して、Ctrl+Shift+C と2つ目の「コピー」項目が書き出す文字列とキャプションを決める。未知の識別子は rgb() にフォールバックする。
	public string CopyFormatKey2
	{
		get => CopyFormatKeys[_copyFormatIndex2];
		set
		{
			int index = Array.IndexOf(CopyFormatKeys, value);
			CopyFormatIndex2 = index >= 0 ? index : Array.IndexOf(CopyFormatKeys, "rgb");
		}
	}




	// 作れる色を表示上どの制約へ丸めるか。制限を選ぶと色見本・16進表記・全スライダーのグラデーション・中央パッドがその制約で表せる色だけで表示される。各色の RGB(スライダーの値の源)は変えないため、制限なしに戻せばすぐ元の色が現れる。各メニュー項目はモード別の真偽プロパティ(IsLimitNone など)を介してこれを切り替える。
	public ColorLimitMode LimitMode
	{
		get => _limitMode;
		set
		{
			if (_limitMode == value)
			{
				return;
			}

			_limitMode = value;

			if (value != ColorLimitMode.None)
			{
				_lastNonNoneLimitMode = value;
			}

			OnPropertyChanged(nameof(LimitMode));
			OnPropertyChanged(nameof(IsColorLimited));
			OnPropertyChanged(nameof(IsLimitNone));
			OnPropertyChanged(nameof(IsLimitWebSafe));
			OnPropertyChanged(nameof(IsLimitRgb565));
			OnPropertyChanged(nameof(IsLimitRgb555));
			OnPropertyChanged(nameof(IsLimitRgb444));
			OnPropertyChanged(nameof(IsLimitRgb332));
			OnPropertyChanged(nameof(IsLimitTerm256));
			OnPropertyChanged(nameof(IsLimitTerm16));
			OnPropertyChanged(nameof(IsLimitTerm8));
			OnPropertyChanged(nameof(LimitNoneShortcut));
			OnPropertyChanged(nameof(LimitWebSafeShortcut));
			OnPropertyChanged(nameof(LimitRgb565Shortcut));
			OnPropertyChanged(nameof(LimitRgb555Shortcut));
			OnPropertyChanged(nameof(LimitRgb444Shortcut));
			OnPropertyChanged(nameof(LimitRgb332Shortcut));
			OnPropertyChanged(nameof(LimitTerm256Shortcut));
			OnPropertyChanged(nameof(LimitTerm16Shortcut));
			OnPropertyChanged(nameof(LimitTerm8Shortcut));
			NotifySnapChanged();
		}
	}




	// 色制限が掛かっているか(制限なし以外か)。コマンドバーの分割ボタンの点灯(オン/オフ)が双方向で束縛し、ボタン部の押下で制限の解除と復帰を切り替える。オンへ戻す先は ToggleLimit と同じく直前に選んでいたモード(無ければ WebSafe)。LimitMode の変更時に通知する。
	public bool IsColorLimited
	{
		get => _limitMode != ColorLimitMode.None;
		set
		{
			if (value == IsColorLimited)
			{
				return;
			}

			LimitMode = value ? _lastNonNoneLimitMode : ColorLimitMode.None;
		}
	}




	// 色制限のメニュー(ラジオ)が束縛する、モード別の選択状態。真にすると対応するモードへ切り替える。偽の書き込みは、同じグループの別項目が選ばれて外れたときに来るため無視する。読み取りは現在のモードに一致するかを返し、LimitMode の変更時にまとめて通知される。
	public bool IsLimitNone
	{
		get => _limitMode == ColorLimitMode.None;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.None;
			}
		}
	}




	public bool IsLimitWebSafe
	{
		get => _limitMode == ColorLimitMode.WebSafe;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.WebSafe;
			}
		}
	}




	public bool IsLimitRgb565
	{
		get => _limitMode == ColorLimitMode.Rgb565;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.Rgb565;
			}
		}
	}




	public bool IsLimitRgb555
	{
		get => _limitMode == ColorLimitMode.Rgb555;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.Rgb555;
			}
		}
	}




	public bool IsLimitRgb444
	{
		get => _limitMode == ColorLimitMode.Rgb444;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.Rgb444;
			}
		}
	}




	public bool IsLimitRgb332
	{
		get => _limitMode == ColorLimitMode.Rgb332;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.Rgb332;
			}
		}
	}




	public bool IsLimitTerm256
	{
		get => _limitMode == ColorLimitMode.Term256;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.Term256;
			}
		}
	}




	public bool IsLimitTerm16
	{
		get => _limitMode == ColorLimitMode.Term16;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.Term16;
			}
		}
	}




	public bool IsLimitTerm8
	{
		get => _limitMode == ColorLimitMode.Term8;
		set
		{
			if (value)
			{
				LimitMode = ColorLimitMode.Term8;
			}
		}
	}




	// 色制限の往復ショートカット(L)の表示文字を、指定したモードの項目について返す。L は「制限なし」と直前に選んだ制限モードを往復する。今が制限中なら次に L が向かう先は「制限なし」、今が制限なしなら向かう先は直前のモードのため、その移動先の項目にだけ "L" を出し、ほかは空にする。これでメニューの表記が現在の状態と食い違わないようにする。
	private string LimitShortcutText(ColorLimitMode mode)
	{
		if (mode == ColorLimitMode.None)
		{
			return _limitMode != ColorLimitMode.None ? "L" : string.Empty;
		}

		return (_limitMode == ColorLimitMode.None && _lastNonNoneLimitMode == mode) ? "L" : string.Empty;
	}




	// 各色制限メニュー項目に出すショートカット文字。LimitShortcutText が今の状態から L の移動先を判定し、その項目にだけ "L" を返す。LimitMode の変更時にまとめて通知する。
	public string LimitNoneShortcut => LimitShortcutText(ColorLimitMode.None);
	public string LimitWebSafeShortcut => LimitShortcutText(ColorLimitMode.WebSafe);
	public string LimitRgb565Shortcut => LimitShortcutText(ColorLimitMode.Rgb565);
	public string LimitRgb555Shortcut => LimitShortcutText(ColorLimitMode.Rgb555);
	public string LimitRgb444Shortcut => LimitShortcutText(ColorLimitMode.Rgb444);
	public string LimitRgb332Shortcut => LimitShortcutText(ColorLimitMode.Rgb332);
	public string LimitTerm256Shortcut => LimitShortcutText(ColorLimitMode.Term256);
	public string LimitTerm16Shortcut => LimitShortcutText(ColorLimitMode.Term16);
	public string LimitTerm8Shortcut => LimitShortcutText(ColorLimitMode.Term8);




	// 色制限の解除と復帰を切り替える。制限中なら None にして下地のフルカラーを見せ、None なら直前に選んでいたモードへ戻す。直前のモードは制限を選ぶたびに記録しており、一度も選んでいなければ WebSafe へ戻す。
	public void ToggleLimit()
	{
		LimitMode = (_limitMode == ColorLimitMode.None) ? _lastNonNoneLimitMode : ColorLimitMode.None;
	}




	// 色制限の現在の設定一式(モード・距離計算・ターミナル参照テーマ)。描画コントロールとパレット警告がこれを読み、Snap と同じ設定で表示を丸める。モード・距離計算・参照テーマのいずれかが変わると通知する。
	public SnapSettings CurrentSnap => new SnapSettings(_limitMode, _snapMetric, _terminalTheme);




	// 2次元スライダー(LCH の L-C 平面・Lab の a-b 平面)で色域外をどう見せるか。描画コントロールがこれを読み、色域外の塗り・境界線・斜線を切り替える。色域内の表示や各色の RGB には影響しない。
	public GamutOutOfRangeStyle OutOfRangeStyle => _oogStyle;




	// 色域外の見せ方の選択(0=クランプ色塗り+境界線, 1=同+斜線, 2=白塗り+斜線)。設定ページの ComboBox が束縛する。2次元スライダーの色域外の描き方だけを変える。
	public int OutOfRangeStyleIndex
	{
		get
		{
			int index = Array.IndexOf(OogStyleByIndex, _oogStyle);
			return index >= 0 ? index : 1;
		}

		set
		{
			GamutOutOfRangeStyle style = (value >= 0 && value < OogStyleByIndex.Length) ? OogStyleByIndex[value] : GamutOutOfRangeStyle.FillBoundaryHatch;

			if (_oogStyle == style)
			{
				return;
			}

			_oogStyle = style;
			OnPropertyChanged(nameof(OutOfRangeStyleIndex));
			OnPropertyChanged(nameof(OutOfRangeStyle));
		}
	}




	// 設定ページで上級者向け設定を表示するか。設定ページのトグルが束縛し、オンのときスライダーつまみレンズの調整などを見せる。
	public bool AdvancedSettings
	{
		get => _advancedSettings;

		set
		{
			if (_advancedSettings == value)
			{
				return;
			}

			_advancedSettings = value;
			OnPropertyChanged(nameof(AdvancedSettings));
			OnPropertyChanged(nameof(AdvancedSettingsVisibility));
		}
	}




	// 上級者向け設定の表示・非表示。AdvancedSettings に連動し、設定ページの該当セクションの可視性が束縛する。
	public Visibility AdvancedSettingsVisibility => _advancedSettings ? Visibility.Visible : Visibility.Collapsed;




	// スライダーつまみのレンズ効果(屈折・色収差)を掛けるか。オフにするとただの拡大になる。設定ページのトグルが束縛する。
	public bool LensEffectEnabled
	{
		get => _lensEffect;

		set
		{
			if (_lensEffect == value)
			{
				return;
			}

			_lensEffect = value;
			Helpers.LensTuning.LensEffect = value;
			OnPropertyChanged(nameof(LensEffectEnabled));
		}
	}




	// レンズの拡大率の係数。設定ページのスライダーが束縛する。0 で等倍(拡大なし)、1.0 で基準どおり、上げるほど拡大が強まる。
	public double LensMagnify
	{
		get => _lensMagnify;

		set
		{
			if (_lensMagnify == value)
			{
				return;
			}

			_lensMagnify = value;
			Helpers.LensTuning.Magnify = value;
			OnPropertyChanged(nameof(LensMagnify));
		}
	}




	// レンズの縁の屈折を掛けるか。設定ページのトグルが束縛する。
	public bool LensRefractionEnabled
	{
		get => _lensRefraction;

		set
		{
			if (_lensRefraction == value)
			{
				return;
			}

			_lensRefraction = value;
			Helpers.LensTuning.Refraction = value;
			OnPropertyChanged(nameof(LensRefractionEnabled));
		}
	}




	// 屈折の強さの倍率(基準値に掛ける)。設定ページのスライダーが束縛する。1.0 で基準どおり。
	public double LensRefractionStrength
	{
		get => _lensRefractionStrength;

		set
		{
			if (_lensRefractionStrength == value)
			{
				return;
			}

			_lensRefractionStrength = value;
			Helpers.LensTuning.RefractionStrength = value;
			OnPropertyChanged(nameof(LensRefractionStrength));
		}
	}




	// 屈折のベベル(縁から内側へ効く幅)の倍率(基準値に掛ける)。設定ページのスライダーが束縛する。色収差も同じ屈折を使うため影響を受ける。
	public double LensBevel
	{
		get => _lensBevel;

		set
		{
			if (_lensBevel == value)
			{
				return;
			}

			_lensBevel = value;
			Helpers.LensTuning.Bevel = value;
			OnPropertyChanged(nameof(LensBevel));
		}
	}




	// 色収差(縁のカラーフリンジ)の倍率(基準値に掛ける)。設定ページのスライダーが束縛する。1.0 で基準どおり、0 で色収差なし。屈折の一部のため屈折オフでは効かない。
	public double LensChromaSpread
	{
		get => _lensChromaSpread;

		set
		{
			if (_lensChromaSpread == value)
			{
				return;
			}

			_lensChromaSpread = value;
			Helpers.LensTuning.ChromaSpread = value;
			OnPropertyChanged(nameof(LensChromaSpread));
		}
	}




	// レンズの効きの全体設定を、描画コントロールが読む静的な保持先へまとめて反映する。構築時の復元後に一度呼ぶ。以後の個別変更は各プロパティの setter が反映する。
	private void SyncLensTuning()
	{
		Helpers.LensTuning.LensEffect = _lensEffect;
		Helpers.LensTuning.Magnify = _lensMagnify;
		Helpers.LensTuning.Refraction = _lensRefraction;
		Helpers.LensTuning.RefractionStrength = _lensRefractionStrength;
		Helpers.LensTuning.Bevel = _lensBevel;
		Helpers.LensTuning.ChromaSpread = _lensChromaSpread;
	}




	// スライダーつまみレンズの調整を既定値へ戻す。設定ページの「デフォルトに戻す」が呼ぶ。各プロパティ経由で静的な保持先へも反映し、束縛中の UI も更新する。
	public void ResetLensTuning()
	{
		LensEffectEnabled = Helpers.LensTuning.DefaultLensEffect;
		LensMagnify = Helpers.LensTuning.DefaultMagnify;
		LensRefractionEnabled = Helpers.LensTuning.DefaultRefraction;
		LensRefractionStrength = Helpers.LensTuning.DefaultRefractionStrength;
		LensBevel = Helpers.LensTuning.DefaultBevel;
		LensChromaSpread = Helpers.LensTuning.DefaultChromaSpread;
	}




	// 画面カラーピッカーのレンズの拡大率。設定ページのスライダーが束縛する。上げるほど1画素が大きく映り採色しやすくなる。
	public double ScreenPickerMagnify
	{
		get => _screenPickerMagnify;

		set
		{
			if (_screenPickerMagnify == value)
			{
				return;
			}

			_screenPickerMagnify = value;
			Helpers.ScreenPickerTuning.Magnify = value;
			// 拡大率の既定を明示的に変えたら、採色中に覚えた最後のライブ拡大率は捨てる。次回の採色はこの新しい既定から始める。
			Helpers.ScreenPickerTuning.LastBlockPx = 0;
			OnPropertyChanged(nameof(ScreenPickerMagnify));
		}
	}




	// 画面カラーピッカーのレンズの直径(DIP)。設定ページのスライダーが束縛する。
	public double ScreenPickerDiameter
	{
		get => _screenPickerDiameter;

		set
		{
			if (_screenPickerDiameter == value)
			{
				return;
			}

			_screenPickerDiameter = value;
			Helpers.ScreenPickerTuning.Diameter = value;
			OnPropertyChanged(nameof(ScreenPickerDiameter));
		}
	}




	// 画面カラーピッカーのガラス効果を掛けるか。設定ページのトグルが束縛する。オフにすると縁と拡大だけの素のルーペになる。
	public bool ScreenPickerGlassEffect
	{
		get => _screenPickerGlassEffect;

		set
		{
			if (_screenPickerGlassEffect == value)
			{
				return;
			}

			_screenPickerGlassEffect = value;
			Helpers.ScreenPickerTuning.GlassEffect = value;
			OnPropertyChanged(nameof(ScreenPickerGlassEffect));
		}
	}




	// 画面カラーピッカーの縁の屈折の強さの倍率(既定値に掛ける)。設定ページのスライダーが束縛する。1.0 で既定どおり、0 で屈折なし。ガラス効果オフでは効かない。
	public double ScreenPickerRefractionStrength
	{
		get => _screenPickerRefractionStrength;

		set
		{
			if (_screenPickerRefractionStrength == value)
			{
				return;
			}

			_screenPickerRefractionStrength = value;
			Helpers.ScreenPickerTuning.RefractionStrength = value;
			OnPropertyChanged(nameof(ScreenPickerRefractionStrength));
		}
	}




	// 画面カラーピッカーのレンズの効きの全体設定を、ピッカーが読む静的な保持先へまとめて反映する。構築時の復元後に一度呼ぶ。以後の個別変更は各プロパティの setter が反映する。
	private void SyncScreenPickerTuning()
	{
		Helpers.ScreenPickerTuning.Magnify = _screenPickerMagnify;
		Helpers.ScreenPickerTuning.Diameter = _screenPickerDiameter;
		Helpers.ScreenPickerTuning.GlassEffect = _screenPickerGlassEffect;
		Helpers.ScreenPickerTuning.RefractionStrength = _screenPickerRefractionStrength;
	}




	// 画面カラーピッカーのレンズ調整を既定値へ戻す。設定ページの「デフォルトに戻す」が呼ぶ。各プロパティ経由で静的な保持先へも反映し、束縛中の UI も更新する。
	public void ResetScreenPickerTuning()
	{
		ScreenPickerMagnify = Helpers.ScreenPickerTuning.DefaultMagnify;
		ScreenPickerDiameter = Helpers.ScreenPickerTuning.DefaultDiameter;
		ScreenPickerGlassEffect = Helpers.ScreenPickerTuning.DefaultGlassEffect;
		ScreenPickerRefractionStrength = Helpers.ScreenPickerTuning.DefaultRefractionStrength;
	}




	// 最も近いパレット色を選ぶ距離計算の選択(0=知覚的(Lab), 1=redmean, 2=単純RGB)。設定ページの ComboBox が束縛する。色制限の格子モード・ターミナルモードの丸め方を変える。各色の RGB は変えない。
	public int SnapMetricIndex
	{
		get
		{
			int index = Array.IndexOf(SnapMetricByIndex, _snapMetric);
			return index >= 0 ? index : 0;
		}

		set
		{
			SnapMetric metric = (value >= 0 && value < SnapMetricByIndex.Length) ? SnapMetricByIndex[value] : SnapMetric.Lab;

			if (_snapMetric == metric)
			{
				return;
			}

			_snapMetric = metric;
			OnPropertyChanged(nameof(SnapMetricIndex));
			NotifySnapChanged();
		}
	}




	// ターミナルモードで基本16色を解決する参照テーマの選択(0=Campbell, 1=VGA, 2=xterm)。設定ページの ComboBox が束縛する。ターミナルモードの表示色と、ターミナル形式のコピー・貼り付けの 0-15 の実RGBを変える。
	public int TerminalThemeIndex
	{
		get
		{
			int index = Array.IndexOf(TerminalThemeByIndex, _terminalTheme);
			return index >= 0 ? index : 0;
		}

		set
		{
			TerminalTheme theme = (value >= 0 && value < TerminalThemeByIndex.Length) ? TerminalThemeByIndex[value] : TerminalTheme.Campbell;

			if (_terminalTheme == theme)
			{
				return;
			}

			_terminalTheme = theme;
			OnPropertyChanged(nameof(TerminalThemeIndex));
			NotifySnapChanged();
		}
	}




	// ターミナルのコピーで ESC をどの表記で書き出すかの選択(0=実体ESC, 1=\e, 2=\x1b, 3=\033, 4=u001b, 5=^[)。設定ページの ComboBox が束縛する。コピー文字列の見た目だけを変え、色や丸めには影響しない。
	public int TerminalEscIndex
	{
		get
		{
			int index = Array.IndexOf(TerminalEscByIndex, _terminalEsc);
			return index >= 0 ? index : 0;
		}

		set
		{
			TerminalEscStyle style = (value >= 0 && value < TerminalEscByIndex.Length) ? TerminalEscByIndex[value] : TerminalEscStyle.Hex;

			if (_terminalEsc == style)
			{
				return;
			}

			_terminalEsc = style;
			OnPropertyChanged(nameof(TerminalEscIndex));
		}
	}




	// ターミナルのコピー文字列で先頭の ESC をどの表記にするか。コピー形式の整形が読む。
	public TerminalEscStyle EscStyle => _terminalEsc;




	// CSS のカラー関数のコピーでアルファをどの表記で書き出すかの選択(0=0–1の数値, 1=0–100%)。設定ページの ComboBox が束縛する。rgba()/hsla()/hwb() 等のコピー文字列のアルファ表記だけを変え、色や丸めには影響しない。
	public int WebAlphaUnitIndex
	{
		get
		{
			int index = Array.IndexOf(WebAlphaUnitByIndex, _webAlphaUnit);
			return index >= 0 ? index : 0;
		}

		set
		{
			WebAlphaUnit unit = (value >= 0 && value < WebAlphaUnitByIndex.Length) ? WebAlphaUnitByIndex[value] : WebAlphaUnit.Number;

			if (_webAlphaUnit == unit)
			{
				return;
			}

			_webAlphaUnit = unit;
			OnPropertyChanged(nameof(WebAlphaUnitIndex));
		}
	}




	// CSS のカラー関数のコピーでアルファをどの表記で書き出すか。コピー形式の整形が読む。
	public WebAlphaUnit CopyAlphaUnit => _webAlphaUnit;




	// 2つ目のコピー形式 (Ctrl+Shift+C) のアルファ表記の選択(0=0–1の数値, 1=0–100%)。設定ページの2つ目のコピー形式エクスパンダーの ComboBox が束縛する。1つ目(WebAlphaUnitIndex)と独立し、色や丸めには影響しない。
	public int WebAlphaUnit2Index
	{
		get
		{
			int index = Array.IndexOf(WebAlphaUnitByIndex, _webAlphaUnit2);
			return index >= 0 ? index : 0;
		}

		set
		{
			WebAlphaUnit unit = (value >= 0 && value < WebAlphaUnitByIndex.Length) ? WebAlphaUnitByIndex[value] : WebAlphaUnit.Number;

			if (_webAlphaUnit2 == unit)
			{
				return;
			}

			_webAlphaUnit2 = unit;
			OnPropertyChanged(nameof(WebAlphaUnit2Index));
		}
	}




	// 2つ目のコピー形式のアルファをどの表記で書き出すか。2つ目のコピーの整形が読む。
	public WebAlphaUnit CopyAlphaUnit2 => _webAlphaUnit2;




	// ターミナルのコピーで末尾にリセットを付けるか。コピーのサブメニューのトグルが束縛し、コピー形式の整形が読む。色や丸めには影響しない。
	public bool TerminalResetSuffix
	{
		get => _terminalResetSuffix;
		set
		{
			if (_terminalResetSuffix == value)
			{
				return;
			}

			_terminalResetSuffix = value;
			OnPropertyChanged(nameof(TerminalResetSuffix));
		}
	}




	// 貼り付けた色の書式に合わせて、対応するタブ(と HSV/HSL の副モード)へ自動で切り替えるか。編集メニューのトグルが束縛し、貼り付け処理が読む。状態は設定に永続化する。色や丸めには影響しない。
	public bool SwitchTabOnPaste
	{
		get => _switchTabOnPaste;
		set
		{
			if (_switchTabOnPaste == value)
			{
				return;
			}

			_switchTabOnPaste = value;
			OnPropertyChanged(nameof(SwitchTabOnPaste));
		}
	}




	// アルファ値のスライダー類を、タブの中身の下に常駐させて表示するか。ツールバーと表示メニューのトグルが双方向で束ね、本体はこの真偽でアルファのスライダー領域の表示を切り替える。状態は設定に永続化する。色や丸めには影響しない。
	public bool ShowAlpha
	{
		get => _showAlpha;
		set
		{
			if (_showAlpha == value)
			{
				return;
			}

			_showAlpha = value;
			OnPropertyChanged(nameof(ShowAlpha));
			OnPropertyChanged(nameof(AlphaPanelVisibility));
		}
	}




	// アルファのスライダー領域の表示状態。ShowAlpha を XAML の Visibility へ写す。タブの中身の下に常駐するアルファ領域がこれを束縛する。
	public Visibility AlphaPanelVisibility => _showAlpha ? Visibility.Visible : Visibility.Collapsed;




	// コマンドバーの各ボタンにキャプション(名前)を表示するか。既定はオン。表示メニューのトグルが双方向で束ね、オフのときコマンドバーの各ボタンはアイコンだけになり横幅を節約する。状態は設定に永続化する。色や丸めには影響しない。
	public bool ShowToolbarCaption
	{
		get => _showToolbarCaption;
		set
		{
			if (_showToolbarCaption == value)
			{
				return;
			}

			_showToolbarCaption = value;
			OnPropertyChanged(nameof(ShowToolbarCaption));
			OnPropertyChanged(nameof(ToolbarCaptionVisibility));
		}
	}




	// コマンドバーのキャプションの表示状態。ShowToolbarCaption を XAML の Visibility へ写す。コマンドバーの各ボタンのキャプション(TextBlock)がこれを束縛し、オフのときアイコンだけを残して畳む。
	public Visibility ToolbarCaptionVisibility => _showToolbarCaption ? Visibility.Visible : Visibility.Collapsed;




	// 色制限の設定(モード・距離計算・参照テーマ)が変わったときに、表示の丸めが一斉に変わるのをまとめて通知する。サイドバーの全色パネル、全スライダーの背景、各表色系のパッドが追従する。色相スライダーは制限なしでは一定だが、制限下では段階的になるため明示的に含める。描画コントロールとパレット警告は CurrentSnap の変化で作り直す。
	private void NotifySnapChanged()
	{
		OnPropertyChanged(nameof(CurrentSnap));
		RefreshAllColorDisplays();
		NotifyColor1Derived();
		NotifyCmykDerived();
		NotifyYuvDerived();
		OnPropertyChanged(nameof(GrayTrackBrush));
		OnPropertyChanged(nameof(HueTrackBrush));
		OnPropertyChanged(nameof(LchLightnessTrackBrush));
		OnPropertyChanged(nameof(LchChromaTrackBrush));
		OnPropertyChanged(nameof(LchHueTrackBrush));
		OnPropertyChanged(nameof(LabLightnessTrackBrush));
		OnPropertyChanged(nameof(LabLightnessTrackBrushVertical));
		OnPropertyChanged(nameof(LabATrackBrush));
		OnPropertyChanged(nameof(LabATrackBrushVertical));
		OnPropertyChanged(nameof(LabBTrackBrush));
		OnPropertyChanged(nameof(LabBTrackBrushVertical));
		OnPropertyChanged(nameof(Color2Brush));
		OnPropertyChanged(nameof(Color2HexText));
		OnPropertyChanged(nameof(Color2ForegroundBrush));
	}




	// 色1の不透明度(アルファ, 0–255)。アクティブな色に属する値で、RGB と独立して編集する。アルファ専用スライダーが束縛する。
	public double Alpha
	{
		get => _alpha;
		set => SetAlpha(value);
	}




	// アルファ値の表示単位(0=0–255, 1=00–FF, 2=0–100%, 3=0.0–1.0)。コンボボックスが束縛する。スライダーの値は常に 0–255 で扱い、これは数値表示の見せ方だけを切り替える。
	public int AlphaUnitIndex
	{
		get => _alphaUnit switch
		{
			AlphaUnit.Hex => 1,
			AlphaUnit.Percent => 2,
			AlphaUnit.Normalized => 3,
			_ => 0,
		};
		set
		{
			AlphaUnit unit = value switch
			{
				1 => AlphaUnit.Hex,
				2 => AlphaUnit.Percent,
				3 => AlphaUnit.Normalized,
				_ => AlphaUnit.Byte,
			};

			if (_alphaUnit == unit)
			{
				return;
			}

			_alphaUnit = unit;
			OnPropertyChanged(nameof(AlphaUnitIndex));
			OnPropertyChanged(nameof(AlphaLargeStep));
		}
	}




	// アルファスライダーの Page Up/Down の移動量(大ステップ)。スライダーの値は常に 0–255 のため、255・FF 表示では 16(=0x10)、100%・1.0 表示ではどちらも全体の1/10に当たる 25.5 を返す。表示単位に追従させ、どの表示でも手応えをそろえる。
	public double AlphaLargeStep => _alphaUnit is AlphaUnit.Percent or AlphaUnit.Normalized ? 25.5 : 16.0;




	// R・G・B の表示単位(0=0–255, 1=00–FF, 2=0.0–1.0)。コンボボックスが束縛する。スライダーの値は常に 0–255 で扱い、これは数値表示と入力解釈の見せ方だけを切り替える。
	public int RgbUnitIndex
	{
		get => _rgbUnit switch
		{
			RgbUnit.Hex => 1,
			RgbUnit.Normalized => 2,
			_ => 0,
		};
		set
		{
			RgbUnit unit = value switch
			{
				1 => RgbUnit.Hex,
				2 => RgbUnit.Normalized,
				_ => RgbUnit.Byte,
			};

			if (_rgbUnit == unit)
			{
				return;
			}

			_rgbUnit = unit;
			OnPropertyChanged(nameof(RgbUnitIndex));
			OnPropertyChanged(nameof(RgbLargeStep));
		}
	}




	// R・G・B・無彩色グレースライダーの Page Up/Down の移動量(大ステップ)。スライダーの値は常に 0–255 のため、255・FF 表示では 16(=0x10)、1.0 表示では表示上 0.1 ぶんに当たる 25.5 を返す。表示単位に追従させ、どの表示でも手応えをそろえる。
	public double RgbLargeStep => _rgbUnit == RgbUnit.Normalized ? 25.5 : 16.0;




	// 色1を現在のアルファで重ねた表示色のブラシ。色パネル右4分の1の透過プレビューで、市松模様の上に重ねる。色制限の丸めは色1と同じく効かせ、不透明度だけを上乗せする。
	public Brush Color1AlphaBrush
	{
		get
		{
			Color color = Color1Display;
			return new SolidColorBrush(Color.FromArgb((byte)Math.Round(_alpha), color.R, color.G, color.B));
		}
	}




	// 不透明度スライダーの背景。現在の色1を、透明(左)から不透明(右)へ振った横グラデーション。スライダー側で市松模様を敷くため、透明寄りでは下の市松が透ける。
	public Brush AlphaTrackBrush
	{
		get
		{
			Color color = Color1Display;
			var brush = new LinearGradientBrush
			{
				StartPoint = new Point(0.0, 0.5),
				EndPoint = new Point(1.0, 0.5),
			};

			brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x00, color.R, color.G, color.B), Offset = 0.0 });
			brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xFF, color.R, color.G, color.B), Offset = 1.0 });
			return brush;
		}
	}




	// 不透明度を設定する。値が変われば、透過プレビューと表示数値を更新する。アルファを反映する設定では文字色とコントラストの見え方も不透明度で変わるため、コントラスト確認欄の文字色と比・スライダーも取り直す(設定がオフなら値は変わらず通知は空振りする)。不透明度は色相・彩度や他の表色系には影響しないため、それらの再同期は行わない。
	private void SetAlpha(double value)
	{
		double clamped = Math.Clamp(value, 0.0, 255.0);

		if (clamped == _alpha)
		{
			return;
		}

		RecordContinuousChange();
		_alpha = clamped;
		SyncActiveColorItem();
		OnPropertyChanged(nameof(Alpha));
		OnPropertyChanged(nameof(Color1AlphaBrush));
		OnPropertyChanged(nameof(ContrastTextBrush));
		NotifyContrastChanged();
	}




	// 色1(編集中)のプレビュー。
	public Brush Color1Brush => new SolidColorBrush(Color1Display);




	// 色1の現在の表示色。色制限が有効ならその丸めを反映した、画面に見えているとおりの色。クリップボードへ書き出す文字列はこの色を基にする。
	public Color DisplayedColor1 => Color1Display;




	// テキストモードの背景色のプレビュー。コントラスト確認用テキスト欄の背景に敷く。
	public Brush Color2Brush => new SolidColorBrush(Color2Display);




	public string Color1HexText => HexText(Color1Display);




	public string Color2HexText => HexText(Color2Display);




	// 色1のスウォッチ上に重ねる文字の前景色。背景(色1)に対して読みやすい黒か白を選ぶ。
	public Brush Color1ForegroundBrush => ContrastBrush(Color1Display);




	// 色2のスウォッチ上に重ねる文字の前景色。背景(色2)に対して読みやすい黒か白を選ぶ。
	public Brush Color2ForegroundBrush => ContrastBrush(Color2Display);




	// 適合・不適合を示すバッジの色。AA/AAA の各基準を満たすかで色2のスウォッチ上のコントラストバッジに使う。
	private static readonly Brush ContrastPassBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0x44));
	private static readonly Brush ContrastFailBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0x44, 0x44));




	// コントラスト確認欄の文字色。文字色の役に就いている色を表示の丸めで返す。アルファを反映する設定のときはその色の不透明度を載せ、テキスト欄の下に敷いた背景色へ透けて見えるようにする。オフのときは不透明にする。
	public Brush ContrastTextBrush
	{
		get
		{
			SidebarColorViewModel item = _colors[_textColorIndex];
			Color c = ToDisplay(item.Rgb);
			byte a = _contrastIncludeAlpha ? (byte)Math.Round(item.Alpha) : (byte)0xFF;
			return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
		}
	}




	// コントラスト比の算出に使う、文字色の実効的な不透明色。アルファを反映する設定で文字色の不透明度が満たないときは、文字色をその不透明度で背景色へ重ねた合成色を返し、背景に透けた見た目どおりのコントラストを測る。それ以外は文字色の表示色をそのまま返す。
	private Color EffectiveContrastColor1
	{
		get
		{
			SidebarColorViewModel item = _colors[_textColorIndex];
			Color c = ToDisplay(item.Rgb);

			if (!_contrastIncludeAlpha || item.Alpha >= 255.0)
			{
				return c;
			}

			Color foreground = Color.FromArgb((byte)Math.Round(item.Alpha), c.R, c.G, c.B);
			return ColorMetrics.AlphaComposite(foreground, Color2Display);
		}
	}




	// 文字色の役と背景色の役の WCAG コントラスト比。どちらの役の色が変わっても再計算する。アルファを反映する設定では、背景色へ透けた文字色の実効色で測る。コントラストチェックの表示に使う。
	public double ContrastRatio => ColorMetrics.ContrastRatio(EffectiveContrastColor1, Color2Display);




	// コントラスト比を「4.53 : 1」の形に整える。バッジ横の数値表示に使う。
	public string ContrastRatioText => $"{ContrastRatio:0.00} : 1";




	// WCAG の通常文字 AA(4.5:1 以上)を満たすか。バッジの色に使う。
	public Brush ContrastAaBrush => ContrastRatio >= 4.5 ? ContrastPassBrush : ContrastFailBrush;




	// WCAG の通常文字 AAA(7:1 以上)を満たすか。バッジの色に使う。
	public Brush ContrastAaaBrush => ContrastRatio >= 7.0 ? ContrastPassBrush : ContrastFailBrush;




	// コントラストスライダーの位置(-1–+1)。色1の色相と彩度を保ったまま明度(OKLch の L)だけを動かし、色2に対するコントラストを調整する。0 は色2と輝度が一致する点で、正へ動かすほど色2より明るく、負へ動かすほど暗くなり、いずれも離れるほどコントラストが上がる。
	public double ContrastSliderValue
	{
		get => _contrastSliderValue;
		set => ApplyContrastSlider(value);
	}




	// スライダー位置から色1を作って反映する。色相と彩度は基準値で固定し、位置を明度へ写してから OKLch で色へ戻す。色域を外れる明度では色相を保ったまま彩度が落ちる。色1の変更は連続編集として畳むため、確定操作ではなく RGB スライダーと同じ R・G・B の経路を通す。
	private void ApplyContrastSlider(double value)
	{
		double s = Math.Clamp(value, -1.0, 1.0);
		double lMatch = ContrastMatchLightness();
		double lTarget = s >= 0.0 ? lMatch + (s * (1.0 - lMatch)) : lMatch * (1.0 + s);
		Color color = OklabColor.FromOklch(lTarget, _contrastChroma, _contrastHue);

		_applyingContrastSlider = true;

		try
		{
			_contrastSliderValue = s;
			R = color.R;
			G = color.G;
			B = color.B;
			_contrastLocusColor = Color1Raw;
		}
		finally
		{
			_applyingContrastSlider = false;
		}

		OnPropertyChanged(nameof(ContrastSliderValue));
	}




	// 色1・色2の変化に合わせてスライダーのつまみ位置を取り直す。スライダー操作の最中は競合を避けるため触れない。色1がスライダー以外で編集されたときだけ、色相と彩度の基準を取り直す。
	private void RefreshContrastSlider()
	{
		if (_applyingContrastSlider)
		{
			return;
		}

		if (!SameRgb(Color1Raw, _contrastLocusColor))
		{
			(double _, double c, double h) = OklabColor.ToOklch(Color1Raw);
			_contrastChroma = c;
			_contrastHue = h;
			_contrastLocusColor = Color1Raw;
		}

		double v = ComputeContrastSliderValue();

		if (Math.Abs(v - _contrastSliderValue) > 1e-9)
		{
			_contrastSliderValue = v;
			OnPropertyChanged(nameof(ContrastSliderValue));
		}
	}




	// 現在の色1の明度から、スライダーのつまみ位置(-1–+1)を逆算する。輝度一致点を 0 とし、白側・黒側それぞれの可動幅で正規化する。
	private double ComputeContrastSliderValue()
	{
		double lCur = OklabColor.ToOklch(Color1Raw).L;
		double lMatch = ContrastMatchLightness();
		double s;

		if (lCur >= lMatch)
		{
			double span = 1.0 - lMatch;
			s = span > 1e-6 ? (lCur - lMatch) / span : 0.0;
		}
		else
		{
			s = lMatch > 1e-6 ? (lCur - lMatch) / lMatch : 0.0;
		}

		return Math.Clamp(s, -1.0, 1.0);
	}




	// 色1の色相・彩度を保ったまま、スライダーの基準色(もう片方の役の色)と相対輝度が一致する明度(OKLch の L)を二分法で求める。コントラスト1:1 の谷底にあたり、スライダーの 0 の基準になる。輝度は明度に対して単調に増えるため二分法で一意に定まる。輝度は丸めを焼き込まない素の基準色で測る。文字色の編集でアルファを反映する設定のときは、背景色へ透けた実効色の輝度で測るため、基準点は表示中のコントラスト比に揃う。
	private double ContrastMatchLightness()
	{
		double targetLum = ColorMetrics.RelativeLuminance(ContrastReferenceColor);
		double lo = 0.0;
		double hi = 1.0;

		for (int i = 0; i < 30; i++)
		{
			double mid = (lo + hi) / 2.0;
			double lum = EffectiveLuminanceAt(mid);

			if (lum < targetLum)
			{
				lo = mid;
			}
			else
			{
				hi = mid;
			}
		}

		return (lo + hi) / 2.0;
	}




	// 指定の明度(OKLch の L、色相・彩度は基準値)での実効的な相対輝度。文字色の役を編集していて、アルファを反映する設定で不透明度が透けるとき(0 と 255 の間)は、その明度の色を現在の不透明度で素の背景色へ重ねた合成色の輝度を返し、スライダーの基準点と位置の算出を表示中のコントラスト比に一致させる。背景色の役の編集では背景は文字の下にあって透けないため合成しない。完全な不透明・透明や設定オフのときも素の色の輝度を返す。
	private double EffectiveLuminanceAt(double l)
	{
		Color color = OklabColor.FromOklch(l, _contrastChroma, _contrastHue);

		if (_contrastFocusIsText && _contrastIncludeAlpha && _alpha > 0.0 && _alpha < 255.0)
		{
			Color foreground = Color.FromArgb((byte)Math.Round(_alpha), color.R, color.G, color.B);
			color = ColorMetrics.AlphaComposite(foreground, ContrastReferenceColor);
		}

		return ColorMetrics.RelativeLuminance(color);
	}




	// 不透明度を無視して2色の RGB が同じか。コントラストスライダーが、色1がスライダー以外で編集されたかを見分けるのに使う。
	private static bool SameRgb(Color a, Color b)
	{
		return a.R == b.R && a.G == b.G && a.B == b.B;
	}




	// 色1(編集中)の素の RGB。スライダーの値の源で、色制限の丸めの影響を受けない。色リストのアクティブ項目へはこの素の値を写し、表示の丸めを焼き込まない。
	private Color Color1Raw => Color.FromArgb(0xFF, (byte)_r, (byte)_g, (byte)_b);




	// 色1(編集中)の表示色。Web セーフ限定がオンなら丸めた色で見せる。下地の RGB は変えないため、限定を外せば元の色に戻る。
	private Color Color1Display => ToDisplay(Color1Raw);




	// テキストモードの背景色の表示色。色制限がオンなら丸めた色で見せる。保持している素の値そのものは変えない。
	private Color Color2Display => ToDisplay(ContrastBackgroundColor);




	// 色1(編集中)を指定の RGB へ丸ごと差し替える。パレットから色を選んだときに使う。RGB が変わらなければ通知しない。色1が変わると他の表色系の表示も変わるため、HSV・HSL のキャッシュを同期して各系統の表示をまとめて通知する。
	public void SetColor1FromRgb(byte r, byte g, byte b)
	{
		BeginDiscrete();

		try
		{
			if (r == (byte)_r && g == (byte)_g && b == (byte)_b)
			{
				return;
			}

			_r = r;
			_g = g;
			_b = b;

			OnPropertyChanged(nameof(R));
			OnPropertyChanged(nameof(G));
			OnPropertyChanged(nameof(B));
			NotifyColor1Derived();
			NotifyCmykDerived();
			SyncHsvCacheFromRgb();
			NotifyHsvDerived();
			SyncHslCacheFromRgb();
			NotifyHslDerived();
			NotifyHwbDerived();
			NotifyYuvDerived();
			NotifyLchDerived();
			NotifyLabDerived();
		}
		finally
		{
			EndDiscrete();
		}
	}




	// Mix タブのつまみで、編集中の色へ RGB を連続編集として反映する。スライダーやパッドの操作と同じく、一連のドラッグを1段の元に戻すへ畳む(離散の SetColor1FromRgb と違い、その都度の段は作らない)。値が同じなら何もしない。各表色系の表示・サイドバーのパネルも併せて更新される。
	public void SetActiveRgbFromMix(byte r, byte g, byte b)
	{
		if (r == (byte)_r && g == (byte)_g && b == (byte)_b)
		{
			return;
		}

		RecordContinuousChange();
		_r = r;
		_g = g;
		_b = b;

		OnPropertyChanged(nameof(R));
		OnPropertyChanged(nameof(G));
		OnPropertyChanged(nameof(B));
		NotifyColor1Derived();
		NotifyCmykDerived();
		SyncHsvCacheFromRgb();
		NotifyHsvDerived();
		SyncHslCacheFromRgb();
		NotifyHslDerived();
		NotifyHwbDerived();
		NotifyYuvDerived();
		NotifyLchDerived();
		NotifyLabDerived();
	}




	// 配色タブのドラッグ・明度スライダー・配色の適用で、複数の色へ RGB をまとめて連続編集として反映する。一連の操作を1段の元に戻すへ畳む(RecordContinuousChange を最初の一度だけ通す)。値が一つも変わらなければ何もしない。アクティブな色は作業値を取り直して全表色系の表示を更新し、それ以外はその色だけ書き換える。不透明度は保つ。
	public void SetHarmonyColors(IReadOnlyList<(int Index, byte R, byte G, byte B)> updates)
	{
		bool anyChange = false;

		foreach ((int index, byte r, byte g, byte b) in updates)
		{
			if (index < 0 || index >= _colors.Count)
			{
				continue;
			}

			if (index == _activeIndex)
			{
				if (r != (byte)_r || g != (byte)_g || b != (byte)_b)
				{
					anyChange = true;
					break;
				}
			}
			else
			{
				Color rgb = _colors[index].Rgb;

				if (r != rgb.R || g != rgb.G || b != rgb.B)
				{
					anyChange = true;
					break;
				}
			}
		}

		if (!anyChange)
		{
			return;
		}

		RecordContinuousChange();

		bool rolesAffected = false;

		foreach ((int index, byte r, byte g, byte b) in updates)
		{
			if (index < 0 || index >= _colors.Count)
			{
				continue;
			}

			if (index == _activeIndex)
			{
				_r = r;
				_g = g;
				_b = b;
				OnPropertyChanged(nameof(R));
				OnPropertyChanged(nameof(G));
				OnPropertyChanged(nameof(B));
				NotifyColor1Derived();
				NotifyCmykDerived();
				SyncHsvCacheFromRgb();
				NotifyHsvDerived();
				SyncHslCacheFromRgb();
				NotifyHslDerived();
				NotifyHwbDerived();
				NotifyYuvDerived();
				NotifyLchDerived();
				NotifyLabDerived();
			}
			else
			{
				SidebarColorViewModel item = _colors[index];
				item.Rgb = Color.FromArgb(0xFF, r, g, b);
				RefreshColorDisplay(item);
			}

			if (index == _textColorIndex || index == _bgColorIndex)
			{
				rolesAffected = true;
			}
		}

		if (rolesAffected)
		{
			NotifyContrastRolesChanged();
		}
	}




	// 色1を完全にランダムな RGB へ差し替える。各成分を 0–255 から無作為に選ぶ。不透明度と他の色は変えない。差し替えは SetColor1FromRgb を通すため、各表色系の表示も併せて更新される。
	public void RandomizeColor1()
	{
		BeginDiscrete();

		try
		{
			byte r = (byte)Random.Shared.Next(256);
			byte g = (byte)Random.Shared.Next(256);
			byte b = (byte)Random.Shared.Next(256);
			SetColor1FromRgb(r, g, b);
		}
		finally
		{
			EndDiscrete();
		}
	}




	// 文字色の役の色を黒(#000000)、背景色の役の色を白(#FFFFFF)にする。両役が同じ色に就いているときは、背景色の役を文字色以外の先頭の色へ就け直してから黒白にする。文字色・背景色の役を前提とする機能のため、コントラスト確認(テキストモード)でないときは何もしない。不透明度は変えない。差し替えた色がアクティブなら作業値も載せ替える。
	public void SetBlackAndWhite()
	{
		if (!EffectiveContrastTextMode)
		{
			return;
		}

		BeginDiscrete();

		try
		{
			EnsureContrastRoles();

			if (_textColorIndex == _bgColorIndex)
			{
				for (int i = 0; i < _colors.Count; i++)
				{
					if (i != _textColorIndex)
					{
						_bgColorIndex = i;
						break;
					}
				}
			}

			_colors[_textColorIndex].Rgb = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
			_colors[_bgColorIndex].Rgb = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
			RefreshColorDisplay(_colors[_textColorIndex]);
			RefreshColorDisplay(_colors[_bgColorIndex]);

			if (_activeIndex == _textColorIndex || _activeIndex == _bgColorIndex)
			{
				LoadActiveColor();
			}
			else
			{
				NotifyContrastRolesChanged();
			}
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// すべての色の不透明度を最大(255)へ揃えて完全不透明にする。RGB成分は変えず不透明度だけを揃える。全色すでに不透明なら EndDiscrete が無変化の段を残さない。
	public void MakeAllOpaque()
	{
		BeginDiscrete();

		try
		{
			foreach (SidebarColorViewModel item in _colors)
			{
				item.Alpha = 255.0;
				RefreshColorDisplay(item);
			}

			LoadActiveColor();
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// 貼り付けや履歴から色を反映する。RGB は常に色1へ差し替え、不透明度は値が与えられたときだけ反映する。与えられなければ現在の不透明度を保つ。
	public void ApplyColor(byte r, byte g, byte b, byte? alpha)
	{
		BeginDiscrete();

		try
		{
			SetColor1FromRgb(r, g, b);

			if (alpha.HasValue)
			{
				Alpha = alpha.Value;
			}
		}
		finally
		{
			EndDiscrete();
		}
	}




	// 指定位置の色の現在の表示色。色制限が有効ならその丸めを反映した、画面に見えているとおりの色を返す。サイドバーの色パネルの右クリックメニューが、その色をそのままコピーする基準にする。
	public Color DisplayedColorAt(int index)
	{
		return ToDisplay(_colors[index].Rgb);
	}




	// 指定位置の色へ RGB を反映する。不透明度は値が与えられたときだけ反映する。アクティブな色が対象なら作業値と各表色系の表示も更新するため ApplyColor を通す。それ以外は編集対象を切り替えずその色だけを書き換える。サイドバーの色パネルの右クリックメニューからの貼り付けに使う。
	public void ApplyColorToIndex(int index, byte r, byte g, byte b, byte? alpha)
	{
		if (index == _activeIndex)
		{
			ApplyColor(r, g, b, alpha);
			return;
		}

		BeginDiscrete();

		try
		{
			SidebarColorViewModel item = _colors[index];
			item.Rgb = Color.FromArgb(0xFF, r, g, b);

			if (alpha.HasValue)
			{
				item.Alpha = alpha.Value;
			}

			RefreshColorDisplay(item);

			if (index == _textColorIndex || index == _bgColorIndex)
			{
				NotifyContrastRolesChanged();
			}
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// 指定位置の色を完全にランダムな RGB へ差し替える。各成分を 0–255 から無作為に選ぶ。不透明度と他の色は変えない。アクティブな色が対象なら作業値と各表色系の表示も更新するため RandomizeColor1 を通す。
	public void RandomizeColorAt(int index)
	{
		if (index == _activeIndex)
		{
			RandomizeColor1();
			return;
		}

		byte r = (byte)Random.Shared.Next(256);
		byte g = (byte)Random.Shared.Next(256);
		byte b = (byte)Random.Shared.Next(256);
		ApplyColorToIndex(index, r, g, b, null);
	}




	// 全色を生成器の返す不透明色へ一斉に差し替える。各色の不透明度は保つ。色制限の丸めは表示にだけ掛かるため、生成した素の RGB をそのまま入れる。差し替えはまとめて1段の元に戻すに畳み、アクティブ色も載せ替わるため作業値を取り直す。
	private void ApplyRandomToAll(Func<Color> next)
	{
		BeginDiscrete();

		try
		{
			foreach (SidebarColorViewModel item in _colors)
			{
				Color c = next();
				item.Rgb = Color.FromArgb(0xFF, c.R, c.G, c.B);
				RefreshColorDisplay(item);
			}

			LoadActiveColor();
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// 各成分を 0–255 から無作為に選んだ不透明色。
	private static Color RandomRgb()
	{
		return Color.FromArgb(0xFF, (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256));
	}




	// 現在のサイドバーの色を不透明度込みで取り出す。お気に入りパレットへの保存に使う。各色は素の RGB で、表示の丸めは焼き込まない。
	public IReadOnlyList<(byte R, byte G, byte B, byte A)> CaptureSidebarColors()
	{
		var list = new List<(byte, byte, byte, byte)>(_colors.Count);

		foreach (SidebarColorViewModel item in _colors)
		{
			list.Add((item.Rgb.R, item.Rgb.G, item.Rgb.B, (byte)Math.Round(item.Alpha)));
		}

		return list;
	}




	// 指定の色でサイドバーの色を丸ごと置き換える。お気に入りパレットの一括取得に使う。件数も与えた色に合わせ、下限 MinColors・上限 MaxColors の範囲へ収める。各色の不透明度も復元し、表示の丸めは表示にだけ掛かるため素の RGB をそのまま入れる。差し替えはまとめて1段の元に戻すへ畳み、アクティブは先頭にして作業値を取り直す。Mix タブのポッチ位置は持ち越さず、Mix タブ側が次に正多角形へ配り直す。色が無ければ何もしない。
	public void LoadColorsIntoSidebar(IReadOnlyList<(byte R, byte G, byte B, byte A)> colors)
	{
		if (colors is null || colors.Count == 0)
		{
			return;
		}

		int count = Math.Clamp(colors.Count, MinColors, MaxColors);

		BeginDiscrete();

		try
		{
			while (_colors.Count > count)
			{
				_colors.RemoveAt(_colors.Count - 1);
			}

			while (_colors.Count < count)
			{
				_colors.Add(new SidebarColorViewModel());
			}

			for (int i = 0; i < count; i++)
			{
				(byte r, byte g, byte b, byte a) = colors[i];
				_colors[i].Rgb = Color.FromArgb(0xFF, r, g, b);
				_colors[i].Alpha = a;
				_colors[i].IsActive = false;
				_colors[i].MixX = double.NaN;
				_colors[i].MixY = double.NaN;
			}

			_activeIndex = 0;
			_colors[0].IsActive = true;
			EnsureContrastRoles();
			RefreshAllColorDisplays();
			NotifyColorCountChanged();
			LoadActiveColor();
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// 指定の明度・色相で、色域に収まる範囲から彩度を無作為に選んだ OKLCh の色。彩度は分布が偏らないよう面積一様(平方根)で取る。
	private static Color RandomOklchAt(double l, double h)
	{
		double cMax = LchColor.MaxChroma(LchSpace.Oklch, l, h);
		double c = cMax * Math.Sqrt(Random.Shared.NextDouble());
		return LchColor.ToRgb(LchSpace.Oklch, l, c, h);
	}




	// 全色を独立に完全ランダムな色へ変更する。各色の各成分を 0–255 から無作為に選ぶ。色同士の関係は考えないため最も自由で雑多な配色になる。不透明度は保つ。
	public void RandomizeAllColors()
	{
		ApplyRandomToAll(RandomRgb);
	}




	// 全色を、明度を揃えてランダムにする。OKLCh の明度 L を1つ無作為に選んで全色で共有し、各色は色相を無作為に、彩度をその明度・色相で色域に収まる範囲から選ぶ。知覚的に同じ明るさに見える配色になる。明度は灰へ潰れない中庸の範囲から選ぶ。不透明度は保つ。
	public void RandomizeAllUniformLightness()
	{
		double l = 0.25 + (Random.Shared.NextDouble() * 0.6);
		ApplyRandomToAll(() => RandomOklchAt(l, Random.Shared.NextDouble() * 360.0));
	}




	// 全色を、彩度を揃えてランダムにする。OKLCh の彩度 C を1つ無作為に選んで全色で共有し、各色はその彩度を保てる明度・色相を無作為に選ぶ。知覚的に同じ鮮やかさに見える配色になる。共有彩度を保てる組が一定回数で見つからないときは、色相に合わせて彩度を詰めて色域へ収める。不透明度は保つ。
	public void RandomizeAllUniformChroma()
	{
		double targetC = 0.04 + (Random.Shared.NextDouble() * 0.14);

		ApplyRandomToAll(() =>
		{
			for (int tries = 0; tries < 256; tries++)
			{
				double l = 0.15 + (Random.Shared.NextDouble() * 0.8);
				double h = Random.Shared.NextDouble() * 360.0;

				if (LchColor.MaxChroma(LchSpace.Oklch, l, h) >= targetC)
				{
					return LchColor.ToRgb(LchSpace.Oklch, l, targetC, h);
				}
			}

			double fallbackL = 0.5;
			double fallbackH = Random.Shared.NextDouble() * 360.0;
			double c = Math.Min(targetC, LchColor.MaxChroma(LchSpace.Oklch, fallbackL, fallbackH));
			return LchColor.ToRgb(LchSpace.Oklch, fallbackL, c, fallbackH);
		});
	}




	// 全色を、色相を揃えてランダムにする。OKLCh の色相 H を1つ無作為に選んで全色で共有し、各色は明度を無作為に、彩度をその明度・色相で色域に収まる範囲から選ぶ。同じ色相の淡色から濃色までが並ぶ単色系の配色になる。不透明度は保つ。
	public void RandomizeAllSingleHue()
	{
		double h = Random.Shared.NextDouble() * 360.0;
		ApplyRandomToAll(() => RandomOklchAt(0.12 + (Random.Shared.NextDouble() * 0.83), h));
	}




	// テキストモードで、全色をランダムにしつつ文字色・背景色の役が WCAG AA(4.5:1)以上のコントラストを満たすようにする。まず全色を完全ランダムにし、文字色・背景色の役だけは基準を満たす組へ差し替える。テキストモードでないときは何もしない。不透明度は保つ。
	public void RandomizeAllContrastSafe()
	{
		if (!EffectiveContrastTextMode)
		{
			return;
		}

		BeginDiscrete();

		try
		{
			EnsureContrastRoles();

			foreach (SidebarColorViewModel item in _colors)
			{
				Color c = RandomRgb();
				item.Rgb = Color.FromArgb(0xFF, c.R, c.G, c.B);
			}

			(Color text, Color background) = RandomContrastPair(4.5);

			// 役の色は不透明にする。コントラスト比は表示の丸めと不透明度を通した実効色で測られ、不透明度が満たないと役の色が背景へ透けて比が下がるため、保証した比をそのまま満たせるよう不透明で置く。
			SidebarColorViewModel textItem = _colors[_textColorIndex];
			textItem.Rgb = text;
			textItem.Alpha = 255.0;

			if (_bgColorIndex != _textColorIndex)
			{
				SidebarColorViewModel bgItem = _colors[_bgColorIndex];
				bgItem.Rgb = background;
				bgItem.Alpha = 255.0;
			}

			foreach (SidebarColorViewModel item in _colors)
			{
				RefreshColorDisplay(item);
			}

			LoadActiveColor();
		}
		finally
		{
			EndDiscrete();
		}

		RaiseColorListChanged();
	}




	// 無作為な不透明色の2色で、WCAG コントラスト比が minRatio 以上になる組を返す。比は実際にUIへ出るのと同じく、表示の丸め(色制限レンズ)を通した色で測る。一定回数で見つからなければ黒と白を返す。黒白は色域制限下でも各自へ丸まり最大の比を保つ。
	private (Color Text, Color Background) RandomContrastPair(double minRatio)
	{
		for (int i = 0; i < 4096; i++)
		{
			Color a = RandomRgb();
			Color b = RandomRgb();

			if (ColorMetrics.ContrastRatio(ToDisplay(a), ToDisplay(b)) >= minRatio)
			{
				return (a, b);
			}
		}

		return (Color.FromArgb(0xFF, 0x00, 0x00, 0x00), Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
	}




	// 貼り付けで解釈した hwb() を反映する。正規化オフのときは、貼った白み・黒み(退化域=白み+黒み>1 を含む)をキャッシュへそのまま入れて色1へ反映するため、スライダーとつまみが貼った値どおりになる。正規化オンのときは正準形でよいため、結果の RGB を色1へ反映してキャッシュは RGB 由来の和≤1 へ揃う。色相は灰でも貼った値を保つ。不透明度は明示されたときだけ反映する。RGB から色相を復元できない灰でも貼った色相を持たせるため、色相由来の表示は明示的に通知する。
	public void ApplyPastedHwb(double hue, double whiteness, double blackness, byte? alpha)
	{
		BeginDiscrete();

		try
		{
			double normalized = ((hue % 360.0) + 360.0) % 360.0;
			_cachedHue = normalized;

			if (_normalizeHwb)
			{
				(byte r, byte g, byte b) = ColorConversion.HwbToRgb(normalized, whiteness, blackness);
				SetColor1FromRgb(r, g, b);
			}
			else
			{
				_cachedWhiteness = Math.Clamp(whiteness, 0.0, 1.0);
				_cachedBlackness = Math.Clamp(blackness, 0.0, 1.0);
				ApplyHwbFromCache();
			}

			// 灰では RGB から色相を復元できないため、貼った色相を保ったうえで色相由来の表示(色相環・下地色・追従回転・スライダー背景)を通知する。色1が変わらない貼り付け(同じ灰)で色相だけ変わった場合も取りこぼさない。
			OnPropertyChanged(nameof(H));
			OnPropertyChanged(nameof(SvBaseColorBrush));
			OnPropertyChanged(nameof(SvPadRotation));
			OnPropertyChanged(nameof(SlSquarePadRotation));
			OnPropertyChanged(nameof(SlPadRotation));
			OnPropertyChanged(nameof(HwbPadRotation));
			OnPropertyChanged(nameof(HwbTrianglePadRotation));
			NotifySliderTrackBrushes();

			if (alpha.HasValue)
			{
				Alpha = alpha.Value;
			}
		}
		finally
		{
			EndDiscrete();
		}
	}




	// 貼り付けで解釈した lch()/oklch() を反映する。貼った書式に合う副モード(OKLCH/CIE LCH)へ切り替え、色域制限オフのときは貼った明度・彩度・色相(色域外を含む)をキャッシュへそのまま入れて色1へ反映するため、スライダーとつまみが貼った値どおりになる。色域制限オンのときは色域内へ収めた RGB を色1へ反映してキャッシュは RGB 由来へ揃う。色相は無彩色でも貼った値を保つ。不透明度は明示されたときだけ反映する。明度・彩度・色相は当該表色系の素の尺度(L は OKLCH で 0–1・CIE で 0–100、H は度)で受け取る。
	public void ApplyPastedLch(LchSpace space, double l, double c, double hue, byte? alpha)
	{
		BeginDiscrete();

		try
		{
			int index = space == LchSpace.CieLch ? 1 : 0;

			// 副モードを貼った書式へ合わせる。LchSpaceIndex の公開セッターはキャッシュを現在の RGB から取り直してしまい、これから入れる貼り付け値を消すため、内部の位置だけを直接更新して尺度・書式の通知を行う。
			if (_lchSpaceIndex != index)
			{
				_lchSpaceIndex = index;
				OnPropertyChanged(nameof(LchSpaceIndex));
				OnPropertyChanged(nameof(LchChromaFormatter));
				OnPropertyChanged(nameof(LchChromaStep));
				OnPropertyChanged(nameof(LchChromaNormStep));
				OnPropertyChanged(nameof(LchChromaNormLargeStep));
			}

			double normalized = ((hue % 360.0) + 360.0) % 360.0;

			if (_lchGamutLimit)
			{
				Color color = LchColor.ToRgb(space, l, c, normalized);
				_cachedLchHue = normalized;
				SetColor1FromRgb(color.R, color.G, color.B);
			}
			else
			{
				_cachedLchL = l;
				_cachedLchChroma = Math.Clamp(c, 0.0, LchColor.CMax(space));
				_cachedLchHue = normalized;
				ApplyLchFromCache();
			}

			// 無彩色では RGB から色相を復元できないため、貼った色相を保ったうえで LCH 由来の表示を通知する。色1が変わらない貼り付け(同じ無彩色)で色相だけ変わった場合も取りこぼさない。
			NotifyLchComponents();
			NotifyLchTrackBrushes();

			if (alpha.HasValue)
			{
				Alpha = alpha.Value;
			}
		}
		finally
		{
			EndDiscrete();
		}
	}




	// 貼り付けで解釈した lab()/oklab() を反映する。貼った書式に合う副モード(OKLab/CIE Lab)へ切り替え、色域制限オフのときは貼った明度・a・b(色域外を含む)をキャッシュへそのまま入れて色1へ反映するため、スライダーとつまみが貼った値どおりになる。色域制限オンのときは色域内へ収めた RGB を色1へ反映してキャッシュは RGB 由来へ揃う。不透明度は明示されたときだけ反映する。明度・a・b は当該表色系の素の尺度(L は OKLab で 0–1・CIE で 0–100)で受け取る。
	public void ApplyPastedLab(LchSpace space, double l, double a, double b, byte? alpha)
	{
		BeginDiscrete();

		try
		{
			int index = space == LchSpace.CieLch ? 1 : 0;

			// 副モードを貼った書式へ合わせる。LabSpaceIndex の公開セッターはキャッシュを現在の RGB から取り直してしまい、これから入れる貼り付け値を消すため、内部の位置だけを直接更新して尺度・書式の通知を行う。
			if (_labSpaceIndex != index)
			{
				_labSpaceIndex = index;
				OnPropertyChanged(nameof(LabSpaceIndex));
				OnPropertyChanged(nameof(LabAbFormatter));
				OnPropertyChanged(nameof(LabAbStep));
				OnPropertyChanged(nameof(LabAbNormStep));
				OnPropertyChanged(nameof(LabAbNormLargeStep));
			}

			if (_labGamutLimit)
			{
				Color color = LabColor.ToRgb(space, l, a, b);
				SetColor1FromRgb(color.R, color.G, color.B);
			}
			else
			{
				double abMax = LabColor.AbMax(space);
				_cachedLabL = Math.Clamp(l, 0.0, LabColor.LMax(space));
				_cachedLabA = Math.Clamp(a, -abMax, abMax);
				_cachedLabB = Math.Clamp(b, -abMax, abMax);
				ApplyLabFromCache();
			}

			// 色1が変わらない貼り付け(色域外を同じ RGB へ収める値)でもスライダー・数値が貼った値を映すよう、Lab 由来の表示を通知する。
			NotifyLabComponents();
			NotifyLabTrackBrushes();

			if (alpha.HasValue)
			{
				Alpha = alpha.Value;
			}
		}
		finally
		{
			EndDiscrete();
		}
	}




	// 直前の色状態へ戻せるか。編集メニューの「元に戻す」の有効・無効が束縛する。
	public bool CanUndo => _undoStack.Count > 0;




	// 戻した色状態をやり直せるか。編集メニューの「やり直し」の有効・無効が束縛する。連続編集のまとまりが開いている間は、その編集が分岐としてやり直しを無効化しうるため、締まるまで無効として扱い、有効に見えるのに押しても何も起きない不整合を防ぐ。
	public bool CanRedo => _redoStack.Count > 0 && !_gestureActive;




	// 一段戻す。連続編集が開いていれば先に締め、現在の状態をやり直し側へ退避してから直前の状態を復元する。
	public void Undo()
	{
		SealGesture();

		if (_undoStack.Count == 0)
		{
			return;
		}

		_redoStack.Add(CaptureSnapshot());
		Restore(RemoveLast(_undoStack));
		RaiseCanUndoRedo();
	}




	// 一段やり直す。現在の状態を戻す側へ退避してから、やり直し先の状態を復元する。
	public void Redo()
	{
		SealGesture();

		if (_redoStack.Count == 0)
		{
			return;
		}

		_undoStack.Add(CaptureSnapshot());
		Restore(RemoveLast(_redoStack));
		RaiseCanUndoRedo();
	}




	// 連続編集(スライダー・パッド)の変化を記録する。実際に色が変わるときだけ呼ぶ。まとまりの開始時に直前の状態を1件退避し、以後は無操作タイマーが切れるまで同じまとまりへ畳む。確定操作の最中や復元中は積まない。
	private void RecordContinuousChange()
	{
		if (_restoring || _transactionDepth > 0)
		{
			return;
		}

		if (!_gestureActive)
		{
			_gestureActive = true;
			PushCheckpoint(CaptureSnapshot());
		}

		RestartGestureTimer();
	}




	// 確定操作(入れ替え・貼り付け・パレット選択・ランダム・黒白)の開始。連続編集のまとまりを締め、最外の操作だけが直前の状態を1件退避する。内側の入れ子では退避しない。EndDiscrete と必ず対で呼ぶ。
	private void BeginDiscrete()
	{
		if (_transactionDepth++ > 0 || _restoring)
		{
			return;
		}

		SealGesture();
		PushCheckpoint(CaptureSnapshot());
	}




	// 確定操作の終了。最外まで戻ったとき、結果が直前と同じなら無変化の段を残さないよう退避を取り消す。
	private void EndDiscrete()
	{
		if (_transactionDepth == 0)
		{
			return;
		}

		_transactionDepth--;

		if (_transactionDepth == 0 && !_restoring)
		{
			ResolveCheckpoint();
		}
	}




	// 連続編集のまとまりを締める。確定操作の直前・戻す/やり直しの直前・無操作タイマーの満了で呼ぶ。結果が開始時と同じなら無変化の段を残さない。
	private void SealGesture()
	{
		if (!_gestureActive)
		{
			return;
		}

		_gestureActive = false;
		_gestureTimer?.Stop();
		ResolveCheckpoint();
	}




	// 直前に積んだ段を確定または取り消す。連続編集の締めと確定操作の終了で呼ぶ。結果が開始時と同じ無変化なら段を取り消してやり直しは元のまま残し、実際に色が変わっていれば段を残してやり直しを分岐として捨てる。
	private void ResolveCheckpoint()
	{
		if (_undoStack.Count > 0 && SnapshotEquals(_undoStack[^1], CaptureSnapshot()))
		{
			RemoveLast(_undoStack);
		}
		else
		{
			_redoStack.Clear();
		}

		RaiseCanUndoRedo();
	}




	// 直前の状態を戻す側へ積む。上限を超えた分は古いものから落とす。やり直し側は、無変化で取り消される段が消してしまわないよう、ここでは触れず、変化が確定した時点で別に捨てる。
	private void PushCheckpoint(ColorSnapshot snapshot)
	{
		_undoStack.Add(snapshot);

		if (_undoStack.Count > MaxUndoDepth)
		{
			_undoStack.RemoveAt(0);
		}

		RaiseCanUndoRedo();
	}




	// リストの末尾を取り出して返す。戻す/やり直しのスタック操作で使う。
	private static ColorSnapshot RemoveLast(List<ColorSnapshot> list)
	{
		ColorSnapshot last = list[^1];
		list.RemoveAt(list.Count - 1);
		return last;
	}




	// スナップショットを色へ適用する。適用そのものを履歴に積まないよう、記録を止めて行う。
	private void Restore(ColorSnapshot snapshot)
	{
		_restoring = true;

		try
		{
			ApplySnapshot(snapshot);
		}
		finally
		{
			_restoring = false;
		}
	}




	// スナップショットの色一覧・アクティブ位置・役の位置を色リストへ戻し、作業値と各表色系の表示をまとめて更新する。件数が合う分は項目を使い回して値だけ入れ替え、過不足は足し引きする。色の追加・削除・並べ替えもこれで一緒に戻る。
	private void ApplySnapshot(ColorSnapshot snapshot)
	{
		while (_colors.Count > snapshot.Entries.Length)
		{
			_colors.RemoveAt(_colors.Count - 1);
		}

		while (_colors.Count < snapshot.Entries.Length)
		{
			_colors.Add(new SidebarColorViewModel());
		}

		for (int i = 0; i < _colors.Count; i++)
		{
			ColorEntry entry = snapshot.Entries[i];
			_colors[i].Rgb = Color.FromArgb(0xFF, entry.R, entry.G, entry.B);
			_colors[i].Alpha = entry.A;
			_colors[i].IsActive = false;
		}

		_activeIndex = Math.Clamp(snapshot.ActiveIndex, 0, _colors.Count - 1);
		_colors[_activeIndex].IsActive = true;
		_textColorIndex = snapshot.TextIndex;
		_bgColorIndex = snapshot.BgIndex;
		EnsureContrastRoles();
		RefreshAllColorDisplays();
		NotifyColorCountChanged();
		LoadActiveColor();
		RaiseColorListChanged();
	}




	// 現在の色リスト(表示の丸めを焼き込まない素の RGB と不透明度)・アクティブ位置・役の位置を1件のスナップショットにする。アクティブな色は作業値から写す。
	private ColorSnapshot CaptureSnapshot()
	{
		var entries = new ColorEntry[_colors.Count];

		for (int i = 0; i < _colors.Count; i++)
		{
			if (i == _activeIndex)
			{
				entries[i] = new ColorEntry((byte)_r, (byte)_g, (byte)_b, (byte)Math.Round(_alpha));
			}
			else
			{
				SidebarColorViewModel item = _colors[i];
				entries[i] = new ColorEntry(item.Rgb.R, item.Rgb.G, item.Rgb.B, (byte)Math.Round(item.Alpha));
			}
		}

		return new ColorSnapshot(entries, _activeIndex, _textColorIndex, _bgColorIndex);
	}




	// 2つのスナップショットが同じ色状態か。無変化の段を見分けるのに使う。
	private static bool SnapshotEquals(ColorSnapshot a, ColorSnapshot b)
	{
		if (a.ActiveIndex != b.ActiveIndex || a.TextIndex != b.TextIndex || a.BgIndex != b.BgIndex || a.Entries.Length != b.Entries.Length)
		{
			return false;
		}

		for (int i = 0; i < a.Entries.Length; i++)
		{
			if (a.Entries[i] != b.Entries[i])
			{
				return false;
			}
		}

		return true;
	}




	// 連続編集のまとまりを閉じる無操作タイマーを測り直す。初回は生成し、以後は止めてから測り直す。満了でまとまりを締める。
	private void RestartGestureTimer()
	{
		if (_gestureTimer is null)
		{
			_gestureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GestureCoalesceSeconds) };
			_gestureTimer.Tick += OnGestureTimerTick;
		}

		_gestureTimer.Stop();
		_gestureTimer.Start();
	}




	// 無操作タイマーの満了。連続編集のまとまりを締めて1段として確定させる。
	private void OnGestureTimerTick(object? sender, object e)
	{
		_gestureTimer?.Stop();
		SealGesture();
	}




	// 戻す/やり直しの可否が変わったことを通知し、メニュー項目の有効・無効を更新させる。
	private void RaiseCanUndoRedo()
	{
		OnPropertyChanged(nameof(CanUndo));
		OnPropertyChanged(nameof(CanRedo));
	}




	private void SetChannel(ref double field, double value, [CallerMemberName] string? name = null)
	{
		if (field == value)
		{
			return;
		}

		RecordContinuousChange();
		field = value;
		OnPropertyChanged(name);
		NotifyColor1Derived();
		NotifyCmykDerived();
		SyncHsvCacheFromRgb();
		NotifyHsvDerived();
		SyncHslCacheFromRgb();
		NotifyHslDerived();
		NotifyHwbDerived();
		NotifyYuvDerived();
		NotifyLchDerived();
		NotifyLabDerived();
	}




	// 無彩色(灰)の明るさを設定する。色1の R・G・B を一括で同値(value)へ揃え、有彩色を脱色して無彩色にする。R・G・B を個別に3回設定すると履歴も3段に割れ派生通知も3度走るため、専用にまとめて1段・1回の派生通知で反映する。既に同値の無彩色なら何もしない。派生通知は SetChannel と同じ一式を通し、各表色系のキャッシュと表示を揃える。
	private void SetGray(double value)
	{
		double v = Math.Round(Math.Clamp(value, 0.0, 255.0));

		if (_r == v && _g == v && _b == v)
		{
			return;
		}

		RecordContinuousChange();
		_r = v;
		_g = v;
		_b = v;
		OnPropertyChanged(nameof(R));
		OnPropertyChanged(nameof(G));
		OnPropertyChanged(nameof(B));
		NotifyColor1Derived();
		NotifyCmykDerived();
		SyncHsvCacheFromRgb();
		NotifyHsvDerived();
		SyncHslCacheFromRgb();
		NotifyHslDerived();
		NotifyHwbDerived();
		NotifyYuvDerived();
		NotifyLchDerived();
		NotifyLabDerived();
	}




	// RGB 平面パッドの操作で、横軸・縦軸に取った2成分を同時に設定する。残る1成分(縦バーが司る固定成分)は保つ。xChannel・yChannel は 0=R, 1=G, 2=B で、値はともに正規化(0–1)で受け取り 0–255 のバイトへ直す。R・G・B を個別に2回設定すると履歴も2段に割れ派生通知も2度走るため、SetGray と同じくまとめて1段・1回の派生通知で反映する。RGB が変わらなければ通知しない。
	public void SetRgbPlane(int xChannel, double x01, int yChannel, double y01)
	{
		double xv = Math.Round(Math.Clamp(x01, 0.0, 1.0) * 255.0);
		double yv = Math.Round(Math.Clamp(y01, 0.0, 1.0) * 255.0);

		double nr = _r;
		double ng = _g;
		double nb = _b;

		AssignChannel(ref nr, ref ng, ref nb, xChannel, xv);
		AssignChannel(ref nr, ref ng, ref nb, yChannel, yv);

		if (nr == _r && ng == _g && nb == _b)
		{
			return;
		}

		RecordContinuousChange();
		_r = nr;
		_g = ng;
		_b = nb;
		OnPropertyChanged(nameof(R));
		OnPropertyChanged(nameof(G));
		OnPropertyChanged(nameof(B));
		NotifyColor1Derived();
		NotifyCmykDerived();
		SyncHsvCacheFromRgb();
		NotifyHsvDerived();
		SyncHslCacheFromRgb();
		NotifyHslDerived();
		NotifyHwbDerived();
		NotifyYuvDerived();
		NotifyLchDerived();
		NotifyLabDerived();
	}




	// R・G・B のいずれか(channel: 0=R, 1=G, 2=B)へ値を書き込む。SetRgbPlane が2軸の成分を順に当てるのに使う。
	private static void AssignChannel(ref double r, ref double g, ref double b, int channel, double value)
	{
		switch (channel)
		{
			case 0: r = value; break;
			case 1: g = value; break;
			default: b = value; break;
		}
	}




	// 色1(R・G・B)から導かれる表示物をまとめて通知する。どの要素を変えても、他要素を固定した3本のグラデーション・色1プレビュー・16進表記が変わる。不透明度プレビューとアルファスライダーの背景、コントラスト確認欄の文字色も色1の色に依るため、ここで併せて通知する。サイドバーのアクティブ色パネルも作業値を写して追従させる。
	private void NotifyColor1Derived()
	{
		SyncActiveColorItem();
		OnPropertyChanged(nameof(Gray));
		OnPropertyChanged(nameof(RedTrackBrush));
		OnPropertyChanged(nameof(GreenTrackBrush));
		OnPropertyChanged(nameof(BlueTrackBrush));
		OnPropertyChanged(nameof(RedTrackBrushVertical));
		OnPropertyChanged(nameof(GreenTrackBrushVertical));
		OnPropertyChanged(nameof(BlueTrackBrushVertical));
		OnPropertyChanged(nameof(Color1Brush));
		OnPropertyChanged(nameof(Color1HexText));
		OnPropertyChanged(nameof(Color1ForegroundBrush));
		OnPropertyChanged(nameof(Color1AlphaBrush));
		OnPropertyChanged(nameof(AlphaTrackBrush));
		OnPropertyChanged(nameof(ContrastTextBrush));

		// 背景色の役の色を編集しているときは、テキストモードの背景面と16進表記もその場で追従させる。
		if (_colors.Count > 0 && _activeIndex == _bgColorIndex)
		{
			OnPropertyChanged(nameof(Color2Brush));
			OnPropertyChanged(nameof(Color2HexText));
			OnPropertyChanged(nameof(Color2ForegroundBrush));
		}

		NotifySliderTrackBrushes();
		NotifyContrastChanged();
	}




	// 色1(文字)と色2(背景)のコントラスト比とその適合バッジをまとめて通知する。比は両色に依存するが、色2が変わる箇所はいずれも色1側の通知も併せて行うため、ここに集約してこの通知を必ず通す。
	private void NotifyContrastChanged()
	{
		OnPropertyChanged(nameof(ContrastRatio));
		OnPropertyChanged(nameof(ContrastRatioText));
		OnPropertyChanged(nameof(ContrastAaBrush));
		OnPropertyChanged(nameof(ContrastAaaBrush));
		RefreshContrastSlider();
	}




	// CMYK は色1から導出するため、色1が変われば4成分とその4本のグラデーションも変わる。まとめて通知する。
	// 色1から導く CMYK 系の表示物をまとめて通知する。CMYK 自身の編集中(_cmykEditing)でなければ、外部(他タブ・貼り付け等)で色が変わった通知とみなして作業用キャッシュを RGB 由来の正準形へ取り直す。編集中は入れた成分(冗長な K を含む)を保つため取り直さない。
	private void NotifyCmykDerived()
	{
		if (!_cmykEditing)
		{
			SyncCmykCacheFromRgb();
		}

		OnPropertyChanged(nameof(C));
		OnPropertyChanged(nameof(M));
		OnPropertyChanged(nameof(Y));
		OnPropertyChanged(nameof(K));
		OnPropertyChanged(nameof(CyanTrackBrush));
		OnPropertyChanged(nameof(MagentaTrackBrush));
		OnPropertyChanged(nameof(YellowTrackBrush));
		OnPropertyChanged(nameof(KeyTrackBrush));
		OnPropertyChanged(nameof(CyanTrackBrushVertical));
		OnPropertyChanged(nameof(MagentaTrackBrushVertical));
		OnPropertyChanged(nameof(YellowTrackBrushVertical));
	}




	// CMYK の1成分(パーセント)を設定する。作業用キャッシュの当該成分だけ差し替え、他成分は保ったまま RGB へ反映する。冗長な4成分を正準形へ畳まず保つため、RGB から取り直さずキャッシュを真として扱う。適用中(ApplyCmykFromCache)の通知がスライダーの双方向束縛を介して再入したときは無視する。
	private void SetCmykChannel(int channel, double percent)
	{
		if (_cmykEditing)
		{
			return;
		}

		double value = Math.Clamp(percent, 0.0, 100.0) / 100.0;
		double c = _cmykC;
		double m = _cmykM;
		double y = _cmykY;
		double k = _cmykK;

		switch (channel)
		{
			case 0: c = value; break;
			case 1: m = value; break;
			case 2: y = value; break;
			default: k = value; break;
		}

		if (c == _cmykC && m == _cmykM && y == _cmykY && k == _cmykK)
		{
			return;
		}

		_cmykC = c;
		_cmykM = m;
		_cmykY = y;
		_cmykK = k;
		ApplyCmykFromCache();
	}




	// CMYK 平面パッドの操作で、横軸・縦軸に取った2つの CMY 成分を同時に設定する。残る CMY 成分(縦バーが司る固定成分)と墨(K)は保つ。xChannel・yChannel は 0=C, 1=M, 2=Y で、値はともに比率(0–1)で受け取る。
	public void SetCmykPlane(int xChannel, double x01, int yChannel, double y01)
	{
		if (_cmykEditing)
		{
			return;
		}

		double c = _cmykC;
		double m = _cmykM;
		double y = _cmykY;

		AssignCmy(ref c, ref m, ref y, xChannel, Math.Clamp(x01, 0.0, 1.0));
		AssignCmy(ref c, ref m, ref y, yChannel, Math.Clamp(y01, 0.0, 1.0));

		if (c == _cmykC && m == _cmykM && y == _cmykY)
		{
			return;
		}

		_cmykC = c;
		_cmykM = m;
		_cmykY = y;
		ApplyCmykFromCache();
	}




	// C・M・Y のいずれか(channel: 0=C, 1=M, 2=Y)へ値を書き込む。SetCmykPlane が2軸の成分を順に当てるのに使う。K は平面の対象外のため扱わない。
	private static void AssignCmy(ref double c, ref double m, ref double y, int channel, double value)
	{
		switch (channel)
		{
			case 0: c = value; break;
			case 1: m = value; break;
			default: y = value; break;
		}
	}




	// 作業用キャッシュの CMYK を RGB へ変換して色1へ反映する。冗長な4成分を保つため RGB からの取り直しはせず、編集中フラグを立てて派生通知での取り直しを抑える。RGB が変わらなければ色由来の通知はせず、CMYK 自身の表示だけ整える。
	private void ApplyCmykFromCache()
	{
		_cmykEditing = true;

		try
		{
			(byte r, byte g, byte b) = CmykToRgb(_cmykC, _cmykM, _cmykY, _cmykK);
			bool changed = !(r == (byte)_r && g == (byte)_g && b == (byte)_b);

			if (changed)
			{
				RecordContinuousChange();
				_r = r;
				_g = g;
				_b = b;

				OnPropertyChanged(nameof(R));
				OnPropertyChanged(nameof(G));
				OnPropertyChanged(nameof(B));
				NotifyColor1Derived();
				SyncHsvCacheFromRgb();
				NotifyHsvDerived();
				SyncHslCacheFromRgb();
				NotifyHslDerived();
				NotifyHwbDerived();
				NotifyYuvDerived();
				NotifyLchDerived();
				NotifyLabDerived();
			}

			NotifyCmykDerived();
		}
		finally
		{
			_cmykEditing = false;
		}
	}




	// 色1の RGB から作業用キャッシュの CMYK を正準形(K=1−max)で取り直す。外部での色変更(NotifyCmykDerived)・キャッシュ初期化(InitColorCaches)で呼ぶ。
	private void SyncCmykCacheFromRgb()
	{
		(double c, double m, double y, double k) = CurrentCmyk();
		_cmykC = c;
		_cmykM = m;
		_cmykY = y;
		_cmykK = k;
	}




	// 現在の色1(R・G・B)を CMYK(各 0–1)へ変換する。純黒は K=1、C・M・Y=0 とする。
	private (double C, double M, double Y, double K) CurrentCmyk()
	{
		double rf = _r / 255.0;
		double gf = _g / 255.0;
		double bf = _b / 255.0;
		double k = 1.0 - Math.Max(rf, Math.Max(gf, bf));

		if (k >= 1.0)
		{
			return (0.0, 0.0, 0.0, 1.0);
		}

		double c = (1.0 - rf - k) / (1.0 - k);
		double m = (1.0 - gf - k) / (1.0 - k);
		double y = (1.0 - bf - k) / (1.0 - k);
		return (c, m, y, k);
	}




	// CMYK(各 0–1)を RGB(各バイト)へ変換する。
	private static (byte R, byte G, byte B) CmykToRgb(double c, double m, double y, double k)
	{
		byte r = (byte)Math.Round(255.0 * (1.0 - c) * (1.0 - k));
		byte g = (byte)Math.Round(255.0 * (1.0 - m) * (1.0 - k));
		byte b = (byte)Math.Round(255.0 * (1.0 - y) * (1.0 - k));
		return (r, g, b);
	}




	// 指定要素だけを 0→255 に変化させるグラデーションを作る。ShowActualColor が真なら残り2要素を現在の色1の値で固定し、偽なら 0 に固定して当該要素単独の基準ランプ(黒→純色)にする。channel は 0=R, 1=G, 2=B。vertical が真なら下端=0・上端=255 の縦向き、偽なら左端=0・右端=255 の横向き。
	private LinearGradientBrush MakeChannelBrush(int channel, bool vertical = false)
	{
		byte r = _showActualColor ? (byte)_r : (byte)0;
		byte g = _showActualColor ? (byte)_g : (byte)0;
		byte b = _showActualColor ? (byte)_b : (byte)0;
		Point start = vertical ? new Point(0.5, 1.0) : new Point(0.0, 0.5);
		Point end = vertical ? new Point(0.5, 0.0) : new Point(1.0, 0.5);
		return MakeTrackBrush(t => ChannelColor(channel, t, r, g, b), start, end);
	}




	// 無彩色スライダーの背景を作る。黒→白のグレーランプ(R=G=B を 0→255 へ)。色制限が有効なら MakeTrackBrush 側で各位置が丸められ段差になる。現在の色1には依らず、グレーの並びそのものを示す。
	private LinearGradientBrush MakeGrayBrush()
	{
		return MakeTrackBrush(t => GrayRampColor(t), new Point(0.0, 0.5), new Point(1.0, 0.5));
	}




	// グレーランプの位置 value(0–1)に対する無彩色(R=G=B)を作る。
	private static Color GrayRampColor(double value)
	{
		byte v = (byte)Math.Round(value * 255.0);
		return Color.FromArgb(0xFF, v, v, v);
	}




	// 指定要素(0=R, 1=G, 2=B)だけを value(0–1)に対応する 0–255 へ差し替え、残りを固定値にした色を作る。
	private static Color ChannelColor(int channel, double value, byte r, byte g, byte b)
	{
		byte v = (byte)Math.Round(value * 255.0);
		return channel switch
		{
			0 => Color.FromArgb(0xFF, v, g, b),
			1 => Color.FromArgb(0xFF, r, v, b),
			_ => Color.FromArgb(0xFF, r, g, v),
		};
	}




	// 指定要素だけを 0→100% に変化させるグラデーションを作る。ShowActualColor が真なら残り3要素を作業用キャッシュの CMYK の値で固定し、偽なら 0 に固定して当該要素単独の基準ランプ(C・M・Y は白→純色、K は白→黒)にする。channel は 0=C, 1=M, 2=Y, 3=K。vertical が真なら下端=0・上端=100% の縦向き、偽なら左端=0・右端=100% の横向き。
	private LinearGradientBrush MakeCmykChannelBrush(int channel, bool vertical = false)
	{
		(double c, double m, double y, double k) = _showActualColor ? (_cmykC, _cmykM, _cmykY, _cmykK) : (0.0, 0.0, 0.0, 0.0);
		Point start = vertical ? new Point(0.5, 1.0) : new Point(0.0, 0.5);
		Point end = vertical ? new Point(0.5, 0.0) : new Point(1.0, 0.5);
		return MakeTrackBrush(t => CmykColor(channel, t, c, m, y, k), start, end);
	}




	// 指定の CMYK 成分だけを value(0–1)に差し替えた色を作る。
	private static Color CmykColor(int channel, double value, double c, double m, double y, double k)
	{
		switch (channel)
		{
			case 0: c = value; break;
			case 1: m = value; break;
			case 2: y = value; break;
			default: k = value; break;
		}

		(byte r, byte g, byte b) = CmykToRgb(c, m, y, k);
		return Color.FromArgb(0xFF, r, g, b);
	}




	// 表示用の色を、色制限モードに従って丸めて返す。制限なしなら素の色をそのまま返す。スライダーの値・つまみ位置は丸めず、見える色だけを制限する。
	private Color ToDisplay(Color color)
	{
		return _limitMode != ColorLimitMode.None ? Snap(color) : color;
	}




	// 色を、現在の色制限設定に従って最も近い表せる色へ丸める。透明度は保つ。
	private Color Snap(Color color)
	{
		(byte r, byte g, byte b) = ColorConversion.Snap(CurrentSnap, color.R, color.G, color.B);
		return Color.FromArgb(color.A, r, g, b);
	}




	// 各表色系の変換が返す RGB タプルを不透明な Color にする。
	private static Color OpaqueColor((byte R, byte G, byte B) rgb)
	{
		return Color.FromArgb(0xFF, rgb.R, rgb.G, rgb.B);
	}




	// 色を "#RRGGBB" 形式の文字列にする。
	private static string HexText(Color color)
	{
		return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
	}




	// 位置 t(0–1)に対する色を返す関数から、スライダー背景のグラデーションを作る。色制限が有効なら各位置の色を丸めて段差状(硬い境目)にし、制限なしなら sampleStops+1 個の標本点を線形補間でつなぐ。各表色系のスライダーは成分に対して RGB が(区分)線形なので、制限なし時は少数の標本で正確に表せる。始点・終点はグラデーションの向き(水平・垂直)を与える。
	private LinearGradientBrush MakeTrackBrush(Func<double, Color> colorAt, Point start, Point end, int sampleStops = 1)
	{
		if (_limitMode != ColorLimitMode.None)
		{
			return SteppedGradient(colorAt, start, end);
		}

		var brush = new LinearGradientBrush
		{
			StartPoint = start,
			EndPoint = end,
		};

		for (int i = 0; i <= sampleStops; i++)
		{
			double t = (double)i / sampleStops;
			brush.GradientStops.Add(new GradientStop { Color = colorAt(t), Offset = t });
		}

		return brush;
	}




	// 位置 t(0–1)に対する色を返す関数を、各位置を現在の色制限モードへ丸めた段階的なグラデーションにする。丸めた色が変わる境目で同じオフセットに前後の色を重ね、なだらかな補間ではなく明確な段差として描く。境目は十分細かく標本化して捉える。
	private LinearGradientBrush SteppedGradient(Func<double, Color> colorAt, Point start, Point end)
	{
		var brush = new LinearGradientBrush
		{
			StartPoint = start,
			EndPoint = end,
		};

		const int samples = 256;
		Color previous = Snap(colorAt(0.0));
		brush.GradientStops.Add(new GradientStop { Color = previous, Offset = 0.0 });

		for (int i = 1; i <= samples; i++)
		{
			double t = (double)i / samples;
			Color current = Snap(colorAt(t));

			if (current.R != previous.R || current.G != previous.G || current.B != previous.B)
			{
				brush.GradientStops.Add(new GradientStop { Color = previous, Offset = t });
				brush.GradientStops.Add(new GradientStop { Color = current, Offset = t });
				previous = current;
			}
		}

		brush.GradientStops.Add(new GradientStop { Color = previous, Offset = 1.0 });
		return brush;
	}




	// 背景色の上に重ねる文字が読みやすくなる前景色(黒か白)のブラシを選ぶ。判定は知覚輝度に基づく ColorMetrics に委ね、色見本のスウォッチと同じ基準でコントラストを決める。
	private static Brush ContrastBrush(Color background)
	{
		return new SolidColorBrush(ColorMetrics.ContrastColor(background));
	}




	// "#RRGGBB" 形式の文字列を R・G・B バイトへ解釈する。先頭の # は省略可。解釈できなければ false を返し、呼び出し側は既定値のままにする。
	private static bool TryParseRgb(string? hex, out byte r, out byte g, out byte b)
	{
		r = g = b = 0;

		if (string.IsNullOrWhiteSpace(hex))
		{
			return false;
		}

		string s = hex.TrimStart('#');

		if (s.Length != 6)
		{
			return false;
		}

		return byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
			&& byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
			&& byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
	}




	// 設定ファイルの文字列を YCbCr 係数規格へ解釈する。未知・未指定は既定の BT.709 とする。
	private static YCbCrStandard ParseStandard(string? value)
	{
		return value switch
		{
			"bt601" => YCbCrStandard.Bt601,
			"bt2020" => YCbCrStandard.Bt2020,
			_ => YCbCrStandard.Bt709,
		};
	}




	// YCbCr 係数規格を設定ファイル用の文字列にする。
	private static string StandardToString(YCbCrStandard standard)
	{
		return standard switch
		{
			YCbCrStandard.Bt601 => "bt601",
			YCbCrStandard.Bt2020 => "bt2020",
			_ => "bt709",
		};
	}




	// 設定ファイルの文字列をアルファ値の表示単位へ解釈する。未知・未指定は既定の 0–255 表記(Byte)とする。
	private static AlphaUnit ParseAlphaUnit(string? value)
	{
		return value switch
		{
			"hex" => AlphaUnit.Hex,
			"percent" => AlphaUnit.Percent,
			"normalized" => AlphaUnit.Normalized,
			_ => AlphaUnit.Byte,
		};
	}




	// アルファ値の表示単位を設定ファイル用の文字列にする。
	private static string AlphaUnitToString(AlphaUnit unit)
	{
		return unit switch
		{
			AlphaUnit.Hex => "hex",
			AlphaUnit.Percent => "percent",
			AlphaUnit.Normalized => "normalized",
			_ => "byte",
		};
	}




	// 設定ファイルの文字列を R・G・B の表示単位へ読み替える。未指定や未知の値は 0–255 表記として扱う。
	private static RgbUnit ParseRgbUnit(string? value)
	{
		return value switch
		{
			"hex" => RgbUnit.Hex,
			"normalized" => RgbUnit.Normalized,
			_ => RgbUnit.Byte,
		};
	}




	// R・G・B の表示単位を設定ファイル用の文字列にする。
	private static string RgbUnitToString(RgbUnit unit)
	{
		return unit switch
		{
			RgbUnit.Hex => "hex",
			RgbUnit.Normalized => "normalized",
			_ => "byte",
		};
	}




	// 保存済みの設定の形式識別子を選択番号へ解釈する。未知・未指定は既定の 16 進 (番号 0) とする。
	private static int ResolveCopyFormatIndex(string? key)
	{
		int index = key is null ? -1 : Array.IndexOf(CopyFormatKeys, key);
		return index >= 0 ? index : 0;
	}




	// 保存済みの2つ目の形式識別子を選択番号へ解釈する。未知・未指定は既定の rgb() とする。
	private static int ResolveCopyFormatIndex2(string? key)
	{
		int index = key is null ? -1 : Array.IndexOf(CopyFormatKeys, key);
		return index >= 0 ? index : Array.IndexOf(CopyFormatKeys, "rgb");
	}




	// 保存済みの色編集状態から色制限モードを決める。color_limit_mode の文字列で選び、未知・未指定は制限なしとする。
	private static ColorLimitMode ResolveLimitMode(EditorState state)
	{
		switch (state.ColorLimitMode)
		{
			case "web_safe": return ColorLimitMode.WebSafe;
			case "rgb565": return ColorLimitMode.Rgb565;
			case "rgb555": return ColorLimitMode.Rgb555;
			case "rgb444": return ColorLimitMode.Rgb444;
			case "rgb332": return ColorLimitMode.Rgb332;
			case "term256": return ColorLimitMode.Term256;
			case "term16": return ColorLimitMode.Term16;
			case "term8": return ColorLimitMode.Term8;
		}

		return ColorLimitMode.None;
	}




	// 色制限モードを設定ファイル用の文字列にする。
	private static string LimitModeToString(ColorLimitMode mode)
	{
		return mode switch
		{
			ColorLimitMode.WebSafe => "web_safe",
			ColorLimitMode.Rgb565 => "rgb565",
			ColorLimitMode.Rgb555 => "rgb555",
			ColorLimitMode.Rgb444 => "rgb444",
			ColorLimitMode.Rgb332 => "rgb332",
			ColorLimitMode.Term256 => "term256",
			ColorLimitMode.Term16 => "term16",
			ColorLimitMode.Term8 => "term8",
			_ => "none",
		};
	}




	// 保存済みの設定の文字列から HSV/HSL タブの副モードの位置 (0=HSV, 1=HSL, 2=HWB) を決める。未知・未指定は HSV とする。
	private static int ResolveHsvSubMode(string? key)
	{
		return key switch
		{
			"hsl" => 1,
			"hwb" => 2,
			_ => 0,
		};
	}




	// HSV/HSL タブの副モードの位置を設定ファイル用の文字列にする。
	private static string HsvSubModeToString(int index)
	{
		return index switch
		{
			1 => "hsl",
			2 => "hwb",
			_ => "hsv",
		};
	}




	// 保存済みの設定の文字列から HSV モードの見せ方の位置 (0=色相リング+正方形, 1=色相・彩度の円盤+明度の縦スライダー, 2=色相×明度の直交パッド+彩度の縦スライダー) を決める。未知・未指定は色相リング+正方形とする。
	private static int ResolveHsvLayout(string? key)
	{
		return key switch
		{
			"hue_sat_wheel" => 1,
			"hue_value_plane" => 2,
			"hue_value_wheel" => 3,
			"hue_sat_plane" => 4,
			"sv_hue_bar" => 5,
			_ => 0,
		};
	}




	// HSV モードの見せ方の位置を設定ファイル用の文字列にする。
	private static string HsvLayoutToString(int index)
	{
		return index switch
		{
			1 => "hue_sat_wheel",
			2 => "hue_value_plane",
			3 => "hue_value_wheel",
			4 => "hue_sat_plane",
			5 => "sv_hue_bar",
			_ => "ring_square",
		};
	}




	// 保存済みの設定の文字列から HSL モードの見せ方の位置を決める。未知・未指定は HSL らしい色相リング+三角形(6)とする。
	private static int ResolveHslLayout(string? key)
	{
		return key switch
		{
			"ring_square" => 0,
			"hue_sat_wheel" => 1,
			"hue_lightness_plane" => 2,
			"hue_lightness_wheel" => 3,
			"hue_sat_plane" => 4,
			"sl_hue_bar" => 5,
			"triangle_hue_bar" => 7,
			_ => 6,
		};
	}




	// HSL モードの見せ方の位置を設定ファイル用の文字列にする。
	private static string HslLayoutToString(int index)
	{
		return index switch
		{
			0 => "ring_square",
			1 => "hue_sat_wheel",
			2 => "hue_lightness_plane",
			3 => "hue_lightness_wheel",
			4 => "hue_sat_plane",
			5 => "sl_hue_bar",
			7 => "triangle_hue_bar",
			_ => "ring_triangle",
		};
	}




	// 保存済みの設定の文字列から HWB モードの見せ方の位置を決める。未知・未指定は現行の色相リング+正方形(0)とする。
	private static int ResolveHwbLayout(string? key)
	{
		return key switch
		{
			"hue_whiteness_wheel" => 1,
			"hue_blackness_plane" => 2,
			"hue_blackness_wheel" => 3,
			"hue_whiteness_plane" => 4,
			"wb_hue_bar" => 5,
			"ring_triangle" => 6,
			"triangle_hue_bar" => 7,
			_ => 0,
		};
	}




	// HWB モードの見せ方の位置を設定ファイル用の文字列にする。
	private static string HwbLayoutToString(int index)
	{
		return index switch
		{
			1 => "hue_whiteness_wheel",
			2 => "hue_blackness_plane",
			3 => "hue_blackness_wheel",
			4 => "hue_whiteness_plane",
			5 => "wb_hue_bar",
			6 => "ring_triangle",
			7 => "triangle_hue_bar",
			_ => "ring_square",
		};
	}




	// 保存済みの設定の文字列から LCH タブの副モードの位置 (0=OKLCH, 1=CIE LCH) を決める。未知・未指定は OKLCH とする。
	private static int ResolveLchSpace(string? key)
	{
		return key == "lch" ? 1 : 0;
	}




	// LCH タブの副モードの位置を設定ファイル用の文字列にする。
	private static string LchSpaceToString(int index)
	{
		return index == 1 ? "lch" : "oklch";
	}




	// 保存済みの設定の文字列から LCH モードの見せ方の位置を決める。未知・未指定は色相リング+平面(0)とする。
	private static int ResolveLchLayout(string? key)
	{
		return key switch
		{
			"cl_hue_bar" => 1,
			"hue_lightness_wheel" => 2,
			"hue_lightness_plane" => 3,
			"hue_chroma_wheel" => 4,
			"hue_chroma_plane" => 5,
			_ => 0,
		};
	}




	// LCH モードの見せ方の位置を設定ファイル用の文字列にする。
	private static string LchLayoutToString(int index)
	{
		return index switch
		{
			1 => "cl_hue_bar",
			2 => "hue_lightness_wheel",
			3 => "hue_lightness_plane",
			4 => "hue_chroma_wheel",
			5 => "hue_chroma_plane",
			_ => "ring_plane",
		};
	}




	// 保存済みの設定の文字列から Lab タブの副モードの位置 (0=OKLab, 1=CIE Lab) を決める。未知・未指定は OKLab とする。
	private static int ResolveLabSpace(string? key)
	{
		return key == "lab" ? 1 : 0;
	}




	// Lab タブの副モードの位置を設定ファイル用の文字列にする。
	private static string LabSpaceToString(int index)
	{
		return index == 1 ? "lab" : "oklab";
	}




	// 保存済みの設定の文字列から Lab タブの見せ方の位置 (0..3) を決める。未知・未指定は a×b 平面+明度の縦バー(0)とする。
	private static int ResolveLabLayout(string? key)
	{
		return key switch
		{
			"a_lightness_plane" => 1,
			"b_lightness_plane" => 2,
			_ => 0,
		};
	}




	// Lab タブの見せ方の位置を設定ファイル用の文字列にする。
	private static string LabLayoutToString(int index)
	{
		return index switch
		{
			1 => "a_lightness_plane",
			2 => "b_lightness_plane",
			_ => "ab_plane",
		};
	}




	// 保存済みの設定の文字列から YUV/YCbCr タブの見せ方の位置 (0..2) を決める。未知・未指定は Cb×Cr 平面+Y の縦バー(0)とする。
	private static int ResolveYuvLayout(string? key)
	{
		return key switch
		{
			"cb_luma_plane" => 1,
			"cr_luma_plane" => 2,
			_ => 0,
		};
	}




	// YUV/YCbCr タブの見せ方の位置を設定ファイル用の文字列にする。
	private static string YuvLayoutToString(int index)
	{
		return index switch
		{
			1 => "cb_luma_plane",
			2 => "cr_luma_plane",
			_ => "cbcr_plane",
		};
	}




	// 保存済みの設定の文字列から RGB/CMYK タブの2次元エディタの見せ方の位置 (0..6) を決める。未知・未指定はパッド無し(0)とする。
	private static int ResolveRgbCmykLayout(string? key)
	{
		return key switch
		{
			"rgb_gb" => 1,
			"rgb_rb" => 2,
			"rgb_rg" => 3,
			"cmyk_my" => 4,
			"cmyk_cy" => 5,
			"cmyk_cm" => 6,
			_ => 0,
		};
	}




	// RGB/CMYK タブの2次元エディタの見せ方の位置を設定ファイル用の文字列にする。
	private static string RgbCmykLayoutToString(int index)
	{
		return index switch
		{
			1 => "rgb_gb",
			2 => "rgb_rb",
			3 => "rgb_rg",
			4 => "cmyk_my",
			5 => "cmyk_cy",
			6 => "cmyk_cm",
			_ => "sliders",
		};
	}




	// 保存済みの設定の文字列から YUV/YCbCr タブの色差平面の表示枠(スケール)の決め方の位置 (0..2) を決める。未知・未指定は固定枠(0)とする。
	private static int ResolveYuvScale(string? key)
	{
		return key switch
		{
			"isotropic" => 1,
			"anisotropic" => 2,
			_ => 0,
		};
	}




	// YUV/YCbCr タブの色差平面の表示枠(スケール)の決め方の位置を設定ファイル用の文字列にする。
	private static string YuvScaleToString(int index)
	{
		return index switch
		{
			1 => "isotropic",
			2 => "anisotropic",
			_ => "none",
		};
	}




	// 保存済みの設定の文字列から Lab タブの a×b 平面の表示枠(スケール)の決め方の位置 (0..2) を決める。未知・未指定は固定枠(0)とする。
	private static int ResolveLabAbScale(string? key)
	{
		return key switch
		{
			"isotropic" => 1,
			"anisotropic" => 2,
			_ => 0,
		};
	}




	// Lab タブの a×b 平面の表示枠(スケール)の決め方の位置を設定ファイル用の文字列にする。
	private static string LabAbScaleToString(int index)
	{
		return index switch
		{
			1 => "isotropic",
			2 => "anisotropic",
			_ => "none",
		};
	}




	// 保存済みの設定の文字列から距離計算を決める。未知・未指定は知覚的(Lab)とする。
	private static SnapMetric ResolveSnapMetric(string? key)
	{
		return key switch
		{
			"redmean" => SnapMetric.Redmean,
			"rgb" => SnapMetric.Rgb,
			_ => SnapMetric.Lab,
		};
	}




	// 距離計算を設定ファイル用の文字列にする。
	private static string SnapMetricToString(SnapMetric metric)
	{
		return metric switch
		{
			SnapMetric.Redmean => "redmean",
			SnapMetric.Rgb => "rgb",
			_ => "lab",
		};
	}




	// 保存済みの設定の文字列から色域外の見せ方を決める。未知・未指定は既定のクランプ色塗り+境界線+斜線とする。
	private static GamutOutOfRangeStyle ResolveOogStyle(string? key)
	{
		return key switch
		{
			"fill_boundary" => GamutOutOfRangeStyle.FillBoundary,
			"white_hatch" => GamutOutOfRangeStyle.WhiteHatch,
			_ => GamutOutOfRangeStyle.FillBoundaryHatch,
		};
	}




	// 色域外の見せ方を設定ファイル用の文字列にする。
	private static string OogStyleToString(GamutOutOfRangeStyle style)
	{
		return style switch
		{
			GamutOutOfRangeStyle.FillBoundary => "fill_boundary",
			GamutOutOfRangeStyle.WhiteHatch => "white_hatch",
			_ => "fill_boundary_hatch",
		};
	}




	// 保存済みの設定の文字列から Mix の色空間を決める。未知・未指定は OKLCH とする。
	private static MixColorSpace ResolveMixSpace(string? key)
	{
		return key switch
		{
			"oklab" => MixColorSpace.Oklab,
			"lch" => MixColorSpace.Lch,
			"lab" => MixColorSpace.Lab,
			"hsl" => MixColorSpace.Hsl,
			"linear_rgb" => MixColorSpace.LinearRgb,
			"rgb" => MixColorSpace.Rgb,
			_ => MixColorSpace.Oklch,
		};
	}




	// Mix の色空間を設定ファイル用の文字列にする。
	private static string MixSpaceToString(MixColorSpace space)
	{
		return space switch
		{
			MixColorSpace.Oklab => "oklab",
			MixColorSpace.Lch => "lch",
			MixColorSpace.Lab => "lab",
			MixColorSpace.Hsl => "hsl",
			MixColorSpace.LinearRgb => "linear_rgb",
			MixColorSpace.Rgb => "rgb",
			_ => "oklch",
		};
	}




	// 保存済みの設定の文字列から Mix の色相の回り方を決める。未知・未指定は近い側とする。
	private static MixHueDirection ResolveMixHueDir(string? key)
	{
		return key switch
		{
			"longer" => MixHueDirection.Longer,
			_ => MixHueDirection.Shorter,
		};
	}




	// Mix の色相の回り方を設定ファイル用の文字列にする。
	private static string MixHueDirToString(MixHueDirection dir)
	{
		return dir switch
		{
			MixHueDirection.Longer => "longer",
			_ => "shorter",
		};
	}




	// 保存済みの設定の文字列から Mix の補間方式を決める。未知・未指定は逆距離加重とする。
	private static MixInterpolation ResolveMixMethod(string? key)
	{
		return key switch
		{
			"gaussian" => MixInterpolation.Gaussian,
			"nearest" => MixInterpolation.Nearest,
			_ => MixInterpolation.InverseDistance,
		};
	}




	// Mix の補間方式を設定ファイル用の文字列にする。
	private static string MixMethodToString(MixInterpolation method)
	{
		return method switch
		{
			MixInterpolation.Gaussian => "gaussian",
			MixInterpolation.Nearest => "nearest",
			_ => "inverse_distance",
		};
	}




	// 保存済みの設定の文字列から CSS 関数のアルファ表記を決める。未知・未指定は 0–1 の数値とする。
	private static WebAlphaUnit ResolveWebAlphaUnit(string? key)
	{
		return key switch
		{
			"percent" => WebAlphaUnit.Percent,
			_ => WebAlphaUnit.Number,
		};
	}




	// CSS 関数のアルファ表記を設定ファイル用の文字列にする。
	private static string WebAlphaUnitToString(WebAlphaUnit unit)
	{
		return unit switch
		{
			WebAlphaUnit.Percent => "percent",
			_ => "number",
		};
	}




	// 保存済みの設定の文字列からターミナルの参照テーマを決める。未知・未指定は Campbell とする。
	private static TerminalTheme ResolveTerminalTheme(string? key)
	{
		return key switch
		{
			"vga" => TerminalTheme.Vga,
			"xterm" => TerminalTheme.Xterm,
			_ => TerminalTheme.Campbell,
		};
	}




	// ターミナルの参照テーマを設定ファイル用の文字列にする。
	private static string TerminalThemeToString(TerminalTheme theme)
	{
		return theme switch
		{
			TerminalTheme.Vga => "vga",
			TerminalTheme.Xterm => "xterm",
			_ => "campbell",
		};
	}




	// 保存済みの設定の文字列から ESC 表現を決める。未知・未指定は16進(hex)とする。
	private static TerminalEscStyle ResolveTerminalEsc(string? key)
	{
		return key switch
		{
			"literal" => TerminalEscStyle.Literal,
			"backslash_e" => TerminalEscStyle.BackslashE,
			"octal" => TerminalEscStyle.Octal,
			"unicode" => TerminalEscStyle.Unicode,
			"caret" => TerminalEscStyle.Caret,
			_ => TerminalEscStyle.Hex,
		};
	}




	// ESC 表現を設定ファイル用の文字列にする。
	private static string TerminalEscToString(TerminalEscStyle style)
	{
		return style switch
		{
			TerminalEscStyle.Literal => "literal",
			TerminalEscStyle.BackslashE => "backslash_e",
			TerminalEscStyle.Octal => "octal",
			TerminalEscStyle.Unicode => "unicode",
			TerminalEscStyle.Caret => "caret",
			_ => "hex",
		};
	}




	public event PropertyChangedEventHandler? PropertyChanged;




	private void OnPropertyChanged(string? name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}




	// 元に戻す/やり直しのための色状態の写し。色リスト全体(表示の丸めを焼き込まない素の RGB と不透明度)・アクティブ位置・役(文字色・背景色)の位置を持ち、色の追加・削除・並べ替えも1段として戻せる。
	private readonly struct ColorSnapshot
	{
		public ColorSnapshot(ColorEntry[] entries, int activeIndex, int textIndex, int bgIndex)
		{
			Entries = entries;
			ActiveIndex = activeIndex;
			TextIndex = textIndex;
			BgIndex = bgIndex;
		}

		public ColorEntry[] Entries { get; }

		public int ActiveIndex { get; }

		public int TextIndex { get; }

		public int BgIndex { get; }
	}




	// スナップショットの1色分。素の RGB と不透明度。
	private readonly record struct ColorEntry(byte R, byte G, byte B, byte A);
}
