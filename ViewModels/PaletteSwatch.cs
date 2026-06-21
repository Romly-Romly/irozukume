// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.Controls.Geometry;

namespace Irozukume.ViewModels;

// Palette タブのリストに並ぶ1色分の表示物。
// 色名 (履歴では取り込んだ元の文字列)・16進表記・スウォッチの塗りと、その上に映える前景色を持つ。
// 固定パレットの色は不透明だが、履歴の色は不透明度を持つことがあり、その場合は16進を8桁にし、塗りを不透明度込みで市松の上に重ねて透過を見せる。塗りは常に元の色で、丸めない。
// 色制限が有効で、かつこの色がそのモードで表せないときは警告を出す。色制限中に変わるのは警告の有無だけで、見える色は変えない。
public sealed class PaletteSwatch : INotifyPropertyChanged
{
	public string Name { get; }

	public string HexText { get; }

	// 丸めや色相計算の基準にする、不透明度を含めない元の色。透過の塗りは別に組む。
	public Color Color { get; }

	// 表示する不透明度。固定パレットの色は常に 255。
	public byte Alpha { get; }

	// この色が不透明度を明示して持つか。履歴の色のみ真になりうる。8桁表記と、色1へ戻すときにアルファも反映するかの判断に使う。
	public bool HasAlpha { get; }

	// 出所。履歴の色のみ値を持ち、固定パレットの色は null。出所アイコンの選択と、履歴パレットの絞り込みに使う。
	public HistoryKind? Kind { get; }

	// 出所を示すアイコンのグリフ(Segoe Fluent Icons)。貼り付け・コピー・画面ピックで取り込み方を見分ける。固定パレットの色は空。
	public string KindGlyph { get; }

	// 出所アイコンの表示・非表示。履歴の色だけ出し、固定パレットの色では出さない。
	public Visibility KindIconVisibility { get; }

	// 履歴順の並びに使う、新しい順の位置 (0 が最新)。固定パレットでは 0。
	public int Order { get; }

	// 色相順の並びに使う色相(度)・輝度(0–1)・無彩色判定。色相環タブと同じ式で色1から導く HSL に揃える。
	public double Hue { get; }

	public double Lightness { get; }

	public bool IsAchromatic { get; }

	public Brush SwatchBrush { get; }

	public Brush ForegroundBrush { get; }

	// 現在の色制限で丸めたときの最も近い色の16進表記。警告の補足として、丸めるとどの色になるかを示す。色制限モードが変わるたびに ApplyLimit で更新する。
	private string _nearestHex;

	private bool _showWarning;




	// 固定パレットの色から作る。不透明色として扱う。
	public PaletteSwatch(NamedColor source, SnapSettings snap)
		: this(source.Name, source.Color.R, source.Color.G, source.Color.B, 0xFF, false, null, 0, snap)
	{
	}




	// 色の履歴から作る。不透明度・出所・新しい順の位置を持つ。不透明度を明示しない履歴は不透明 (255) として扱う。表示名は出所で決め、画面ピックは保存値ではなく現在の言語で解決する。
	public PaletteSwatch(ColorHistoryEntry entry, int order, SnapSettings snap)
		: this(NameFor(entry), entry.R, entry.G, entry.B, entry.HasAlpha ? entry.A : (byte)0xFF, entry.HasAlpha, entry.Kind, order, snap)
	{
	}




	// 履歴の色の表示名。画面ピックは色そのものではなく取得手段が出所のため、保存値を焼き付けず表示時に現在の言語でラベルを解決する。貼り付け・コピーは利用者が扱った実際の色文字列をそのまま使う。
	private static string NameFor(ColorHistoryEntry entry)
	{
		return entry.Kind == HistoryKind.Pick
			? Loc.Get("Picker_HistorySource")
			: entry.Source;
	}




