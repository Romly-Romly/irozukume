// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Globalization.NumberFormatting;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;
using Irozukume.Controls.Generators;
using Irozukume.Controls.Generators.Planes;

namespace Irozukume.Views;

// 「RGB/CMYK」タブの中身。色1(編集中)を R・G・B と C・M・Y・K の両系統のスライダーで編集する。CMYK は RGB から導出されるため、どちらを動かしても色1へ反映され、互いに追従する。
// 両群の下には独立した無彩色(灰)群としてスライダーを1本添え、つまむと R=G=B を同値へ揃えて脱色する。無彩色は3要素が連動するマクロのため、独立して動かせる R・G・B・C・M・Y・K の群とは分けて並べ、取り違えを避ける。
// 無彩色スライダーの値も 0–255 で、R・G・B と同じ単位コンボ(RgbUnitIndex)に従う。R・G・B のスライダーは常に 0–255 で扱い、R・G・B 共通の単位コンボボックスは数値入力欄の見せ方(0–255 / 00–FF / 0.0–1.0)だけを切り替える。
// 数値入力欄は選んだ単位に追従し、範囲・刻み・書式をコード側で組み替える。編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class RgbCmykTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// 数値入力欄をモデルに合わせて組み替えている最中か。組み替えに伴う NumberBox の値変化を、利用者の入力と取り違えてモデルへ書き戻さないために立てる。
	private bool _syncing;

	// 無彩色スライダーの値をモデルに合わせて流し込んでいる最中か。Gray は get(Rec.601 投影)と set(R=G=B 一括)が互いに逆関数でないため、R・G・B 変更に伴う Gray の再計算をスライダーへ流し込むと、双方向束縛なら書き戻されて脱色が起きる。これを立てて、流し込みに伴う ValueChanged を利用者の操作と取り違えてモデルへ書き戻さないようにする。
	private bool _graySliderSyncing;

	// 現在の RGB 平面の見せ方。Sliders は RGB の平面を出さない。GbPlane=G×B 平面+R の縦バー、RbPlane=R×B 平面+G の縦バー、RgPlane=R×G 平面+B の縦バー。VM の RgbCmykLayoutIndex の RGB 域(1〜3)を反映する。
	private RgbLayout _layout = RgbLayout.Sliders;

	// 現在のレイアウトが平面パッドホスト(RgbPad)を使うか。偽なら3本スライダーのみ。
	private bool _planeHost;

	// RGB 平面の下地画像を生成した際の画素サイズ・固定成分(縦バーが司る成分)の値・色制限設定。同じ条件での作り直しを避けるために覚えておく。固定成分は未生成を表す -1 で初期化する。
	private int _planePixels = -1;
	private int _planeFixedValue = -1;
	private SnapSettings _planeSnap;
	private RgbLayout _planeLayout = (RgbLayout)(-1);
	private bool _planeValid;

	// 現在の CMYK 平面の見せ方。Sliders は CMYK の平面を出さない。MyPlane=M×Y 平面+C の縦バー、CyPlane=C×Y 平面+M の縦バー、CmPlane=C×M 平面+Y の縦バー。VM の RgbCmykLayoutIndex の CMYK 域(4〜6)を反映する。
	private CmykLayout _cmykLayout = CmykLayout.Sliders;

	// 現在の CMYK の見せ方が平面パッドホスト(CmykPad)を使うか。偽なら4本スライダーのみ。
	private bool _cmykPlaneHost;

	// CMYK 平面の下地画像を生成した際の画素サイズ・固定成分(縦バーが司る CMY 成分)の値・墨(K)の値・色制限設定。同じ条件での作り直しを避けるために覚えておく。固定成分・墨は未生成を表す NaN で初期化する。
	private int _cmykPlanePixels = -1;
	private double _cmykPlaneFixed = double.NaN;
	private double _cmykPlaneK = double.NaN;
	private SnapSettings _cmykPlaneSnap;
	private CmykLayout _cmykPlaneLayout = (CmykLayout)(-1);
	private bool _cmykPlaneValid;

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// パッドと縦スライダーの間隔。XAML の中央寄せの組の ColumnSpacing と同じ値にし、パッドの一辺を幅から決めるときにスライダー幅と併せて差し引く。
	private const double PadRailGap = 12.0;

	// パッドの一辺の下限・上限。エリアの幅が広いときも上限で頭打ちにして、平面が嵩張りすぎないようにする。
	private const double MinPadSide = 160.0;
	private const double MaxPadSide = 360.0;


	public RgbCmykTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 平面パッド(RgbPad)は PlanarPad を流用するため、つまみ・レンズ・矢印操作はそのまま得られる。操作は2成分をまとめて扱うため ValuesChanged からレイアウトに応じた設定へ振り分け、大きさ変化で下地画像を作り直す。
		RgbPad.ValuesChanged += OnRgbPadValuesChanged;
		RgbPad.SizeChanged += OnRgbPadSizeChanged;

		// CMYK 平面パッド(CmykPad)も同じく PlanarPad を流用する。操作は CMY のうち2成分をまとめて扱う。
		CmykPad.ValuesChanged += OnCmykPadValuesChanged;
		CmykPad.SizeChanged += OnCmykPadSizeChanged;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 表示倍率(DPI)の変化で平面を作り直せるよう XamlRoot の変化を購読する。表示先が変わっていれば購読を張り替える。
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// R・G・B の値・表示単位・見せ方・色制限の変更で、数値入力欄・平面・つまみを整えるための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		SyncAllBoxes();
		SyncGraySlider();
		ApplyLayout();
	}




	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		if (_subscribedRoot is not null)
		{
			_subscribedRoot.Changed -= OnXamlRootChanged;
			_subscribedRoot = null;
		}

		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
	}




	private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
	{
		RegeneratePlane();
		RegenerateCmykPlane();
	}




	// R・G・B のいずれかの値、または表示単位が変わったら数値入力欄を組み替える。単位の変更時は3つともまとめて組み替える。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(ColorEditorViewModel.R):
				SyncBox(RValueBox, ViewModel.R);
				OnChannelChanged();
				break;

			case nameof(ColorEditorViewModel.G):
				SyncBox(GValueBox, ViewModel.G);
				OnChannelChanged();
				break;

			case nameof(ColorEditorViewModel.B):
				SyncBox(BValueBox, ViewModel.B);
				OnChannelChanged();
				break;

			case nameof(ColorEditorViewModel.Gray):
				SyncBox(GrayValueBox, ViewModel.Gray);
				SyncGraySlider();
				break;

			case nameof(ColorEditorViewModel.RgbUnitIndex):
				SyncAllBoxes();
				break;

			case nameof(ColorEditorViewModel.RgbCmykLayoutIndex):
				ApplyLayout();
				break;

			case nameof(ColorEditorViewModel.C):
			case nameof(ColorEditorViewModel.M):
			case nameof(ColorEditorViewModel.Y):
			case nameof(ColorEditorViewModel.K):
				OnCmykChannelChanged();
				break;

			case nameof(ColorEditorViewModel.CurrentSnap):
				RegeneratePlane();
				RegenerateCmykPlane();
				UpdatePadLayoutPickerIcon();
				break;
		}
	}




	// R・G・B のいずれかが変わったら、平面ホストが活性ならつまみ位置を合わせ直し、固定成分が動いていれば(キャッシュの空振り判定で)平面を作り直す。
	private void OnChannelChanged()
	{
		if (!_planeHost)
		{
			return;
		}

		UpdatePadThumb();
		RegeneratePlane();
	}




	// C・M・Y・K のいずれかが変わったら、CMYK 平面ホストが活性ならつまみ位置を合わせ直し、固定成分(または墨)が動いていれば(キャッシュの空振り判定で)平面を作り直す。墨(K)はつまみには効かないが平面の下地には効くため作り直しの判定に含める。
	private void OnCmykChannelChanged()
	{
		if (!_cmykPlaneHost)
		{
			return;
		}

		UpdateCmykPadThumb();
		RegenerateCmykPlane();
	}




	// R・G・B の数値入力欄を、現在の値と表示単位に合わせてまとめて組み替える。
	private void SyncAllBoxes()
	{
		SyncBox(RValueBox, ViewModel.R);
		SyncBox(GValueBox, ViewModel.G);
		SyncBox(BValueBox, ViewModel.B);
		SyncBox(GrayValueBox, ViewModel.Gray);
	}




	// 1つの数値入力欄を、現在の成分値(0–255)と表示単位に合わせて組み替える。単位ごとに範囲・刻み・書式を変え、値を単位の数値へ直して入れる。組み替え中は書き戻しを止め、範囲の切り替えで起きる値の丸めを利用者の入力と取り違えないようにする。
	private void SyncBox(NumberBox box, double channel)
	{
		_syncing = true;

		switch (ViewModel.RgbUnitIndex)
		{
			case 1:
				ConfigureBox(box, 0.0, 255.0, 1.0, 16.0, NumberFormatters.Hex, Math.Round(channel));
				break;
			case 2:
				ConfigureBox(box, 0.0, 1.0, 0.01, 0.1, NumberFormatters.TwoDecimal, channel / 255.0);
				break;
			default:
				ConfigureBox(box, 0.0, 255.0, 1.0, 16.0, NumberFormatters.Integer, Math.Round(channel));
				break;
		}

		_syncing = false;
	}




	// 数値入力欄の範囲・刻み・書式・値をまとめて設定する。範囲を先に整えてから値を入れることで、前の単位の値が新しい範囲へ丸められても最終的に正しい値で上書きする。
	private static void ConfigureBox(NumberBox box, double minimum, double maximum, double smallChange, double largeChange, INumberFormatter2 formatter, double value)
	{
		box.Minimum = minimum;
		box.Maximum = maximum;
		box.SmallChange = smallChange;
		box.LargeChange = largeChange;
		box.NumberFormatter = formatter;
		box.Value = value;
	}




	// 数値入力欄の値が利用者の操作で変わったら、現在の単位の数値を 0–255 の成分へ直してモデルへ反映する。どの成分かは Tag で判別する。組み替え中の変化や空欄(NaN)は無視する。
	private void OnRgbValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_syncing || double.IsNaN(sender.Value))
		{
			return;
		}

		double channel = ViewModel.RgbUnitIndex == 2 ? sender.Value * 255.0 : sender.Value;
		double rounded = Math.Round(channel);

		switch (sender.Tag as string)
		{
			case "R":
				ViewModel.R = rounded;
				break;

			case "G":
				ViewModel.G = rounded;
				break;

			case "B":
				ViewModel.B = rounded;
				break;

			case "Gray":
				ViewModel.Gray = rounded;
				break;
		}
	}




	// 無彩色スライダーのつまみを、現在の Gray(色1の Rec.601 投影)へ合わせる。流し込み中は書き戻しを止め、R・G・B の変更に伴う Gray の再計算が利用者の操作と取り違えられて脱色を起こすのを防ぐ。
	private void SyncGraySlider()
	{
		_graySliderSyncing = true;
		GraySlider.Value = ViewModel.Gray;
		_graySliderSyncing = false;
	}




	// 無彩色スライダーの値が利用者の操作で変わったら、その明るさへ R=G=B を一括で揃える(= 脱色)。流し込み中の変化や空欄(NaN)は無視し、R・G・B 変更に伴う追従では脱色しない。
	private void OnGraySliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		if (_graySliderSyncing || double.IsNaN(e.NewValue))
		{
			return;
		}

		ViewModel.Gray = Math.Round(e.NewValue);
	}




	// 見せ方を、現在の選択に合わせて切り替える。RGB・CMYK 共通の1つの位置(RgbCmykLayoutIndex)から、出すパッド(RGB 系・CMYK 系・無し)を決める。タブに出すパッドは高々1枚で、RGB の平面(1〜3)なら RGB パッドを、CMYK の平面(4〜6)なら CMYK パッドを出し、もう一方は畳む。0 のときは両方畳んで線形スライダーだけにする。出すホストの縦スライダーの束縛を差し替え、寸法・つまみ・平面・隅アイコンを整える。
	private void ApplyLayout()
	{
		int idx = ViewModel.RgbCmykLayoutIndex;

		_layout = idx switch
		{
			1 => RgbLayout.GbPlane,
			2 => RgbLayout.RbPlane,
			3 => RgbLayout.RgPlane,
			_ => RgbLayout.Sliders,
		};

		_cmykLayout = idx switch
		{
			4 => CmykLayout.MyPlane,
			5 => CmykLayout.CyPlane,
			6 => CmykLayout.CmPlane,
			_ => CmykLayout.Sliders,
		};

		_planeHost = _layout != RgbLayout.Sliders;
		_cmykPlaneHost = _cmykLayout != CmykLayout.Sliders;

		RgbPadArea.Visibility = _planeHost ? Visibility.Visible : Visibility.Collapsed;
		CmykPadArea.Visibility = _cmykPlaneHost ? Visibility.Visible : Visibility.Collapsed;

		if (_planeHost)
		{
			// RGB 平面のキャッシュを無効化して必ず作り直す。縦スライダーの束縛を差し替え、寸法・つまみ・平面を整える。
			_planeValid = false;
			ConfigureCutSlider();
			UpdatePadSize();
			UpdatePadThumb();
			RegeneratePlane();
		}

		if (_cmykPlaneHost)
		{
			// CMYK 平面のキャッシュを無効化して必ず作り直す。
			_cmykPlaneValid = false;
			ConfigureCmykCutSlider();
			UpdateCmykPadSize();
			UpdateCmykPadThumb();
			RegenerateCmykPlane();
		}

		UpdatePadLayoutPickerIcon();
	}




	// 切り出した成分(平面の2軸に取らない残り1成分)の縦スライダーを、値・背景・名前ごと束ね直す。G×B 平面では R、R×B 平面では G、R×G 平面では B を司る。値は 0–255 で、横向きの R・G・B スライダーと同じ成分へ双方向に束縛する。
	private void ConfigureCutSlider()
	{
		(int fixedChannel, int _, int _) = Channels(_layout);
		BindingOperations.SetBinding(RgbCutSlider, RangeBase.ValueProperty, MakeViewModelBinding(ChannelPath(fixedChannel), BindingMode.TwoWay));
		BindingOperations.SetBinding(RgbCutSlider, GradientSlider.TrackBrushProperty, MakeViewModelBinding(ChannelBrushPath(fixedChannel), BindingMode.OneWay));
		AutomationProperties.SetName(RgbCutSlider, ChannelLetter(fixedChannel));
	}




	// 平面パッドのつまみを、現在のレイアウトの2軸の成分値(0–255 を 0–1 へ正規化)の位置へ移す。XValue・YValue はドラッグで局所設定されると束縛が外れるため、束縛に頼らずコードで更新する。設定で発火するのはつまみ位置の更新だけで、ValuesChanged はポインタ・キーボード操作のときしか発火しないため、ここでの設定が操作の取り込みへ回り込むことはない。
	private void UpdatePadThumb()
	{
		if (!_planeHost)
		{
			return;
		}

		(int _, int xChannel, int yChannel) = Channels(_layout);
		RgbPad.XValue = ChannelValue(xChannel) / 255.0;
		RgbPad.YValue = ChannelValue(yChannel) / 255.0;
	}




	// 平面パッドを操作したら、横軸・縦軸に取った2成分をまとめて VM へ渡す。固定成分(縦スライダーが司る側)は VM 側で保たれる。
	private void OnRgbPadValuesChanged(object? sender, EventArgs e)
	{
		(int _, int xChannel, int yChannel) = Channels(_layout);
		ViewModel.SetRgbPlane(xChannel, RgbPad.XValue, yChannel, RgbPad.YValue);
	}




	// 平面パッドの大きさが変わったら、下地画像を作り直す。パッドを初めて表示したときの寸法確定もこの経路で拾う。
	private void OnRgbPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegeneratePlane();
	}




	// 編集エリアの幅が変わったら、パッドの一辺を算出し直す。
	private void OnRgbPadAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// パッドを正方形のまま、エリアの幅から縦スライダーの幅と間隔を除いた残りへ合わせ(上限・下限で頭打ち)、縦スライダーの高さもそれに揃える。これでパッドとスライダーの組が一定間隔を保ったまま中央寄せで拡縮する。パッドの大きさが変わると、それを受けた SizeChanged で平面画像が作り直される。
	private void UpdatePadSize()
	{
		if (!_planeHost)
		{
			return;
		}

		double available = RgbPadArea.ActualWidth;

		if (available <= 0.0)
		{
			return;
		}

		double sliderColumn = RgbCutSlider.ActualWidth > 0.0 ? RgbCutSlider.ActualWidth : 32.0;
		double widthBudget = available - sliderColumn - PadRailGap;
		double side = Math.Clamp(widthBudget, MinPadSide, MaxPadSide);

		if (side <= 0.0)
		{
			return;
		}

		if (RgbPad.Width != side)
		{
			RgbPad.Width = side;
			RgbPad.Height = side;
			RgbCutSlider.Height = side;
		}
	}




	// RGB 平面の下地画像を、現在のレイアウト・固定成分(縦バーが司る成分)の値・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・固定成分・色制限・レイアウトなら作り直さない。2軸はパッド全面に渡って変わるため鍵に含めない。
	private void RegeneratePlane()
	{
		if (!_planeHost)
		{
			return;
		}

		double size = RgbPad.ActualWidth;

		if (size <= 0.0 || RgbPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(size * scale);

		if (pixels <= 0)
		{
			return;
		}

		(int fixedChannel, int xChannel, int yChannel) = Channels(_layout);
		int fixedValue = (int)Math.Round(ChannelValue(fixedChannel));
		SnapSettings snap = ViewModel.CurrentSnap;

		if (pixels == _planePixels && fixedValue == _planeFixedValue && _planeValid && snap == _planeSnap && _layout == _planeLayout)
		{
			return;
		}

		_planePixels = pixels;
		_planeFixedValue = fixedValue;
		_planeSnap = snap;
		_planeLayout = _layout;
		_planeValid = true;

		WriteableBitmap plane = RgbPlane.Create(pixels, pixels, xChannel, yChannel, fixedChannel, (byte)fixedValue, snap);
		RgbPlaneImage.Source = plane;

		// ドラッグ中のレンズは、生成したこのビットマップをそのまま読んで映す。固定成分はドラッグ中一定のため、画像は据え置きで足りる。
		RgbPad.LensColorSampler = new BitmapFieldSampler(plane, RgbPad).Sample;
	}




	// 指定したレイアウトの縮小見本(サムネイル)を作る。下地そのもので区別がつくため、各平面はそのまま描く。固定成分は色が映える代表値(128)を当てる。0=3本スライダー(R・G・B のランプ)、1=G×B 平面(固定 R=128)、2=R×B 平面(固定 G=128)、3=R×G 平面(固定 B=128)。
	private static WriteableBitmap LayoutThumbnailFor(int index, int pixels, SnapSettings snap)
	{
		return index switch
		{
			1 => RgbPlane.Create(pixels, pixels, 1, 2, 0, 128, snap),
			2 => RgbPlane.Create(pixels, pixels, 0, 2, 1, 128, snap),
			3 => RgbPlane.Create(pixels, pixels, 0, 1, 2, 128, snap),
			_ => RgbPlane.CreateSlidersIcon(pixels, pixels),
		};
	}




	// RGB/CMYK 共通の見せ方ピッカーの番号(0..6)に対応する縮小見本を作る。0=線形スライダーのみ、1〜3=RGB の平面(G×B・R×B・R×G)、4〜6=CMYK の平面(M×Y・C×Y・C×M)。RGB・CMYK それぞれの縮小見本生成へ振り分ける。
	private static WriteableBitmap PadLayoutThumbnailFor(int index, int pixels, SnapSettings snap)
	{
		return index switch
		{
			1 => LayoutThumbnailFor(1, pixels, snap),
			2 => LayoutThumbnailFor(2, pixels, snap),
			3 => LayoutThumbnailFor(3, pixels, snap),
			4 => CmykLayoutThumbnailFor(1, pixels, snap),
			5 => CmykLayoutThumbnailFor(2, pixels, snap),
			6 => CmykLayoutThumbnailFor(3, pixels, snap),
			_ => RgbPlane.CreateSlidersIcon(pixels, pixels),
		};
	}




	// 隅のレイアウトボタンのアイコンを、現在選んでいる見せ方の縮小見本に差し替える。アイコン自体が現在の状態表示を兼ねる。
	private void UpdatePadLayoutPickerIcon()
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(24.0 * scale));
		PadLayoutPickerIcon.Source = PadLayoutThumbnailFor(ViewModel.RgbCmykLayoutIndex, pixels, ViewModel.CurrentSnap);
	}




	// 見せ方選択のフライアウトを開くときに、7つの見せ方のサムネイルを今の色制限で作り直し、現在の選択を縁取りで示す。
	private void OnPadLayoutFlyoutOpening(object sender, object e)
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(56.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;

		LayoutSlidersThumbImage.Source = PadLayoutThumbnailFor(0, pixels, snap);
		LayoutRgbGbThumbImage.Source = PadLayoutThumbnailFor(1, pixels, snap);
		LayoutRgbRbThumbImage.Source = PadLayoutThumbnailFor(2, pixels, snap);
		LayoutRgbRgThumbImage.Source = PadLayoutThumbnailFor(3, pixels, snap);
		LayoutCmykMyThumbImage.Source = PadLayoutThumbnailFor(4, pixels, snap);
		LayoutCmykCyThumbImage.Source = PadLayoutThumbnailFor(5, pixels, snap);
		LayoutCmykCmThumbImage.Source = PadLayoutThumbnailFor(6, pixels, snap);

		var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
		var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
		int current = ViewModel.RgbCmykLayoutIndex;
		LayoutSlidersThumbBorder.BorderBrush = current == 0 ? accent : clear;
		LayoutRgbGbThumbBorder.BorderBrush = current == 1 ? accent : clear;
		LayoutRgbRbThumbBorder.BorderBrush = current == 2 ? accent : clear;
		LayoutRgbRgThumbBorder.BorderBrush = current == 3 ? accent : clear;
		LayoutCmykMyThumbBorder.BorderBrush = current == 4 ? accent : clear;
		LayoutCmykCyThumbBorder.BorderBrush = current == 5 ? accent : clear;
		LayoutCmykCmThumbBorder.BorderBrush = current == 6 ? accent : clear;
	}




	private void OnPickSlidersLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.RgbCmykLayoutIndex = 0;
		PadLayoutPickerFlyout.Hide();
	}




	private void OnPickRgbGbLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.RgbCmykLayoutIndex = 1;
		PadLayoutPickerFlyout.Hide();
	}




	private void OnPickRgbRbLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.RgbCmykLayoutIndex = 2;
		PadLayoutPickerFlyout.Hide();
	}




	private void OnPickRgbRgLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.RgbCmykLayoutIndex = 3;
		PadLayoutPickerFlyout.Hide();
	}




	private void OnPickCmykMyLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.RgbCmykLayoutIndex = 4;
		PadLayoutPickerFlyout.Hide();
	}




	private void OnPickCmykCyLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.RgbCmykLayoutIndex = 5;
		PadLayoutPickerFlyout.Hide();
	}




	private void OnPickCmykCmLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.RgbCmykLayoutIndex = 6;
		PadLayoutPickerFlyout.Hide();
	}




	// レイアウトごとの成分の割り当て。返り値は (固定成分, 横軸成分, 縦軸成分) で、いずれも 0=R, 1=G, 2=B。G×B 平面は固定 R・横 G・縦 B、R×B 平面は固定 G・横 R・縦 B、R×G 平面は固定 B・横 R・縦 G。
	private static (int Fixed, int X, int Y) Channels(RgbLayout layout)
	{
		return layout switch
		{
			RgbLayout.GbPlane => (0, 1, 2),
			RgbLayout.RbPlane => (1, 0, 2),
			RgbLayout.RgPlane => (2, 0, 1),
			_ => (0, 1, 2),
		};
	}




	// 成分(0=R, 1=G, 2=B)の現在値(0–255)。平面のつまみ位置・固定成分の代表値に使う。
	private double ChannelValue(int channel)
	{
		return channel switch
		{
			0 => ViewModel.R,
			1 => ViewModel.G,
			_ => ViewModel.B,
		};
	}




	// 縦スライダーが束ねる成分の ViewModel プロパティ名(値)。
	private static string ChannelPath(int channel)
	{
		return channel switch
		{
			0 => nameof(ColorEditorViewModel.R),
			1 => nameof(ColorEditorViewModel.G),
			_ => nameof(ColorEditorViewModel.B),
		};
	}




	// 縦スライダーが束ねる成分の縦向き背景の ViewModel プロパティ名。
	private static string ChannelBrushPath(int channel)
	{
		return channel switch
		{
			0 => nameof(ColorEditorViewModel.RedTrackBrushVertical),
			1 => nameof(ColorEditorViewModel.GreenTrackBrushVertical),
			_ => nameof(ColorEditorViewModel.BlueTrackBrushVertical),
		};
	}




	// 成分を表す言語非依存の名前。アクセシビリティ名に使う。
	private static string ChannelLetter(int channel)
	{
		return channel switch
		{
			0 => "R",
			1 => "G",
			_ => "B",
		};
	}




	// ViewModel をソースにした束縛を作る。コードビハインドから DP へ動的に束ねるのに使う。
	private Binding MakeViewModelBinding(string path, BindingMode mode)
	{
		return new Binding
		{
			Path = new PropertyPath(path),
			Source = ViewModel,
			Mode = mode,
		};
	}




	// 現在の RGB 群の見せ方。Sliders は R・G・B の3本スライダー(既定)。GbPlane=G×B 平面+R の縦バー、RbPlane=R×B 平面+G の縦バー、RgPlane=R×G 平面+B の縦バー。
	private enum RgbLayout
	{
		Sliders,
		GbPlane,
		RbPlane,
		RgPlane,
	}




	// 切り出した成分(平面の2軸に取らない残りの CMY 成分)の縦スライダーを、値・背景・名前ごと束ね直す。M×Y 平面では C、C×Y 平面では M、C×M 平面では Y を司る。値は 0–100% で、横向きの C・M・Y スライダーと同じ成分へ双方向に束縛する。
	private void ConfigureCmykCutSlider()
	{
		(int fixedChannel, int _, int _) = CmykChannels(_cmykLayout);
		BindingOperations.SetBinding(CmykCutSlider, RangeBase.ValueProperty, MakeViewModelBinding(CmykChannelPath(fixedChannel), BindingMode.TwoWay));
		BindingOperations.SetBinding(CmykCutSlider, GradientSlider.TrackBrushProperty, MakeViewModelBinding(CmykChannelBrushPath(fixedChannel), BindingMode.OneWay));
		AutomationProperties.SetName(CmykCutSlider, CmykChannelLetter(fixedChannel));
	}




	// CMYK 平面パッドのつまみを、現在のレイアウトの2軸の成分値(0–100% を 0–1 へ正規化)の位置へ移す。XValue・YValue はドラッグで局所設定されると束縛が外れるため、束縛に頼らずコードで更新する。
	private void UpdateCmykPadThumb()
	{
		if (!_cmykPlaneHost)
		{
			return;
		}

		(int _, int xChannel, int yChannel) = CmykChannels(_cmykLayout);
		CmykPad.XValue = ChannelPercent(xChannel) / 100.0;
		CmykPad.YValue = ChannelPercent(yChannel) / 100.0;
	}




	// CMYK 平面パッドを操作したら、横軸・縦軸に取った2つの CMY 成分をまとめて VM へ渡す。固定成分(縦スライダーが司る側)と墨(K)は VM 側で保たれる。
	private void OnCmykPadValuesChanged(object? sender, EventArgs e)
	{
		(int _, int xChannel, int yChannel) = CmykChannels(_cmykLayout);
		ViewModel.SetCmykPlane(xChannel, CmykPad.XValue, yChannel, CmykPad.YValue);
	}




	// CMYK 平面パッドの大きさが変わったら、下地画像を作り直す。パッドを初めて表示したときの寸法確定もこの経路で拾う。
	private void OnCmykPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateCmykPlane();
	}




	// CMYK 編集エリアの幅が変わったら、パッドの一辺を算出し直す。
	private void OnCmykPadAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateCmykPadSize();
	}




	// CMYK パッドを正方形のまま、エリアの幅から縦スライダーの幅と間隔を除いた残りへ合わせ(上限・下限で頭打ち)、縦スライダーの高さもそれに揃える。パッドの大きさが変わると、それを受けた SizeChanged で平面画像が作り直される。
	private void UpdateCmykPadSize()
	{
		if (!_cmykPlaneHost)
		{
			return;
		}

		double available = CmykPadArea.ActualWidth;

		if (available <= 0.0)
		{
			return;
		}

		double sliderColumn = CmykCutSlider.ActualWidth > 0.0 ? CmykCutSlider.ActualWidth : 32.0;
		double widthBudget = available - sliderColumn - PadRailGap;
		double side = Math.Clamp(widthBudget, MinPadSide, MaxPadSide);

		if (side <= 0.0)
		{
			return;
		}

		if (CmykPad.Width != side)
		{
			CmykPad.Width = side;
			CmykPad.Height = side;
			CmykCutSlider.Height = side;
		}
	}




	// CMYK 平面の下地画像を、現在のレイアウト・固定成分(縦バーが司る CMY 成分)の値・墨(K)・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・固定成分・墨・色制限・レイアウトなら作り直さない。平面の2軸はパッド全面に渡って変わるため鍵に含めない。
	private void RegenerateCmykPlane()
	{
		if (!_cmykPlaneHost)
		{
			return;
		}

		double size = CmykPad.ActualWidth;

		if (size <= 0.0 || CmykPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(size * scale);

		if (pixels <= 0)
		{
			return;
		}

		(int fixedChannel, int xChannel, int yChannel) = CmykChannels(_cmykLayout);
		double fixedValue = ChannelPercent(fixedChannel) / 100.0;
		double k = ViewModel.K / 100.0;
		SnapSettings snap = ViewModel.CurrentSnap;

		if (pixels == _cmykPlanePixels && fixedValue == _cmykPlaneFixed && k == _cmykPlaneK && _cmykPlaneValid && snap == _cmykPlaneSnap && _cmykLayout == _cmykPlaneLayout)
		{
			return;
		}

		_cmykPlanePixels = pixels;
		_cmykPlaneFixed = fixedValue;
		_cmykPlaneK = k;
		_cmykPlaneSnap = snap;
		_cmykPlaneLayout = _cmykLayout;
		_cmykPlaneValid = true;

		WriteableBitmap plane = CmykPlane.Create(pixels, pixels, xChannel, yChannel, fixedChannel, fixedValue, k, snap);
		CmykPlaneImage.Source = plane;

		// ドラッグ中のレンズは、生成したこのビットマップをそのまま読んで映す。固定成分・墨はドラッグ中一定のため、画像は据え置きで足りる。
		CmykPad.LensColorSampler = new BitmapFieldSampler(plane, CmykPad).Sample;
	}




	// 指定したレイアウトの縮小見本(サムネイル)を作る。下地そのもので区別がつくため、各平面はそのまま描く。色が映えるよう固定成分(縦バーの CMY 成分)と墨はともに 0 を当てる。0=4本スライダー(C・M・Y・K のランプ)、1=M×Y 平面(固定 C=0)、2=C×Y 平面(固定 M=0)、3=C×M 平面(固定 Y=0)。
	private static WriteableBitmap CmykLayoutThumbnailFor(int index, int pixels, SnapSettings snap)
	{
		return index switch
		{
			1 => CmykPlane.Create(pixels, pixels, 1, 2, 0, 0.0, 0.0, snap),
			2 => CmykPlane.Create(pixels, pixels, 0, 2, 1, 0.0, 0.0, snap),
			3 => CmykPlane.Create(pixels, pixels, 0, 1, 2, 0.0, 0.0, snap),
			_ => CmykPlane.CreateSlidersIcon(pixels, pixels),
		};
	}




	// CMYK レイアウトごとの成分の割り当て。返り値は (固定成分, 横軸成分, 縦軸成分) で、いずれも 0=C, 1=M, 2=Y。M×Y 平面は固定 C・横 M・縦 Y、C×Y 平面は固定 M・横 C・縦 Y、C×M 平面は固定 Y・横 C・縦 M。墨(K)は平面の対象外。
	private static (int Fixed, int X, int Y) CmykChannels(CmykLayout layout)
	{
		return layout switch
		{
			CmykLayout.MyPlane => (0, 1, 2),
			CmykLayout.CyPlane => (1, 0, 2),
			CmykLayout.CmPlane => (2, 0, 1),
			_ => (0, 1, 2),
		};
	}




	// CMY 成分(0=C, 1=M, 2=Y)の現在値(0–100%)。平面のつまみ位置・固定成分の代表値に使う。
	private double ChannelPercent(int channel)
	{
		return channel switch
		{
			0 => ViewModel.C,
			1 => ViewModel.M,
			_ => ViewModel.Y,
		};
	}




	// 縦スライダーが束ねる CMY 成分の ViewModel プロパティ名(値)。
	private static string CmykChannelPath(int channel)
	{
		return channel switch
		{
			0 => nameof(ColorEditorViewModel.C),
			1 => nameof(ColorEditorViewModel.M),
			_ => nameof(ColorEditorViewModel.Y),
		};
	}




	// 縦スライダーが束ねる CMY 成分の縦向き背景の ViewModel プロパティ名。
	private static string CmykChannelBrushPath(int channel)
	{
		return channel switch
		{
			0 => nameof(ColorEditorViewModel.CyanTrackBrushVertical),
			1 => nameof(ColorEditorViewModel.MagentaTrackBrushVertical),
			_ => nameof(ColorEditorViewModel.YellowTrackBrushVertical),
		};
	}




	// CMY 成分を表す言語非依存の名前。アクセシビリティ名に使う。
	private static string CmykChannelLetter(int channel)
	{
		return channel switch
		{
			0 => "C",
			1 => "M",
			_ => "Y",
		};
	}




	// 現在の CMYK 群の見せ方。Sliders は C・M・Y・K の4本スライダー(既定)。MyPlane=M×Y 平面+C の縦バー、CyPlane=C×Y 平面+M の縦バー、CmPlane=C×M 平面+Y の縦バー。
	private enum CmykLayout
	{
		Sliders,
		MyPlane,
		CyPlane,
		CmPlane,
	}
}
