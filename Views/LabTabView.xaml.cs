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

// 「Lab」タブの中身。色1(編集中)を Lab の直交軸で編集する。
// 上段は2次元の編集エリアと、その右の縦並び設定(OKLab/CIE Lab 切替・色域制限トグル・見せ方ピッカー)。編集エリアは見せ方(レイアウト)で3種を入れ替える: a×b 平面+明度 L の縦バー(既定)、a×L 平面+b の縦バー、b×L 平面+a の縦バー。いずれもパッドはウィンドウへ追従して拡縮し、添える縦スライダーの高さもそれに揃える。
// 下段に L・a・b の水平スライダーを置き、上段とどちらからでも操作できる(全レイアウトで常時表示)。各平面は sRGB 色域に収まる部分だけを実色で塗り、色域外はハッチで透かして可視化する。
// 各平面は固定成分(その面の2軸に取らない残り)と副モードに依って色と色域の形が変わるため、それらが変わるたびにコードで画像を作って差し込む。編集対象の状態は色1・色2を束ねる共有モデルを外部から受け取る。
public sealed partial class LabTabView : UserControl
{
	public ColorEditorViewModel ViewModel { get; }

	// a-b 平面画像を生成した際の画素サイズ・明度・色制限設定・副モード・色域外の見せ方。同じ条件での作り直しを避けるために覚えておく。明度は未生成を表す NaN で初期化する。
	private int _planePixels = -1;
	private double _planeLightness = double.NaN;
	private SnapSettings _planeSnap;
	private int _planeSpaceIndex = -1;
	private GamutOutOfRangeStyle _planeStyle = (GamutOutOfRangeStyle)(-1);
	private int _planeScaleIndex = -1;

	// 直交パッド(a×L・b×L)の下地画像を生成した際の画素サイズ(縦横独立)・固定成分の値・色制限設定・副モード・色域外の見せ方。同じ条件での作り直しを避けるために覚えておく。固定成分はパッドの2軸に取らない側で、未生成を表す NaN で初期化する。
	private int _cartPixelWidth = -1;
	private int _cartPixelHeight = -1;
	private double _cartFixed = double.NaN;
	private SnapSettings _cartSnap;
	private int _cartSpaceIndex = -1;
	private GamutOutOfRangeStyle _cartStyle = (GamutOutOfRangeStyle)(-1);
	private int _cartScaleIndex = -1;

	// 直交パッド(a×L・b×L)のドラッグ中のレンズが読む、生成済みのビットマップサンプラー。色域ハッチなど計算の込み入った下地を作り直さず、表示中の画像をそのまま読んで映す。生成のたびに差し替える。
	private BitmapFieldSampler? _cartSampler;

	// RasterizationScale(表示倍率)の変化を拾うために購読している XamlRoot。表示先が変わったら張り替える。
	private XamlRoot? _subscribedRoot;

	// タブの中身を包む祖先のスクロール領域。読み込み後に視覚ツリーを辿って一度だけ見つけ、その可視高さ(ViewportHeight)をパッドの一辺の算出に使う。
	private ScrollViewer? _scrollHost;

	// パッドの一辺の下限。これより縮める必要がある高さになったら、それ以上は縮めずスクロール領域に委ねる。
	private const double MinPadSide = 180.0;

	// a-b パッドと明度レール(および各レイアウトのパッドと縦スライダー)の間隔。XAML の中央寄せの組の ColumnSpacing と同じ値にし、パッドの一辺を幅から決めるときにスライダー幅と併せて差し引く。
	private const double PadRailGap = 12.0;

	// 副モードのラジオを VM の復元値に合わせる間、SelectionChanged が VM を上書きしないようにする。構築中の初期化を無視するため真で始める。
	private bool _spaceSyncing = true;

	// 現在の見せ方。AbPlane は a×b 平面+明度 L の縦バー(既定)。AlPlane=a×L 平面+b、BlPlane=b×L 平面+a。VM の LabLayoutIndex を反映する。
	private LabLayout _layout = LabLayout.AbPlane;