	private PaletteSwatch(string name, byte r, byte g, byte b, byte alpha, bool hasAlpha, HistoryKind? kind, int order, SnapSettings snap)
	{
		Name = name;
		Color = Windows.UI.Color.FromArgb(0xFF, r, g, b);
		Alpha = alpha;
		HasAlpha = hasAlpha;
		Kind = kind;
		Order = order;

		// 出所アイコンは履歴の色だけに出す。グリフはツールバー・編集メニューで使うものに合わせ、コピーはコピー(E8C8)、貼り付けは貼り付け(E77F)、画面ピックはアイドロッパー(EF3C)にする。
		KindGlyph = kind switch
		{
			HistoryKind.Copy => "",
			HistoryKind.Paste => "",
			HistoryKind.Pick => "",
			_ => "",
		};
		KindIconVisibility = kind.HasValue ? Visibility.Visible : Visibility.Collapsed;

		HexText = hasAlpha
			? $"#{r:X2}{g:X2}{b:X2}{alpha:X2}"
			: $"#{r:X2}{g:X2}{b:X2}";

		// スウォッチは不透明度を含めた色で塗り、市松の上に重ねて透過を見せる。不透明色では下の市松が完全に隠れて元の色そのものになる。
		SwatchBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, r, g, b));

		// 前景色のコントラストは実際に見える地で判定する。最も明るい市松の升へこの色を不透明度ぶん合成した色を地とみなし、それに映える黒か白を選ぶ。不透明色では合成結果が元の色に一致するため固定パレットの見え方は変わらない。
		double opacity = alpha / 255.0;
		var over = CheckerboardGeometry.LightColor;
		byte composedR = (byte)Math.Round((r * opacity) + (over.R * (1.0 - opacity)));
		byte composedG = (byte)Math.Round((g * opacity) + (over.G * (1.0 - opacity)));
		byte composedB = (byte)Math.Round((b * opacity) + (over.B * (1.0 - opacity)));
		ForegroundBrush = new SolidColorBrush(ColorMetrics.ContrastColor(Windows.UI.Color.FromArgb(0xFF, composedR, composedG, composedB)));

		(double hue, double _, double lightness) = ColorConversion.RgbToHsl(r, g, b);
		Hue = hue;
		Lightness = lightness;
		IsAchromatic = r == g && g == b;

		_nearestHex = $"#{r:X2}{g:X2}{b:X2}";
		ApplyLimit(snap);
	}




	// 現在の色制限設定に合わせて警告と最寄り色を更新する。その制限で表せない色のとき警告を出し、丸めるとどの色になるかを最寄り色として持つ。色制限の切替時と、パレット読み込み時に呼ぶ。
	public void ApplyLimit(SnapSettings snap)
	{
		(byte sr, byte sg, byte sb) = ColorConversion.Snap(snap, Color.R, Color.G, Color.B);
		bool representable = sr == Color.R && sg == Color.G && sb == Color.B;
		_nearestHex = $"#{sr:X2}{sg:X2}{sb:X2}";
		ShowWarning = snap.Mode != ColorLimitMode.None && !representable;

		// 最寄り色はモードで変わるため、警告の有無が変わらない場合でもツールチップを更新する。
		OnPropertyChanged(nameof(WarningTooltip));
	}




	// 警告の三角を出すか。色制限が有効で、かつこの色がそのモードで表せないとき真。設定は ApplyLimit からのみ行う。
	public bool ShowWarning
	{
		get => _showWarning;
		private set
		{
			if (_showWarning == value)
			{
				return;
			}

			_showWarning = value;
			OnPropertyChanged(nameof(ShowWarning));
			OnPropertyChanged(nameof(WarningVisibility));
			OnPropertyChanged(nameof(AccessibleName));
		}
	}




	// 警告の三角の表示・非表示。bool から Visibility への変換をここに持たせ、テンプレートでは変換器を介さず直接束縛する。
	public Visibility WarningVisibility => _showWarning ? Visibility.Visible : Visibility.Collapsed;




	// 警告に添えるツールチップ。現在の色制限で表せないことと、最も近い色を示す。
	public string WarningTooltip => Loc.Get("Palette_Warning_TooltipFormat", _nearestHex);




	// スウォッチのボタンの読み上げ名。リスト項目に明示名を与えると配下の文字(色名・16進)は読み上げから外れるため、色名・カラーコードと、警告時はその旨をまとめてここに含める。警告の有無で変わるため ShowWarning の変更時に通知する。
	public string AccessibleName => _showWarning
		? $"{Name} {HexText} {Loc.Get("Palette_Warning_AccessibleSuffix")}"
		: $"{Name} {HexText}";




	public event PropertyChangedEventHandler? PropertyChanged;




	private void OnPropertyChanged(string name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
