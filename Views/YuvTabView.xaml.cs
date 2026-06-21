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
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;
using Irozukume.Controls.Generators;
using Irozukume.Controls.Generators.Planes;

namespace Irozukume.Views;

// 「YUV/YCbCr」タブの中身。色1(編集中)を YCbCr で編集する。上段は2次元の編集エリアと、その右の縦並び設定(YCbCr/YUV 切替・係数規格・量子化レンジ・色域制限・見せ方ピッカー)。編集エリアは見せ方(レイアウト)で3種を入れ替える: Cb×Cr 色差平面+輝度 Y の縦バー(既定)、Cb×Y 平面+Cr の縦バー、Cr×Y 平面+Cb の縦バー。いずれもパッドはウィンドウへ追従して拡縮し、添える縦スライダーの高さもそれに揃える。
// 下段に Y・Cb・Cr の水平スライダーを置き、上段とどちらからでも操作できる(全レイアウトで常時表示)。各平面は sRGB 色域に収まる部分だけを実色で塗り、色域外はハッチで透かして可視化する。
// 各平面は固定成分(その面の2軸に取らない残り)と符号化形式(規格・レンジ)に依って色と色域の形が変わるため、それらが変わるたびにコードで画像を作って差し込む。係数規格・量子化レンジ・YUV(符号付き)表記は切り替えられるが、いずれも色1の RGB は変えず数値の読み方とガモットの形だけを変える。RGB が真実の源で、他タブの編集にも追従する。編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class YuvTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// Cb-Cr 平面画像を生成した際の画素サイズ・輝度・符号化形式・色制限設定・色域外の見せ方・表示枠の決め方。同じ条件での作り直しを避けるために覚えておく。輝度は未生成を表す NaN で初期化する。スケールは固定成分(輝度)が変わると枠も変わるため、輝度とともに鍵に含めて追従させる。
	private int _planePixels = -1;
	private double _planeLuma = double.NaN;
	private YCbCrFormat _planeFormat;
	private SnapSettings _planeSnap;
	private GamutOutOfRangeStyle _planeStyle = (GamutOutOfRangeStyle)(-1);
	private int _planeScaleIndex = -1;
	private bool _planeValid;

	// 直交パッド(Cb×Y・Cr×Y)の下地画像を生成した際の画素サイズ(縦横独立)・固定成分の値・符号化形式・色制限設定・色域外の見せ方・表示枠の決め方。同じ条件での作り直しを避けるために覚えておく。固定成分はパッドの2軸に取らない側の色差で、未生成を表す NaN で初期化する。スケールは固定成分が変わると枠も変わるため、固定成分とともに鍵に含めて追従させる。
	private int _cartPixelWidth = -1;
	private int _cartPixelHeight = -1;
	private double _cartFixed = double.NaN;
	private YCbCrFormat _cartFormat;
	private SnapSettings _cartSnap;
	private GamutOutOfRangeStyle _cartStyle = (GamutOutOfRangeStyle)(-1);
	private int _cartScaleIndex = -1;
	private bool _cartValid;

	// 直交パッド(Cb×Y・Cr×Y)のドラッグ中のレンズが読む、生成済みのビットマップサンプラー。色域ハッチなど計算の込み入った下地を作り直さず、表示中の画像をそのまま読んで映す。生成のたびに差し替える。
	private BitmapFieldSampler? _cartSampler;

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)をパッドの一辺の算出に使う。
	private ScrollViewer? _scrollHost;

	// パッドの一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinPadSide = 180.0;

	// パッドと縦スライダー(輝度レール・切り出し色差の縦スライダー)の間隔。XAML の中央寄せの組の ColumnSpacing と同じ値にし、パッドの一辺を幅から決めるときにスライダー幅と併せて差し引く。
	private const double PadRailGap = 12.0;

	// 表示モード(YCbCr/YUV)のラジオを VM と同期する間、SelectionChanged が VM を上書きしないようにする。構築中の初期化を無視するため真で始める。
	private bool _modeSyncing = true;

	// Cb・Cr の数値入力欄をモデルに合わせて組み替えている最中か。組み替えに伴う NumberBox の値変化を利用者の入力と取り違えてモデルへ書き戻さないために立てる。
	private bool _chromaSyncing;

	// 現在の見せ方。CbCrPlane は Cb×Cr 平面+輝度 Y の縦バー(既定)。CbYPlane=Cb×Y 平面+Cr、CrYPlane=Cr×Y 平面+Cb。VM の YuvLayoutIndex を反映する。
	private YuvLayout _layout = YuvLayout.CbCrPlane;

	// 現在のレイアウトが直交パッドホスト(YuvCartPad)を使うか。偽なら既定の Cb×Cr 平面+輝度レール。
	private bool _cartHost;


	public YuvTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 復元済みの表示モードをラジオに反映する。ここまでの SelectionChanged は _modeSyncing で無視し、以降の操作だけ VM へ伝える。
		ModeSelector.SelectedIndex = ViewModel.IsSignedMode ? 1 : 0;
		_modeSyncing = false;

		CbCrPad.SizeChanged += OnPadSizeChanged;

		// Cb-Cr パッドの操作は Cb・Cr を二次元でまとめて扱う(色域制限オン時の色域内への最近傍寄せ)ため、横・縦の個別束縛ではなく ValuesChanged からまとめて VM へ渡す。
		CbCrPad.ValuesChanged += OnCbCrPadValuesChanged;

		// 直交パッド(YuvCartPad)は PlanarPad を流用するため、つまみ・レンズ・矢印操作はそのまま得られる。操作は2成分をまとめて扱うため ValuesChanged からレイアウトに応じた設定へ振り分け、レンズに映す色面のサンプラーを与え、大きさ変化で下地画像を作り直す。軸の束縛はレイアウトに応じて ConfigureCartPad が差し替える。
		YuvCartPad.ValuesChanged += OnCartValuesChanged;
		YuvCartPad.LensColorSampler = SampleCartField;
		YuvCartPad.SizeChanged += OnCartPadSizeChanged;

		// パッドの一辺はスクロール領域の可視高さからスライダー群の高さを引いて決めるため、その高さが変わったら算出し直す。可視高さの変化は読み込み後に見つけるスクロール領域の SizeChanged で、エリアの幅変化は PadArea の SizeChanged で拾う。
		// パッドの幅はエリアの幅から縦スライダーの幅と間隔を差し引いて決めるため、スライダーの実寸が定まる(初回計測)タイミングでも算出し直す。
		SliderHost.SizeChanged += OnLayoutMetricChanged;

		// 右の縦並び設定列は上段の行高の下限になるため、その高さが変わったらパッドの一辺を算出し直す。
		SideControls.SizeChanged += OnLayoutMetricChanged;

		LumaSlider.SizeChanged += OnLayoutMetricChanged;
		YuvCartSlider.SizeChanged += OnLayoutMetricChanged;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 表示倍率(DPI)の変化で各面を作り直せるよう XamlRoot の変化を購読する。表示先が変わっていれば購読を張り替える。
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// 輝度・色差・符号化形式・色制限設定・色域外の見せ方・見せ方の変更で各面を塗り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		// タブの中身を包む祖先のスクロール領域を一度だけ見つけ、その可視高さの変化でパッドを算出し直せるようにする。
		if (_scrollHost is null)
		{
			_scrollHost = FindScrollHost();

			if (_scrollHost is not null)
			{
				_scrollHost.SizeChanged += OnLayoutMetricChanged;
			}
		}

		ApplyLayout();
		SyncChromaBoxes();
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
		RegenerateActiveField();
	}




	private void OnPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegeneratePlane();
	}




	// Cb-Cr パッドを操作したら、Cb・Cr を二次元でまとめて VM へ渡す。色域制限オン時はカーソルに最も近い色域内の点へ寄せる処理が VM 側で行われる。
	private void OnCbCrPadValuesChanged(object? sender, EventArgs e)
	{
		ViewModel.SetYuvPad(CbCrPad.XValue, CbCrPad.YValue);
	}




	// 編集エリアの幅が変わったら、パッドの一辺を算出し直す。
	private void OnPadAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// 全体の高さ、スライダー群の高さ、各レイアウトの縦スライダーの幅のいずれかが変わったら、パッドの一辺を算出し直す。
	private void OnLayoutMetricChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// 色1の輝度・色差・符号化形式・色制限設定・色域外の見せ方・見せ方が変わったら、活性なホストの下地とつまみを整える。各 Regenerate は空振り判定とキャッシュを持つため、固定成分が動いていなければ実際には描き直さない。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// 見せ方が変わったら、ホストの出し分け・寸法・画像・つまみを一斉に整える。
		if (e.PropertyName == nameof(ColorEditorViewModel.YuvLayoutIndex))
		{
			ApplyLayout();
			return;
		}

		// 表示枠(スケール)が変わったら、活性なホストの下地を作り直す。既定 Cb×Cr 平面ではつまみ位置はパッドの XValue・YValue 束縛が VM の通知で追従し、直交パッドではここで UpdateCartThumb がコードで合わせ直す。
		if (e.PropertyName == nameof(ColorEditorViewModel.YuvScaleIndex))
		{
			RegenerateActiveField();

			if (_cartHost)
			{
				UpdateCartThumb();
			}

			return;
		}

		// 輝度・色差・符号化形式・色制限が変わったら、活性なホストの下地(固定成分が変わったときだけ)とつまみを整える。Cb×Cr 平面では輝度が、Cb×Y 平面では Cr が、Cr×Y 平面では Cb が固定成分。表示モード(YCbCr/YUV)の切替では平面は変わらず、数値の読み方だけが変わるため塗り直さない。
		if (e.PropertyName == nameof(ColorEditorViewModel.Luma)
			|| e.PropertyName == nameof(ColorEditorViewModel.Cb)
			|| e.PropertyName == nameof(ColorEditorViewModel.Cr)
			|| e.PropertyName == nameof(ColorEditorViewModel.StandardIndex)
			|| e.PropertyName == nameof(ColorEditorViewModel.UseStudioRange)
			|| e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap)
			|| e.PropertyName == nameof(ColorEditorViewModel.OutOfRangeStyle))
		{
			RegenerateActiveField();

			if (_cartHost)
			{
				UpdateCartThumb();
			}
		}

		// 符号化形式・色制限・色域外の見せ方が変わると、固定成分の代表値で描く隅アイコンの色・色域の形も変わる。輝度・色差だけの変化では代表値固定の隅アイコンは変わらないため作り直さない。
		if (e.PropertyName == nameof(ColorEditorViewModel.StandardIndex)
			|| e.PropertyName == nameof(ColorEditorViewModel.UseStudioRange)
			|| e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap)
			|| e.PropertyName == nameof(ColorEditorViewModel.OutOfRangeStyle))
		{
			UpdateLayoutPickerIcon();
		}

		// 色差の値そのものか、表記(YCbCr/符号付き YUV)が変わったら、Cb・Cr の数値入力欄を組み替える。係数規格・量子化レンジの変更でも色差の値は変わり、その通知(Cb・Cr)で拾われる。
		if (e.PropertyName == nameof(ColorEditorViewModel.Cb)
			|| e.PropertyName == nameof(ColorEditorViewModel.Cr)
			|| e.PropertyName == nameof(ColorEditorViewModel.IsSignedMode))
		{
			SyncChromaBoxes();
		}
	}




	// 活性なホスト(直交パッド・Cb×Cr 平面)の下地を、現在の固定成分・符号化形式・色制限・色域外の見せ方に合わせて作り直す。各 Regenerate は空振り判定とキャッシュを持つため、変わっていなければ実際には描き直さない。
	private void RegenerateActiveField()
	{
		if (_cartHost)
		{
			RegenerateCart();
		}
		else
		{
			RegeneratePlane();
		}
	}




	// パッドを正方形(直交パッドのときは横長)のまま、エリアの幅から縦スライダーの幅と間隔を除いた残りと、下に並ぶ要素を除いた残りの可視高さの小さい方へ合わせ、縦スライダーの高さもそれに揃える。これでパッドとスライダーの組が一定間隔を保ったまま中央寄せで拡縮する。直交パッド(Cb×Y・Cr×Y)は横軸の色差を細かく取れるよう横長にする。可視高さはスクロール領域のビューポート高さから、下段のスライダー群と行間を引いて求める。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる。パッドの大きさが変わると、それを受けた SizeChanged で各面の画像が作り直される。
	private void UpdatePadSize()
	{
		double available = _scrollHost?.ViewportHeight ?? LayoutRoot.ActualHeight;

		if (available <= 0.0)
		{
			return;
		}

		double heightBudget = available - SliderHost.ActualHeight - LayoutRoot.RowSpacing;

		// 上段の行高はパッドエリアと、その右の縦並び設定列の高い方で決まる。設定列の方が嵩むと、パッドをそれ未満へ縮めても行は縮まず、パッドは上端へ詰まったまま下端との間に空白が生じ、下段スライダー群との間が空く。これを防ぐため、パッドの下限を既定の下限と設定列の高さの大きい方に取る。設定列の実寸が未確定の初回は既定の下限で見積もり、確定後の SizeChanged で正す。
		double minSide = Math.Max(MinPadSide, SideControls.ActualHeight);

		// 直交パッド(Cb×Y・Cr×Y)のとき。横軸の色差を細かく取れるよう横いっぱいに広げ、縦は残りの高さに合わせる。
		if (_cartHost)
		{
			double sliderColumn = YuvCartSlider.ActualWidth > 0.0 ? YuvCartSlider.ActualWidth : 32.0;
			double widthBudget = PadArea.ActualWidth - sliderColumn - PadRailGap;
			double padHeight = Math.Max(minSide, heightBudget);

			if (widthBudget <= 0.0)
			{
				return;
			}

			if (YuvCartPad.Width != widthBudget)
			{
				YuvCartPad.Width = widthBudget;
			}

			if (YuvCartPad.Height != padHeight)
			{
				YuvCartPad.Height = padHeight;
			}

			if (YuvCartSlider.Height != padHeight)
			{
				YuvCartSlider.Height = padHeight;
			}

			return;
		}

		// 既定の Cb×Cr 平面+輝度レール。パッドとレールを一定間隔で並べた組を中央寄せにするため、パッドが取れる幅はエリアの幅からレールの幅と間隔を除いた残り。
		double cbcrWidthBudget = PadArea.ActualWidth - LumaSlider.ActualWidth - PadRailGap;
		double side = Math.Min(cbcrWidthBudget, Math.Max(minSide, heightBudget));

		if (side <= 0.0)
		{
			return;
		}

		if (CbCrPad.Width != side)
		{
			CbCrPad.Width = side;
			CbCrPad.Height = side;
			LumaSlider.Height = side;
		}
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




	// YCbCr(0–255 中心 128)と YUV(符号付き 中心 0)の表示切替。色は変えず数値の読み方だけが変わる。構築中の初期化は無視する。
	private void OnModeChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_modeSyncing)
		{
			return;
		}

		ViewModel.IsSignedMode = ModeSelector.SelectedIndex == 1;
	}




	// 見せ方を、現在の選択に合わせて切り替える。ホストの出し分けと束縛の差し替えを行い、寸法・画像・つまみ・隅アイコンを整える。
	private void ApplyLayout()
	{
		_layout = ViewModel.YuvLayoutIndex switch
		{
			1 => YuvLayout.CbYPlane,
			2 => YuvLayout.CrYPlane,
			_ => YuvLayout.CbCrPlane,
		};

		_cartHost = _layout == YuvLayout.CbYPlane || _layout == YuvLayout.CrYPlane;
		bool cbcr = !_cartHost;

		YuvCbCrLayoutRoot.Visibility = cbcr ? Visibility.Visible : Visibility.Collapsed;
		YuvCartLayoutRoot.Visibility = _cartHost ? Visibility.Visible : Visibility.Collapsed;

		if (_cartHost)
		{
			// 直交パッドの2軸と縦スライダーの成分を決め、束縛を差し替える。画像キャッシュを無効化して必ず作り直す。
			_cartValid = false;
			ConfigureCartPad();
			ConfigureCartSlider();
		}

		if (cbcr)
		{
			// Cb×Cr 平面のキャッシュを無効化して必ず作り直す。
			_planeValid = false;
		}

		UpdatePadSize();
		UpdateLayoutPickerIcon();

		if (_cartHost)
		{
			RegenerateCart();
			UpdateCartThumb();
		}
		else
		{
			RegeneratePlane();
		}
	}




	// 直交パッドの切り出し成分(2軸に取らない側の色差)の縦スライダーを束ね直す。Cb×Y 平面では Cr、Cr×Y 平面では Cb を司る。
	private void ConfigureCartSlider()
	{
		CutComponent component = _layout == YuvLayout.CbYPlane ? CutComponent.Cr : CutComponent.Cb;
		ConfigureCutSlider(YuvCartSlider, component);
	}




	// 直交パッドの切り出し成分(Cb または Cr)の縦スライダーを、値・背景・色域外区間・名前ごと束ね直す。Cb・Cr はともに 0–1 の正規化。背景は各成分の縦向きグラデーション、色域外区間は各成分の判定。値域・刻みは XAML で 0–1・YuvChromaStep に定めてあるため、ここでは値・背景・色域外・名前だけを差し替える。
	private void ConfigureCutSlider(GradientSlider slider, CutComponent component)
	{
		BindingOperations.SetBinding(slider, RangeBase.ValueProperty, MakeViewModelBinding(CutValuePath(component), BindingMode.TwoWay));
		BindingOperations.SetBinding(slider, GradientSlider.TrackBrushProperty, MakeViewModelBinding(CutBrushPath(component), BindingMode.OneWay));
		BindingOperations.SetBinding(slider, GradientSlider.OutOfGamutSegmentsProperty, MakeViewModelBinding(CutGamutPath(component), BindingMode.OneWay));
		AutomationProperties.SetName(slider, CutLetter(component));
	}




	// 直交パッドの横軸・縦軸が表す成分(Cb×Y は横=Cb・縦=Y、Cr×Y は横=Cr・縦=Y)を決め、アクセシビリティ名を整える。つまみ位置は UpdateCartThumb がコードで更新する。XValue・YValue を束縛で受け取ると、ドラッグ時の局所設定で OneWay 束縛が外れ、以後スライダー操作がつまみへ届かなくなるため、円盤のつまみと同じく束縛に頼らない。操作は2成分をまとめて ValuesChanged から VM へ渡す。
	private void ConfigureCartPad()
	{
		string letters = _layout == YuvLayout.CbYPlane ? "Cb × Y" : "Cr × Y";
		AutomationProperties.SetName(YuvCartPad, letters);
	}




	// 直交パッドのつまみを、現在のレイアウトの2軸の正規化値の位置へ移す。Cb×Y は横=Cb・縦=Y、Cr×Y は横=Cr・縦=Y。正規化値は表示枠を介して読むフィット済みのプロパティから採るため、固定枠のときは Cb01/Cr01・Y/255 と一致し、フィットのときは枠の縮尺・中心に追従する。XValue・YValue はドラッグで局所設定されると OneWay 束縛が外れるため、円盤のつまみと同じく束縛に頼らずコードで更新する。設定で発火するのはつまみ位置の更新だけで、ValuesChanged はポインタ・キーボード操作のときしか発火しないため、ここでの設定が操作の取り込みへ回り込むことはない。
	private void UpdateCartThumb()
	{
		if (!_cartHost)
		{
			return;
		}

		if (_layout == YuvLayout.CbYPlane)
		{
			YuvCartPad.XValue = ViewModel.YuvCbLumaPadCbNorm;
			YuvCartPad.YValue = ViewModel.YuvCbLumaPadYNorm;
		}
		else
		{
			YuvCartPad.XValue = ViewModel.YuvCrLumaPadCrNorm;
			YuvCartPad.YValue = ViewModel.YuvCrLumaPadYNorm;
		}
	}




	// 直交パッドの切り出し成分(Cb または Cr)の縦スライダーの値が束ねる ViewModel プロパティ名。Cb・Cr はともに 0–1 の正規化。
	private static string CutValuePath(CutComponent component)
	{
		return component switch
		{
			CutComponent.Cb => nameof(ColorEditorViewModel.Cb01),
			_ => nameof(ColorEditorViewModel.Cr01),
		};
	}




	// 直交パッドの切り出し成分(Cb または Cr)の縦スライダーの背景が束ねる ViewModel プロパティ名。各成分の縦向きグラデーション。
	private static string CutBrushPath(CutComponent component)
	{
		return component switch
		{
			CutComponent.Cb => nameof(ColorEditorViewModel.CbTrackBrushVertical),
			_ => nameof(ColorEditorViewModel.CrTrackBrushVertical),
		};
	}




	// 直交パッドの切り出し成分(Cb または Cr)の縦スライダーの色域外区間が束ねる ViewModel プロパティ名。各成分の色域外の範囲。
	private static string CutGamutPath(CutComponent component)
	{
		return component switch
		{
			CutComponent.Cb => nameof(ColorEditorViewModel.CbGamut),
			_ => nameof(ColorEditorViewModel.CrGamut),
		};
	}




	// 成分を表す言語非依存の名前。アクセシビリティ名に使う。
	private static string CutLetter(CutComponent component)
	{
		return component switch
		{
			CutComponent.Cb => "Cb",
			_ => "Cr",
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




	// 直交パッドを操作したら、レイアウトに応じた2成分をまとめて VM へ渡す。Cb×Y は Cr を保って Cb・輝度を、Cr×Y は Cb を保って Cr・輝度を設定する。固定成分(縦スライダーが司る側)は VM 側で保たれる。
	private void OnCartValuesChanged(object? sender, EventArgs e)
	{
		if (_layout == YuvLayout.CbYPlane)
		{
			ViewModel.SetYuvCbLumaPad(YuvCartPad.XValue, YuvCartPad.YValue);
		}
		else
		{
			ViewModel.SetYuvCrLumaPad(YuvCartPad.XValue, YuvCartPad.YValue);
		}
	}




	// 直交パッドの大きさが変わったら、下地画像を作り直す。パッドを初めて表示したときの寸法確定もこの経路で拾う。
	private void OnCartPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateCart();
	}




	// 直交パッドの固定成分(2軸に取らない側の色差)の現在値。色制限・寸法とともに画像キャッシュの鍵に使う。Cb×Y は Cr、Cr×Y は Cb。
	private double CartFixedValue()
	{
		return _layout == YuvLayout.CbYPlane ? ViewModel.Cr : ViewModel.Cb;
	}




	// Cb-Cr 色差平面画像を、現在の輝度・符号化形式・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・輝度・形式・色制限設定なら作り直さない。色域外ハッチを LCH/Lab の平面と同じ太さ・間隔で見せるため、表示実寸と表示倍率に合わせた等倍で生成する。
	private void RegeneratePlane()
	{
		if (_cartHost)
		{
			return;
		}

		double size = CbCrPad.ActualWidth;

		if (size <= 0.0 || CbCrPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(size * scale);

		if (pixels <= 0)
		{
			return;
		}

		double luma = ViewModel.Luma;
		YCbCrFormat format = ViewModel.Format;
		SnapSettings snap = ViewModel.CurrentSnap;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;
		int scaleIndex = ViewModel.YuvScaleIndex;

		if (pixels == _planePixels && luma == _planeLuma && _planeValid && snap == _planeSnap && style == _planeStyle && scaleIndex == _planeScaleIndex
			&& format.Standard == _planeFormat.Standard && format.FullRange == _planeFormat.FullRange)
		{
			return;
		}

		_planePixels = pixels;
		_planeLuma = luma;
		_planeFormat = format;
		_planeSnap = snap;
		_planeStyle = style;
		_planeScaleIndex = scaleIndex;
		_planeValid = true;

		WriteableBitmap plane = CbCrPlane.Create(pixels, pixels, luma, format, snap, scale, style, (AbFitMode)scaleIndex);
		PlaneImage.Source = plane;

		// ドラッグ中のレンズは、色域外のハッチや境界線など計算の込み入った色差平面を作り直さず、生成したこのビットマップをそのまま読んで映す。輝度はドラッグ中一定のため、平面は据え置きで足りる。
		CbCrPad.LensColorSampler = new BitmapFieldSampler(plane, CbCrPad).Sample;
	}




	// 直交パッド(Cb×Y・Cr×Y)の下地画像を、現在のレイアウト・固定成分・符号化形式・色制限設定・色域外の見せ方・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・固定成分・形式・色制限なら作り直さない(レイアウト切り替え時は ApplyLayout がキャッシュを無効化する)。2軸はパッド全面に渡って変わるため鍵に含めない。
	private void RegenerateCart()
	{
		if (!_cartHost)
		{
			return;
		}

		if (YuvCartPad.ActualWidth <= 0.0 || YuvCartPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixelWidth = (int)Math.Round(YuvCartPad.ActualWidth * scale);
		int pixelHeight = (int)Math.Round(YuvCartPad.ActualHeight * scale);

		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return;
		}

		YCbCrFormat format = ViewModel.Format;
		SnapSettings snap = ViewModel.CurrentSnap;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;
		double fixedComponent = CartFixedValue();
		int scaleIndex = ViewModel.YuvScaleIndex;

		if (pixelWidth == _cartPixelWidth && pixelHeight == _cartPixelHeight && fixedComponent == _cartFixed && _cartValid && snap == _cartSnap && style == _cartStyle && scaleIndex == _cartScaleIndex
			&& format.Standard == _cartFormat.Standard && format.FullRange == _cartFormat.FullRange)
		{
			return;
		}

		_cartPixelWidth = pixelWidth;
		_cartPixelHeight = pixelHeight;
		_cartFixed = fixedComponent;
		_cartFormat = format;
		_cartSnap = snap;
		_cartStyle = style;
		_cartScaleIndex = scaleIndex;
		_cartValid = true;

		var fit = (AbFitMode)scaleIndex;
		WriteableBitmap plane = _layout == YuvLayout.CbYPlane
			? YuvLumaPlane.CreateCbY(pixelWidth, pixelHeight, fixedComponent, format, snap, scale, style, fit)
			: YuvLumaPlane.CreateCrY(pixelWidth, pixelHeight, fixedComponent, format, snap, scale, style, fit);

		YuvCartImage.Source = plane;

		// ドラッグ中のレンズは、色域ハッチなど計算の込み入った下地を作り直さず、生成したこのビットマップをそのまま読んで映す。固定成分はドラッグ中一定のため、画像は据え置きで足りる。
		_cartSampler = new BitmapFieldSampler(plane, YuvCartPad);
	}




	// 直交パッドのレンズのサンプラー。生成済みの下地ビットマップをそのまま読む。パッドの外は透明を返し、レンズの縁では下地のカード色が透ける。
	private Color SampleCartField(double x, double y)
	{
		return _cartSampler?.Sample(x, y) ?? Color.FromArgb(0, 0, 0, 0);
	}




	// 指定したレイアウトの縮小見本(サムネイル)を作る。下地そのもので区別がつくため、各面はそのまま描く。固定成分は色が映える代表値(輝度は中央付近の 128、固定の色差は無彩色の 128)を当てる。0=Cb×Cr 平面(固定輝度 128)・1=Cb×Y 平面(固定 Cr=128)・2=Cr×Y 平面(固定 Cb=128)。サムネイルはレイアウトの形を示すものなので、表示枠は常に固定枠(全域)で描き、フィットの縮尺には追従させない。
	private WriteableBitmap LayoutThumbnailFor(int index, int pixels, YCbCrFormat format, SnapSettings snap, GamutOutOfRangeStyle style)
	{
		return index switch
		{
			1 => YuvLumaPlane.CreateCbY(pixels, pixels, 128.0, format, snap, 1.0, style, AbFitMode.None),
			2 => YuvLumaPlane.CreateCrY(pixels, pixels, 128.0, format, snap, 1.0, style, AbFitMode.None),
			_ => CbCrPlane.Create(pixels, pixels, 128.0, format, snap, 1.0, style, AbFitMode.None),
		};
	}




	// 隅のレイアウトボタンのアイコンを、現在選んでいるレイアウトの縮小見本に差し替える。アイコン自体が現在の状態表示を兼ねる。
	private void UpdateLayoutPickerIcon()
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(24.0 * scale));
		YuvLayoutPickerIcon.Source = LayoutThumbnailFor(ViewModel.YuvLayoutIndex, pixels, ViewModel.Format, ViewModel.CurrentSnap, ViewModel.OutOfRangeStyle);
	}




	// レイアウト選択のフライアウトを開くときに、全レイアウトのサムネイルを今の符号化形式・色制限・色域外の見せ方で作り直し、現在の選択を縁取りで示す。
	private void OnYuvLayoutFlyoutOpening(object sender, object e)
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(56.0 * scale));
		YCbCrFormat format = ViewModel.Format;
		SnapSettings snap = ViewModel.CurrentSnap;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;

		YuvCbCrPlaneThumbImage.Source = LayoutThumbnailFor(0, pixels, format, snap, style);
		YuvCbYPlaneThumbImage.Source = LayoutThumbnailFor(1, pixels, format, snap, style);
		YuvCrYPlaneThumbImage.Source = LayoutThumbnailFor(2, pixels, format, snap, style);

		var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
		var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
		int current = ViewModel.YuvLayoutIndex;
		YuvCbCrPlaneThumbBorder.BorderBrush = current == 0 ? accent : clear;
		YuvCbYPlaneThumbBorder.BorderBrush = current == 1 ? accent : clear;
		YuvCrYPlaneThumbBorder.BorderBrush = current == 2 ? accent : clear;
	}




	private void OnPickYuvCbCrPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.YuvLayoutIndex = 0;
		YuvLayoutPickerFlyout.Hide();
	}




	private void OnPickYuvCbYPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.YuvLayoutIndex = 1;
		YuvLayoutPickerFlyout.Hide();
	}




	private void OnPickYuvCrYPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.YuvLayoutIndex = 2;
		YuvLayoutPickerFlyout.Hide();
	}




	// 青方向の色差スライダーのラベル。YUV 表記では U、YCbCr 表記では Cb。
	public string ChromaLabelB(bool signed)
	{
		return signed ? "U" : "Cb";
	}




	// 赤方向の色差スライダーのラベル。YUV 表記では V、YCbCr 表記では Cr。
	public string ChromaLabelR(bool signed)
	{
		return signed ? "V" : "Cr";
	}




	// Cb・Cr の数値入力欄を、現在の色差と表記に合わせて組み替える。YCbCr 表記では 0–255、符号付き YUV 表記では中心 128 を 0 とした符号付きの値で見せる。範囲を先に整えてから値を入れ、表記の切り替えで起きる丸めは組み替え中として書き戻さない。
	private void SyncChromaBoxes()
	{
		_chromaSyncing = true;

		bool signed = ViewModel.IsSignedMode;
		ConfigureChromaBox(CbValueBox, ViewModel.Cb01, signed);
		ConfigureChromaBox(CrValueBox, ViewModel.Cr01, signed);

		_chromaSyncing = false;
	}




	// 色差の数値入力欄ひとつを、束縛値(0–1)と表記から範囲・値を決めて設定する。スライダーと同じ 0–1 を源にし、0–255 へ直してから表記に応じて符号付きへ寄せる。
	private static void ConfigureChromaBox(NumberBox box, double value01, bool signed)
	{
		double code = value01 * 255.0;

		if (signed)
		{
			box.Minimum = -128.0;
			box.Maximum = 127.0;
			box.Value = Math.Round(code - 128.0);
		}
		else
		{
			box.Minimum = 0.0;
			box.Maximum = 255.0;
			box.Value = Math.Round(code);
		}
	}




	// 青方向の色差の数値入力欄が利用者の操作で変わったら、現在の表記の数値を 0–1 の束縛値へ直してモデルへ反映する。組み替え中の変化や空欄(NaN)は無視する。
	private void OnCbValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_chromaSyncing || double.IsNaN(sender.Value))
		{
			return;
		}

		ViewModel.Cb01 = ChromaCodeToValue01(sender.Value, ViewModel.IsSignedMode);
	}




	// 赤方向の色差の数値入力欄が利用者の操作で変わったら、現在の表記の数値を 0–1 の束縛値へ直してモデルへ反映する。組み替え中の変化や空欄(NaN)は無視する。
	private void OnCrValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_chromaSyncing || double.IsNaN(sender.Value))
		{
			return;
		}

		ViewModel.Cr01 = ChromaCodeToValue01(sender.Value, ViewModel.IsSignedMode);
	}




	// 数値入力欄の値(表記に応じた 0–255 または符号付き)を、束縛用の 0–1 へ直す。符号付き表記では中心の 128 を足してから割る。
	private static double ChromaCodeToValue01(double code, bool signed)
	{
		double code255 = signed ? code + 128.0 : code;
		return code255 / 255.0;
	}




	// 見せ方。CbCrPlane は Cb×Cr 平面+輝度 Y の縦バー(既定)。CbYPlane=Cb×Y 平面+Cr の縦バー、CrYPlane=Cr×Y 平面+Cb の縦バー。後の2つは直交パッド。
	private enum YuvLayout
	{
		CbCrPlane,
		CbYPlane,
		CrYPlane,
	}




	// 切り出して縦スライダーが司る成分。直交パッドの2次元に取らない残り1成分の色差(Cb×Y では Cr、Cr×Y では Cb)。
	private enum CutComponent
	{
		Cb,
		Cr,
	}
}
