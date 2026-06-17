// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「配色」タブの中身。サイドバーの色を円盤に番号つきマーカーで並べ、配色の種類(補色・類似・三色・分裂補色・四色・正方形)に沿って色相の角度関係を保ちながら、各マーカーをドラッグして複数の色を一度に動かせる。
// 円盤は中心からの角度が色相、中心からの距離が半径軸で、ディスクの種類で半径軸が変わる。明度ディスク(OKLCH)では半径=明度・下のスライダー=彩度、HSV ディスクでは半径=彩度(S)・スライダー=明度(V)。円盤の下地は各画素を色域内へ収めて塗り、編集対象の色には依存しないため、色制限の変更や円盤の拡縮・種類の変更のときだけ作り直す。
// 各色の作業値(色相・半径軸・スライダー軸)はこのタブが保持し、共有モデルへは sRGB を書き出す。外部(他タブ・元に戻す)で色が変わったときだけ作業値を取り直し、自分のドラッグ中の書き込みでは取り直さないことで、編集が丸めで目減りするのを防ぐ。編集対象の状態は色リストを束ねる共有モデルを外部から受け取る。
public sealed partial class HarmonyTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// 各色の作業値(色相 Hue=度・半径軸 Radial・スライダー軸 Slider)。サイドバーの色と同じ並び。半径軸とスライダー軸の意味はディスクの種類で変わり、明度ディスクでは Radial=明度・Slider=彩度、HSV ディスクでは Radial=彩度(S)・Slider=明度(V)。色域外でも保てる本来の値をここに持ち、円盤に映る色は色域へ収めた sRGB から作る。
	private readonly List<(double Hue, double Radial, double Slider)> _work = new();

	// 下のスライダー(色数ぶん)。並びは色と同じ。半径が担わない残りの軸(明度ディスクでは彩度、HSV ディスクでは明度 V)を受け持つ。
	private readonly List<GradientSlider> _sliders = new();

	// スライダーを今 HSV(明度 V)用に作ったか。明度ディスク⇄HSV ディスクを跨ぐと役割と表示名が変わるため、跨いだときはスライダーを作り直す。
	private bool _slidersHsv;

	// スライダーを今 共有トーン(全色共通の1本)で作ったか。色ごとの N 本⇄共通1本を跨ぐ配色の切り替えでスライダーを作り直す。
	private bool _slidersShared;

	// 円盤画像を生成した際の画素サイズ・色制限設定・ディスクの種類。下地は編集対象の色に依存しないため、この3つが同じなら作り直さない。種類は未生成を表す無効値で初期化する。
	private int _planePixels = -1;
	private SnapSettings _planeSnap;
	private LightnessDiscPattern _planePattern = (LightnessDiscPattern)(-1);

	// 案内の曲線を引くための、色相ごとの cusp 明度の表。cusp を使うディスクで、曲線のサンプルごとに cusp を求め直すと重いため事前計算して使い回す。ディスクの種類が変わったら作り直し、円になるディスクでは持たない。
	private double[]? _cuspTable;
	private LightnessDiscPattern _cuspTablePattern = (LightnessDiscPattern)(-1);

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)を円盤の一辺の算出に使う。
	private ScrollViewer? _scrollHost;

	// マーカーをドラッグ中か。ドラッグ中は自分の書き込みで作業値を取り直さず、編集が丸めで目減りするのを防ぐ。
	private bool _dragging;

	// 自分(このタブ)が色を書き込んでいる最中か。共有モデルの ColorsChanged を受けて作業値を取り直す処理を、自分の書き込みでは止める。
	private bool _applyingOwnChange;

	// スライダーへ値を流し込む間、ValueChanged が色を書き換えないようにする。
	private bool _sliderSyncing;

	// 円盤のヒント(Shift ドラッグの説明)のツールチップ。マーカーのドラッグ中はずっと出て邪魔になるため、ドラッグ中だけ外して離したら戻せるよう、初期値を覚えておく。
	private object? _discHintTooltip;

	// 円盤の一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinPadSide = 200.0;

	// 明度ディスクでスライダーが受け持つ彩度の上限。OKLCH の彩度の表示上限に合わせる。HSV ディスクでは上限が明度 V の 1 になる。
	private static readonly double CMax = LchColor.CMax(LchSpace.Oklch);

	// モノトーンのトーンカーブのプリセットで両端に置く明度。明(上)〜暗(下)の標準域で、純白・純黒の少し内側に取り、整えた直後の端マーカーを掴みやすくする。間の色はこの2点の間にカーブで配る。
	private const double MonotoneLightnessHi = 0.95;
	private const double MonotoneLightnessLo = 0.05;

	// ガンマのプリセットのべき指数。明暗どちらか一方へ段を寄せる強さ。反転と組み合わせると寄せる側が入れ替わる。
	private const double GammaExponent = 2.2;




	public HarmonyTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 円盤のヒントのツールチップを覚えておき、ドラッグ中だけ外せるようにする。配色の種類とディスクの種類の ComboBox は SelectedValue を VM のキーへ TwoWay 束縛しているため、ここでの手当ては要らない。
		_discHintTooltip = ToolTipService.GetToolTip(Disc);

		Disc.MarkerMoved += OnMarkerMoved;
		Disc.DragStarted += OnDragStarted;
		Disc.DragEnded += OnDragEnded;
		Disc.SizeChanged += OnDiscSizeChanged;

		Ramp.MarkerMoved += OnRampMarkerMoved;
		Ramp.DragStarted += OnRampDragStarted;
		Ramp.DragEnded += OnRampDragEnded;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	// 現在のディスクが HSV ディスクか。半径=彩度・スライダー=明度になり、色のモデルも OKLCH ではなく HSV になる。
	private bool IsHsvDisc => ViewModel.LightnessDiscPattern == LightnessDiscPattern.Hsv;




	// スライダー(0–100)の 100 に対応するスライダー軸の実値。明度ディスクは彩度の上限、HSV ディスクは明度 V の上限(1)。
	private double SliderMax => IsHsvDisc ? 1.0 : CMax;




	// 現在の配色の拘束の種類。固定オフセットか、色相を揃える(モノクロマティック)か、トーンを揃える(ドミナントトーン・トーナル)か。
	private HarmonySchemeKind SchemeKind => ColorHarmony.Kind(ViewModel.HarmonyScheme);




	// トーンを全色で揃える配色か。マーカーは同じ半径の輪に乗り、色相だけ自由に動き、スライダーは全色共通の1本になる。
	private bool IsSharedTone => SchemeKind == HarmonySchemeKind.SharedTone;




	// 無彩色だけの配色(モノトーン)か。円盤・彩度スライダーの代わりに、明度の縦パッド(MonotoneRamp)とトーンカーブのプリセットで扱う。
	private bool IsMonotone => SchemeKind == HarmonySchemeKind.Achromatic;




	// トーナルで半径軸を収める中間色域の下限・上限。明度ディスクは明度、HSV ディスクは彩度(S)の許容範囲。
	private double TonalRadialLo => IsHsvDisc ? 0.15 : 0.40;
	private double TonalRadialHi => IsHsvDisc ? 0.55 : 0.70;




	// 共通トーンをくすんだ中間色域に収める配色(トーナル)で、半径軸の値を中間域へ制限する。
	private double ClampTonalRadial(double radial)
	{
		return Math.Clamp(radial, TonalRadialLo, TonalRadialHi);
	}




	// トーナルで、スライダー軸の値を中間〜低めへ制限する。明度ディスクは彩度を低〜中へ、HSV ディスクは明度 V を中庸へ収める。
	private double ClampTonalSlider(double slider)
	{
		double norm = SliderMax > 0.0 ? slider / SliderMax : 0.0;
		double lo = IsHsvDisc ? 0.45 : 0.18;
		double hi = IsHsvDisc ? 0.78 : 0.50;
		return Math.Clamp(norm, lo, hi) * SliderMax;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 表示倍率(DPI)の変化で円盤を作り直せるよう XamlRoot の変化を購読する。表示先が変わっていれば購読を張り替える。
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// 色の並び・件数・アクティブ・値・色制限の変更で円盤とマーカー・スライダーを取り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.ColorListChanged -= OnColorListChanged;
		ViewModel.ColorListChanged += OnColorListChanged;
		ViewModel.ColorsChanged -= OnColorsChanged;
		ViewModel.ColorsChanged += OnColorsChanged;
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		// 配色タブの表示中に色を追加したら、その番号のマーカーの色で追加されるよう、新しい色の色を決める委譲を共有モデルへ預ける。タブを離れたら外す。
		ViewModel.NextColorProvider = GetHarmonyColorForSlot;

		if (_scrollHost is null)
		{
			_scrollHost = FindScrollHost();

			if (_scrollHost is not null)
			{
				_scrollHost.SizeChanged += OnLayoutMetricChanged;
			}
		}

		UpdatePadSize();
		BuildInitial();
		RegenerateDisc();
	}




	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		if (_subscribedRoot is not null)
		{
			_subscribedRoot.Changed -= OnXamlRootChanged;
			_subscribedRoot = null;
		}

		ViewModel.ColorListChanged -= OnColorListChanged;
		ViewModel.ColorsChanged -= OnColorsChanged;
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.NextColorProvider = null;
	}




	private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
	{
		RegenerateDisc();
	}




	// 色の並び・件数が変わったら、作業値とスライダー・マーカーを取り直す。円盤の下地は色に依存しないため塗り直さない。ドラッグ中は自分の操作なので取り直さない。
	private void OnColorListChanged(object? sender, EventArgs e)
	{
		if (_dragging)
		{
			return;
		}

		RefreshAll();
	}




	// いずれかの色の値が変わったら、作業値とマーカー・スライダーを取り直す。円盤の下地は色に依存しないため塗り直さない。ただしドラッグ中と自分の書き込み中は、編集が丸めで目減りするのを防ぐため取り直さない。
	private void OnColorsChanged(object? sender, EventArgs e)
	{
		if (_dragging || _applyingOwnChange)
		{
			return;
		}

		RefreshAll();
	}




	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// 色制限(表示レンズ)が変わると円盤の下地の丸めが変わる。色域外は生じない描き方のため、色域外の見せ方は影響しない。配色の種類はこのタブの ComboBox が束ねるため、ここでは扱わない。
		if (e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap))
		{
			RegenerateDisc();
		}
		else if (e.PropertyName == nameof(ColorEditorViewModel.LightnessDiscPattern))
		{
			// ディスクの種類が変わると半径の意味(明度↔彩度)や色のモデルが変わるため、作業値・スライダー・マーカーを取り直し、下地を作り直す。
			RefreshAll();
			RegenerateDisc();
		}
		else if (e.PropertyName == nameof(ColorEditorViewModel.HarmonyScheme))
		{
			// 配色の種類が変わったら、表示(円盤⇄モノトーンの縦パッド・操作群・彩度スライダー)を切り替え、各色を新しい配色の色モデルで取り直してから、その配色へ寄せ直す。取り直しを挟むのは、半径軸・スライダー軸の意味が種類で変わる境(無彩色のモノトーンと HSV など)で、前の作業値が別の意味に読まれるのを防ぐため。ComboBox は Tag で VM のキーへ TwoWay 束縛しており、選択の変化はここで受ける。
			UpdateModeVisibility();
			DeriveWorkingValues();
			ApplyScheme();

			// 円盤に戻ったときは隠れていた下地が古い色制限のままになりうるため、モノトーンに入ったときは縦パッドの下地へ今の色制限を渡すため、いずれも下地を引き直す。
			RegenerateDisc();
		}
		else if (e.PropertyName == nameof(ColorEditorViewModel.HarmonyReverse))
		{
			ApplyReverse();
		}
	}




	private void OnPadAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	private void OnSliderHostSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	private void OnLayoutMetricChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	private void OnDiscSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateDisc();
	}




	// 円盤を正方形のまま、エリアの幅と、下のスライダーを除いた残りの可視高さの小さい方へ合わせる。可視高さはスクロール領域のビューポート高さを使う。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private void UpdatePadSize()
	{
		double available = _scrollHost?.ViewportHeight ?? LayoutRoot.ActualHeight;

		if (available <= 0.0)
		{
			return;
		}

		if (IsMonotone)
		{
			// モノトーンの縦パッドは正方形に縛らず、領域いっぱい(左列の幅最大・残りの可視高さ)へ広げる。彩度スライダーが無いぶん高さの差し引きも要らない。スクロール領域の中では縦が伸び放題になるため、高さは明示して与える。
			double padWidth = PadArea.ActualWidth;
			double padHeight = Math.Max(MinPadSide, available - 12.0);

			if (padWidth > 0.0 && Ramp.Width != padWidth)
			{
				Ramp.Width = padWidth;
			}

			if (Ramp.Height != padHeight)
			{
				Ramp.Height = padHeight;
			}

			return;
		}

		double heightBudget = available - SliderHost.ActualHeight - 12.0;
		double side = Math.Min(PadArea.ActualWidth, Math.Max(MinPadSide, heightBudget));

		if (side <= 0.0)
		{
			return;
		}

		if (Disc.Width != side)
		{
			Disc.Width = side;
			Disc.Height = side;
		}
	}




	// 読み込み時の組み立て。作業値を取り直し、スライダーを色数ぶん作り、マーカー(と配色の線の閉じ方)を据える。
	private void BuildInitial()
	{
		DeriveWorkingValues();
		UpdateModeVisibility();

		if (IsMonotone)
		{
			Ramp.SetMarkers(BuildRampMarkers());
			return;
		}

		EnsureSliders();
		SyncSliders();
		Disc.ConnectMarkers = !IsSharedTone;
		Disc.SetMarkers(BuildMarkers(), ColorHarmony.IsClosed(ViewModel.HarmonyScheme));
		UpdateToneGuide();
	}




	// 色の変更を受けての取り直し。作業値・スライダー・マーカーを今の色に合わせる。マーカーは掴み判定の根を作り直さず位置と色だけ更新し、ドラッグ中でも掴みを失わないようにする。
	private void RefreshAll()
	{
		DeriveWorkingValues();
		UpdateModeVisibility();

		if (IsMonotone)
		{
			Ramp.UpdateMarkers(BuildRampMarkers());
			return;
		}

		EnsureSliders();
		SyncSliders();
		Disc.ConnectMarkers = !IsSharedTone;
		Disc.UpdateMarkers(BuildMarkers());
		UpdateToneGuide();
	}




	// 配色の種類に応じて表示を切り替える。モノトーンでは明度の縦パッドとトーンカーブの操作を出し、円盤・彩度スライダー・角度系の操作を隠す。それ以外は逆にする。
	private void UpdateModeVisibility()
	{
		bool mono = IsMonotone;

		Disc.Visibility = mono ? Visibility.Collapsed : Visibility.Visible;
		Ramp.Visibility = mono ? Visibility.Visible : Visibility.Collapsed;
		SliderHost.Visibility = mono ? Visibility.Collapsed : Visibility.Visible;
		OffsetControls.Visibility = mono ? Visibility.Collapsed : Visibility.Visible;
		MonotoneControls.Visibility = mono ? Visibility.Visible : Visibility.Collapsed;

		UpdatePadSize();
	}




	// サイドバーの各色の sRGB から、今のディスクの色モデルで作業値を取り直す。ただし、今の作業値がそのままその色の sRGB を生むなら、その色は前回このタブが書いた値のままで外部からは変わっていないとみなし、取り直さずに据え置く。色域外へ詰められた本来の値や 8bit 丸めの差で値がにじむのを防ぐためで、生む色が食い違う色(外部で変わった色・追加された色・ディスクの種類を跨いだ色)だけを取り直す。無彩色に近い色は色相が定まらないため、直前の色相を保つ。件数の増減にも合わせる。
	private void DeriveWorkingValues()
	{
		IReadOnlyList<SidebarColorViewModel> colors = ViewModel.Colors;

		while (_work.Count > colors.Count)
		{
			_work.RemoveAt(_work.Count - 1);
		}

		while (_work.Count < colors.Count)
		{
			_work.Add((0.0, 0.0, 0.0));
		}

		for (int i = 0; i < colors.Count; i++)
		{
			Color rgb = colors[i].Rgb;
			Color produced = ComposeColor(_work[i].Hue, _work[i].Radial, _work[i].Slider);

			if (produced.R == rgb.R && produced.G == rgb.G && produced.B == rgb.B)
			{
				continue;
			}

			(double hue, double radial, double slider) = DecomposeColor(rgb.R, rgb.G, rgb.B);

			// 無彩色(明度ディスクでは彩度、HSV では彩度 S が 0 に近い)は色相が定まらないため、直前の色相を保つ。彩度を表す軸はディスクの種類で異なる。
			double chromaAxis = IsHsvDisc ? radial : slider;

			if (chromaAxis < 1e-4)
			{
				hue = _work[i].Hue;
			}

			_work[i] = (hue, radial, slider);
		}

		// トーンを揃える配色では、各色の半径軸(明度や彩度)とスライダー軸を全色で同じトーンに保つ。1番の色のトーンを共通トーンとして全色へ写し、各色は色相だけが違う状態にする。
		if (IsSharedTone && _work.Count > 0)
		{
			double radial = _work[0].Radial;
			double slider = _work[0].Slider;

			for (int i = 0; i < _work.Count; i++)
			{
				_work[i] = (_work[i].Hue, radial, slider);
			}
		}
	}




	// 今のディスクの色モデルで、sRGB を作業値(色相・半径軸・スライダー軸)へ分解する。明度ディスクは OKLCH の(色相・明度・彩度)、HSV ディスクは(色相・彩度 S・明度 V)。
	private (double Hue, double Radial, double Slider) DecomposeColor(byte r, byte g, byte b)
	{
		if (IsMonotone)
		{
			// 無彩色は明度だけが意味を持つ。OKLCH の明度を半径軸に取り、色相とスライダー軸(彩度)は持たない。
			(double lightness, _, _) = LchColor.FromRgb(LchSpace.Oklch, r, g, b);
			return (0.0, lightness, 0.0);
		}

		if (IsHsvDisc)
		{
			(double h, double s, double v) = ColorConversion.RgbToHsv(r, g, b);
			return (h, s, v);
		}

		(double l, double c, double hue) = LchColor.FromRgb(LchSpace.Oklch, r, g, b);
		return (hue, l, c);
	}




	// 今のディスクの色モデルで、作業値(色相・半径軸・スライダー軸)を sRGB へ合成する。明度ディスクは OKLCH(色域外は彩度を詰めて収める)、HSV ディスクは HSV。
	private Color ComposeColor(double hue, double radial, double slider)
	{
		if (IsMonotone)
		{
			// 半径軸を明度とし、彩度0の無彩色(純グレー)を作る。色相とスライダー軸は使わない。
			return LchColor.ToRgb(LchSpace.Oklch, radial, 0.0, 0.0);
		}

		if (IsHsvDisc)
		{
			(byte r, byte g, byte b) = ColorConversion.HsvToRgb(hue, radial, slider);
			return Color.FromArgb(0xFF, r, g, b);
		}

		return LchColor.ToRgb(LchSpace.Oklch, radial, slider, hue);
	}




	// 今の作業値・配色の種類から、円盤に並べるマーカー群を組む。マーカーの数は配色が要する点数(schemeMax)。先頭から色のあるぶん(realCount)は実在のマーカー、足りないぶんは1番の色の半径軸・スライダー軸と配色の角度で作った仮マーカー(予告)にする。位置は中心からの角度が色相、距離が半径軸。配色の点数より多い色は出さない。配色タブは編集対象の1色という概念を持たないため、マーカーに強調は付けない。
	private List<HarmonyDisc.Marker> BuildMarkers()
	{
		double[] offsets = EffectiveOffsets();
		int schemeMax = SchemeMax();
		int realCount = Math.Min(ViewModel.Colors.Count, schemeMax);

		double baseHue = _work.Count > 0 ? _work[0].Hue : 0.0;
		double baseRadial = _work.Count > 0 ? _work[0].Radial : 0.5;
		double baseSlider = _work.Count > 0 ? _work[0].Slider : 0.0;

		var markers = new List<HarmonyDisc.Marker>(schemeMax);

		for (int i = 0; i < schemeMax; i++)
		{
			if (i < realCount && i < _work.Count)
			{
				(double nx, double ny) = PositionForColor(_work[i].Hue, _work[i].Radial);
				Color fill = ComposeColor(_work[i].Hue, _work[i].Radial, _work[i].Slider);
				markers.Add(new HarmonyDisc.Marker(nx, ny, fill, i + 1, isActive: false, isGhost: false));
			}
			else
			{
				double hue = Normalize(baseHue + offsets[i]);
				(double nx, double ny) = PositionForColor(hue, baseRadial);
				Color fill = ComposeColor(hue, baseRadial, baseSlider);
				markers.Add(new HarmonyDisc.Marker(nx, ny, fill, i + 1, false, isGhost: true));
			}
		}

		return markers;
	}




	// 配色が要する点数。固定オフセットの配色は色相オフセットの数、拘束系(色相を揃える・トーンを揃える)の配色は固定の点数を持たず現在の色数に追従する。いずれも色数の上限を超えない。
	private int SchemeMax()
	{
		int basis = SchemeKind == HarmonySchemeKind.Offset
			? ColorHarmony.HueOffsets(ViewModel.HarmonyScheme).Length
			: ViewModel.Colors.Count;
		return Math.Min(basis, ColorEditorViewModel.MaxColors);
	}




	// 現在の配色の色相オフセット。固定オフセットの配色は配色の並びを返し、反転がオンで非対称なときは左右反転(符号反転)した鏡像を返す。拘束系(色相を揃える・トーンを揃える)の配色は固定の角度を持たないため、色数ぶんの全0(基準色相からのずれなし)を返す。色相を揃える配色では全点が同一色相になり、トーンを揃える配色ではオフセットは使わず各色の色相をそのまま使う。
	private double[] EffectiveOffsets()
	{
		if (SchemeKind != HarmonySchemeKind.Offset)
		{
			return new double[SchemeMax()];
		}

		double[] baseOffsets = ColorHarmony.HueOffsets(ViewModel.HarmonyScheme);

		if (!ViewModel.HarmonyReverse || !ColorHarmony.IsDirectional(ViewModel.HarmonyScheme))
		{
			return baseOffsets;
		}

		var reversed = new double[baseOffsets.Length];

		for (int i = 0; i < baseOffsets.Length; i++)
		{
			reversed[i] = Normalize(-baseOffsets[i]);
		}

		return reversed;
	}




	// 色相・半径軸を円盤の正規化位置(0–1・左上原点)へ写す。中心からの角度を色相、距離を半径軸とし、半径軸から距離への対応はディスクの種類に従う。色相 0(赤)を上(北)に置き、時計回りに増やす。
	private (double X, double Y) PositionForColor(double hue, double radial)
	{
		double radius = RadiusForRadial(hue, radial);
		double radians = (90.0 - hue) * Math.PI / 180.0;
		double x = 0.5 + (radius * Math.Cos(radians) * 0.5);
		double y = 0.5 - (radius * Math.Sin(radians) * 0.5);
		return (x, y);
	}




	// 円盤の正規化位置(0–1)を色相・半径軸へ戻す。PositionForColor の逆。距離から半径軸への対応はディスクの種類に従う。
	private (double Hue, double Radial) PolarFromPosition(double x, double y)
	{
		double aNorm = (x - 0.5) * 2.0;
		double bNorm = (0.5 - y) * 2.0;
		double radius = Math.Min(1.0, Math.Sqrt((aNorm * aNorm) + (bNorm * bNorm)));

		// 色相 0(赤)を上(北)・時計回りに取る PositionForColor の逆。画面の角度から 90 度回し符号を反転する。
		double hue = Normalize(90.0 - (Math.Atan2(bNorm, aNorm) * 180.0 / Math.PI));
		return (hue, RadialFromRadius(hue, radius));
	}




	// 半径軸の値を円盤の正規化半径(0–1)へ写す。HSV ディスクは半径=彩度 S をそのまま、明度ディスクは型と色相ごとの cusp 明度に従って明度を半径へ写す。
	private double RadiusForRadial(double hue, double radial)
	{
		if (IsHsvDisc)
		{
			return Math.Clamp(radial, 0.0, 1.0);
		}

		LightnessDiscPattern pattern = ViewModel.LightnessDiscPattern;
		double edge = HueLightnessField.EdgeLightness(pattern, LchSpace.Oklch, hue);
		return HueLightnessField.RadiusFromLightness(pattern, radial, edge);
	}




	// 円盤の正規化半径(0–1)を半径軸の値へ戻す。RadiusForRadial の逆。
	private double RadialFromRadius(double hue, double radius)
	{
		if (IsHsvDisc)
		{
			return radius;
		}

		LightnessDiscPattern pattern = ViewModel.LightnessDiscPattern;
		double edge = HueLightnessField.EdgeLightness(pattern, LchSpace.Oklch, hue);
		return HueLightnessField.LightnessFromRadius(pattern, radius, edge);
	}




	// トーンを揃える配色の案内(マーカーが乗る曲線と、トーナルの帯の覆い)を引き直す。トーンを揃えない配色では消す。共有トーンの半径軸の値から、その値を保つ曲線を組み、トーナルでは帯の下限・上限の曲線も渡して動ける範囲を示す。
	private void UpdateToneGuide()
	{
		if (!IsSharedTone || _work.Count == 0)
		{
			Disc.ClearToneGuide();
			return;
		}

		EnsureCuspTable();

		var ring = SampleToneCurve(_work[0].Radial);

		if (ColorHarmony.IsTonal(ViewModel.HarmonyScheme))
		{
			Disc.SetToneGuide(ring, SampleToneCurve(TonalRadialLo), SampleToneCurve(TonalRadialHi));
		}
		else
		{
			Disc.SetToneGuide(ring, null, null);
		}
	}




	// 指定した半径軸の値を全色相で保つ曲線を、円盤の正規化座標(0–1)の点列として組む。HSV と全域ディスクは真円、cusp を使うディスクは色相ごとに半径が変わるウネウネした曲線になる。向きは PositionForColor と同じ(色相0を上・時計回り)。
	private List<Windows.Foundation.Point> SampleToneCurve(double radial)
	{
		const int samples = 120;
		var points = new List<Windows.Foundation.Point>(samples);

		for (int i = 0; i < samples; i++)
		{
			double hue = i * 360.0 / samples;
			double r = CurveRadius(hue, radial);
			double radians = (90.0 - hue) * Math.PI / 180.0;
			double x = 0.5 + (r * Math.Cos(radians) * 0.5);
			double y = 0.5 - (r * Math.Sin(radians) * 0.5);
			points.Add(new Windows.Foundation.Point(x, y));
		}

		return points;
	}




	// 曲線のサンプル1点ぶんの、半径軸の値から正規化半径への写し。cusp を使うディスクでは事前計算した表から cusp 明度を引いて重い再計算を避ける。RadiusForRadial と同じ対応を与える。
	private double CurveRadius(double hue, double radial)
	{
		if (IsHsvDisc)
		{
			return Math.Clamp(radial, 0.0, 1.0);
		}

		LightnessDiscPattern pattern = ViewModel.LightnessDiscPattern;

		if (pattern == LightnessDiscPattern.WhiteToCusp || pattern == LightnessDiscPattern.BlackToCusp)
		{
			return HueLightnessField.RadiusFromLightness(pattern, radial, CuspFromTable(hue));
		}

		double edge = HueLightnessField.EdgeLightness(pattern, LchSpace.Oklch, hue);
		return HueLightnessField.RadiusFromLightness(pattern, radial, edge);
	}




	// cusp を使うディスクで、色相ごとの cusp 明度の表を用意する。種類が変わったときだけ作り直す。円になるディスクでは表は要らない。
	private void EnsureCuspTable()
	{
		LightnessDiscPattern pattern = ViewModel.LightnessDiscPattern;

		if (pattern != LightnessDiscPattern.WhiteToCusp && pattern != LightnessDiscPattern.BlackToCusp)
		{
			return;
		}

		if (_cuspTablePattern == pattern && _cuspTable is not null)
		{
			return;
		}

		const int buckets = 180;
		var table = new double[buckets];

		System.Threading.Tasks.Parallel.For(0, buckets, b =>
		{
			table[b] = LchColor.CuspLightness(LchSpace.Oklch, b * 360.0 / buckets);
		});

		_cuspTable = table;
		_cuspTablePattern = pattern;
	}




	// 事前計算した表から、指定した色相に最も近い cusp 明度を引く。表が無いときはその場で求める。
	private double CuspFromTable(double hue)
	{
		if (_cuspTable is null || _cuspTable.Length == 0)
		{
			return LchColor.CuspLightness(LchSpace.Oklch, hue);
		}

		int n = _cuspTable.Length;
		int bucket = (int)Math.Round(hue / 360.0 * n) % n;
		return _cuspTable[bucket < 0 ? bucket + n : bucket];
	}




	private void OnDragStarted()
	{
		_dragging = true;

		// ドラッグ中はヒントのツールチップを外す。ポインタが円盤上に乗り続けて出っぱなしになり、操作の邪魔になるのを防ぐ。
		ToolTipService.SetToolTip(Disc, null);
	}




	// ドラッグ終了で固定を解く。作業値はこのタブが持ち続けるため取り直さず、色相・半径軸の変化で古くなったスライダーのランプだけ引き直す。外していたヒントのツールチップも戻す。
	private void OnDragEnded()
	{
		_dragging = false;
		ToolTipService.SetToolTip(Disc, _discHintTooltip);
		SyncSliders();
	}




	// マーカーが動いたら、配色の拘束を当てて各色の作業値を更新し、共有モデルへ sRGB を書き出してマーカーを引き直す。掴んだ点が指す色相へ配色全体を剛体回転させ、掴んだ点の半径軸(中心からの距離)だけを指した位置へ更新して他の点は半径軸を保つ。スライダー軸(明度ディスクでは彩度、HSV では明度)はスライダーが受け持つため、全点で各自の値を保つ。Shift を押している間は掴んだ点の半径軸も保ち、純粋に色相だけを回す。
	private void OnMarkerMoved(int index, double x, double y, bool keepRadius)
	{
		int realCount = Math.Min(ViewModel.Colors.Count, SchemeMax());

		if (index < 0 || index >= realCount || index >= _work.Count)
		{
			return;
		}

		(double hue, double radial) = PolarFromPosition(x, y);

		if (IsSharedTone)
		{
			// トーンを揃える配色。掴んだ点の色相だけを自由に更新し、半径軸(共通トーンの距離)は全色で一斉に動かす。Shift のときは共通の距離を保ち色相だけ回す。トーナルでは距離を中間色域へ収める。
			double sharedRadial = keepRadius ? _work[0].Radial : radial;

			if (ColorHarmony.IsTonal(ViewModel.HarmonyScheme))
			{
				sharedRadial = ClampTonalRadial(sharedRadial);
			}

			var touched = new List<int>();

			for (int j = 0; j < realCount; j++)
			{
				double hueJ = j == index ? hue : _work[j].Hue;
				_work[j] = (hueJ, sharedRadial, _work[j].Slider);
				touched.Add(j);
			}

			PushColors(touched);
			Disc.UpdateMarkers(BuildMarkers());
			SyncSliders();
			UpdateToneGuide();
			return;
		}

		double[] offsets = EffectiveOffsets();
		double baseHue = Normalize(hue - offsets[index]);

		var affected = new List<int>();

		for (int j = 0; j < realCount; j++)
		{
			double hueJ = Normalize(baseHue + offsets[j]);

			// 掴んだ点だけは指した位置の半径軸へ更新し、他は各自の半径軸を保つ。Shift のときは掴んだ点の半径軸も保ち、色相だけ回す。スライダー軸は全点で保つ。
			double radialJ = j == index && !keepRadius ? radial : _work[j].Radial;
			_work[j] = (hueJ, radialJ, _work[j].Slider);
			affected.Add(j);
		}

		PushColors(affected);
		Disc.UpdateMarkers(BuildMarkers());

		// スライダーのランプは各色の色相・半径軸で塗るため、回転や半径軸の変化で古くなる。ドラッグ中も引き直してスライダーの下地を即座に追従させる。
		SyncSliders();
	}




	// 「整える」ボタン。現在の配色の種類へ各色を寄せ直す。
	private void OnApplyClick(object sender, RoutedEventArgs e)
	{
		ApplyScheme();
	}




	// 現在の配色の種類へ各色を寄せる。固定オフセットの配色は色相を配色の角度へ揃え半径軸を全色の平均に均す。色相を揃える配色(モノクロマティック)は全色を1番の色相へ揃え半径軸を等間隔の階調にする。トーンを揃える配色(ドミナントトーン・トーナル)は色相を等間隔に配りトーン(半径軸・スライダー軸)を共通の1つへ揃える(トーナルは中間色域へ収める)。色相の向きの基準には1番の色を使う。配色に組み込まない余りの色は変えない。
	private void ApplyScheme()
	{
		if (_work.Count == 0)
		{
			return;
		}

		int realCount = Math.Min(ViewModel.Colors.Count, SchemeMax());

		if (realCount == 0)
		{
			return;
		}

		if (IsMonotone)
		{
			// 無彩色の配色。種類を選んだ時点では、明度を標準域の等間隔(リニア)に並べた素直なグレーランプにする。以降の整形はトーンカーブのボタンが受け持つ。
			ApplyMonotoneCurve(MonotoneCurve.Linear);
			return;
		}

		var affected = new List<int>();

		if (IsSharedTone)
		{
			// トーンを揃える配色。色相を等間隔に配り、トーン(半径軸・スライダー軸)を共通の1つへ揃える。色相の向きの基準には1番の色を使う。トーナルでは共通トーンを中間色域へ収める。
			double baseHue = _work[0].Hue;
			double radial = _work[0].Radial;
			double slider = _work[0].Slider;

			if (ColorHarmony.IsTonal(ViewModel.HarmonyScheme))
			{
				radial = ClampTonalRadial(radial);
				slider = ClampTonalSlider(slider);
			}

			for (int j = 0; j < realCount; j++)
			{
				_work[j] = (Normalize(baseHue + (j * 360.0 / realCount)), radial, slider);
				affected.Add(j);
			}
		}
		else if (SchemeKind == HarmonySchemeKind.SharedHue)
		{
			// 色相を揃える配色(モノクロマティック)。全色を1番の色相へ揃え、半径軸(明度や彩度)を全域へ等間隔に配って濃淡の階調にする。スライダー軸は各色のまま保つ。
			double baseHue = _work[0].Hue;

			for (int j = 0; j < realCount; j++)
			{
				double radial = realCount > 1 ? 0.95 - (0.9 * j / (realCount - 1)) : _work[j].Radial;
				_work[j] = (baseHue, radial, _work[j].Slider);
				affected.Add(j);
			}
		}
		else
		{
			// 固定オフセットの配色。各色の色相を配色の角度へ揃え、半径軸を全色の平均に均し、スライダー軸は各色のまま保つ。色相の向きの基準には1番の色を使う。
			double[] offsets = EffectiveOffsets();
			double baseHue = _work[0].Hue;
			double averageRadial = 0.0;

			for (int j = 0; j < realCount; j++)
			{
				averageRadial += _work[j].Radial;
			}

			averageRadial /= realCount;

			for (int j = 0; j < realCount; j++)
			{
				_work[j] = (Normalize(baseHue + offsets[j]), averageRadial, _work[j].Slider);
				affected.Add(j);
			}
		}

		PushColors(affected);
		Disc.ConnectMarkers = !IsSharedTone;
		Disc.SetMarkers(BuildMarkers(), ColorHarmony.IsClosed(ViewModel.HarmonyScheme));
		EnsureSliders();
		SyncSliders();
		UpdateToneGuide();
	}




	// 反転トグルが切り替わったら、配色に組み込まれた各色の色相を1番の色の向きで左右反転し、配色全体を鏡像へ移す。半径軸・スライダー軸は保つ。対称な配色では反転が効かないため何もしない。
	private void ApplyReverse()
	{
		if (_work.Count == 0 || !ColorHarmony.IsDirectional(ViewModel.HarmonyScheme))
		{
			return;
		}

		int realCount = Math.Min(ViewModel.Colors.Count, SchemeMax());
		double baseHue = _work[0].Hue;

		var affected = new List<int>();

		for (int j = 1; j < realCount; j++)
		{
			_work[j] = (Normalize((2.0 * baseHue) - _work[j].Hue), _work[j].Radial, _work[j].Slider);
			affected.Add(j);
		}

		PushColors(affected);
		Disc.SetMarkers(BuildMarkers(), ColorHarmony.IsClosed(ViewModel.HarmonyScheme));
		SyncSliders();
	}




	// 配色タブの表示中に色を追加するとき、共有モデルが新しい色の色を決めるために呼ぶ。1番の色から配色の角度で色相を回した色を返し、その番号の仮マーカーがそのまま実体化するようにする。半径軸・スライダー軸は1番の色のものを使う。固定オフセットの配色は配色の点数までを受け持ち、それより後ろは null を返して既定の色選びに委ねる。拘束系(色相を揃える・トーンを揃える)の配色は点数が色数に追従するため、色数の上限まで色相のずれなし(オフセット0)で受け持つ。
	private Color? GetHarmonyColorForSlot(int slotIndex)
	{
		int max = SchemeKind == HarmonySchemeKind.Offset ? SchemeMax() : ColorEditorViewModel.MaxColors;

		if (slotIndex < 0 || slotIndex >= max)
		{
			return null;
		}

		IReadOnlyList<SidebarColorViewModel> colors = ViewModel.Colors;

		if (colors.Count == 0)
		{
			return null;
		}

		double[] offsets = EffectiveOffsets();
		double offset = SchemeKind == HarmonySchemeKind.Offset && slotIndex < offsets.Length ? offsets[slotIndex] : 0.0;

		Color anchor = colors[0].Rgb;
		(double hue, double radial, double slider) = DecomposeColor(anchor.R, anchor.G, anchor.B);
		double newHue = Normalize(hue + offset);
		return ComposeColor(newHue, radial, slider);
	}




	// 指定した色の作業値を今のディスクの色モデルで sRGB へ直し、共有モデルへまとめて書き出す。自分の書き込み中は ColorsChanged での取り直しを止め、作業値が丸めで上書きされないようにする。
	private void PushColors(IReadOnlyList<int> indices)
	{
		if (indices.Count == 0)
		{
			return;
		}

		var updates = new List<(int Index, byte R, byte G, byte B)>(indices.Count);

		foreach (int i in indices)
		{
			Color rgb = ComposeColor(_work[i].Hue, _work[i].Radial, _work[i].Slider);
			updates.Add((i, rgb.R, rgb.G, rgb.B));
		}

		_applyingOwnChange = true;

		try
		{
			ViewModel.SetHarmonyColors(updates);
		}
		finally
		{
			_applyingOwnChange = false;
		}
	}




	// スライダーを用意する。トーンを揃える配色では全色共通の1本、それ以外では実在する色のぶんだけ並べる。件数・ディスクの種類(明度⇄HSV)・共有か否かが合っていれば作り直さない。各行は番号(共有のときは空)と、ランプを下地にした水平スライダー。
	private void EnsureSliders()
	{
		int count = IsSharedTone ? 1 : Math.Min(ViewModel.Colors.Count, SchemeMax());

		if (_sliders.Count == count && _slidersHsv == IsHsvDisc && _slidersShared == IsSharedTone)
		{
			return;
		}

		_slidersHsv = IsHsvDisc;
		_slidersShared = IsSharedTone;
		SliderHost.Children.Clear();
		_sliders.Clear();

		string nameKey = IsSharedTone
			? (IsHsvDisc ? "HarmonySharedValue" : "HarmonySharedChroma")
			: (IsHsvDisc ? "HarmonyValueN" : "HarmonyChromaN");

		for (int i = 0; i < count; i++)
		{
			var row = new Grid { ColumnSpacing = 8 };
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

			var number = new TextBlock
			{
				Text = IsSharedTone ? string.Empty : (i + 1).ToString(),
				Width = 16.0,
				VerticalAlignment = VerticalAlignment.Center,
				TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
			};
			Grid.SetColumn(number, 0);

			var slider = new GradientSlider
			{
				Minimum = 0.0,
				Maximum = 100.0,
				StepFrequency = 1.0,
				IsThumbToolTipEnabled = false,
				VerticalAlignment = VerticalAlignment.Center,
			};
			AutomationProperties.SetName(slider, IsSharedTone ? Loc.Get(nameKey) : Loc.Get(nameKey, i + 1));
			Grid.SetColumn(slider, 1);

			int index = i;
			slider.ValueChanged += (_, _) => OnSliderChanged(index);

			row.Children.Add(number);
			row.Children.Add(slider);
			SliderHost.Children.Add(row);
			_sliders.Add(slider);
		}
	}




	// スライダーの値と下地を今の作業値に合わせる。値の流し込みでは ValueChanged を無視する。下地は色相・半径軸でのランプにする。トーンを揃える配色では共通の1本を1番の色のトーンで合わせ、それ以外は各色ぶんを合わせる。
	private void SyncSliders()
	{
		_sliderSyncing = true;

		try
		{
			if (IsSharedTone)
			{
				if (_sliders.Count > 0 && _work.Count > 0)
				{
					_sliders[0].Value = _work[0].Slider / SliderMax * 100.0;
					_sliders[0].TrackBrush = MakeSliderRamp(_work[0].Hue, _work[0].Radial);
				}
			}
			else
			{
				for (int i = 0; i < _sliders.Count && i < _work.Count; i++)
				{
					_sliders[i].Value = _work[i].Slider / SliderMax * 100.0;
					_sliders[i].TrackBrush = MakeSliderRamp(_work[i].Hue, _work[i].Radial);
				}
			}
		}
		finally
		{
			_sliderSyncing = false;
		}
	}




	// スライダーが動いたら、スライダー軸を更新して書き出す。スライダー軸は中心からの距離(半径軸)に関わらないためマーカーの位置は変わらず、色だけ更新する。円盤の下地は色に依存しないため塗り直さない。トーンを揃える配色では共通の1本が全色のスライダー軸を一斉に動かし、トーナルでは中間色域へ収める。
	private void OnSliderChanged(int index)
	{
		if (_sliderSyncing || index < 0 || index >= _sliders.Count)
		{
			return;
		}

		if (IsSharedTone)
		{
			double shared = _sliders[index].Value / 100.0 * SliderMax;

			if (ColorHarmony.IsTonal(ViewModel.HarmonyScheme))
			{
				shared = ClampTonalSlider(shared);
			}

			var affected = new List<int>();

			for (int j = 0; j < _work.Count; j++)
			{
				_work[j] = (_work[j].Hue, _work[j].Radial, shared);
				affected.Add(j);
			}

			PushColors(affected);
			Disc.UpdateMarkers(BuildMarkers());
			SyncSliders();
			return;
		}

		if (index >= _work.Count)
		{
			return;
		}

		double slider = _sliders[index].Value / 100.0 * SliderMax;
		_work[index] = (_work[index].Hue, _work[index].Radial, slider);

		PushColors(new[] { index });
		Disc.UpdateMarkers(BuildMarkers());
	}




	// 指定した色相・半径軸でのスライダー軸のランプ(0→上限)のブラシを作る。スライダーの下地に使い、そのスライダーで動かせる色の幅を見せる。各値は今のディスクの色モデルで sRGB に直す。明度ディスクでは無彩色→鮮やかの彩度ランプ、HSV では黒→純色の明度ランプになる。
	private LinearGradientBrush MakeSliderRamp(double hue, double radial)
	{
		var brush = new LinearGradientBrush
		{
			StartPoint = new Windows.Foundation.Point(0.0, 0.5),
			EndPoint = new Windows.Foundation.Point(1.0, 0.5),
		};

		const int stops = 8;

		for (int i = 0; i <= stops; i++)
		{
			double t = (double)i / stops;
			Color color = ComposeColor(hue, radial, t * SliderMax);
			brush.GradientStops.Add(new GradientStop { Offset = t, Color = color });
		}

		return brush;
	}




	// 円盤の下地を、色制限・ディスクの種類・円盤の大きさ・表示倍率に合わせて作り直す。同じ条件なら作り直さない。下地は各画素を色域内へ収めて塗るため編集対象の色には依存せず、色制限・種類の変更と拡縮のときだけ作り直す。表示倍率そのままの実画素解像度で生成して等倍で表示する。
	private void RegenerateDisc()
	{
		// モノトーンでは円盤の下地を使わず、縦グラデーションは MonotoneRamp が自前で持つ。その下地のグレー階調も色制限(表示レンズ)で丸めるため、今の設定を縦パッドへ渡す。
		if (IsMonotone)
		{
			Ramp.SetSnap(ViewModel.CurrentSnap);
			return;
		}

		double size = Disc.ActualWidth;

		if (size <= 0.0 || Disc.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(size * scale);

		if (pixels <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		LightnessDiscPattern pattern = ViewModel.LightnessDiscPattern;

		if (pixels == _planePixels && snap == _planeSnap && pattern == _planePattern)
		{
			return;
		}

		_planePixels = pixels;
		_planeSnap = snap;
		_planePattern = pattern;

		WriteableBitmap plane = IsHsvDisc
			? HsvWheel.Create(pixels, pixels, snap)
			: HueLightnessField.Create(pixels, pixels, LchSpace.Oklch, snap, pattern);
		Disc.FieldImage = plane;
	}




	// 角度を [0, 360) へ収める。
	private static double Normalize(double degrees)
	{
		double value = degrees % 360.0;
		return value < 0.0 ? value + 360.0 : value;
	}




	// 視覚ツリーを上へ辿って、最初に見つかる祖先のスクロール領域を返す。タブの中身が包まれているスクロール領域を、束縛先を直接知らずに取得するために使う。
	private ScrollViewer? FindScrollHost()
	{
		DependencyObject? node = VisualTreeHelper.GetParent(this);

		while (node is not null)
		{
			if (node is ScrollViewer scroller)
			{
				return scroller;
			}

			node = VisualTreeHelper.GetParent(node);
		}

		return null;
	}




	// モノトーンのトーンカーブのプリセットの形。t(0–1)を f(0–1)へ写す。Linear は直線、Cubic はスムーズステップ(S字)、InverseS はその逆関数(逆S字)、Gamma はべき乗で明暗どちらか一方へ寄せる。
	private enum MonotoneCurve
	{
		Linear,
		Cubic,
		InverseS,
		Gamma,
	}




	// 今の作業値から、モノトーンの縦パッドに並べるマーカー群を組む。横位置は色の並び順に沿い、両端の余白もマーカー間隔と揃うよう (i+1)/(N+1) で等間隔に固定する(端へ張り付かない)。縦位置は明度(上が明)、色は明度から作る純グレー。件数は現在の色数(上限まで)。
	private List<MonotoneRamp.Marker> BuildRampMarkers()
	{
		int count = Math.Min(_work.Count, ColorEditorViewModel.MaxColors);
		var markers = new List<MonotoneRamp.Marker>(count);

		for (int i = 0; i < count; i++)
		{
			double x = (double)(i + 1) / (count + 1);
			double lightness = Math.Clamp(_work[i].Radial, 0.0, 1.0);
			double y = 1.0 - lightness;
			Color fill = ComposeColor(0.0, lightness, 0.0);
			markers.Add(new MonotoneRamp.Marker(x, y, fill, i + 1));
		}

		return markers;
	}




	// 縦パッドのマーカーが動いたら、その色の明度(明度 = 1 − y)を更新して書き出し、マーカーを引き直す。横位置(並び順)は動かない。
	private void OnRampMarkerMoved(int index, double y)
	{
		if (index < 0 || index >= _work.Count)
		{
			return;
		}

		double lightness = Math.Clamp(1.0 - y, 0.0, 1.0);
		_work[index] = (0.0, lightness, 0.0);

		PushColors(new[] { index });
		Ramp.UpdateMarkers(BuildRampMarkers());
	}




	private void OnRampDragStarted()
	{
		_dragging = true;
	}




	private void OnRampDragEnded()
	{
		_dragging = false;
	}




	// 今の明度の並びが、左(1番)から右へおおむね明るくなる向きかを返す。トーンカーブのプリセットを現状のざっくりした向きへ合わせ、手で並べた向きと逆のカーブになるのを防ぐのに使う。スロット位置と明度の共分散の符号で判定し、傾きが無い(平ら)ときは慣習的な明→暗の降順とみなして偽を返す。
	private bool MonotoneAscending()
	{
		int count = Math.Min(_work.Count, ColorEditorViewModel.MaxColors);

		if (count < 2)
		{
			return false;
		}

		double meanIndex = (count - 1) / 2.0;
		double meanLightness = 0.0;

		for (int i = 0; i < count; i++)
		{
			meanLightness += _work[i].Radial;
		}

		meanLightness /= count;

		double covariance = 0.0;

		for (int i = 0; i < count; i++)
		{
			covariance += (i - meanIndex) * (_work[i].Radial - meanLightness);
		}

		return covariance > 0.0;
	}




	// モノトーンの明度の並びを、指定したトーンカーブのプリセットへ整える。両端を標準域(明 0.95・暗 0.05)に置き、間の各色を t = i/(N−1) でカーブに沿って配る。明暗どちらを左に置くかは現状のざっくりした向きに合わせ、手で並べた向きと逆にならないようにする。各色は彩度0の無彩色で、明度だけが変わる。
	private void ApplyMonotoneCurve(MonotoneCurve curve)
	{
		int count = Math.Min(_work.Count, ColorEditorViewModel.MaxColors);

		if (count == 0)
		{
			return;
		}

		bool ascending = MonotoneAscending();
		double startLightness = ascending ? MonotoneLightnessLo : MonotoneLightnessHi;
		double endLightness = ascending ? MonotoneLightnessHi : MonotoneLightnessLo;

		var affected = new List<int>();

		for (int i = 0; i < count; i++)
		{
			double t = count > 1 ? (double)i / (count - 1) : 0.0;
			double shaped = CurveShape(curve, t);
			double lightness = startLightness + ((endLightness - startLightness) * shaped);
			_work[i] = (0.0, lightness, 0.0);
			affected.Add(i);
		}

		PushColors(affected);
		Ramp.SetMarkers(BuildRampMarkers());
	}




	// トーンカーブのプリセットの形を与える。t(0–1)を単調増加の f(0–1)へ写し、f(0)=0・f(1)=1。Cubic はスムーズステップ、InverseS はその逆関数、Gamma はべき乗で明暗どちらか一方へ段を寄せる。
	private static double CurveShape(MonotoneCurve curve, double t)
	{
		double x = Math.Clamp(t, 0.0, 1.0);

		return curve switch
		{
			MonotoneCurve.Cubic => x * x * (3.0 - (2.0 * x)),
			MonotoneCurve.InverseS => 0.5 - Math.Sin(Math.Asin(1.0 - (2.0 * x)) / 3.0),
			MonotoneCurve.Gamma => Math.Pow(x, GammaExponent),
			_ => x,
		};
	}




	// 明度の並び順を逆にして、ランプの向き(明→暗 ⇄ 暗→明)を入れ替える。今の各色の明度を左右に鏡映するだけなので、プリセット直後でも手で動かした後でも効く。
	private void ReverseMonotone()
	{
		int count = Math.Min(_work.Count, ColorEditorViewModel.MaxColors);

		if (count < 2)
		{
			return;
		}

		var lightnesses = new double[count];

		for (int i = 0; i < count; i++)
		{
			lightnesses[i] = _work[count - 1 - i].Radial;
		}

		var affected = new List<int>();

		for (int i = 0; i < count; i++)
		{
			_work[i] = (0.0, lightnesses[i], 0.0);
			affected.Add(i);
		}

		PushColors(affected);
		Ramp.SetMarkers(BuildRampMarkers());
	}




	private void OnCurveLinearClick(object sender, RoutedEventArgs e)
	{
		ApplyMonotoneCurve(MonotoneCurve.Linear);
	}




	private void OnCurveCubicClick(object sender, RoutedEventArgs e)
	{
		ApplyMonotoneCurve(MonotoneCurve.Cubic);
	}




	private void OnCurveInverseSClick(object sender, RoutedEventArgs e)
	{
		ApplyMonotoneCurve(MonotoneCurve.InverseS);
	}




	private void OnCurveGammaClick(object sender, RoutedEventArgs e)
	{
		ApplyMonotoneCurve(MonotoneCurve.Gamma);
	}




	private void OnCurveReverseClick(object sender, RoutedEventArgs e)
	{
		ReverseMonotone();
	}
}
