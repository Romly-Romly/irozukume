// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 「LCH」タブの中身。色1(編集中)を色相環(リング)と中央の L-C パッドで編集し、下段の明度・彩度・色相スライダーで精密に詰める。
// 色相環は色相を、L-C パッドは明度(縦)と彩度(横)を担う。L-C 平面は sRGB 色域に収まる部分だけを実色で塗り、色域外はハッチで透かして可視化する。
// 副モードのラジオで OKLCH(OKLab 基準)と CIE LCH(CIELAB 基準)を切り替える。色相環の画像と L-C 平面の画像は、リングの実寸と表示倍率に合わせてコードで用意する。
// 編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class LchTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// 色相環画像を生成した際の画素サイズ・帯の内外半径(画素)・色制限設定・副モード。同じ条件での作り直しを避けるために覚えておく。色相環は表色系で色が変わるため副モードも判定の鍵に含める。
	private int _wheelPixelWidth;
	private int _wheelPixelHeight;
	private double _wheelInnerRadius;
	private double _wheelOuterRadius;
	private SnapSettings _wheelSnap;
	private int _wheelSpaceIndex = -1;

	// L-C 平面の画像を生成した際の画素サイズ・色相・色制限設定・副モード・色域外の見せ方。同じ条件での作り直しを避けるために覚えておく。色相は未生成を表す NaN で初期化する。
	private int _lcPixels = -1;
	private double _lcHue = double.NaN;
	private SnapSettings _lcSnap;
	private int _lcSpaceIndex = -1;
	private GamutOutOfRangeStyle _lcStyle = (GamutOutOfRangeStyle)(-1);
	private bool _lcFit;

	// ドラッグ中のレンズが読む、生成済みの色相環ホイールと L-C 平面のビットマップサンプラー。色相環のレンズは内側のパッドを、パッドのレンズは外側の色相環を映すため、双方の合成サンプラーがどちらの色面も参照できるよう、生成のたびにここへ控える。画像はドラッグ中は内容が変わらないため据え置きで足りる。
	private BitmapFieldSampler? _wheelSampler;
	private BitmapFieldSampler? _planeSampler;

	// 副モードのラジオを VM の復元値に合わせる間、SelectionChanged が VM を上書きしないようにする。構築中の初期化を無視するため真で始める。
	private bool _spaceSyncing = true;

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)を色相環の一辺の算出に使う。
	private ScrollViewer? _scrollHost;

	// 色相環の一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinRingSide = 180.0;

	// 現在の見せ方。RingPlane は色相リング+C×L 平面(既定)。残りは2次元コントロール+切り出した成分の縦スライダーで、円盤系(角度=色相・半径=明度/彩度)と直交パッド系(2成分を縦横)に分かれる。VM の LchLayoutIndex を反映する。
	private LchLayout _layout = LchLayout.RingPlane;

	// 現在のレイアウトが円盤ホスト(DiskHost)・直交パッドホスト(LcCartPad)のいずれを使うか。いずれも偽なら色相リング+C×L 平面のホスト。
	private bool _diskHost;
	private bool _cartHost;

	// 円盤の半径に彩度を取るか(true=半径=彩度・固定明度)、明度を取るか(false=半径=明度・固定彩度)。レイアウトに応じて切り替える。半径に取らない側が固定成分(縦スライダー)になる。
	private bool _diskRadiusIsChroma;

	// 円盤画像を生成した際の画素サイズ・固定成分の値・色制限設定・副モード・色域外の見せ方。同じ条件での作り直しを避けるために覚えておく。固定成分は半径に取らない側で、未生成を表す NaN で初期化する。色相は円盤の全方位に現れるため鍵に含めない。
	private int _diskPixels = -1;
	private double _diskFixed = double.NaN;
	private SnapSettings _diskSnap;
	private int _diskSpaceIndex = -1;
	private GamutOutOfRangeStyle _diskStyle = (GamutOutOfRangeStyle)(-1);
	private bool _diskFit;

	// 直交パッドの下地画像を生成した際の画素サイズ(縦横独立)・固定成分の値・色制限設定・副モード・色域外の見せ方。同じ条件での作り直しを避けるために覚えておく。固定成分はパッドの2軸に取らない側で、未生成を表す NaN で初期化する。
	private int _cartPixelWidth = -1;
	private int _cartPixelHeight = -1;
	private double _cartFixed = double.NaN;
	private SnapSettings _cartSnap;
	private int _cartSpaceIndex = -1;
	private GamutOutOfRangeStyle _cartStyle = (GamutOutOfRangeStyle)(-1);
	private bool _cartFit;

	// 円盤・直交パッドのドラッグ中のレンズが読む、生成済みのビットマップサンプラー。色域ハッチなど計算の込み入った下地を作り直さず、表示中の画像をそのまま読んで映す。生成のたびに差し替える。
	private BitmapFieldSampler? _diskSampler;
	private BitmapFieldSampler? _cartSampler;

	// 円盤をドラッグ中かどうか。ポインタを捕捉している間だけ真にする。
	private bool _diskDragging;

	// 円盤のドラッグ中につまみをガラスレンズへ膨らませる管理役。置き場(DiskLens)へ載せ、座標は円盤局所(DIP)で渡す。
	private LensController? _diskLens;

	// 円盤のつまみのガラスレンズの効き。2次元パッドと同じ効きにそろえる。各項目の意味と単位は GlassLensParams を参照。
	private static readonly GlassLensParams DiskLensParams = new()
	{
		Diameter = 50.0,
		Magnify = 1.4,
		EdgeAmount = -24.0,
		Chroma = true,
		ChromaSpread = 0.4,
		BevelFraction = 0.4,
	};

	// 下地画像の再生成を背景スレッドへ逃がすためのコアレス機構の状態。画素計算(重い)は ThreadPool で回し、WriteableBitmap 化と差し込み(安価)だけを UI スレッドで行う。走らせるのは常に1本だけ(_regenRunning)にし、走行中に来た要求は最新の1件だけを保持(_regenPending と _pending*)して、完了時にそれを次へ回す。色相スライダー等で固定成分が毎ティック変わっても UI スレッドが画素計算で塞がらないようにするためのもの。EnqueueRegen と完了コールバックはともに UI スレッドで動くため、これらの読み書きにロックは要らない。
	private bool _regenRunning;
	private bool _regenPending;
	private int _pendingWidth;
	private int _pendingHeight;
	private Func<byte[]>? _pendingCompute;
	private Action<WriteableBitmap>? _pendingApply;


	public LchTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 復元済みの副モードをラジオに反映する。ここまでの SelectionChanged は _spaceSyncing で無視し、以降の操作だけ VM へ伝える。
		SpaceSelector.SelectedIndex = ViewModel.LchSpaceIndex;
		_spaceSyncing = false;

		// L-C パッドの操作は明度・彩度を二次元でまとめて扱う(色域内への最近傍寄せ)ため、横・縦の個別束縛ではなく ValuesChanged からまとめて VM へ渡す。
		LcPad.ValuesChanged += OnLcPadValuesChanged;

		// 直交パッド(LcCartPad)は PlanarPad を流用するため、つまみ・レンズ・矢印操作はそのまま得られる。操作は2成分をまとめて扱うため ValuesChanged からレイアウトに応じた設定へ振り分け、レンズに映す色面のサンプラーを与え、大きさ変化で下地画像を作り直す。軸の束縛はレイアウトに応じて ConfigureCartPad が差し替える。
		LcCartPad.ValuesChanged += OnCartValuesChanged;
		LcCartPad.LensColorSampler = SampleCartField;
		LcCartPad.SizeChanged += OnCartPadSizeChanged;

		// 円盤(角度=色相)のレンズ管理役。置き場は円盤と同座標の Canvas(DiskLens)。
		_diskLens = new LensController(DiskHost, DiskLens, DiskLensParams);

		HueRing.SizeChanged += OnHueRingSizeChanged;

		// 色相環の一辺はスクロール領域の可視高さからスライダー群の高さを引いて決めるため、スライダー群の高さが変わったら算出し直す。
		SliderHost.SizeChanged += OnLayoutMetricChanged;

		// 円盤・直交パッドの一辺は、隣の縦スライダーの幅が確定して初めて正しく決まる。スライダーの寸法が定まったら算出し直す。
		LchDiskSlider.SizeChanged += OnLayoutMetricChanged;
		LchCartSlider.SizeChanged += OnLayoutMetricChanged;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 表示倍率(DPI)の変化で色相環・L-C 平面の画像を作り直せるよう XamlRoot の変化を購読する。表示先が変わっていれば購読を張り替える。
		if (XamlRoot is not null && !ReferenceEquals(_subscribedRoot, XamlRoot))
		{
			if (_subscribedRoot is not null)
			{
				_subscribedRoot.Changed -= OnXamlRootChanged;
			}

			_subscribedRoot = XamlRoot;
			_subscribedRoot.Changed += OnXamlRootChanged;
		}

		// 色相・副モード・色制限モードの変更で L-C 平面や色相環を塗り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;

		// タブの中身を包む祖先のスクロール領域を一度だけ見つけ、その可視高さの変化で色相環を算出し直せるようにする。
		if (_scrollHost is null)
		{
			_scrollHost = FindScrollHost();

			if (_scrollHost is not null)
			{
				_scrollHost.SizeChanged += OnLayoutMetricChanged;
			}
		}

		UpdateRingSize();
		UpdateRingVisuals();
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
		UpdateRingVisuals();
	}




	private void OnHueRingSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateRingVisuals();
	}




	// 色相環エリアの幅が変わったら、色相環の一辺を算出し直す。
	private void OnRingAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateRingSize();
	}




	// 全体の高さ、またはスライダー群の高さが変わったら、色相環の一辺を算出し直す。
	private void OnLayoutMetricChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateRingSize();
	}




	// 色1の色相・副モード・色制限モードが変わったら L-C 平面や色相環を塗り直す。色相が変わるのは L-C 平面のヒレの形だけで、色相環は色相に依らないため作り直さない。副モードが変わると色相環の色も尺度・色域も変わるため双方を作り直す。色制限を切り替えると色相環・L-C 平面の丸めが一斉に変わる。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// 見せ方が変わったら、ホストの出し分け・寸法・画像・つまみを一斉に整える。
		if (e.PropertyName == nameof(ColorEditorViewModel.LchLayoutIndex))
		{
			ApplyLayout();
			return;
		}

		// 彩度フィットが変わったら、活性なホストの下地を作り直す。L-C 平面・色相×彩度の平面のつまみは彩度軸の束縛(LchPadCNorm・LchHueChromaCNorm)が VM の通知で追従し、円盤のつまみはコードで合わせ直す。彩度軸を持たない見せ方では各 Regenerate が空振りする。
		if (e.PropertyName == nameof(ColorEditorViewModel.LchChromaFit))
		{
			RegenerateActiveField();

			if (_diskHost)
			{
				UpdateDiskThumb();
			}
			else if (_cartHost)
			{
				UpdateCartThumb();
			}

			return;
		}

		// 明度・彩度・色相が変わったら、活性なホストの下地(固定成分が変わったときだけ)とつまみを整える。各 Regenerate は空振り判定とキャッシュを持つため、固定成分が動いていなければ実際には描き直さない。色相リング+平面・彩度×明度の平面では、固定の色相が変わると L-C 平面のヒレが変わる。
		if (e.PropertyName == nameof(ColorEditorViewModel.LchH) || e.PropertyName == nameof(ColorEditorViewModel.LchL) || e.PropertyName == nameof(ColorEditorViewModel.LchC))
		{
			RegenerateActiveField();

			if (_diskHost)
			{
				UpdateDiskThumb();
			}
			else if (_cartHost)
			{
				UpdateCartThumb();
			}

			return;
		}

		// 副モードや色制限が変わると、色相環の色・尺度・色域も、各ホストの下地も一斉に変わる。
		if (e.PropertyName == nameof(ColorEditorViewModel.LchSpaceIndex) || e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap))
		{
			UpdateRingVisuals();

			if (_diskHost)
			{
				RegenerateDisk();
				UpdateDiskThumb();
			}

			if (_cartHost)
			{
				RegenerateCart();
				UpdateCartThumb();
			}

			UpdateLayoutPickerIcon();
			return;
		}

		// 色域外の見せ方は、L-C 平面・円盤・直交パッドの色域外の描き方だけを変える。色相環は色域外を持たないため作り直さない。
		if (e.PropertyName == nameof(ColorEditorViewModel.OutOfRangeStyle))
		{
			RegenerateActiveField();
			UpdateLayoutPickerIcon();
		}
	}




	// 活性なホスト(円盤・直交パッド・色相リング+平面)の下地を、現在の固定成分・色制限・色域外の見せ方に合わせて作り直す。各 Regenerate は空振り判定とキャッシュを持つため、変わっていなければ実際には描き直さない。
	private void RegenerateActiveField()
	{
		if (_diskHost)
		{
			RegenerateDisk();
		}
		else if (_cartHost)
		{
			RegenerateCart();
		}
		else
		{
			RegenerateLcPlane();
		}
	}




	// 下地画像の再生成要求を投入する。compute は画素を詰める重い処理(背景スレッドで回す)、apply は計算済みの配列をビットマップ化して差し込む安価な処理(UI スレッドで回す)。最新の1件だけを保持(latest-wins)し、走行中でなければ即座に走らせる。走行中なら、いま走っているジョブの完了時に最新の保持ぶんが回される。
	private void EnqueueRegen(int pixelWidth, int pixelHeight, Func<byte[]> compute, Action<WriteableBitmap> apply)
	{
		_pendingWidth = pixelWidth;
		_pendingHeight = pixelHeight;
		_pendingCompute = compute;
		_pendingApply = apply;
		_regenPending = true;

		if (!_regenRunning)
		{
			PumpRegen();
		}
	}




	// 保持している最新の再生成要求を1本だけ走らせる。画素計算を ThreadPool へ投げ、完了したら UI スレッドへ戻してビットマップ化・差し込みを行い、その時点で保持されている次の要求があればまた走らせる。EnqueueRegen も完了コールバックも UI スレッドで動くため、_regenRunning・_pending* の読み書きはロック不要。差し込み先の UI が既に畳まれていれば(タブ破棄等)走行フラグだけ下ろして打ち切る。
	private void PumpRegen()
	{
		if (!_regenPending)
		{
			return;
		}

		int width = _pendingWidth;
		int height = _pendingHeight;
		Func<byte[]> compute = _pendingCompute!;
		Action<WriteableBitmap> apply = _pendingApply!;
		_regenPending = false;
		_pendingCompute = null;
		_pendingApply = null;
		_regenRunning = true;

		var dispatcher = DispatcherQueue;

		Task.Run(() =>
		{
			byte[] pixels = compute();

			bool enqueued = dispatcher.TryEnqueue(() =>
			{
				WriteableBitmap bitmap = LchGamutField.Blit(pixels, width, height);
				apply(bitmap);
				_regenRunning = false;
				PumpRegen();
			});

			if (!enqueued)
			{
				_regenRunning = false;
			}
		});
	}




	// L-C パッドを操作したら、明度・彩度を二次元でまとめて VM へ渡す。色域制限オン時はカーソルに最も近い色域内の点へ寄せる処理が VM 側で行われる。
	private void OnLcPadValuesChanged(object? sender, EventArgs e)
	{
		ViewModel.SetLchPad(LcPad.XValue, LcPad.YValue);
	}




	// OKLCH / CIE LCH の切り替え。選んだ副モードを VM へ伝える。構築中の初期化(_spaceSyncing)は無視する。
	private void OnSpaceChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_spaceSyncing)
		{
			return;
		}

		ViewModel.LchSpaceIndex = SpaceSelector.SelectedIndex;
	}




	// 副モードを OKLCH へ切り替える。貼り付け連動から呼ぶ。SpaceSelector の選択を変えることで表示の切り替えを行う。既に OKLCH のときは選択が変わらず何も起きない。
	public void ShowOklchMode()
	{
		SpaceSelector.SelectedIndex = 0;
	}




	// 副モードを CIE LCH へ切り替える。貼り付け連動から呼ぶ。SpaceSelector の選択を変えることで表示の切り替えを行う。既に CIE LCH のときは選択が変わらず何も起きない。
	public void ShowLchMode()
	{
		SpaceSelector.SelectedIndex = 1;
	}




	// 色相環を正方形のまま、エリアの幅と、スライダー群を除いた残りの可視高さの小さい方へ合わせる。可視高さはスクロール領域のビューポート高さからスライダー群の高さと行間を引いて求める。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる。色相環の大きさが変わると、それを受けた SizeChanged で色相環・L-C 平面の画像とパッドの寸法が作り直される。
	private void UpdateRingSize()
	{
		double available = _scrollHost?.ViewportHeight ?? LayoutRoot.ActualHeight;

		if (available <= 0.0)
		{
			return;
		}

		double heightBudget = available - SliderHost.ActualHeight - LayoutRoot.RowSpacing;

		// 円盤レイアウトのときは正方形。縦スライダーのぶんの幅を引いた残りと残りの高さの小さい方を円盤の一辺にする。スライダーの実寸が未確定の初回は概算の幅で見積もり、確定後の SizeChanged で正す。
		if (_diskHost)
		{
			double sliderColumn = LchDiskSlider.ActualWidth > 0.0 ? LchDiskSlider.ActualWidth : 32.0;
			double widthBudget = RingArea.ActualWidth - sliderColumn - LchDiskLayoutRoot.ColumnSpacing;
			double diskSide = Math.Min(widthBudget, Math.Max(MinRingSide, heightBudget));

			if (diskSide <= 0.0)
			{
				return;
			}

			if (DiskHost.Width != diskSide)
			{
				DiskHost.Width = diskSide;
				DiskHost.Height = diskSide;
			}

			if (LchDiskSlider.Height != diskSide)
			{
				LchDiskSlider.Height = diskSide;
			}

			return;
		}

		// 直交パッドのとき。横軸の成分を細かく取れるよう横長にする配置(色相×明度・色相×彩度は色相を横軸に、明度×彩度は明度を横軸に取る)は正方形に縛らず横いっぱいに広げ、縦は残りの高さに合わせる(横へ広く取るほど横軸の刻みが細かくなる)。3配置とも横軸を活かすため横長にする。
		if (_cartHost)
		{
			double sliderColumn = LchCartSlider.ActualWidth > 0.0 ? LchCartSlider.ActualWidth : 32.0;
			double widthBudget = RingArea.ActualWidth - sliderColumn - LchCartLayoutRoot.ColumnSpacing;
			bool wide = _layout == LchLayout.HueLightnessPlane || _layout == LchLayout.HueChromaPlane || _layout == LchLayout.ClHueBar;
			double padWidth;
			double padHeight;

			if (wide)
			{
				padWidth = widthBudget;
				padHeight = Math.Max(MinRingSide, heightBudget);
			}
			else
			{
				double square = Math.Min(widthBudget, Math.Max(MinRingSide, heightBudget));
				padWidth = square;
				padHeight = square;
			}

			if (padWidth <= 0.0)
			{
				return;
			}

			if (LcCartPad.Width != padWidth)
			{
				LcCartPad.Width = padWidth;
			}

			if (LcCartPad.Height != padHeight)
			{
				LcCartPad.Height = padHeight;
			}

			if (LchCartSlider.Height != padHeight)
			{
				LchCartSlider.Height = padHeight;
			}

			return;
		}

		double side = Math.Min(RingArea.ActualWidth, Math.Max(MinRingSide, heightBudget));

		if (side <= 0.0)
		{
			return;
		}

		if (HueRing.Width != side)
		{
			HueRing.Width = side;
			HueRing.Height = side;
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




	// 色相環画像と L-C パッドの大きさを、リングの実寸と表示倍率に合わせて更新する。L-C パッドは内円に内接する正方形(内径÷√2 の辺長)にする。色相環の画像は同じ条件なら作り直さない。
	private void UpdateRingVisuals()
	{
		double width = HueRing.ActualWidth;
		double height = HueRing.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		RingMetrics metrics = RingGeometry.Compute(width, height, HueRing.RingThickness, HueRing.ThumbDiameter);

		LcPad.Width = metrics.InnerRadius * Math.Sqrt(2.0);
		LcPad.Height = LcPad.Width;

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixelWidth = (int)Math.Round(width * scale);
		int pixelHeight = (int)Math.Round(height * scale);
		double innerRadius = metrics.InnerRadius * scale;
		double outerRadius = metrics.OuterRadius * scale;

		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		int spaceIndex = ViewModel.LchSpaceIndex;

		// 画素サイズ・帯の内外半径・色制限設定・副モードが前回と同じなら作り直さない。
		if (pixelWidth != _wheelPixelWidth || pixelHeight != _wheelPixelHeight || innerRadius != _wheelInnerRadius || outerRadius != _wheelOuterRadius || snap != _wheelSnap || spaceIndex != _wheelSpaceIndex)
		{
			_wheelPixelWidth = pixelWidth;
			_wheelPixelHeight = pixelHeight;
			_wheelInnerRadius = innerRadius;
			_wheelOuterRadius = outerRadius;
			_wheelSnap = snap;
			_wheelSpaceIndex = spaceIndex;

			LchSpace wheelSpace = spaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;
			WriteableBitmap wheel = LchHueWheel.Create(pixelWidth, pixelHeight, innerRadius, outerRadius, wheelSpace, snap);
			HueRing.TrackBrush = new ImageBrush
			{
				ImageSource = wheel,
				Stretch = Stretch.Fill,
			};

			// ドラッグ中のレンズは、生成したこの色相環ビットマップをそのまま読んで映す。色相環は色相ドラッグ中も内容が変わらないため、据え置きで足りる。色相環のレンズは合成サンプラーで、帯の外で内側へ入る点は中央の L-C パッドの色面とつまみを映す。
			_wheelSampler = new BitmapFieldSampler(wheel, HueRing);
			HueRing.LensColorSampler = SampleRingThroughLens;
		}

		RegenerateLcPlane();
	}




	// L-C 平面の画像を、現在の色相・副モード・色制限設定・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・色相・設定・副モードなら作り直さない。大きさはパッドの実寸ではなくリングの幾何(内円に内接する正方形)から求める。
	private void RegenerateLcPlane()
	{
		if (HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0)
		{
			return;
		}

		RingMetrics metrics = RingGeometry.Compute(HueRing.ActualWidth, HueRing.ActualHeight, HueRing.RingThickness, HueRing.ThumbDiameter);
		double box = metrics.InnerRadius * Math.Sqrt(2.0);
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(box * scale);

		if (pixels <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		int spaceIndex = ViewModel.LchSpaceIndex;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;

		double hue = ViewModel.LchH;
		bool fit = ViewModel.LchChromaFit;

		if (pixels == _lcPixels && hue == _lcHue && snap == _lcSnap && spaceIndex == _lcSpaceIndex && style == _lcStyle && fit == _lcFit)
		{
			return;
		}

		_lcPixels = pixels;
		_lcHue = hue;
		_lcSnap = snap;
		_lcSpaceIndex = spaceIndex;
		_lcStyle = style;
		_lcFit = fit;

		LchSpace space = spaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;

		// 彩度軸の表示上限。フィット無効なら CMax、有効ならこの色相の cusp 彩度へ詰める。下地・つまみ・操作で同じ尺度を使う。
		double chromaAxisMax = LchColor.ChromaAxisMax(space, hue, fit);

		// 最近傍探索の前計算表を UI スレッドで一度温めてから背景へ投げる。背景スレッドは構築済みの表を読むだけにして、ColorConversion 側の「表示は UI スレッドのみ」の前提を崩さない。
		ColorConversion.Snap(snap, 0, 0, 0);

		EnqueueRegen(pixels, pixels,
			() => LcPlane.ComputePixels(pixels, pixels, space, hue, snap, scale, style, chromaAxisMax),
			bitmap =>
			{
				LcImage.Source = bitmap;

				// ドラッグ中のレンズは、色域ハッチなど計算の込み入った L-C 平面を作り直さず、生成したこのビットマップをそのまま読んで映す。色相はドラッグ中一定のため、平面は据え置きで足りる。パッドのレンズは合成サンプラーで、パッドの外側は、その点の背後にあるリング(帯＋つまみ)を映す。
				_planeSampler = new BitmapFieldSampler(bitmap, LcPad);
				LcPad.LensColorSampler = SamplePadThroughLens;
			});
	}




	// 色相環のレンズのサンプラー(合成)。重ね順は奥から「ホイールの帯 → L-C 平面の色面 → パッドのつまみ」。まず帯(または帯の外は透明)を求め、その上にパッド内なら平面の色面を重ね、最後にパッドのつまみを最前面で重ねる。つまみは色面の縁から帯側へはみ出すため、帯に隠さない。色相環そのものをドラッグ中はつまみがレンズへ置き換わるため、ここでは色相環自身のつまみは描かない。参照するのは平面の色面のみで、相互参照のループにはならない。
	private Color SampleRingThroughLens(double x, double y)
	{
		if (_wheelSampler is null)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		Color result = _wheelSampler.Sample(x, y);

		if (_planeSampler is null || HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0 || LcPad.ActualWidth <= 0.0 || LcPad.ActualHeight <= 0.0)
		{
			return result;
		}

		Point pad = RingGeometry.RingPointToPad(LcPad.ActualWidth, LcPad.ActualHeight, HueRing.ActualWidth, HueRing.ActualHeight, LcPad.PadRotation, x, y);

		// パッド内なら平面の色面を帯の上へ重ねる。色面は帯の内側で帯とは重ならないが、つまみは色面の縁から帯側へはみ出すため、つまみは矩形の内外に依らず最後に最前面で重ねる。
		if (pad.X >= 0.0 && pad.X <= LcPad.ActualWidth && pad.Y >= 0.0 && pad.Y <= LcPad.ActualHeight)
		{
			Color field = _planeSampler.Sample(pad.X, pad.Y);

			if (field.A != 0)
			{
				result = field;
			}
		}

		return LcPad.SampleThumbOverlay(result, pad.X, pad.Y);
	}




	// L-C パッドのレンズのサンプラー(合成)。パッドの矩形内は L-C 平面の色面、外はその点の背後にあるリング(帯＋つまみ)を映す。平面は色域外で半透明の画素を持つため、内外はアルファではなくパッドの矩形で判定する。
	private Color SamplePadThroughLens(double x, double y)
	{
		if (_planeSampler is not null && x >= 0.0 && x <= LcPad.ActualWidth && y >= 0.0 && y <= LcPad.ActualHeight)
		{
			return _planeSampler.Sample(x, y);
		}

		if (_wheelSampler is null || HueRing.ActualWidth <= 0.0 || HueRing.ActualHeight <= 0.0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		Point ring = RingGeometry.PadPointToRing(LcPad.ActualWidth, LcPad.ActualHeight, HueRing.ActualWidth, HueRing.ActualHeight, LcPad.PadRotation, x, y);
		Color band = _wheelSampler.Sample(ring.X, ring.Y);
		return HueRing.SampleThumbOverlay(band, ring.X, ring.Y);
	}




	// 現在の副モードの表色系。LchSpaceIndex から決め、画像生成・色変換で使う。
	private LchSpace CurrentSpace => ViewModel.LchSpaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;




	// 現在の明度を、表色系の素の尺度(OKLCH は 0–1、CIE LCH は 0–100)で返す。固定明度の画像生成へ渡す。
	private double LchLightnessNative()
	{
		return ViewModel.LchLNorm * LchColor.LMax(CurrentSpace);
	}




	// 現在の彩度を、表色系の素の尺度で返す。LchC は素の尺度のため、そのまま返す。固定彩度の画像生成へ渡す。
	private double LchChromaNative()
	{
		return ViewModel.LchC;
	}




	// 見せ方を、現在の選択に合わせて切り替える。ホストの出し分けと束縛の差し替えを行い、寸法・画像・つまみ・隅アイコンを整える。
	private void ApplyLayout()
	{
		_layout = ViewModel.LchLayoutIndex switch
		{
			1 => LchLayout.ClHueBar,
			2 => LchLayout.HueLightnessWheel,
			3 => LchLayout.HueLightnessPlane,
			4 => LchLayout.HueChromaWheel,
			5 => LchLayout.HueChromaPlane,
			_ => LchLayout.RingPlane,
		};

		_diskHost = _layout == LchLayout.HueLightnessWheel || _layout == LchLayout.HueChromaWheel;
		_cartHost = _layout == LchLayout.ClHueBar || _layout == LchLayout.HueLightnessPlane || _layout == LchLayout.HueChromaPlane;
		bool ring = !_diskHost && !_cartHost;

		HueRing.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
		LchDiskLayoutRoot.Visibility = _diskHost ? Visibility.Visible : Visibility.Collapsed;
		LchCartLayoutRoot.Visibility = _cartHost ? Visibility.Visible : Visibility.Collapsed;

		// 彩度フィットは彩度軸を持つ見せ方でだけ意味を持つ。固定色相の L-C 平面(色相リング+平面・C×L 平面+色相バー)と、固定明度の色相×彩度(平面・半径=彩度の円盤)。彩度軸を持たない色相×明度の平面・半径=明度の円盤では隠す。
		bool chromaFitApplies = _layout == LchLayout.RingPlane
			|| _layout == LchLayout.ClHueBar
			|| _layout == LchLayout.HueChromaPlane
			|| _layout == LchLayout.HueChromaWheel;
		ChromaFitPanel.Visibility = chromaFitApplies ? Visibility.Visible : Visibility.Collapsed;

		if (_diskHost)
		{
			// 半径=彩度(固定明度)か半径=明度(固定彩度)かを決め、残りの固定成分を縦スライダーへ。レイアウトが変わったら画像キャッシュを無効化して必ず作り直す。
			_diskRadiusIsChroma = _layout == LchLayout.HueChromaWheel;
			_diskPixels = -1;
			ConfigureDiskSlider();
		}

		if (_cartHost)
		{
			// 直交パッドの2軸と縦スライダーの成分を決め、束縛を差し替える。画像キャッシュを無効化して必ず作り直す。
			_cartPixelWidth = -1;
			ConfigureCartPad();
			ConfigureCartSlider();
		}

		UpdateRingSize();
		UpdateLayoutPickerIcon();

		if (_diskHost)
		{
			RegenerateDisk();
			UpdateDiskThumb();
		}

		if (_cartHost)
		{
			RegenerateCart();
			UpdateCartThumb();
		}

		if (ring)
		{
			UpdateRingVisuals();
		}
	}




	// 円盤の切り出し成分(半径に取らない側)の縦スライダーを束ね直す。半径=明度なら彩度、半径=彩度なら明度を司る。
	private void ConfigureDiskSlider()
	{
		ConfigureCutSlider(LchDiskSlider, _diskRadiusIsChroma ? CutComponent.Lightness : CutComponent.Chroma);
	}




	// 直交パッドの切り出し成分(2軸に取らない側)の縦スライダーを束ね直す。彩度×明度では色相、色相×明度では彩度、色相×彩度では明度を司る。
	private void ConfigureCartSlider()
	{
		CutComponent component = _layout switch
		{
			LchLayout.HueLightnessPlane => CutComponent.Chroma,
			LchLayout.HueChromaPlane => CutComponent.Lightness,
			_ => CutComponent.Hue,
		};
		ConfigureCutSlider(LchCartSlider, component);
	}




	// 切り出した成分の縦スライダーを、値・値域・刻み・背景・色域外区間・名前ごと束ね直す。明度・彩度は 0–1 の正規化、色相は 0–360 度。背景は各成分の縦向きグラデーション、色域外区間は各成分の判定。値域・刻みを先に整えてから値を束ね、旧い値域が残ったまま束ねて端でクランプされるのを避ける。
	private void ConfigureCutSlider(GradientSlider slider, CutComponent component)
	{
		if (component == CutComponent.Hue)
		{
			slider.Minimum = 0.0;
			slider.Maximum = 360.0;
			slider.StepFrequency = 1.0;
			slider.LargeChange = 10.0;
		}
		else
		{
			slider.Minimum = 0.0;
			slider.Maximum = 1.0;
			slider.StepFrequency = 0.01;
			slider.LargeChange = 0.1;
		}

		BindingOperations.SetBinding(slider, RangeBase.ValueProperty, MakeViewModelBinding(CutValuePath(component), BindingMode.TwoWay));
		BindingOperations.SetBinding(slider, GradientSlider.TrackBrushProperty, MakeViewModelBinding(CutBrushPath(component), BindingMode.OneWay));
		BindingOperations.SetBinding(slider, GradientSlider.OutOfGamutSegmentsProperty, MakeViewModelBinding(CutGamutPath(component), BindingMode.OneWay));
		AutomationProperties.SetName(slider, CutLetter(component));
	}




	// 直交パッドの横軸・縦軸が表す成分(色相×明度=H×L、色相×彩度=H×C、彩度×明度=L×C)を決め、アクセシビリティ名を整える。つまみ位置は UpdateCartThumb がコードで更新する。XValue・YValue を束縛で受け取ると、ドラッグ時の局所設定で OneWay 束縛が外れ、以後スライダー操作がつまみへ届かなくなるため、円盤のつまみと同じく束縛に頼らない。操作は2成分をまとめて ValuesChanged から VM へ渡す。
	private void ConfigureCartPad()
	{
		string letters = _layout switch
		{
			LchLayout.HueLightnessPlane => "H × L",
			LchLayout.HueChromaPlane => "H × C",
			_ => "L × C",
		};
		AutomationProperties.SetName(LcCartPad, letters);
	}




	// 直交パッドのつまみを、現在のレイアウトの2軸の正規化値の位置へ移す。色相×明度は横=色相・縦=明度、色相×彩度は横=色相・縦=彩度、彩度×明度は横=明度・縦=彩度。彩度軸は見せ方ごとに別の正規化(LchHueChromaCNorm・LchPadCNorm)を使い、操作の設定先(SetLchHueChroma・SetLchPad)と同じ尺度に合わせる。XValue・YValue はドラッグで局所設定されると OneWay 束縛が外れるため、円盤のつまみと同じく束縛に頼らずコードで更新する。設定で発火するのはつまみ位置の更新だけで、ValuesChanged はポインタ・キーボード操作のときしか発火しないため、ここでの設定が操作の取り込みへ回り込むことはない。
	private void UpdateCartThumb()
	{
		if (!_cartHost)
		{
			return;
		}

		(double x, double y) = _layout switch
		{
			LchLayout.HueLightnessPlane => (ViewModel.LchHNorm, ViewModel.LchLNorm),
			LchLayout.HueChromaPlane => (ViewModel.LchHNorm, ViewModel.LchHueChromaCNorm),
			_ => (ViewModel.LchLNorm, ViewModel.LchPadCNorm),
		};

		LcCartPad.XValue = x;
		LcCartPad.YValue = y;
	}




	// 切り出した成分の縦スライダーの値が束ねる ViewModel プロパティ名。明度・彩度は 0–1 の正規化、色相は 0–360 度。
	private static string CutValuePath(CutComponent component)
	{
		return component switch
		{
			CutComponent.Lightness => nameof(ColorEditorViewModel.LchLNorm),
			CutComponent.Chroma => nameof(ColorEditorViewModel.LchCNorm),
			_ => nameof(ColorEditorViewModel.LchH),
		};
	}




	// 切り出した成分の縦スライダーの背景が束ねる ViewModel プロパティ名。各成分の縦向きグラデーション。
	private static string CutBrushPath(CutComponent component)
	{
		return component switch
		{
			CutComponent.Lightness => nameof(ColorEditorViewModel.LchLightnessTrackBrushVertical),
			CutComponent.Chroma => nameof(ColorEditorViewModel.LchChromaTrackBrushVertical),
			_ => nameof(ColorEditorViewModel.LchHueTrackBrushVertical),
		};
	}




	// 切り出した成分の縦スライダーの色域外区間が束ねる ViewModel プロパティ名。各成分の色域外の範囲。
	private static string CutGamutPath(CutComponent component)
	{
		return component switch
		{
			CutComponent.Lightness => nameof(ColorEditorViewModel.LchLightnessGamut),
			CutComponent.Chroma => nameof(ColorEditorViewModel.LchChromaGamut),
			_ => nameof(ColorEditorViewModel.LchHueGamut),
		};
	}




	// 成分を表す言語非依存の1文字。アクセシビリティ名に使う。
	private static string CutLetter(CutComponent component)
	{
		return component switch
		{
			CutComponent.Lightness => "L",
			CutComponent.Chroma => "C",
			_ => "H",
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




	// 直交パッドを操作したら、レイアウトに応じた2成分をまとめて VM へ渡す。明度×彩度は色相を保って明度・彩度を、色相×明度は彩度を保って色相・明度を、色相×彩度は明度を保って色相・彩度を設定する。固定成分(縦スライダーが司る側)は VM 側で保たれる。明度×彩度の平面は横軸=明度・縦軸=彩度のため、SetLchPad(彩度, 明度) の引数順に合わせて Y(彩度)・X(明度) を渡す。
	private void OnCartValuesChanged(object? sender, EventArgs e)
	{
		switch (_layout)
		{
			case LchLayout.HueLightnessPlane:
				ViewModel.SetLchHueLightness(LcCartPad.XValue, LcCartPad.YValue);
				break;

			case LchLayout.HueChromaPlane:
				ViewModel.SetLchHueChroma(LcCartPad.XValue, LcCartPad.YValue);
				break;

			default:
				ViewModel.SetLchPad(LcCartPad.YValue, LcCartPad.XValue);
				break;
		}
	}




	// 直交パッドの大きさが変わったら、下地画像を作り直す。つまみ位置は PlanarPad が束縛から合わせる。パッドを初めて表示したときの寸法確定もこの経路で拾う。
	private void OnCartPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateCart();
	}




	// 直交パッドの固定成分(2軸に取らない側)の現在値。色制限・寸法とともに画像キャッシュの鍵に使う。
	private double CartFixedValue()
	{
		return _layout switch
		{
			LchLayout.HueLightnessPlane => LchChromaNative(),
			LchLayout.HueChromaPlane => LchLightnessNative(),
			_ => ViewModel.LchH,
		};
	}




	// 直交パッドの下地画像を、現在のレイアウト・固定成分・色制限設定・色域外の見せ方・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・固定成分・設定・副モード・見せ方なら作り直さない(レイアウト切り替え時は ApplyLayout がキャッシュを無効化する)。2軸はパッド全面に渡って変わるため鍵に含めない。
	private void RegenerateCart()
	{
		if (!_cartHost)
		{
			return;
		}

		if (LcCartPad.ActualWidth <= 0.0 || LcCartPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixelWidth = (int)Math.Round(LcCartPad.ActualWidth * scale);
		int pixelHeight = (int)Math.Round(LcCartPad.ActualHeight * scale);

		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		int spaceIndex = ViewModel.LchSpaceIndex;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;
		double fixedComponent = CartFixedValue();
		bool fit = ViewModel.LchChromaFit;

		if (pixelWidth == _cartPixelWidth && pixelHeight == _cartPixelHeight && fixedComponent == _cartFixed && snap == _cartSnap && spaceIndex == _cartSpaceIndex && style == _cartStyle && fit == _cartFit)
		{
			return;
		}

		_cartPixelWidth = pixelWidth;
		_cartPixelHeight = pixelHeight;
		_cartFixed = fixedComponent;
		_cartSnap = snap;
		_cartSpaceIndex = spaceIndex;
		_cartStyle = style;
		_cartFit = fit;

		LchSpace space = CurrentSpace;
		LchLayout layout = _layout;
		double chromaNative = LchChromaNative();
		double lightnessNative = LchLightnessNative();
		double hue = ViewModel.LchH;

		// 最近傍探索の前計算表を UI スレッドで一度温めてから背景へ投げる。背景スレッドは構築済みの表を読むだけにして、ColorConversion 側の「表示は UI スレッドのみ」の前提を崩さない。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 固定成分はいま(UI スレッド)の値を捕まえて背景へ渡す。背景スレッドから ViewModel を読み直さない。彩度軸の上限は活性なレイアウトの分だけ各ラムダ内で求める(純関数のため背景スレッドで安全)。C×L 平面(ClHueBar)は固定色相の cusp、色相×彩度の平面は固定明度で全色相を通じた最大彩度を基準にし、フィットに応じて詰める。色相×明度の平面は彩度軸を持たないため対象外。
		Func<byte[]> compute = layout switch
		{
			LchLayout.HueLightnessPlane => () => LchHueLightnessPlane.ComputePixels(pixelWidth, pixelHeight, space, chromaNative, snap, scale, style),
			LchLayout.HueChromaPlane => () => LchHueChromaPlane.ComputePixels(pixelWidth, pixelHeight, space, lightnessNative, snap, scale, style, LchColor.ChromaAxisMaxAtLightness(space, lightnessNative, fit)),
			_ => () => LchLightnessChromaPlane.ComputePixels(pixelWidth, pixelHeight, space, hue, snap, scale, style, LchColor.ChromaAxisMax(space, hue, fit)),
		};

		EnqueueRegen(pixelWidth, pixelHeight, compute, bitmap =>
		{
			LcCartImage.Source = bitmap;

			// ドラッグ中のレンズは、色域ハッチなど計算の込み入った下地を作り直さず、生成したこのビットマップをそのまま読んで映す。固定成分はドラッグ中一定のため、画像は据え置きで足りる。
			_cartSampler = new BitmapFieldSampler(bitmap, LcCartPad);
		});
	}




	// 直交パッドのレンズのサンプラー。生成済みの下地ビットマップをそのまま読む。パッドの外は透明を返し、レンズの縁では下地のカード色が透ける。
	private Color SampleCartField(double x, double y)
	{
		return _cartSampler?.Sample(x, y) ?? Color.FromArgb(0, 0, 0, 0);
	}




	// 円盤の大きさが変わったら、円盤画像を作り直し、つまみ位置を合わせる。円盤を初めて表示したときの寸法確定もこの経路で拾う。
	private void OnDiskHostSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateDisk();
		UpdateDiskThumb();
	}




	// 円盤画像を、現在のレイアウト・固定成分・色制限設定・色域外の見せ方・円盤の大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・固定成分・設定・副モード・見せ方なら作り直さない(レイアウト切り替え時は ApplyLayout がキャッシュを無効化する)。色相は円盤の全方位に現れるため鍵に含めない。
	private void RegenerateDisk()
	{
		if (!_diskHost)
		{
			return;
		}

		if (DiskHost.ActualWidth <= 0.0 || DiskHost.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = (int)Math.Round(Math.Min(DiskHost.ActualWidth, DiskHost.ActualHeight) * scale);

		if (pixels <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		int spaceIndex = ViewModel.LchSpaceIndex;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;
		double fixedComponent = _diskRadiusIsChroma ? LchLightnessNative() : LchChromaNative();
		bool fit = ViewModel.LchChromaFit;

		if (pixels == _diskPixels && fixedComponent == _diskFixed && snap == _diskSnap && spaceIndex == _diskSpaceIndex && style == _diskStyle && fit == _diskFit)
		{
			return;
		}

		_diskPixels = pixels;
		_diskFixed = fixedComponent;
		_diskSnap = snap;
		_diskSpaceIndex = spaceIndex;
		_diskStyle = style;
		_diskFit = fit;

		LchSpace space = CurrentSpace;
		bool radiusIsChroma = _diskRadiusIsChroma;
		double lightnessNative = LchLightnessNative();
		double chromaNative = LchChromaNative();

		// 最近傍探索の前計算表を UI スレッドで一度温めてから背景へ投げる。背景スレッドは構築済みの表を読むだけにして、ColorConversion 側の「表示は UI スレッドのみ」の前提を崩さない。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 固定成分はいま(UI スレッド)の値を捕まえて背景へ渡す。半径=彩度の円盤は固定明度で全色相を通じた最大彩度を基準に、彩度軸(半径)をフィットに応じて詰める。半径=明度の円盤は彩度軸を持たないため対象外。彩度軸の上限は純関数のためラムダ内(背景スレッド)で求めてよい。
		Func<byte[]> compute = radiusIsChroma
			? () => LchHueChromaDisk.ComputePixels(pixels, pixels, space, lightnessNative, snap, scale, style, LchColor.ChromaAxisMaxAtLightness(space, lightnessNative, fit))
			: () => LchHueLightnessDisk.ComputePixels(pixels, pixels, space, chromaNative, snap, scale, style);

		EnqueueRegen(pixels, pixels, compute, bitmap =>
		{
			DiskImage.Source = bitmap;
			_diskSampler = new BitmapFieldSampler(bitmap, DiskHost);
		});
	}




	// 円盤のつまみを、現在の色相(角度)と半径成分(中心からの半径の割合)の位置へ移す。半径成分は配置に応じて明度または彩度。角度の取り方は円盤描画と同じ RingGeometry の規約に従い、円盤の色とつまみがずれない。寸法が無いときは何もしない。
	private void UpdateDiskThumb()
	{
		if (DiskThumbOffset is null)
		{
			return;
		}

		double width = DiskHost.ActualWidth;
		double height = DiskHost.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		double maxRadius = Math.Min(width, height) / 2.0;
		double radiusComponent = _diskRadiusIsChroma ? ViewModel.LchHueChromaCNorm : ViewModel.LchLNorm;
		double radius = Math.Clamp(radiusComponent, 0.0, 1.0) * maxRadius;
		Point offset = RingGeometry.OffsetForValue(radius, ViewModel.LchH);
		DiskThumbOffset.X = offset.X;
		DiskThumbOffset.Y = offset.Y;
		DiskThumbDarkOffset.X = offset.X;
		DiskThumbDarkOffset.Y = offset.Y;
	}




	private void OnDiskPointerPressed(object sender, PointerRoutedEventArgs e)
	{
		double width = DiskHost.ActualWidth;
		double height = DiskHost.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		Point position = e.GetCurrentPoint(DiskHost).Position;
		double maxRadius = Math.Min(width, height) / 2.0;
		double dx = position.X - (width / 2.0);
		double dy = position.Y - (height / 2.0);

		// 円の外を押したときは操作も捕捉もしない。ドラッグで縁の外へ出たぶんは半径成分を縁(1)へ留める。
		if (Math.Sqrt((dx * dx) + (dy * dy)) > maxRadius)
		{
			return;
		}

		_diskDragging = DiskHost.CapturePointer(e.Pointer);
		DiskHost.Focus(FocusState.Pointer);
		ApplyDiskPoint(dx, dy, maxRadius);

		if (_diskDragging)
		{
			BeginDiskLens();
		}

		e.Handled = true;
	}




	private void OnDiskPointerMoved(object sender, PointerRoutedEventArgs e)
	{
		if (!_diskDragging)
		{
			return;
		}

		double width = DiskHost.ActualWidth;
		double height = DiskHost.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		Point position = e.GetCurrentPoint(DiskHost).Position;
		double maxRadius = Math.Min(width, height) / 2.0;
		ApplyDiskPoint(position.X - (width / 2.0), position.Y - (height / 2.0), maxRadius);
		UpdateDiskLens();
		e.Handled = true;
	}




	private void OnDiskPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (!_diskDragging)
		{
			return;
		}

		_diskDragging = false;
		EndDiskLens();
		DiskHost.ReleasePointerCapture(e.Pointer);
		e.Handled = true;
	}




	private void OnDiskPointerCaptureLost(object sender, PointerRoutedEventArgs e)
	{
		_diskDragging = false;
		EndDiskLens();
	}




	// 円盤上の中心からの相対位置(dx, dy)を、色相(角度)と半径成分(半径の割合)へ写して色1へ反映する。半径成分は配置に応じて彩度または明度。角度の取り方は円盤描画・RingGeometry と同じ規約。半径が円を越えたぶんは半径成分を1へ留める。固定成分(縦スライダーが司る側)は VM 側で保たれる。
	private void ApplyDiskPoint(double dx, double dy, double maxRadius)
	{
		double radius = Math.Sqrt((dx * dx) + (dy * dy));
		double radiusComponent = maxRadius > 0.0 ? Math.Clamp(radius / maxRadius, 0.0, 1.0) : 0.0;
		double hueNorm = RingGeometry.ValueFromPoint(dx, dy) / 360.0;

		if (_diskRadiusIsChroma)
		{
			ViewModel.SetLchHueChroma(hueNorm, radiusComponent);
		}
		else
		{
			ViewModel.SetLchHueLightness(hueNorm, radiusComponent);
		}
	}




	// 矢印キーで円盤のつまみを画面上で1段ずつ動かす。移動方向は画面に固定(右=右、上=上)で、移動後の点を色相・半径成分へ写し直す。回転を持たない円盤のため逆回転は要らない。1段の量は円盤の一辺の1%(最低1DIP)。
	private void OnDiskKeyDown(object sender, KeyRoutedEventArgs e)
	{
		double width = DiskHost.ActualWidth;
		double height = DiskHost.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		double step = Math.Max(1.0, Math.Min(width, height) * 0.01);
		double deltaX = 0.0;
		double deltaY = 0.0;

		switch (e.Key)
		{
			case VirtualKey.Right: deltaX = step; break;
			case VirtualKey.Left: deltaX = -step; break;
			case VirtualKey.Up: deltaY = -step; break;
			case VirtualKey.Down: deltaY = step; break;
			default: return;
		}

		double maxRadius = Math.Min(width, height) / 2.0;
		double radiusComponent = _diskRadiusIsChroma ? ViewModel.LchHueChromaCNorm : ViewModel.LchLNorm;
		double radius = Math.Clamp(radiusComponent, 0.0, 1.0) * maxRadius;
		Point offset = RingGeometry.OffsetForValue(radius, ViewModel.LchH);
		ApplyDiskPoint(offset.X + deltaX, offset.Y + deltaY, maxRadius);
		e.Handled = true;
	}




	// 円盤のドラッグ開始時にレンズを出す。つまみ(二重の輪)を隠してレンズへ置き換える。
	private void BeginDiskLens()
	{
		if (_diskLens is null)
		{
			return;
		}

		_diskLens.Begin();
		DiskThumb.Opacity = 0.0;
		DiskThumbDark.Opacity = 0.0;
		UpdateDiskLens();
	}




	// レンズを現在のつまみ位置(円盤局所座標)へ追従させ、その点まわりの色面を映し直す。色面は生成済みの円盤ビットマップを読む。
	private void UpdateDiskLens()
	{
		if (_diskLens is null || !_diskLens.IsActive)
		{
			return;
		}

		double width = DiskHost.ActualWidth;
		double height = DiskHost.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		double maxRadius = Math.Min(width, height) / 2.0;
		double radiusComponent = _diskRadiusIsChroma ? ViewModel.LchHueChromaCNorm : ViewModel.LchLNorm;
		double radius = Math.Clamp(radiusComponent, 0.0, 1.0) * maxRadius;
		Point offset = RingGeometry.OffsetForValue(radius, ViewModel.LchH);
		_diskLens.Update(SampleDiskField, (width / 2.0) + offset.X, (height / 2.0) + offset.Y);
	}




	// 円盤のドラッグ終了時にレンズを退場させ、つまみを戻す。
	private void EndDiskLens()
	{
		if (_diskLens is null)
		{
			return;
		}

		_diskLens.End();
		DiskThumb.Opacity = 1.0;
		DiskThumbDark.Opacity = 1.0;
	}




	// 円盤のレンズのサンプラー。生成済みの円盤ビットマップをそのまま読む。円の外は透明を返し、レンズの縁では下地のカード色が透ける。
	private Color SampleDiskField(double x, double y)
	{
		return _diskSampler?.Sample(x, y) ?? Color.FromArgb(0, 0, 0, 0);
	}




	// 指定したレイアウトの縮小見本(サムネイル)を作る。中央の2次元面だけでは区別がつかない色相リング+平面・彩度×明度+色相バーは構造込み(リング囲み・横バー添え)で描き分け、円盤・色相軸の矩形は下地そのもので区別がつくためそのまま描く。固定成分は色が映える代表値(彩度は表示上限の4割、明度は最大の6割)を当て、色相固定の平面は色相0度で描く。
	private WriteableBitmap LayoutThumbnailFor(int index, int pixels, LchSpace space, SnapSettings snap, GamutOutOfRangeStyle style)
	{
		double chromaRep = LchColor.CMax(space) * 0.4;
		double lightnessRep = LchColor.LMax(space) * 0.6;

		return index switch
		{
			1 => LayoutThumbnail.ShapeWithBar(pixels, snap, s => LchLightnessChromaPlane.Create(s, s, space, 0.0, snap, 1.0, style, LchColor.CMax(space))),
			2 => LchHueLightnessDisk.Create(pixels, pixels, space, chromaRep, snap, 1.0, style),
			3 => LchHueLightnessPlane.Create(pixels, pixels, space, chromaRep, snap, 1.0, style),
			4 => LchHueChromaDisk.Create(pixels, pixels, space, lightnessRep, snap, 1.0, style, LchColor.CMax(space)),
			5 => LchHueChromaPlane.Create(pixels, pixels, space, lightnessRep, snap, 1.0, style, LchColor.CMax(space)),
			_ => LayoutThumbnail.RingWithShape(pixels, 1.0 / Math.Sqrt(2.0), snap, s => LcPlane.Create(s, s, space, 0.0, snap, 1.0, style, LchColor.CMax(space))),
		};
	}




	// 隅のレイアウトボタンのアイコンを、現在選んでいるレイアウトの縮小見本に差し替える。アイコン自体が現在の状態表示を兼ねる。
	private void UpdateLayoutPickerIcon()
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(24.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;
		LchLayoutPickerIcon.Source = LayoutThumbnailFor(ViewModel.LchLayoutIndex, pixels, CurrentSpace, snap, ViewModel.OutOfRangeStyle);
	}




	// レイアウト選択のフライアウトを開くときに、全レイアウトのサムネイルを今の副モード・色制限・色域外の見せ方で作り直し、現在の選択を縁取りで示す。
	private void OnLchLayoutFlyoutOpening(object sender, object e)
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(56.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;
		LchSpace space = CurrentSpace;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;

		LchRingPlaneThumbImage.Source = LayoutThumbnailFor(0, pixels, space, snap, style);
		LchHueLightnessWheelThumbImage.Source = LayoutThumbnailFor(2, pixels, space, snap, style);
		LchHueChromaWheelThumbImage.Source = LayoutThumbnailFor(4, pixels, space, snap, style);
		LchClHueBarThumbImage.Source = LayoutThumbnailFor(1, pixels, space, snap, style);
		LchHueLightnessPlaneThumbImage.Source = LayoutThumbnailFor(3, pixels, space, snap, style);
		LchHueChromaPlaneThumbImage.Source = LayoutThumbnailFor(5, pixels, space, snap, style);

		var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
		var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
		int current = ViewModel.LchLayoutIndex;
		LchRingPlaneThumbBorder.BorderBrush = current == 0 ? accent : clear;
		LchClHueBarThumbBorder.BorderBrush = current == 1 ? accent : clear;
		LchHueLightnessWheelThumbBorder.BorderBrush = current == 2 ? accent : clear;
		LchHueLightnessPlaneThumbBorder.BorderBrush = current == 3 ? accent : clear;
		LchHueChromaWheelThumbBorder.BorderBrush = current == 4 ? accent : clear;
		LchHueChromaPlaneThumbBorder.BorderBrush = current == 5 ? accent : clear;
	}




	private void OnPickLchRingPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LchLayoutIndex = 0;
		LchLayoutPickerFlyout.Hide();
	}




	private void OnPickLchClHueBarLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LchLayoutIndex = 1;
		LchLayoutPickerFlyout.Hide();
	}




	private void OnPickLchHueLightnessWheelLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LchLayoutIndex = 2;
		LchLayoutPickerFlyout.Hide();
	}




	private void OnPickLchHueLightnessPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LchLayoutIndex = 3;
		LchLayoutPickerFlyout.Hide();
	}




	private void OnPickLchHueChromaWheelLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LchLayoutIndex = 4;
		LchLayoutPickerFlyout.Hide();
	}




	private void OnPickLchHueChromaPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LchLayoutIndex = 5;
		LchLayoutPickerFlyout.Hide();
	}




	// 見せ方。RingPlane は色相リング+C×L 平面。残りは「2次元コントロール+切り出した1成分の縦スライダー」で、円盤系(角度=色相・半径=明度/彩度)と直交パッド系(2成分を縦横)に分かれる。ClHueBar=彩度×明度の平面+色相、HueLightnessWheel=半径=明度の円盤+彩度、HueLightnessPlane=色相×明度の平面+彩度、HueChromaWheel=半径=彩度の円盤+明度、HueChromaPlane=色相×彩度の平面+明度。
	private enum LchLayout
	{
		RingPlane,
		ClHueBar,
		HueLightnessWheel,
		HueLightnessPlane,
		HueChromaWheel,
		HueChromaPlane,
	}




	// 切り出して縦スライダーが司る成分。直交パッド・円盤の2次元に取らない残り1成分。
	private enum CutComponent
	{
		Lightness,
		Chroma,
		Hue,
	}
}