	// 現在のレイアウトが直交パッドホスト(LabCartPad)を使うか。偽なら既定の a×b 平面+明度レール。
	private bool _cartHost;

	// 下地画像の再生成を背景スレッドへ逃がすためのコアレス機構の状態。画素計算(重い)は ThreadPool で回し、WriteableBitmap 化と差し込み(安価)だけを UI スレッドで行う。走らせるのは常に1本だけ(_regenRunning)にし、走行中に来た要求は最新の1件だけを保持(_regenPending と _pending*)して、完了時にそれを次へ回す。固定成分が毎ティック変わっても UI スレッドが画素計算で塞がらないようにするためのもの。EnqueueRegen と完了コールバックはともに UI スレッドで動くため、これらの読み書きにロックは要らない。
	private bool _regenRunning;
	private bool _regenPending;
	private int _pendingWidth;
	private int _pendingHeight;
	private Func<byte[]>? _pendingCompute;
	private Action<WriteableBitmap>? _pendingApply;


	public LabTabView(ColorEditorViewModel viewModel)
	{
		ViewModel = viewModel;
		this.InitializeComponent();

		// 復元済みの副モードをラジオに反映する。ここまでの SelectionChanged は _spaceSyncing で無視し、以降の操作だけ VM へ伝える。
		SpaceSelector.SelectedIndex = ViewModel.LabSpaceIndex;
		_spaceSyncing = false;

		// a-b パッドの操作は両軸を二次元でまとめて扱う(色域内への半径方向の寄せ)ため、横・縦の個別束縛ではなく ValuesChanged からまとめて VM へ渡す。
		AbPad.ValuesChanged += OnAbPadValuesChanged;

		AbPad.SizeChanged += OnPadSizeChanged;

		// 直交パッド(LabCartPad)は PlanarPad を流用するため、つまみ・レンズ・矢印操作はそのまま得られる。操作は2成分をまとめて扱うため ValuesChanged からレイアウトに応じた設定へ振り分け、レンズに映す色面のサンプラーを与え、大きさ変化で下地画像を作り直す。軸の束縛はレイアウトに応じて ConfigureCartPad が差し替える。
		LabCartPad.ValuesChanged += OnCartValuesChanged;
		LabCartPad.LensColorSampler = SampleCartField;
		LabCartPad.SizeChanged += OnCartPadSizeChanged;

		// パッドの一辺はスクロール領域の可視高さからスライダー群の高さを引いて決めるため、その高さが変わったら算出し直す。
		// 可視高さの変化は読み込み後に見つけるスクロール領域の SizeChanged で、エリアの幅変化は PadArea の SizeChanged で拾う。
		// パッドの幅はエリアの幅から縦スライダーの幅と間隔を差し引いて決めるため、スライダーの実寸が定まる(初回計測)タイミングでも算出し直す。
		SliderHost.SizeChanged += OnLayoutMetricChanged;

		// 右の縦並び設定列は上段の行高の下限になるため、その高さが変わったらパッドの一辺を算出し直す。
		SideControls.SizeChanged += OnLayoutMetricChanged;

		LightnessSlider.SizeChanged += OnLayoutMetricChanged;
		LabCartSlider.SizeChanged += OnLayoutMetricChanged;

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

		// 明度・a・b・副モード・色制限モード・見せ方の変更で各面を塗り直すための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
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




	// a-b 平面エリアの幅が変わったら、パッドの一辺を算出し直す。
	private void OnPadAreaSizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// 全体の高さ、スライダー群の高さ、各レイアウトの縦スライダーの幅のいずれかが変わったら、パッドの一辺を算出し直す。
	private void OnLayoutMetricChanged(object sender, SizeChangedEventArgs e)
	{
		UpdatePadSize();
	}




	// 色1の明度・a・b・副モード・色制限設定・色域外の見せ方・見せ方が変わったら、活性なホストの下地とつまみを整える。各 Regenerate は空振り判定とキャッシュを持つため、固定成分が動いていなければ実際には描き直さない。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// 見せ方が変わったら、ホストの出し分け・寸法・画像・つまみを一斉に整える。
		if (e.PropertyName == nameof(ColorEditorViewModel.LabLayoutIndex))
		{
			ApplyLayout();
			return;
		}

		// 表示枠(スケール)が変わったら、活性なホストの下地を作り直す。a×b 平面ではつまみ位置はパッドの XValue・YValue 束縛が VM の通知で追従し、直交パッドではここで UpdateCartThumb がコードで合わせ直す。
		if (e.PropertyName == nameof(ColorEditorViewModel.LabAbScaleIndex))
		{
			RegenerateActiveField();

			if (_cartHost)
			{
				UpdateCartThumb();
			}

			return;
		}

		// 明度・a・b が変わったら、活性なホストの下地(固定成分が変わったときだけ)とつまみを整える。a×b 平面では明度が、a×L 平面では b が、b×L 平面では a が固定成分。
		if (e.PropertyName == nameof(ColorEditorViewModel.LabL)
			|| e.PropertyName == nameof(ColorEditorViewModel.LabA)
			|| e.PropertyName == nameof(ColorEditorViewModel.LabB))
		{
			RegenerateActiveField();

			if (_cartHost)
			{
				UpdateCartThumb();
			}

			return;
		}

		// 副モードや色制限が変わると、各ホストの下地も尺度・色域も一斉に変わる。隅アイコンも作り直す。
		if (e.PropertyName == nameof(ColorEditorViewModel.LabSpaceIndex)
			|| e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap)
			|| e.PropertyName == nameof(ColorEditorViewModel.OutOfRangeStyle))
		{
			// 副モードが変わると a・b の正規化刻みが変わる。直交パッドの a・b 縦スライダーは刻みを副モードから取るため、束ね直して刻みを今の表色系へ合わせる。
			if (_cartHost && e.PropertyName == nameof(ColorEditorViewModel.LabSpaceIndex))
			{
				ConfigureCartSlider();
			}

			RegenerateActiveField();

			if (_cartHost)
			{
				UpdateCartThumb();
			}

			UpdateLayoutPickerIcon();
		}
	}




	// 活性なホスト(直交パッド・a×b 平面)の下地を、現在の固定成分・色制限・色域外の見せ方に合わせて作り直す。各 Regenerate は空振り判定とキャッシュを持つため、変わっていなければ実際には描き直さない。
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




	// a-b パッドを正方形のまま、エリアの幅から縦スライダーの幅と間隔を除いた残りと、下に並ぶ要素を除いた残りの可視高さの小さい方へ合わせ、縦スライダーの高さもそれに揃える。これでパッドとスライダーの組が一定間隔を保ったまま中央寄せで拡縮する。直交パッド(a×L・b×L)は横軸の刻みを細かく取れるよう横長にする。可視高さはスクロール領域のビューポート高さから、下段のスライダー群と行間を引いて求める。下限を割る高さになったら、それ以上は縮めずスクロール領域に委ねる。パッドの大きさが変わると、それを受けた SizeChanged で各面の画像が作り直される。
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

		// 直交パッド(a×L・b×L)のとき。横軸の色軸を細かく取れるよう横いっぱいに広げ、縦は残りの高さに合わせる。
		if (_cartHost)
		{
			double sliderColumn = LabCartSlider.ActualWidth > 0.0 ? LabCartSlider.ActualWidth : 32.0;
			double widthBudget = PadArea.ActualWidth - sliderColumn - PadRailGap;
			double padHeight = Math.Max(minSide, heightBudget);

			if (widthBudget <= 0.0)
			{
				return;
			}

			if (LabCartPad.Width != widthBudget)
			{
				LabCartPad.Width = widthBudget;
			}

			if (LabCartPad.Height != padHeight)
			{
				LabCartPad.Height = padHeight;
			}

			if (LabCartSlider.Height != padHeight)
			{
				LabCartSlider.Height = padHeight;
			}

			return;
		}

		// 既定の a×b 平面+明度レール。パッドとレールを一定間隔で並べた組を中央寄せにするため、パッドが取れる幅はエリアの幅からレールの幅と間隔を除いた残り。
		double abWidthBudget = PadArea.ActualWidth - LightnessSlider.ActualWidth - PadRailGap;
		double side = Math.Min(abWidthBudget, Math.Max(minSide, heightBudget));

		if (side <= 0.0)
		{
			return;
		}

		if (AbPad.Width != side)
		{
			AbPad.Width = side;
			AbPad.Height = side;
			LightnessSlider.Height = side;
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




	// a-b パッドを操作したら、両軸を二次元でまとめて VM へ渡す。色域制限オン時はカーソル位置を色相(a:b の比)を保って色域の縁へ寄せる処理が VM 側で行われる。
	private void OnAbPadValuesChanged(object? sender, EventArgs e)
	{
		ViewModel.SetLabPad(AbPad.XValue, AbPad.YValue);
	}




	// OKLab / CIE Lab の切り替え。選んだ副モードを VM へ伝える。構築中の初期化(_spaceSyncing)は無視する。
	private void OnSpaceChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_spaceSyncing)
		{
			return;
		}

		ViewModel.LabSpaceIndex = SpaceSelector.SelectedIndex;
	}




	// 副モードを OKLab へ切り替える。貼り付け連動から呼ぶ。SpaceSelector の選択を変えることで表示の切り替えを行う。既に OKLab のときは選択が変わらず何も起きない。
	public void ShowOklabMode()
	{
		SpaceSelector.SelectedIndex = 0;
	}




	// 副モードを CIE Lab へ切り替える。貼り付け連動から呼ぶ。SpaceSelector の選択を変えることで表示の切り替えを行う。既に CIE Lab のときは選択が変わらず何も起きない。
	public void ShowLabMode()
	{
		SpaceSelector.SelectedIndex = 1;
	}




	// 現在の副モードの表色系。LabSpaceIndex から決め、画像生成・色変換で使う。
	private LchSpace CurrentSpace => ViewModel.LabSpaceIndex == 1 ? LchSpace.CieLch : LchSpace.Oklch;




	// 現在の a 軸を、表色系の素の尺度で返す。LabA は素の尺度のため、そのまま返す。固定 a の画像生成へ渡す。
	private double LabANative()
	{
		return ViewModel.LabA;
	}




	// 現在の b 軸を、表色系の素の尺度で返す。LabB は素の尺度のため、そのまま返す。固定 b の画像生成へ渡す。
	private double LabBNative()
	{
		return ViewModel.LabB;
	}




	// 見せ方を、現在の選択に合わせて切り替える。ホストの出し分けと束縛の差し替えを行い、寸法・画像・つまみ・隅アイコンを整える。
	private void ApplyLayout()
	{
		_layout = ViewModel.LabLayoutIndex switch
		{
			1 => LabLayout.AlPlane,
			2 => LabLayout.BlPlane,
			_ => LabLayout.AbPlane,
		};

		_cartHost = _layout == LabLayout.AlPlane || _layout == LabLayout.BlPlane;
		bool ab = !_cartHost;

		LabAbLayoutRoot.Visibility = ab ? Visibility.Visible : Visibility.Collapsed;
		LabCartLayoutRoot.Visibility = _cartHost ? Visibility.Visible : Visibility.Collapsed;

		if (_cartHost)
		{
			// 直交パッドの2軸と縦スライダーの成分を決め、束縛を差し替える。画像キャッシュを無効化して必ず作り直す。
			_cartPixelWidth = -1;
			ConfigureCartPad();
			ConfigureCartSlider();
		}

		if (ab)
		{
			// a×b 平面のキャッシュを無効化して必ず作り直す。
			_planePixels = -1;
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




	// 直交パッドの切り出し成分(2軸に取らない側)の縦スライダーを束ね直す。a×L 平面では b、b×L 平面では a を司る。
	private void ConfigureCartSlider()
	{
		CutComponent component = _layout == LabLayout.AlPlane ? CutComponent.B : CutComponent.A;
		ConfigureCutSlider(LabCartSlider, component);
	}




	// 直交パッドの切り出し成分(a または b)の縦スライダーを、値・値域・刻み・背景・色域外区間・名前ごと束ね直す。a・b は 0–1 の正規化(0.5 が 0)。背景は各成分の縦向きグラデーション、色域外区間は各成分の判定。値域・刻みを先に整えてから値を束ね、旧い値域が残ったまま束ねて端でクランプされるのを避ける。
	private void ConfigureCutSlider(GradientSlider slider, CutComponent component)
	{
		slider.Minimum = 0.0;
		slider.Maximum = 1.0;
		slider.StepFrequency = ViewModel.LabAbNormStep;
		slider.LargeChange = ViewModel.LabAbNormLargeStep;

		BindingOperations.SetBinding(slider, RangeBase.ValueProperty, MakeViewModelBinding(CutValuePath(component), BindingMode.TwoWay));
		BindingOperations.SetBinding(slider, GradientSlider.TrackBrushProperty, MakeViewModelBinding(CutBrushPath(component), BindingMode.OneWay));
		BindingOperations.SetBinding(slider, GradientSlider.OutOfGamutSegmentsProperty, MakeViewModelBinding(CutGamutPath(component), BindingMode.OneWay));
		AutomationProperties.SetName(slider, CutLetter(component));
	}




	// 直交パッドの横軸・縦軸が表す成分(a×L は横=a・縦=L、b×L は横=b・縦=L)を決め、アクセシビリティ名を整える。つまみ位置は UpdateCartThumb がコードで更新する。XValue・YValue を束縛で受け取ると、ドラッグ時の局所設定で OneWay 束縛が外れ、以後スライダー操作がつまみへ届かなくなるため、円盤のつまみと同じく束縛に頼らない。操作は2成分をまとめて ValuesChanged から VM へ渡す。
	private void ConfigureCartPad()
	{
		string letters = _layout == LabLayout.AlPlane ? "a × L" : "b × L";
		AutomationProperties.SetName(LabCartPad, letters);
	}




	// 直交パッドのつまみを、現在のレイアウトの2軸の正規化値の位置へ移す。a×L は横=a・縦=L、b×L は横=b・縦=L。XValue・YValue はドラッグで局所設定されると OneWay 束縛が外れるため、円盤のつまみと同じく束縛に頼らずコードで更新する。設定で発火するのはつまみ位置の更新だけで、ValuesChanged はポインタ・キーボード操作のときしか発火しないため、ここでの設定が操作の取り込みへ回り込むことはない。
	private void UpdateCartThumb()
	{
		if (!_cartHost)
		{
			return;
		}

		// つまみ位置は a×b パッドと同じく表示枠を介して読む。固定枠のときは a・b・明度の全域正規化と一致し、フィットのときは枠の縮尺・中心に追従する。
		LabCartPad.XValue = _layout == LabLayout.AlPlane ? ViewModel.LabAlPadXNorm : ViewModel.LabBlPadXNorm;
		LabCartPad.YValue = _layout == LabLayout.AlPlane ? ViewModel.LabAlPadYNorm : ViewModel.LabBlPadYNorm;
	}




	// 直交パッドの切り出し成分(a または b)の縦スライダーの値が束ねる ViewModel プロパティ名。a・b は 0–1 の正規化(0.5 が 0)。
	private static string CutValuePath(CutComponent component)
	{
		return component switch
		{
			CutComponent.A => nameof(ColorEditorViewModel.LabANorm),
			_ => nameof(ColorEditorViewModel.LabBNorm),
		};
	}




	// 直交パッドの切り出し成分(a または b)の縦スライダーの背景が束ねる ViewModel プロパティ名。各成分の縦向きグラデーション。
	private static string CutBrushPath(CutComponent component)
	{
		return component switch
		{
			CutComponent.A => nameof(ColorEditorViewModel.LabATrackBrushVertical),
			_ => nameof(ColorEditorViewModel.LabBTrackBrushVertical),
		};
	}




	// 直交パッドの切り出し成分(a または b)の縦スライダーの色域外区間が束ねる ViewModel プロパティ名。各成分の色域外の範囲。
	private static string CutGamutPath(CutComponent component)
	{
		return component switch
		{
			CutComponent.A => nameof(ColorEditorViewModel.LabAGamut),
			_ => nameof(ColorEditorViewModel.LabBGamut),
		};
	}




	// 成分を表す言語非依存の1文字。アクセシビリティ名に使う。
	private static string CutLetter(CutComponent component)
	{
		return component switch
		{
			CutComponent.A => "a",
			_ => "b",
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




	// 直交パッドを操作したら、レイアウトに応じた2成分をまとめて VM へ渡す。a×L は b を保って a・明度を、b×L は a を保って b・明度を設定する。固定成分(縦スライダーが司る側)は VM 側で保たれる。
	private void OnCartValuesChanged(object? sender, EventArgs e)
	{
		if (_layout == LabLayout.AlPlane)
		{
			ViewModel.SetLabALPad(LabCartPad.XValue, LabCartPad.YValue);
		}
		else
		{
			ViewModel.SetLabBLPad(LabCartPad.XValue, LabCartPad.YValue);
		}
	}




	// 直交パッドの大きさが変わったら、下地画像を作り直す。つまみ位置は PlanarPad が束縛から合わせる。パッドを初めて表示したときの寸法確定もこの経路で拾う。
	private void OnCartPadSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RegenerateCart();
	}




	// 直交パッドの固定成分(2軸に取らない側)の現在値。色制限・寸法とともに画像キャッシュの鍵に使う。a×L は b、b×L は a。
	private double CartFixedValue()
	{
		return _layout == LabLayout.AlPlane ? LabBNative() : LabANative();
	}




	// a×b 平面の下地画像を、現在の明度・副モード・色制限設定・色域外の見せ方・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・明度・設定・副モード・見せ方なら作り直さない。色域外のハッチを等間隔に保つため、表示倍率そのままの実画素解像度で生成して等倍で表示する。
	private void RegeneratePlane()
	{
		if (_cartHost)
		{
			return;
		}

		double size = AbPad.ActualWidth;

		if (size <= 0.0 || AbPad.ActualHeight <= 0.0)
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
		int spaceIndex = ViewModel.LabSpaceIndex;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;
		double lightness = ViewModel.LabL;
		int scaleIndex = ViewModel.LabAbScaleIndex;

		if (pixels == _planePixels && lightness == _planeLightness && snap == _planeSnap && spaceIndex == _planeSpaceIndex && style == _planeStyle && scaleIndex == _planeScaleIndex)
		{
			return;
		}

		_planePixels = pixels;
		_planeLightness = lightness;
		_planeSnap = snap;
		_planeSpaceIndex = spaceIndex;
		_planeStyle = style;
		_planeScaleIndex = scaleIndex;

		LchSpace space = CurrentSpace;
		AbFitMode fit = (AbFitMode)scaleIndex;

		// 平面の生成は素の明度(表示尺度 0–100 ではなく表色系の尺度)で行う。表示尺度から素の尺度へ戻して渡す。
		double nativeLightness = lightness / 100.0 * LabColor.LMax(space);

		// 最近傍探索の前計算表を UI スレッドで一度温めてから背景へ投げる。背景スレッドは構築済みの表を読むだけにして、ColorConversion 側の「表示は UI スレッドのみ」の前提を崩さない。
		ColorConversion.Snap(snap, 0, 0, 0);

		EnqueueRegen(pixels, pixels,
			() => AbPlane.ComputePixels(pixels, pixels, space, nativeLightness, snap, scale, style, fit),
			bitmap =>
			{
				PlaneImage.Source = bitmap;

				// ドラッグ中のレンズは、色域ハッチなど計算の込み入った平面を作り直さず、生成したこのビットマップをそのまま読んで映す。明度はパッドのドラッグ中一定のため、平面は据え置きで足りる。
				AbPad.LensColorSampler = new BitmapFieldSampler(bitmap, AbPad).Sample;
			});
	}




	// 直交パッド(a×L・b×L)の下地画像を、現在のレイアウト・固定成分・色制限設定・色域外の見せ方・パッドの大きさ・表示倍率に合わせて作り直す。同じ画素サイズ・固定成分・設定・副モード・見せ方なら作り直さない(レイアウト切り替え時は ApplyLayout がキャッシュを無効化する)。2軸はパッド全面に渡って変わるため鍵に含めない。
	private void RegenerateCart()
	{
		if (!_cartHost)
		{
			return;
		}

		if (LabCartPad.ActualWidth <= 0.0 || LabCartPad.ActualHeight <= 0.0)
		{
			return;
		}

		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixelWidth = (int)Math.Round(LabCartPad.ActualWidth * scale);
		int pixelHeight = (int)Math.Round(LabCartPad.ActualHeight * scale);

		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			return;
		}

		SnapSettings snap = ViewModel.CurrentSnap;
		int spaceIndex = ViewModel.LabSpaceIndex;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;
		double fixedComponent = CartFixedValue();
		int scaleIndex = ViewModel.LabAbScaleIndex;

		if (pixelWidth == _cartPixelWidth && pixelHeight == _cartPixelHeight && fixedComponent == _cartFixed && snap == _cartSnap && spaceIndex == _cartSpaceIndex && style == _cartStyle && scaleIndex == _cartScaleIndex)
		{
			return;
		}

		_cartPixelWidth = pixelWidth;
		_cartPixelHeight = pixelHeight;
		_cartFixed = fixedComponent;
		_cartSnap = snap;
		_cartSpaceIndex = spaceIndex;
		_cartStyle = style;
		_cartScaleIndex = scaleIndex;

		LchSpace space = CurrentSpace;
		LabLayout layout = _layout;
		AbFitMode fit = (AbFitMode)scaleIndex;
		double aNative = LabANative();
		double bNative = LabBNative();

		// 最近傍探索の前計算表を UI スレッドで一度温めてから背景へ投げる。背景スレッドは構築済みの表を読むだけにして、ColorConversion 側の「表示は UI スレッドのみ」の前提を崩さない。
		ColorConversion.Snap(snap, 0, 0, 0);

		// 固定成分はいま(UI スレッド)の値を捕まえて背景へ渡す。背景スレッドから ViewModel を読み直さない。
		Func<byte[]> compute = layout == LabLayout.AlPlane
			? () => AbLightnessPlane.ComputePixelsAL(pixelWidth, pixelHeight, space, bNative, snap, scale, style, fit)
			: () => AbLightnessPlane.ComputePixelsBL(pixelWidth, pixelHeight, space, aNative, snap, scale, style, fit);

		EnqueueRegen(pixelWidth, pixelHeight, compute, bitmap =>
		{
			LabCartImage.Source = bitmap;

			// ドラッグ中のレンズは、色域ハッチなど計算の込み入った下地を作り直さず、生成したこのビットマップをそのまま読んで映す。固定成分はドラッグ中一定のため、画像は据え置きで足りる。
			_cartSampler = new BitmapFieldSampler(bitmap, LabCartPad);
		});
	}




	// 直交パッドのレンズのサンプラー。生成済みの下地ビットマップをそのまま読む。パッドの外は透明を返し、レンズの縁では下地のカード色が透ける。
	private Color SampleCartField(double x, double y)
	{
		return _cartSampler?.Sample(x, y) ?? Color.FromArgb(0, 0, 0, 0);
	}




	// 指定したレイアウトの縮小見本(サムネイル)を作る。下地そのもので区別がつくため、各面はそのまま描く。固定成分は色が映える代表値(明度は最大の6割、固定の色軸は 0)を当てる。0=a×b 平面(固定明度)・1=a×L 平面(固定 b=0)・2=b×L 平面(固定 a=0)。見せ方の区別を示すための見本のため、a×b 平面はスケールの設定に依らず固定枠(AbFitMode.None)で描いてレイアウト間の比較を安定させる。
	private WriteableBitmap LayoutThumbnailFor(int index, int pixels, LchSpace space, SnapSettings snap, GamutOutOfRangeStyle style)
	{
		double lightnessRep = LabColor.LMax(space) * 0.6;

		return index switch
		{
			1 => AbLightnessPlane.CreateAL(pixels, pixels, space, 0.0, snap, 1.0, style, AbFitMode.None),
			2 => AbLightnessPlane.CreateBL(pixels, pixels, space, 0.0, snap, 1.0, style, AbFitMode.None),
			_ => AbPlane.Create(pixels, pixels, space, lightnessRep, snap, 1.0, style, AbFitMode.None),
		};
	}




	// 隅のレイアウトボタンのアイコンを、現在選んでいるレイアウトの縮小見本に差し替える。アイコン自体が現在の状態表示を兼ねる。
	private void UpdateLayoutPickerIcon()
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(24.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;
		LabLayoutPickerIcon.Source = LayoutThumbnailFor(ViewModel.LabLayoutIndex, pixels, CurrentSpace, snap, ViewModel.OutOfRangeStyle);
	}




	// レイアウト選択のフライアウトを開くときに、全レイアウトのサムネイルを今の副モード・色制限・色域外の見せ方で作り直し、現在の選択を縁取りで示す。
	private void OnLabLayoutFlyoutOpening(object sender, object e)
	{
		double scale = XamlRoot?.RasterizationScale ?? 1.0;
		int pixels = Math.Max(1, (int)Math.Round(56.0 * scale));
		SnapSettings snap = ViewModel.CurrentSnap;
		LchSpace space = CurrentSpace;
		GamutOutOfRangeStyle style = ViewModel.OutOfRangeStyle;

		LabAbPlaneThumbImage.Source = LayoutThumbnailFor(0, pixels, space, snap, style);
		LabAlPlaneThumbImage.Source = LayoutThumbnailFor(1, pixels, space, snap, style);
		LabBlPlaneThumbImage.Source = LayoutThumbnailFor(2, pixels, space, snap, style);

		var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
		var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
		int current = ViewModel.LabLayoutIndex;
		LabAbPlaneThumbBorder.BorderBrush = current == 0 ? accent : clear;
		LabAlPlaneThumbBorder.BorderBrush = current == 1 ? accent : clear;
		LabBlPlaneThumbBorder.BorderBrush = current == 2 ? accent : clear;
	}




	private void OnPickLabAbPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LabLayoutIndex = 0;
		LabLayoutPickerFlyout.Hide();
	}




	private void OnPickLabAlPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LabLayoutIndex = 1;
		LabLayoutPickerFlyout.Hide();
	}




	private void OnPickLabBlPlaneLayout(object sender, RoutedEventArgs e)
	{
		ViewModel.LabLayoutIndex = 2;
		LabLayoutPickerFlyout.Hide();
	}




	// 見せ方。AbPlane は a×b 平面+明度 L の縦バー(既定)。AlPlane=a×L 平面+b の縦バー、BlPlane=b×L 平面+a の縦バー。いずれも直交パッド。
	private enum LabLayout
	{
		AbPlane,
		AlPlane,
		BlPlane,
	}




	// 切り出して縦スライダーが司る成分。直交パッドの2次元に取らない残り1成分(a×L では b、b×L では a)。
	private enum CutComponent
	{
		A,
		B,
	}
}
