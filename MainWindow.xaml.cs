// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Irozukume.Controls;
using Irozukume.Helpers;
using Irozukume.Interop;
using Irozukume.Models;
using Irozukume.ScreenPicker;
using Irozukume.Services;
using Irozukume.ViewModels;
using Irozukume.Views;

namespace Irozukume;

// アプリのメインウィンドウ。左に色リスト(最大5色)のプレビュー、右にコマンドバー・タブ切り替え・タブ内容を並べる。トレイ操作で表示・退避する対象となる。
public sealed partial class MainWindow : Window
{
	// ウィンドウの最低サイズ (DIP)。スライダーや色プレビューが破綻しない下限で、最低サイズの強制と、保存配置を復元する際のクランプの双方で基準にする。
	public const int MinWidthDip = 720;
	public const int MinHeightDip = 560;

	// 色リストと編集中(アクティブ)の色の RGB を束ねる共有モデル。左の色プレビューと各タブのスライダーが同じ状態を参照する。保存済みの色編集状態があれば、それで初期化する。
	public ColorEditorViewModel ViewModel { get; }

	// 「形式を選択してコピー」のサブメニュー用に、アクティブな色の現在値を各形式の文字列へ整える。メニューのキャプション束縛が参照する。
	public CopyFormatsViewModel CopyFormats { get; }

	// アプリの外観設定(テーマ)を持つモデル。設定ページのテーマ選択が束縛し、変更を受けてルート要素へ即時適用する。保存済みの外観設定があれば、それで初期化する。
	public AppearanceViewModel Appearance { get; }

	// 貼り付け・コピーで取り込んだ色の履歴を持つ共有ストア。貼り付け・コピー操作からの追加と、パレットタブのコピー／貼り付け履歴の表示が同じこれを参照する。保存済みの履歴があれば、それで初期化する。
	private readonly ColorHistory _history;

	// 利用者が取っておいたお気に入りパレットの共有ストア。パレットタブの保存・一括取得操作と、設定への取り出しが同じこれを参照する。保存済みのお気に入りがあれば、それで初期化する。
	private readonly FavoritePalettes _favorites;

	// 画面カラーピッカーのセッションを取り回す。トレイ項目とツールバーのスポイトボタンの双方が同じこれを使い、多重起動は1セッションに制限する。
	private readonly ScreenColorPickerService _screenPicker = new();

	// RGB/CMYK タブの中身は共有モデルを介して状態を持つため、タブを切り替えても作り直さず使い回す。
	private RgbCmykTabView? _rgbCmykTab;

	// HSV/HSL タブの中身も共有モデルを介して状態を持つため、タブを切り替えても作り直さず使い回す。
	private HsvHslTabView? _hsvHslTab;

	// LCH タブの中身も共有モデルを介して状態を持つため、タブを切り替えても作り直さず使い回す。
	private LchTabView? _lchTab;

	// Lab タブの中身も共有モデルを介して状態を持つため、タブを切り替えても作り直さず使い回す。
	private LabTabView? _labTab;

	// YUV/YCbCr タブの中身も共有モデルを介して状態を持つため、タブを切り替えても作り直さず使い回す。
	private YuvTabView? _yuvTab;

	// Palette タブの中身も検索語や並び順の状態を持つため、タブを切り替えても作り直さず使い回す。
	private PaletteTabView? _paletteTab;

	// Mix タブの中身も共有モデルを介して状態を持つため、タブを切り替えても作り直さず使い回す。
	private MixTabView? _mixTab;
	private HarmonyTabView? _harmonyTab;

	// アルファのスライダー領域の中身。タブを跨いでタブの中身の下に常駐するため、初回に一度だけ生成して枠へ収め、表示・非表示は ShowAlpha に束ねた枠の可視性で切り替える。
	private AlphaTabView? _alphaTab;

	// スライダー・正方形を持つタブ(RGB/CMYK・HSV/HSL・YUV/YCbCr)を縦スクロール可能にするための包み。窓を低くして中身が入りきらないとき、タブ全体をスクロールで辿れるようにする。パレット・履歴は固定ヘッダと内部リストのスクロールを自前で持つため包まない。タブと同じく作り直さず使い回す。
	private ScrollViewer? _rgbCmykScroller;
	private ScrollViewer? _hsvHslScroller;
	private ScrollViewer? _lchScroller;
	private ScrollViewer? _labScroller;
	private ScrollViewer? _yuvScroller;
	private ScrollViewer? _mixScroller;
	private ScrollViewer? _harmonyScroller;

	// 最低ウィンドウサイズの番人。リサイズでスライダーや色プレビューが潰れないよう、ドラッグ縮小の下限を課す。ネイティブへ渡したサブクラスを生かし続けるためフィールドで保持する。
	private WindowMinSizer? _minSizer;

	// SelectorBar の読み込み後に選び直す、保存済みのタブ見出し。読み込み前に選択を変えても既定へ戻されるため、適用を Loaded まで保留する。適用後は null に戻す。
	private string? _pendingActiveTab;

	// コマンドバーのオーバーフロー判定中の再入を抑える札。畳む/戻すで Visibility を差し替えると SizeChanged が呼び戻されるため、判定の最中は無視する。
	private bool _updatingToolbarOverflow;

	// コントラストマトリックスの補助ウィンドウ。共有モデルを介して状態を持つため、初回に生成して以後は表示・非表示で使い回す。
	private ContrastMatrixWindow? _matrixWindow;

	// コントラストマトリックスの保存済み配置。初回生成時に適用する。保存が無ければ null で、既定のサイズで開く。
	private readonly WindowPlacement? _matrixPlacement;

	// サイドバーの色パネル(1色1枚)。色リストの並びと同じ順で持ち、リストの組み直しのたびに作り直す。イベントの発生元から位置を引くのにも使う。
	private readonly List<ColorSwatchPanel> _colorPanels = new();

	// 色リストの行高(アクティブだけ2倍)の切り替えをアニメーションで見せる補助。アクティブの移動で組み直すたびに、旧→新の高さをこれでつなぐ。
	private GridStarHeightAnimator? _rowHeightAnimator;

	// 色リストの行高(アクティブの2倍化)が変わるときのアニメーションの長さ。
	private static readonly TimeSpan RowHeightAnimationDuration = TimeSpan.FromMilliseconds(250);

	// 次の組み直しで行高アニメーションを抑制し、即座に確定するか。並べ替えではアクティブの当人は変わらず、掴んだパネルはドラッグ中すでに2倍で見えているため、ドロップ後に行高を1倍から2倍へ動かし直すと膨らみが目について不自然になる。並べ替えの直後だけ立て、その組み直しで消費する。
	private bool _suppressHeightAnimation;

	// 並べ替えドラッグ中のパネル。ドラッグしていない間は null。
	private ColorSwatchPanel? _dragPanel;

	// ドラッグ開始時点の各パネルの上端位置と高さ(色リスト座標)。ドラッグ中のはみ出し制限とドロップ先の判定に使う。
	private double[] _dragTops = Array.Empty<double>();
	private double[] _dragHeights = Array.Empty<double>();

	// 現在のドロップ先の挿入位置。ドラッグ中の移動で求め直し、変わったときだけ掴んでいないパネルの隙間を開け直す。ドラッグしていない間は -1。
	private int _dragTo = -1;

	// 掴んでいない各パネルの現在の平行移動の目標(縦)。同じ目標へ繰り返しアニメーションを掛け直さないよう控える。
	private double[] _dragGapTargets = Array.Empty<double>();

	// 隙間ずらしのアニメーションの長さ。
	private static readonly TimeSpan DragGapAnimationDuration = TimeSpan.FromMilliseconds(200);

	// OS の「アニメーションを表示する」設定。オフのときは隙間ずらしを即座に確定し、利用者の設定を尊重する。
	private static readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();




	public MainWindow(WindowPlacement? placement, EditorState? editorState, AppearanceState? appearanceState, WindowPlacement? matrixPlacement)
	{
		// x:Bind が解決される InitializeComponent より前に、保存済みの色編集状態で ViewModel を組み立てる。
		ViewModel = new ColorEditorViewModel(editorState);

		// コントラストマトリックスの保存済み配置は、初回に開くときの適用へ持ち越す。
		_matrixPlacement = matrixPlacement;

		// 貼り付けの履歴も、保存済みがあればそれで復元しておく。履歴タブの生成と貼り付け・コピー操作の双方がこのストアを参照する。
		_history = new ColorHistory(editorState?.History);

		// お気に入りパレットも、保存済みがあればそれで復元しておく。パレットタブの保存・一括取得操作と設定への取り出しが同じこのストアを参照する。
		_favorites = new FavoritePalettes(editorState?.SavedPalettes);

		// コピーのサブメニューのキャプションは、この ViewModel を介してアクティブな色の現在値を各形式の文字列で映す。
		CopyFormats = new CopyFormatsViewModel(ViewModel);

		// 外観設定も、保存済みがあればそれで復元する。設定ページのテーマ選択がこれを束縛する。
		Appearance = new AppearanceViewModel(appearanceState);

		this.InitializeComponent();
		this.Title = Loc.Get("AppName");

		// ファイルメニューの「管理者として再起動」の文言と有効状態を昇格状態へ合わせる。トレイメニューと同じ文字列・方針で、既に管理者なら現状を示して無効化する。
		if (ElevationHelper.IsElevated)
		{
			RestartAsAdminMenuItem.Text = Loc.Get("Tray_RunningAsAdmin");
			RestartAsAdminMenuItem.IsEnabled = false;
		}
		else
		{
			RestartAsAdminMenuItem.Text = Loc.Get("Tray_RestartAsAdmin");
		}

		// タスクバーと Alt+Tab に出るウィンドウアイコンを明示する。exe 埋め込みアイコンとは別に AppWindow へ当てないと、非パッケージのWinUIウィンドウは既定アイコンのままになる。ico の実体は出力フォルダへコピーしている。
		var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Irozukume.ico");
		AppWindow.SetIcon(iconPath);

		// タイトルバー左に同じ ico の絵柄を出す。XAML へは非パッケージで解決できる絶対パスを書けないため、ここで実行フォルダ基準の file URI を当てる。
		TitleBarIconImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));

		// メニュー先頭項目の文字をアイコンから TitleBarHeaderInset の位置へ揃える。MenuBarItem は内部に左パディングを持ち、その量を固定で持たないため、初回レイアウトが整った時点で実測して相殺する。
		AppTitleBar.LayoutUpdated += OnTitleBarLayoutUpdated;

		// 既定のタイトルバーを退け、アプリ内容をタイトルバー領域まで広げたうえで、自前の TitleBar コントロールをドラッグ領域として登録する。
		this.ExtendsContentIntoTitleBar = true;
		this.SetTitleBar(AppTitleBar);

		// 設定ページからの戻り(タイトルバー左の ←)を受けて本体表示へ戻す。
		AppTitleBar.BackRequested += OnTitleBarBackRequested;

		// 保存済み(無ければシステム設定)のテーマをルート要素へ適用し、以後の選択変更も購読して即時反映する。表示前に適用するためちらつかない。
		ApplyTheme();
		Appearance.ThemeChanged += OnAppearanceThemeChanged;

		// 表示言語の選択が変わったら、保存して再起動を促す。読み込み済み UI は言語を動的に差し替えられないため、反映には再起動が要る。
		Appearance.LanguageChanged += OnAppearanceLanguageChanged;

		// キャプションボタン(最小化・最大化・閉じる)はルートの RequestedTheme に追従しないため、実効テーマに合わせて配色を当てる。Default 時の OS テーマ変更にも追えるよう実効テーマの変化を購読し、初期状態も一度当てる。
		if (this.Content is FrameworkElement rootElement)
		{
			rootElement.ActualThemeChanged += OnRootActualThemeChanged;
		}

		UpdateCaptionButtonColors();

		// 保存済み配置があれば復元し、無ければ既定サイズで開く。表示前に適用することで、ちらつきなく目的の位置・サイズで現れる。
		if (placement is not null)
		{
			WindowPlacementService.Apply(this, placement, MinWidthDip, MinHeightDip);
		}
		else
		{
			WindowPlacementService.ResizeToDip(this, 960, 640);
		}

		// 最低サイズを課す。復元・既定のどちらで開いても常に効かせる。
		_minSizer = new WindowMinSizer(this, MinWidthDip, MinHeightDip);

		// 保存済みのサイドバー幅があれば、左の色プレビュー列へ絶対幅として復元する。右列は * のまま残りを占める。列の MinWidth が下限を担うため、過小な値でも潰れない。無ければ XAML 既定の比率(2:3)で開く。
		if (editorState?.SidebarWidth is double sidebarWidth && sidebarWidth > 0.0)
		{
			SidebarColumn.Width = new GridLength(sidebarWidth, GridUnitType.Pixel);
		}

		// テキストモードの切り替えでサイドバーの構成を組み替え、色リストの並び・件数・アクティブの変化でパネル一覧を組み直すための購読。ウィンドウと共有モデルは寿命が同じため、購読は解かない。保存値で始まる初期状態もここで一度反映する。
		ViewModel.PropertyChanged += OnViewModelPropertyChanged;
		ViewModel.ColorListChanged += OnColorListChanged;
		_rowHeightAnimator = new GridStarHeightAnimator(ColorListGrid);

		// テキストモードの役選択バーは、どちらの役を担うかを固定し、クリック通知の宛先を一度だけ結ぶ。中身(色の並びと選択)の反映は RebuildContrastRolePanel が担う。
		TextRoleChips.IsTextRole = true;
		BgRoleChips.IsTextRole = false;
		TextRoleChips.SelectionRequested += OnRoleChipSelectionRequested;
		BgRoleChips.SelectionRequested += OnRoleChipSelectionRequested;

		RebuildColorList();
		ApplySidebarTextMode();

		// 初期選択では SelectionChanged が発火しないため、起動時の表示内容をここで一度反映する。
		UpdateTabContent(TabSelectorBar.SelectedItem);

		// アルファのスライダー領域はタブを跨いで常駐するため、初回に一度だけ生成してタブの中身の下の枠へ収める。枠の可視性は ShowAlpha に束ねてあり、表示・非表示はそれが切り替える。
		_alphaTab = new AlphaTabView(ViewModel);
		AlphaPanelBorder.Child = _alphaTab;

		// 保存済みの隠しタブを反映する。表示/非表示は Visibility (Collapsed) で切り替え、読み込みで戻される選択とは違ってここで当てた値はそのまま残る。
		ApplyHiddenTabs(editorState?.HiddenTabs);

		// 保存済みの選択タブの復元と、隠れたタブが選ばれた状態の是正は、SelectorBar の読み込み後に Loaded でまとめて行う。構築段階で SelectedItem を変えても、読み込み時に XAML 既定 (RGB/CMYK) の選択へ戻されてしまうため、適用を Loaded まで待つ。XAML 既定で選ばれる RGB/CMYK 自体が隠れている場合もあるため、選択タブの保存有無に依らず購読する。
		_pendingActiveTab = editorState?.ActiveTab;
		TabSelectorBar.Loaded += TabSelectorBar_Loaded;

		// 「設定を開く」の Ctrl+, は、コンマ(VK_OEM_COMMA = 0xBC)に名前付きの VirtualKey が無く XAML のキー指定では書けないため、コードで登録する。他のショートカットと同じく最上位レイアウト(RootGrid)に置き、メニュー側は表記だけを担う。
		var openSettingsAccelerator = new KeyboardAccelerator
		{
			Modifiers = Windows.System.VirtualKeyModifiers.Control,
			Key = (Windows.System.VirtualKey)0xBC,
		};
		openSettingsAccelerator.Invoked += OnOpenSettingsAccelerator;
		RootGrid.KeyboardAccelerators.Add(openSettingsAccelerator);

		// ドラッグ中のルーペを最前面で描く透明オーバーレイを、このウィンドウの XamlRoot に結び付けて登録する。スライダー・色相環・パッドが自分の XamlRoot から引いてレンズをそこへ載せる。XamlRoot は読み込み後に定まるため Loaded を待つ。
		RootGrid.Loaded += OnRootGridLoaded;
	}




	// 読み込みが済んでウィンドウの XamlRoot が定まったら、レンズの最前面オーバーレイを登録する。
	private void OnRootGridLoaded(object sender, RoutedEventArgs e)
	{
		if (RootGrid.XamlRoot is not null)
		{
			LensOverlayService.Register(RootGrid.XamlRoot, LensOverlay);
		}

		// 設定オーバーレイ用 Frame の待機状態。透明な土台ページへ無遷移で移し、戻る操作(GoBack)の到達先を用意する。土台の間は本体への操作を通すため当たり判定を切る。
		SettingsFrame.Navigate(typeof(BackdropPage));
		SettingsFrame.IsHitTestVisible = false;

		// 設定の読み込みで壊れた箇所を初期値で補っていたら、その旨を一度だけ知らせる。元のファイルは settings.json.bak に退避済みである旨も伝える。
		if (SettingsStore.LastLoadWasRepaired)
		{
			_ = ShowSettingsRepairedNoticeAsync();
		}
	}




	// 設定ファイルの一部が読み込めず初期値で補って起動したことを知らせる。起動直後に一度だけ出す。別のダイアログ表示中などで出せなくても起動は妨げない。
	private async Task ShowSettingsRepairedNoticeAsync()
	{
		var dialog = new ContentDialog
		{
			XamlRoot = Content.XamlRoot,
			Title = Loc.Get("SettingsRepairedTitle"),
			Content = Loc.Get("SettingsRepairedBody"),
			CloseButtonText = Loc.Get("CommonOk"),
			DefaultButton = ContentDialogButton.Close,
		};

		try
		{
			await dialog.ShowAsync();
		}
		catch
		{
		}
	}




	// 共有モデルの変更のうち、サイドバーの構成に関わるものを受け取る。テキストモードの実効状態(トグルと色数で決まる)の変化でサイドバーを組み替える。
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorEditorViewModel.EffectiveContrastTextMode))
		{
			ApplySidebarTextMode();
		}
	}




	// サイドバーをテキストモードの実効状態に合わせて組み替える。オンのときは色リストを隠し、背景色の面とその上の役選択・テキスト欄・16進表記・コントラストパネルを見せる。オフのときは色リストへ戻す。テキスト欄とコントラストパネルの可視そのものは ContrastTextVisibility の束縛が担う。
	private void ApplySidebarTextMode()
	{
		bool textMode = ViewModel.EffectiveContrastTextMode;
		ColorListGrid.Visibility = textMode ? Visibility.Collapsed : Visibility.Visible;
		Color2Area.Visibility = textMode ? Visibility.Visible : Visibility.Collapsed;

		if (textMode)
		{
			RebuildContrastRolePanel();
		}
	}




	// 色リストの並び・件数・アクティブ・役の変化を受けて、サイドバーのパネル一覧と役選択チップを組み直す。
	private void OnColorListChanged(object? sender, EventArgs e)
	{
		RebuildColorList();
		RebuildContrastRolePanel();
	}




	// テキストモードの役選択バーへ、色リストの現在の並びと役の選択・編集フォーカスを反映する。バー側は並びが同じなら選択の移動を幅アニメーションでつなぎ、並びが変わったら組み直す。
	private void RebuildContrastRolePanel()
	{
		TextRoleChips.SetState(ViewModel.Colors, ViewModel.ContrastTextColorIndex, ViewModel.ContrastFocusIsText);
		BgRoleChips.SetState(ViewModel.Colors, ViewModel.ContrastBgColorIndex, !ViewModel.ContrastFocusIsText);
	}




	// 役選択バーのチップがクリックされたら、その役へその色を就ける。どちらの役のバーかはバーの IsTextRole で見分ける。
	private void OnRoleChipSelectionRequested(object? sender, int index)
	{
		if (sender is RoleChipBar bar)
		{
			ViewModel.SelectContrastRole(bar.IsTextRole, index);
		}
	}




	// サイドバーの色パネルを色リストの現在の並びで作り直す。1色1行で、アクティブな色の行だけ2倍の星付けにして他の2倍の高さで見せる。件数が高々5のため、差分更新はせず毎回作り直す。アクティブの移動など件数が変わらない組み直しでは、行高をいきなり切り替えず旧→新へアニメーションでつなぐ。
	private void RebuildColorList()
	{
		// 組み直し前の行高(star)を控える。件数が同じときだけ、これを開始値にして新しい高さへ補間する。並べ替えや件数の変化では補間せず即座に確定する。
		int previousRowCount = ColorListGrid.RowDefinitions.Count;
		double[] previousStars = new double[previousRowCount];
		for (int i = 0; i < previousRowCount; i++)
		{
			previousStars[i] = ColorListGrid.RowDefinitions[i].Height.Value;
		}

		ColorListGrid.Children.Clear();
		ColorListGrid.RowDefinitions.Clear();
		_colorPanels.Clear();

		// 並べ替えの直後は行高を即座に確定する(膨らみ防止)。旗はここで消費し、以後の組み直しに持ち越さない。
		bool animateHeights = previousRowCount == ViewModel.Colors.Count && !_suppressHeightAnimation;
		_suppressHeightAnimation = false;
		double[] targetStars = new double[ViewModel.Colors.Count];

		for (int i = 0; i < ViewModel.Colors.Count; i++)
		{
			SidebarColorViewModel item = ViewModel.Colors[i];
			double target = item.IsActive ? 2.0 : 1.0;
			targetStars[i] = target;

			// 件数が同じときは旧の高さから始め、この後のアニメーションで目標へ寄せる。件数が変わるときは目標で確定する。
			double start = animateHeights ? previousStars[i] : target;
			ColorListGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(start, GridUnitType.Star) });

			var panel = new ColorSwatchPanel(item);
			Grid.SetRow(panel, i);
			AutomationProperties.SetName(panel, Loc.Get("SwatchColorN", i + 1));
			panel.Activated += OnColorPanelActivated;
			panel.AlphaPreviewClicked += OnColorPanelAlphaPreviewClicked;
			panel.AddRequested += OnColorPanelAddRequested;
			panel.DeleteRequested += OnColorPanelDeleteRequested;
			panel.FavoriteRequested += OnColorPanelFavoriteRequested;
			panel.DragStarted += OnColorPanelDragStarted;
			panel.DragDelta += OnColorPanelDragDelta;
			panel.DragCompleted += OnColorPanelDragCompleted;
			panel.DragCanceled += OnColorPanelDragCanceled;
			panel.ContextMenuRequested += OnColorPanelContextMenuRequested;
			panel.HexEditCommitted += OnColorPanelHexEditCommitted;
			panel.ColorParser = text =>
			{
				bool ok = TryParseHexEditText(text, out ParsedColor parsed);
				return (ok, parsed);
			};
			ColorListGrid.Children.Add(panel);
			_colorPanels.Add(panel);
		}

		// 件数が変わらない組み直し(アクティブの移動が主)では、開始値に置いた行高を目標へアニメーションで寄せる。OS のアニメーション設定がオフのときは補助側が即座に確定する。
		if (animateHeights)
		{
			_rowHeightAnimator?.AnimateTo(targetStars, RowHeightAnimationDuration);
		}
	}




	// 色パネルのクリック。その色をアクティブ(編集対象)にする。
	private void OnColorPanelActivated(object? sender, EventArgs e)
	{
		if (sender is ColorSwatchPanel panel)
		{
			ViewModel.ActivateColor(_colorPanels.IndexOf(panel));
		}
	}




	// 色パネルの透過プレビューのクリック。その色をアクティブ(編集対象)にした上でアルファのスライダー領域を表示し、クリック1つで不透明度の編集へ入れるショートカットにする。
	private void OnColorPanelAlphaPreviewClicked(object? sender, EventArgs e)
	{
		if (sender is ColorSwatchPanel panel)
		{
			ViewModel.ActivateColor(_colorPanels.IndexOf(panel));
			ViewModel.ShowAlpha = true;
		}
	}




	// 色パネルの「下に追加」ボタン。その色の複製を直下へ追加する。
	private void OnColorPanelAddRequested(object? sender, EventArgs e)
	{
		if (sender is ColorSwatchPanel panel)
		{
			ViewModel.AddColorBelow(_colorPanels.IndexOf(panel));
		}
	}




	// 色パネルの「削除」ボタン。その色を削除する。
	private void OnColorPanelDeleteRequested(object? sender, EventArgs e)
	{
		if (sender is ColorSwatchPanel panel)
		{
			ViewModel.RemoveColor(_colorPanels.IndexOf(panel));
		}
	}




	// 色パネルの「お気に入りに追加」ボタン。その色を単体でお気に入りへ加える。
	private async void OnColorPanelFavoriteRequested(object? sender, EventArgs e)
	{
		if (sender is ColorSwatchPanel panel)
		{
			await AddSingleColorToFavoriteAsync(_colorPanels.IndexOf(panel));
		}
	}




	// 色パネルの16進ラベルの編集で色を確定した。その色をこのパネルの位置へ反映する。アクティブな色が対象なら作業値と各表色系の表示も追従する(ApplyColorToIndex が内部でアクティブ経路へ振り分ける)。
	private void OnColorPanelHexEditCommitted(object? sender, ParsedColor color)
	{
		if (sender is ColorSwatchPanel panel)
		{
			int index = _colorPanels.IndexOf(panel);

			if (index >= 0)
			{
				ViewModel.ApplyColorToIndex(index, color.R, color.G, color.B, color.HasAlpha ? color.A : (byte?)null);
			}
		}
	}




	// 色パネルの右クリック・長押し・キーボードによるコンテキストメニュー要求。その色を対象にしたコピー・貼り付け・ランダムのメニューを組み立てて表示する。位置を取れればその位置に、取れなければパネル基準で出す。
	private void OnColorPanelContextMenuRequested(object? sender, Point? pos)
	{
		if (sender is not ColorSwatchPanel panel)
		{
			return;
		}

		int index = _colorPanels.IndexOf(panel);

		if (index < 0)
		{
			return;
		}

		MenuFlyout flyout = BuildSwatchContextMenu(index);

		if (pos is Point position)
		{
			flyout.ShowAt(panel, new FlyoutShowOptions { Position = position });
		}
		else
		{
			flyout.ShowAt(panel);
		}
	}




	// 指定位置の色に対する右クリックメニューを組み立てる。編集メニューと同じ構成(コピー2つ・形式を選択してコピー2つ・貼り付け・ランダム)を、その色を対象に並べる。各コピー項目のキャプションは実際に書き出される値の生きた表示で、開いた時点の表示色(色制限の丸めを反映)を基にする。
	private MenuFlyout BuildSwatchContextMenu(int index)
	{
		Color disp = ViewModel.DisplayedColorAt(index);
		byte alpha = (byte)Math.Round(ViewModel.Colors[index].Alpha);

		var flyout = new MenuFlyout();

		MenuFlyoutItem CopyItem(string key, string caption, string? glyph = null, WebAlphaUnit? alphaUnit = null)
		{
			var item = new MenuFlyoutItem { Text = caption };
			if (glyph is not null)
			{
				item.Icon = new FontIcon { Glyph = glyph };
			}
			item.Click += (_, _) => CopyColorAsFormatFor(index, key, alphaUnit);
			return item;
		}

		// コピー(既定形式1・2)。キャプションは「コピー - …」の形で実際に書き出す値を見せる。2つ目はアルファ表記も2つ目の設定に従う。
		flyout.Items.Add(CopyItem(ViewModel.CopyFormatKey, CopyFormats.CopyCaptionFor(disp, alpha, ViewModel.CopyFormatKey), "\uE8C8"));
		flyout.Items.Add(CopyItem(ViewModel.CopyFormatKey2, CopyFormats.CopyCaptionFor(disp, alpha, ViewModel.CopyFormatKey2, ViewModel.CopyAlphaUnit2), "\uE8C8", ViewModel.CopyAlphaUnit2));

		// 形式を選択してコピー(Web)。CSS で使う形式を編集メニューと同じ並びで集める。名前付きカラーは色が CSS 名に一致したときだけ加える。
		var web = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_CopyFormatWeb") };

		void AddWeb(string key)
		{
			web.Items.Add(CopyItem(key, CopyFormats.FormatFor(disp, alpha, key)));
		}

		AddWeb("hex");
		AddWeb("hexa");
		web.Items.Add(new MenuFlyoutSeparator());
		AddWeb("rgb");
		AddWeb("rgba");
		AddWeb("rgb_modern");
		AddWeb("rgba_modern");
		AddWeb("hsl");
		AddWeb("hsla");
		AddWeb("hsl_modern");
		AddWeb("hsla_modern");
		AddWeb("hwb");
		AddWeb("hwba");
		web.Items.Add(new MenuFlyoutSeparator());
		AddWeb("oklch");
		AddWeb("oklcha");
		AddWeb("lch");
		AddWeb("lcha");
		web.Items.Add(new MenuFlyoutSeparator());
		AddWeb("oklab");
		AddWeb("oklaba");
		AddWeb("lab");
		AddWeb("laba");

		string named = CopyFormats.FormatFor(disp, alpha, "named");

		if (!string.IsNullOrEmpty(named))
		{
			web.Items.Add(new MenuFlyoutSeparator());
			AddWeb("named");
		}

		flyout.Items.Add(web);

		// 形式を選択してコピー(その他)。0x のパック値と、ターミナルの前景4種・背景4種を並べ、末尾にリセット付与のトグルを置く。ターミナル項目のキャプションは「ターミナル前景(フルカラー) - …」の形にする。
		var other = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_CopyFormat") };
		other.Items.Add(CopyItem("packed", CopyFormats.FormatFor(disp, alpha, "packed")));
		other.Items.Add(CopyItem("packeda", CopyFormats.FormatFor(disp, alpha, "packeda")));
		other.Items.Add(new MenuFlyoutSeparator());

		void AddTerm(string key)
		{
			other.Items.Add(CopyItem(key, CopyFormats.TermCaptionFor(disp, alpha, key)));
		}

		AddTerm("term_tc_fg");
		AddTerm("term_256_fg");
		AddTerm("term_16_fg");
		AddTerm("term_8_fg");
		other.Items.Add(new MenuFlyoutSeparator());
		AddTerm("term_tc_bg");
		AddTerm("term_256_bg");
		AddTerm("term_16_bg");
		AddTerm("term_8_bg");
		other.Items.Add(new MenuFlyoutSeparator());

		var reset = new ToggleMenuFlyoutItem { Text = Loc.Get("Ctx_TermReset"), IsChecked = ViewModel.TerminalResetSuffix };
		reset.Click += (_, _) => ViewModel.TerminalResetSuffix = reset.IsChecked;
		other.Items.Add(reset);

		flyout.Items.Add(other);

		flyout.Items.Add(new MenuFlyoutSeparator());

		// 貼り付け。編集対象を切り替えず、この色へクリップボードの色を反映する。
		var paste = new MenuFlyoutItem { Text = Loc.Get("Ctx_Paste"), Icon = new FontIcon { Glyph = "\uE77F" } };
		paste.Click += async (_, _) => await PasteColorToIndexAsync(index);
		flyout.Items.Add(paste);

		flyout.Items.Add(new MenuFlyoutSeparator());

		// ランダム。編集対象を切り替えず、この色を無作為な色へ差し替える。
		var random = new MenuFlyoutItem { Text = Loc.Get("Ctx_Random"), Icon = new FontIcon { FontFamily = new FontFamily("ms-appx:///Assets/Fonts/romoji.ttf#Romoji"), Glyph = "\uE005" } };
		random.Click += (_, _) => ViewModel.RandomizeColorAt(index);
		flyout.Items.Add(random);

		flyout.Items.Add(new MenuFlyoutSeparator());

		// お気に入りに追加。この色を1色だけのお気に入りパレットとして保存する。名前の初期値にこの色のカラーコードを入れたダイアログを開く。
		var favorite = new MenuFlyoutItem { Text = Loc.Get("Ctx_AddFavorite"), Icon = new FontIcon { Glyph = "\uE734" } };
		favorite.Click += async (_, _) => await AddSingleColorToFavoriteAsync(index);
		flyout.Items.Add(favorite);

		return flyout;
	}




	// 並べ替えドラッグの開始。各パネルの現在の位置と高さを記録し、ドラッグ中のパネルを前面へ出して半透明にする。ドラッグ中はレイアウトを動かさず、見た目の移動はレイアウト位置に加算される Translation だけで行う。掴んだパネルはポインタへ即時追従させ、掴んでいないパネルはドロップ先に合わせて隙間を開ける。後者の移動は MoveDragGap がアニメーションでつなぐ。
	private void OnColorPanelDragStarted(object? sender, EventArgs e)
	{
		if (sender is not ColorSwatchPanel panel)
		{
			return;
		}

		_dragPanel = panel;
		_dragTops = new double[_colorPanels.Count];
		_dragHeights = new double[_colorPanels.Count];
		_dragGapTargets = new double[_colorPanels.Count];

		for (int i = 0; i < _colorPanels.Count; i++)
		{
			GeneralTransform transform = _colorPanels[i].TransformToVisual(ColorListGrid);
			_dragTops[i] = transform.TransformPoint(new Point(0.0, 0.0)).Y;
			_dragHeights[i] = _colorPanels[i].ActualHeight;
		}

		int from = _colorPanels.IndexOf(panel);
		_dragTo = from;

		for (int i = 0; i < _colorPanels.Count; i++)
		{
			ColorSwatchPanel p = _colorPanels[i];
			ElementCompositionPreview.SetIsTranslationEnabled(p, true);
			Visual visual = ElementCompositionPreview.GetElementVisual(p);
			visual.Properties.InsertVector3("Translation", Vector3.Zero);
		}

		Canvas.SetZIndex(panel, 1);
		panel.Opacity = 0.85;
	}




	// 並べ替えドラッグ中の移動。掴んだパネルをポインタへ追従させ、色リストの上下端からはみ出さない範囲に収める。あわせて掴んだパネルの中心から現在のドロップ先を求め、変わったときだけ掴んでいないパネルの隙間を開け直してドロップ後の並びを予告する。
	private void OnColorPanelDragDelta(object? sender, double dy)
	{
		if (_dragPanel is null)
		{
			return;
		}

		int index = _colorPanels.IndexOf(_dragPanel);

		if (index < 0)
		{
			return;
		}

		double y = ClampDragOffset(index, dy);
		SetPanelTranslationY(_dragPanel, y);

		double center = _dragTops[index] + (_dragHeights[index] / 2.0) + y;
		int to = DropIndexForCenter(index, center);

		if (to != _dragTo)
		{
			_dragTo = to;
			UpdateDragGaps(index, to);
		}
	}




	// 並べ替えドラッグの確定。ドラッグ中のパネルの中心位置からドロップ先を求め、並びが変わるなら色リストへ反映する。反映すればパネル一覧が組み直され、変わらなければ見た目だけを元へ戻す。
	private void OnColorPanelDragCompleted(object? sender, double dy)
	{
		if (_dragPanel is null)
		{
			return;
		}

		int from = _colorPanels.IndexOf(_dragPanel);
		int to = from;

		if (from >= 0)
		{
			double offset = ClampDragOffset(from, dy);
			double center = _dragTops[from] + (_dragHeights[from] / 2.0) + offset;
			to = DropIndexForCenter(from, center);
		}

		ResetDragVisual();

		if (from >= 0 && to != from)
		{
			// 並べ替えによる組み直しでは行高を即座に確定し、ドロップ直後の膨らみを避ける。
			_suppressHeightAnimation = true;
			ViewModel.MoveColor(from, to);
		}
	}




	// 並べ替えドラッグの中断。見た目だけを元へ戻し、並びは変えない。
	private void OnColorPanelDragCanceled(object? sender, EventArgs e)
	{
		ResetDragVisual();
	}




	// ドラッグ中の見た目(掴んだパネルの前面表示・半透明と、各パネルの平行移動)を元へ戻し、隙間ずらしのアニメーションを止める。並べ替えを反映する場合はこの直後にパネル一覧が作り直され、各パネルは正しい位置で生まれ直す。
	private void ResetDragVisual()
	{
		if (_dragPanel is null)
		{
			return;
		}

		foreach (ColorSwatchPanel panel in _colorPanels)
		{
			Visual visual = ElementCompositionPreview.GetElementVisual(panel);
			visual.StopAnimation("Translation");
			visual.Properties.InsertVector3("Translation", Vector3.Zero);
			ElementCompositionPreview.SetIsTranslationEnabled(panel, false);
		}

		Canvas.SetZIndex(_dragPanel, 0);
		_dragPanel.Opacity = 1.0;
		_dragPanel = null;
		_dragTo = -1;
		_dragGapTargets = Array.Empty<double>();
	}




	// ドラッグ中のパネルの中心位置から、ドロップ先(挿入位置)を求める。掴んだパネルを抜いて残りを上から詰め、挿入位置 k に掴んだパネルを戻したときにその中心が来る位置を P_k として、現在の中心に最も近い k を選ぶ。中心の可動範囲(クランプ後)が P_k の取りうる範囲と一致するため、背の高いアクティブ色でも先頭(0)・末尾(末)まで届く。中心だけを他のパネルの中心と比べる方式では、背の高いパネルの中心が端へ寄り切れず両端に届かない。
	private int DropIndexForCenter(int from, double center)
	{
		double dragHeight = _dragHeights[from];
		int count = _colorPanels.Count;
		int best = 0;
		double bestDistance = double.MaxValue;
		double top = 0.0;
		int others = 0;

		for (int k = 0; k < count; k++)
		{
			double slotCenter = top + (dragHeight / 2.0);
			double distance = Math.Abs(center - slotCenter);

			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = k;
			}

			// 挿入位置 k の手前に詰まる残りパネル(掴んだパネル以外で others 番目)の高さを足し、次の挿入位置の上端へ進める。
			int otherIndex = others < from ? others : others + 1;

			if (otherIndex < count)
			{
				top += _dragHeights[otherIndex];
			}

			others++;
		}

		return best;
	}




	// ドラッグ中のパネルの移動量(縦)を、色リストの上下端からはみ出さない範囲へ収める。
	private double ClampDragOffset(int index, double dy)
	{
		double min = -_dragTops[index];
		double max = ColorListGrid.ActualHeight - (_dragTops[index] + _dragHeights[index]);
		return Math.Clamp(dy, min, max);
	}




	// パネルの Translation の縦成分を即座に設定する。掴んだパネルをポインタへ遅延なく追従させるのに使う。
	private static void SetPanelTranslationY(UIElement panel, double y)
	{
		Visual visual = ElementCompositionPreview.GetElementVisual(panel);
		visual.Properties.InsertVector3("Translation", new Vector3(0.0f, (float)y, 0.0f));
	}




	// 掴んでいない各パネルを、現在のドロップ先に合わせて上下へずらす。掴んだパネルが抜けて差し込まれることで詰まる/開く向きへ、掴んだパネルの高さぶんだけ動かし、ドロップ後の並びを先取りして見せる。目標が変わったパネルだけ動かし、同じ目標へアニメーションを掛け直さない。
	private void UpdateDragGaps(int from, int to)
	{
		for (int i = 0; i < _colorPanels.Count; i++)
		{
			if (i == from)
			{
				continue;
			}

			double target = 0.0;

			if (to > from && i > from && i <= to)
			{
				target = -_dragHeights[from];
			}
			else if (to < from && i >= to && i < from)
			{
				target = _dragHeights[from];
			}

			if (target != _dragGapTargets[i])
			{
				_dragGapTargets[i] = target;
				MoveDragGap(_colorPanels[i], target);
			}
		}
	}




	// 掴んでいないパネルを、現在値から目標値 y へ Translation のアニメーションで動かす。0番目のキーフレームを置かないことで、いまの位置から目標へ滑らかに寄せる。OS の「アニメーションを表示する」設定がオフのときは即座に確定する。
	private void MoveDragGap(UIElement panel, double y)
	{
		Visual visual = ElementCompositionPreview.GetElementVisual(panel);

		if (_uiSettings.AnimationsEnabled)
		{
			Vector3KeyFrameAnimation animation = visual.Compositor.CreateVector3KeyFrameAnimation();
			animation.InsertKeyFrame(1.0f, new Vector3(0.0f, (float)y, 0.0f));
			animation.Duration = DragGapAnimationDuration;
			visual.StartAnimation("Translation", animation);
		}
		else
		{
			visual.Properties.InsertVector3("Translation", new Vector3(0.0f, (float)y, 0.0f));
		}
	}




	// タブ代わりのボタンリストで選択が変わったら、対応する内容をタブ領域へ差し替える。
	private void TabSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
	{
		UpdateTabContent(sender.SelectedItem);
	}




	// 選択中のタブの識別子(Tag)に応じて、対応する内容をタブ領域へ差し替える。識別子は表示テキストと分離してあり、表示が言語で変わっても判定は変わらない。
	private void UpdateTabContent(SelectorBarItem? selectedItem)
	{
		var key = selectedItem?.Tag as string ?? "";

		if (key == "rgbcmyk")
		{
			if (_rgbCmykTab is null)
			{
				_rgbCmykTab = new RgbCmykTabView(ViewModel);
				_rgbCmykScroller = WrapInScroller(_rgbCmykTab);
			}

			TabContent.Content = _rgbCmykScroller;
			return;
		}

		if (key == "hsvhsl")
		{
			if (_hsvHslTab is null)
			{
				_hsvHslTab = new HsvHslTabView(ViewModel);
				_hsvHslScroller = WrapInScroller(_hsvHslTab);
			}

			TabContent.Content = _hsvHslScroller;
			return;
		}

		if (key == "lch")
		{
			if (_lchTab is null)
			{
				_lchTab = new LchTabView(ViewModel);
				_lchScroller = WrapInScroller(_lchTab);
			}

			TabContent.Content = _lchScroller;
			return;
		}

		if (key == "lab")
		{
			if (_labTab is null)
			{
				_labTab = new LabTabView(ViewModel);
				_labScroller = WrapInScroller(_labTab);
			}

			TabContent.Content = _labScroller;
			return;
		}

		if (key == "yuvycbcr")
		{
			if (_yuvTab is null)
			{
				_yuvTab = new YuvTabView(ViewModel);
				_yuvScroller = WrapInScroller(_yuvTab);
			}

			TabContent.Content = _yuvScroller;
			return;
		}

		if (key == "palette")
		{
			_paletteTab ??= new PaletteTabView(ViewModel, _history, _favorites);
			TabContent.Content = _paletteTab;
			return;
		}

		if (key == "mix")
		{
			if (_mixTab is null)
			{
				_mixTab = new MixTabView(ViewModel);
				_mixScroller = WrapInScroller(_mixTab);
			}

			TabContent.Content = _mixScroller;
			return;
		}

		if (key == "harmony")
		{
			if (_harmonyTab is null)
			{
				_harmonyTab = new HarmonyTabView(ViewModel);
				_harmonyScroller = WrapInScroller(_harmonyTab);
			}

			TabContent.Content = _harmonyScroller;
			return;
		}

		TabContent.Content = new TextBlock
		{
			Text = Loc.Get("Tab_FallbackFormat", key),
			FontSize = 16,
		};
	}




	// スライダー・正方形を持つタブの中身を縦スクロール可能にして使い回す。窓を低くして中身が入りきらないとき、タブ全体をスクロールで辿れるようにする。横スクロールは出さない。色相環・色差平面を持つタブは、この包みのビューポート高さを基準に正方形を縮める。ドラッグ時の拡大つまみ(ルーペ)は最前面オーバーレイへ描かれ、このスクロール領域には切り抜かれないため、逃げ場の余白は要らない。
	private static ScrollViewer WrapInScroller(UIElement content)
	{
		return new ScrollViewer
		{
			Content = content,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollMode = ScrollMode.Disabled,
		};
	}




	// SelectorBar の読み込みが済んでから、保存済みの選択タブを復元する。トレイ退避からの再表示で再度読み込まれても上書きしないよう、購読は一度きりで解く。
	private void TabSelectorBar_Loaded(object sender, RoutedEventArgs e)
	{
		TabSelectorBar.Loaded -= TabSelectorBar_Loaded;
		RestoreActiveTab(_pendingActiveTab);
		_pendingActiveTab = null;

		// 復元先が無い・隠れているなどで選択が定まらないときは、表示中の先頭タブへ寄せる。XAML 既定で選ばれる RGB/CMYK が隠されている場合に、隠れたタブが選ばれたまま残るのを防ぐ。
		if (TabSelectorBar.SelectedItem is not SelectorBarItem current || current.Visibility != Visibility.Visible)
		{
			SelectFirstVisibleTab();
		}
	}




	// 保存済みのタブ識別子(Tag)に一致する項目を選び直す。一致が無い・未指定なら XAML 既定の選択のままにする。表示テキストで記録された古い設定からも復元できるよう、識別子へ読み替えてから照合する。
	private void RestoreActiveTab(string? tabKey)
	{
		if (string.IsNullOrEmpty(tabKey))
		{
			return;
		}

		SelectTabByTag(MigrateLegacyTabKey(tabKey));
	}




	// 識別子(Tag)が一致するタブを選ぶ。一致が無ければ何もしない。隠れているタブへは切り替えない(貼り付けによる自動切り替えで、利用者が隠したタブへ移らないようにする)。SelectedItem を変えると SelectionChanged を介して内容も差し替わるが、既に選択中の項目を指定したときは変化が生じず差し替えも起きない。
	private void SelectTabByTag(string tag)
	{
		foreach (SelectorBarItem item in TabSelectorBar.Items)
		{
			if (item.Tag as string == tag)
			{
				if (item.Visibility == Visibility.Visible)
				{
					TabSelectorBar.SelectedItem = item;
				}

				return;
			}
		}
	}




	// SelectorBar の指定位置のタブを選ぶ。範囲外なら何もしない。位置はタブの並び順そのままで、隠れたタブも数に含める(Ctrl+数字とツールチップの対応を保つ)。指定先が隠れているときは何もしない。Ctrl+数字のタブ切り替えが使う。
	private void SelectTabByIndex(int index)
	{
		if (index < 0 || index >= TabSelectorBar.Items.Count)
		{
			return;
		}

		if (TabSelectorBar.Items[index] is SelectorBarItem item && item.Visibility == Visibility.Visible)
		{
			TabSelectorBar.SelectedItem = item;
		}
	}




	// 現在の選択から delta だけ離れたタブへ、端で巻き戻る循環で移る。隠れたタブは飛ばし、表示中のタブだけを巡る。Ctrl+Tab / Ctrl+Shift+Tab が使う。
	private void StepTab(int delta)
	{
		int count = TabSelectorBar.Items.Count;

		if (count == 0)
		{
			return;
		}

		int current = TabSelectorBar.SelectedItem is SelectorBarItem selected ? TabSelectorBar.Items.IndexOf(selected) : 0;

		// 最大 count 回だけ進め、最初に出会った表示中のタブへ移る。最低1枚は表示するため、必ず見つかる。
		for (int i = 0; i < count; i++)
		{
			current = ((current + delta) % count + count) % count;

			if (TabSelectorBar.Items[current] is SelectorBarItem item && item.Visibility == Visibility.Visible)
			{
				TabSelectorBar.SelectedItem = item;
				return;
			}
		}
	}




	// 保存済みの隠しタブを反映する。一覧に挙がった識別子(Tag)のタブを Collapsed にして隠す。万一すべてのタブが対象でも、最低1枚は表示するため先頭のタブだけは隠さず残す。
	private void ApplyHiddenTabs(IReadOnlyList<string>? hiddenTags)
	{
		if (hiddenTags is null || hiddenTags.Count == 0)
		{
			return;
		}

		var hidden = new HashSet<string>(hiddenTags);

		foreach (SelectorBarItem item in TabSelectorBar.Items)
		{
			if (item.Tag is string tag && hidden.Contains(tag))
			{
				item.Visibility = Visibility.Collapsed;
			}
		}

		// すべて隠してしまった設定からの復帰。先頭タブを表示へ戻し、必ず1枚は見えるようにする。
		if (CountVisibleTabs() == 0 && TabSelectorBar.Items.Count > 0 && TabSelectorBar.Items[0] is SelectorBarItem first)
		{
			first.Visibility = Visibility.Visible;
		}
	}




	// 現在隠しているタブの識別子(Tag)の一覧を、保存用に取り出す。表示状態の真実は各タブの Visibility が持つため、別に状態を抱えずここから組み立てる。1枚も隠していなければ null を返し、設定にキー自体を残さない。
	private List<string>? CaptureHiddenTabs()
	{
		var hidden = new List<string>();

		foreach (SelectorBarItem item in TabSelectorBar.Items)
		{
			if (item.Visibility != Visibility.Visible && item.Tag is string tag && tag.Length > 0)
			{
				hidden.Add(tag);
			}
		}

		return hidden.Count > 0 ? hidden : null;
	}




	// 識別子(Tag)が一致するタブを返す。一致が無ければ null。表示メニューの項目とタブを Tag で結ぶために使う。
	private SelectorBarItem? FindTabByTag(string tag)
	{
		foreach (SelectorBarItem item in TabSelectorBar.Items)
		{
			if (item.Tag as string == tag)
			{
				return item;
			}
		}

		return null;
	}




	// 表示中のタブの枚数を数える。最後の1枚を隠せないようにする判定に使う。
	private int CountVisibleTabs()
	{
		int count = 0;

		foreach (SelectorBarItem item in TabSelectorBar.Items)
		{
			if (item.Visibility == Visibility.Visible)
			{
				count++;
			}
		}

		return count;
	}




	// 表示中の先頭のタブを選ぶ。隠したタブが選ばれたままになったときの寄せ先に使う。
	private void SelectFirstVisibleTab()
	{
		foreach (SelectorBarItem item in TabSelectorBar.Items)
		{
			if (item.Visibility == Visibility.Visible)
			{
				TabSelectorBar.SelectedItem = item;
				return;
			}
		}
	}




	// タブバーの右クリックメニューを開く直前に、各項目のチェック状態を現在の表示/非表示へ合わせる。表示中が1枚だけのときは、その最後の1枚を隠せないよう当該項目を無効にする。「全タブを表示」はすべて表示済みのとき押す意味がないので無効にする。
	private void OnTabVisibilityMenuOpening(object sender, object e)
	{
		if (sender is not MenuFlyout flyout)
		{
			return;
		}

		int visibleCount = CountVisibleTabs();

		foreach (MenuFlyoutItemBase element in flyout.Items)
		{
			if (element is ToggleMenuFlyoutItem item && item.Tag is string tag)
			{
				bool visible = FindTabByTag(tag)?.Visibility == Visibility.Visible;
				item.IsChecked = visible;
				item.IsEnabled = !(visible && visibleCount <= 1);
			}
		}

		ShowAllTabsItem.IsEnabled = visibleCount < TabSelectorBar.Items.Count;
	}




	// タブバーの右クリックメニューの項目クリック。チェックの状態に合わせて対応するタブを表示/非表示する。最後の1枚は隠さず、隠したのが選択中のタブなら表示中の別タブへ選択を移す。
	private void OnTabVisibilityToggle(object sender, RoutedEventArgs e)
	{
		if (sender is not ToggleMenuFlyoutItem item || item.Tag is not string tag)
		{
			return;
		}

		SelectorBarItem? tab = FindTabByTag(tag);

		if (tab is null)
		{
			return;
		}

		if (item.IsChecked)
		{
			tab.Visibility = Visibility.Visible;
			return;
		}

		// 最後の1枚は隠さない。チェックを元へ戻して何もしない(メニューを開く際の無効化と二重の歯止め)。
		if (CountVisibleTabs() <= 1)
		{
			item.IsChecked = true;
			return;
		}

		tab.Visibility = Visibility.Collapsed;

		// 隠したのが今選んでいるタブなら、表示中の別タブへ選択を移して中身を差し替える。
		if (ReferenceEquals(TabSelectorBar.SelectedItem, tab))
		{
			SelectFirstVisibleTab();
		}
	}




	// タブバーの右クリックメニューの「全タブを表示」。隠していたタブをすべて表示へ戻す。選択中のタブは表示のまま残るため、選択の移し替えは要らない。すべて表示済みのときはメニューを開く際に無効化してあり、ここへは来ない。
	private void OnShowAllTabsClick(object sender, RoutedEventArgs e)
	{
		foreach (SelectorBarItem item in TabSelectorBar.Items)
		{
			item.Visibility = Visibility.Visible;
		}
	}




	// Ctrl+1～Ctrl+9 の割り当て。押された数字に対応する位置のタブを選ぶ。テキスト入力中でもタブは切り替えられるよう、フォーカスに依らず受け取る。
	private void OnSelectTabAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		SelectTabByIndex((int)sender.Key - (int)Windows.System.VirtualKey.Number1);
	}




	// Ctrl+Tab の割り当て。次のタブへ循環で進む。
	private void OnNextTabAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		StepTab(1);
	}




	// Ctrl+Shift+Tab の割り当て。前のタブへ循環で戻る。
	private void OnPrevTabAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		StepTab(-1);
	}




	// 表示メニューの「次のタブ」。次のタブへ循環で進む。
	private void OnNextTabMenuClick(object sender, RoutedEventArgs e)
	{
		StepTab(1);
	}




	// 表示メニューの「前のタブ」。前のタブへ循環で戻る。
	private void OnPrevTabMenuClick(object sender, RoutedEventArgs e)
	{
		StepTab(-1);
	}




	// 設定ファイルに表示テキスト形式で記録された選択タブを、現在の Tag 識別子へ読み替える。Tag 形式の値はそのまま返す。両形式を受け付けることで、表示テキストで保存された設定からでも選択タブを復元できる。履歴タブはパレットタブのコピー／貼り付け履歴へ統合したため、表示テキスト・Tag のどちらで履歴が保存されていてもパレットタブへ寄せる。
	private static string MigrateLegacyTabKey(string value)
	{
		return value switch
		{
			"RGB/CMYK" => "rgbcmyk",
			"HSV/HSL" => "hsvhsl",
			"YUV/YCbCr" => "yuvycbcr",
			"Palette" => "palette",
			"履歴" => "palette",
			"history" => "palette",
			_ => value,
		};
	}




	// 永続用のエディタ状態を組み立てる。色の各値は ViewModel が、選択中のタブは SelectorBar が持つため、双方を抱えるこのウィンドウでまとめて取り出す。
	public EditorState CaptureEditorState()
	{
		var state = ViewModel.CaptureState();
		state.ActiveTab = TabSelectorBar.SelectedItem?.Tag as string;
		state.HiddenTabs = CaptureHiddenTabs();
		state.History = _history.Capture();
		state.SavedPalettes = _favorites.Capture();

		// サイドバー(左の色プレビュー列)の現在の表示幅を覚えておき、次回起動で復元する。まだレイアウトされておらず幅が定まらないときは記録しない。
		double sidebarWidth = SidebarColumn.ActualWidth;

		if (sidebarWidth > 0.0)
		{
			state.SidebarWidth = sidebarWidth;
		}

		return state;
	}




	// 永続用の外観設定を取り出す。テーマは Appearance が持つため、それを通して取り出す。
	public AppearanceState CaptureAppearanceState()
	{
		return Appearance.CaptureState();
	}




	// ファイルメニューの「閉じる」。×ボタンと同じくアプリは終了させず、ウィンドウをトレイへ退避させる。常駐は続き、トレイ操作で再表示できる。
	private void OnCloseMenuClick(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).HideWindowToTray();
	}




	// Ctrl+W の割り当て。×ボタン・メニューの「閉じる」と同じく、ウィンドウをトレイへ退避させる。ウィンドウ全体の操作のため、テキスト入力中でもその入力欄へ譲らず受け取る。
	private void OnCloseAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		((App)Application.Current).HideWindowToTray();
	}




	// メインメニューの「終了」。トレイメニューの「終了」と同じ App.ExitApplication を呼び、トレイアイコンを片付けてからプロセスを終了する。
	private void OnExitMenuClick(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).ExitApplication();
	}




	// ファイルメニューの「管理者として再起動」。トレイメニューの同項目と同じ App.RestartAsAdministrator を呼び、UAC を経て昇格セッションへ移る。
	private void OnRestartAsAdminClick(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).RestartAsAdministrator();
	}




	// メニューの「設定を開く」とツールバーの「設定」ボタンの共用ハンドラ。
	private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
	{
		OpenSettings();
	}




	// ツールバー・メニューの「お気に入りに追加」。
	private async void OnAddFavoriteClick(object sender, RoutedEventArgs e)
	{
		await AddCurrentColorsToFavoriteAsync();
	}




	// Ctrl+D の割り当て。お気に入りに追加する。テキスト入力中でも受け取れるよう最上位レイアウトで拾う。
	private void OnAddFavoriteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		_ = AddCurrentColorsToFavoriteAsync();
	}




	// 現在のサイドバーの色を1つのお気に入りパレットとして保存する。名前は既定の連番を初期値に出し、利用者が編集できる。保存したら共有ストアへ加え、Palette タブのお気に入りリストへ即時に反映する(タブを開いていれば行が増える)。不意の終了で失わないよう、終了を待たず設定ファイルへも書き出す。色が空のときは保存しない。
	private async Task AddCurrentColorsToFavoriteAsync()
	{
		string defaultName = Loc.Get("Favorite_DefaultNameFormat", _favorites.Items.Count + 1);
		string? name = await PromptFavoriteNameAsync(Loc.Get("Favorite_SaveDialogTitle"), defaultName);

		if (name is null)
		{
			return;
		}

		var colors = new List<FavoriteColor>();

		foreach ((byte r, byte g, byte b, byte a) in ViewModel.CaptureSidebarColors())
		{
			colors.Add(new FavoriteColor(r, g, b, a));
		}

		if (_favorites.Add(name, colors) is null)
		{
			return;
		}

		((App)Application.Current).PersistSettings();
	}




	// 指定位置の色を1色だけのお気に入りパレットとして保存する。名前はその色のカラーコードを初期値に出し、全選択した状態で渡すため、そのまま確定すればカラーコードが名前になり、打ち替えれば即座に消える。確定したら共有ストアへ加え、Palette タブのお気に入りリストへ即時に反映する。不意の終了で失わないよう、終了を待たず設定ファイルへも書き出す。位置が範囲外のときは何もしない。
	private async Task AddSingleColorToFavoriteAsync(int index)
	{
		if (index < 0 || index >= ViewModel.Colors.Count)
		{
			return;
		}

		SidebarColorViewModel item = ViewModel.Colors[index];
		byte r = item.Rgb.R;
		byte g = item.Rgb.G;
		byte b = item.Rgb.B;
		byte a = (byte)Math.Round(item.Alpha);

		string defaultName = $"#{r:X2}{g:X2}{b:X2}";
		string? name = await PromptFavoriteNameAsync(Loc.Get("Favorite_SaveDialogTitle"), defaultName);

		if (name is null)
		{
			return;
		}

		if (_favorites.Add(name, new List<FavoriteColor> { new FavoriteColor(r, g, b, a) }) is null)
		{
			return;
		}

		((App)Application.Current).PersistSettings();
	}




	// お気に入りの名前を入力するダイアログを出し、確定された名前を返す。取り消したときや空白だけのときは null を返す。初期値は全選択した状態で出し、すぐ書き換えられるようにする。
	private async Task<string?> PromptFavoriteNameAsync(string title, string initialName)
	{
		var textBox = new TextBox
		{
			Text = initialName,
			SelectionStart = 0,
			SelectionLength = initialName.Length,
		};

		var dialog = new ContentDialog
		{
			XamlRoot = Content.XamlRoot,
			Title = title,
			Content = textBox,
			PrimaryButtonText = Loc.Get("Favorite_DialogSave"),
			CloseButtonText = Loc.Get("Favorite_DialogCancel"),
			DefaultButton = ContentDialogButton.Primary,
		};

		if (await dialog.ShowAsync() != ContentDialogResult.Primary)
		{
			return null;
		}

		string name = textBox.Text.Trim();
		return name.Length == 0 ? null : name;
	}




	// Ctrl+, の割り当て。設定ページを開く。テキスト入力中でも設定は開けるよう、フォーカスに依らず受け取る。
	private void OnOpenSettingsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		OpenSettings();
	}




	// 本体の上へ設定ページを重ねて切り替え、メニューバーを見出しに差し替えてタイトルバー左に ← を出す。設定 UI は宿主ページへ載せて Frame でナビゲートし、組み込みの NavigationThemeTransition で現す。本体は背後に残し、設定面(不透明)が覆うことで隠す。
	private void OpenSettings()
	{
		// 既に設定表示中なら何もしない。土台から設定への一回ぶんだけ進め、戻り先を一段に保つ。
		if (SettingsFrame.CanGoBack)
		{
			return;
		}

		// XAML 要素はナビゲーションの引数で WinRT 境界を越えられず渡せないため、引数では渡さず、ナビゲート後に宿主ページへ差し込む。設定 UI は閉じる際に破棄されるため、開くたびに作る。
		var settings = new SettingsView(ViewModel, Appearance);

		SettingsFrame.IsHitTestVisible = true;
		SettingsFrame.Navigate(typeof(SettingsHostPage), null, new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
		(SettingsFrame.Content as SettingsHostPage)?.SetContent(settings);

		AppMenuBar.Visibility = Visibility.Collapsed;
		SettingsHeaderText.Visibility = Visibility.Visible;
		AppTitleBar.IsBackButtonVisible = true;

		// ページ遷移に合わせて見出しの入れ替えも動かす。アイコンは据え置きのアンカーのため動かさない。
		PlayTitleBarHeaderEntrance(SettingsHeaderText);
	}




	// タイトルバー左の ← で本体表示へ戻す。
	private void OnTitleBarBackRequested(TitleBar sender, object args)
	{
		CloseSettings();
	}




	// 設定ページを閉じ、本体表示へ戻す。トレイ退避からの再表示で常に色ピッカーへ戻れるよう、閉じる前に App からも呼ばれる。既に本体表示なら何もしない。戻る(GoBack)で設定面が NavigationThemeTransition の逆再生で退き、背後の本体が現れる。
	public void CloseSettings()
	{
		if (!SettingsFrame.CanGoBack)
		{
			return;
		}

		SettingsFrame.GoBack();

		// 設定面が退く間も本体を操作できるよう、当たり判定を本体側へ戻す。
		SettingsFrame.IsHitTestVisible = false;

		AppMenuBar.Visibility = Visibility.Visible;
		SettingsHeaderText.Visibility = Visibility.Collapsed;
		AppTitleBar.IsBackButtonVisible = false;

		// 戻る際も見出しをメニューへ入れ替えるため、本体への復帰に合わせてメニューを同じ動きで現す。
		PlayTitleBarHeaderEntrance(AppMenuBar);
	}




	// タイトルバーの見出しをアイコンから揃える基準。タイトルバー設計指針の「タイトルはアイコンから16px」に合わせる。
	private const double TitleBarHeaderInset = 16.0;




	// メニュー位置の補正を一度行ったか。初回レイアウトで一度だけ測って当てれば足りる。
	private bool _titleBarMenuAligned;




	// 初回レイアウトが整った時点で一度だけメニュー位置を補正し、以後は購読を解く。
	private void OnTitleBarLayoutUpdated(object? sender, object e)
	{
		if (TryAlignTitleBarMenu())
		{
			AppTitleBar.LayoutUpdated -= OnTitleBarLayoutUpdated;
		}
	}




	// メニュー先頭項目の文字がアイコン右端から TitleBarHeaderInset の位置へ来るよう、メニュー全体の左マージンで補正する。レイアウト未確定(幅0)や要素が未実体化のときは false を返し、次のレイアウトで再試行する。
	private bool TryAlignTitleBarMenu()
	{
		if (_titleBarMenuAligned)
		{
			return true;
		}

		if (TitleBarIcon.ActualWidth <= 0 || AppMenuBar.ActualWidth <= 0)
		{
			return false;
		}

		if (FindFirstDescendant<TextBlock>(AppMenuBar) is not TextBlock firstLabel)
		{
			return false;
		}

		var labelLeft = firstLabel.TransformToVisual(TitleBarIcon).TransformPoint(new Point(0, 0)).X;
		var currentGap = labelLeft - TitleBarIcon.ActualWidth;
		AppMenuBar.Margin = new Thickness(AppMenuBar.Margin.Left + (TitleBarHeaderInset - currentGap), 0, 0, 0);
		_titleBarMenuAligned = true;
		return true;
	}




	// 視覚ツリーを先行順に辿り、最初に見つかる指定型の子孫を返す。無ければ null。
	private static T? FindFirstDescendant<T>(DependencyObject root) where T : class
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
			{
				return match;
			}

			if (FindFirstDescendant<T>(child) is T nested)
			{
				return nested;
			}
		}

		return null;
	}




	// タイトルバーの見出し(メニュー⇆設定見出し)を入れ替える際、ページ遷移に合わせて軽くフェードと上送りで現す。アイコンは据え置きのアンカーのため動かさない。
	private static void PlayTitleBarHeaderEntrance(FrameworkElement element)
	{
		var translate = new TranslateTransform { Y = 8.0 };
		element.RenderTransform = translate;

		var duration = new Duration(TimeSpan.FromMilliseconds(200));
		var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

		var fade = new DoubleAnimation { From = 0.0, To = 1.0, Duration = duration, EasingFunction = ease };
		Storyboard.SetTarget(fade, element);
		Storyboard.SetTargetProperty(fade, "Opacity");

		var slide = new DoubleAnimation { From = 8.0, To = 0.0, Duration = duration, EasingFunction = ease };
		Storyboard.SetTarget(slide, translate);
		Storyboard.SetTargetProperty(slide, "Y");

		var storyboard = new Storyboard();
		storyboard.Children.Add(fade);
		storyboard.Children.Add(slide);
		storyboard.Begin();
	}




	// タイトルバー左のアイコンをクリックしたら、標準アプリと同じくウィンドウのシステムメニュー(元のサイズに戻す・移動・サイズ変更・最小化・最大化・閉じる)を出す。カスタムタイトルバーではシステム既定アイコンが持つこの挙動が無いため、明示的に提示する。
	private void OnTitleBarIconClick(object sender, RoutedEventArgs e)
	{
		ShowSystemMenu();
	}




	// システムメニューをアイコンの左下へ出す。要素位置は DIP で得られるため現在 DPI で物理ピクセル化し、クライアント座標からスクリーン座標へ変換してメッセージへ渡す。項目の有効/無効はシステムが現在のウィンドウ状態に合わせる。
	private void ShowSystemMenu()
	{
		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

		// アイコンの左下を基準にし、その真下へメニューを開く。
		var origin = TitleBarIcon.TransformToVisual(this.Content).TransformPoint(new Point(0, TitleBarIcon.ActualHeight));
		uint dpi = NativeMethods.GetDpiForWindow(hwnd);
		double scale = dpi == 0 ? 1.0 : dpi / 96.0;

		var point = new NativeMethods.POINT
		{
			X = (int)Math.Round(origin.X * scale),
			Y = (int)Math.Round(origin.Y * scale),
		};
		NativeMethods.ClientToScreen(hwnd, ref point);

		// lParam は下位ワードに X、上位ワードに Y を詰める。
		var lParam = (IntPtr)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
		NativeMethods.PostMessage(hwnd, NativeMethods.WM_POPUPSYSTEMMENU, IntPtr.Zero, lParam);
	}




	// 外観設定のテーマをウィンドウのルート要素へ適用する。ルートに RequestedTheme を設定すると、配下のタブ・設定ページを含む UI 全体が一斉に再テーマ化される。Default はシステム設定に従う。
	private void ApplyTheme()
	{
		if (this.Content is FrameworkElement root)
		{
			root.RequestedTheme = Appearance.Theme;
		}
	}




	// テーマ選択が変わったら、ルート要素へ即時適用する。
	private void OnAppearanceThemeChanged(object? sender, EventArgs e)
	{
		ApplyTheme();
	}




	// 表示言語が変わったら、選択を保存し再起動を促す。読み込み済みの UI は言語を動的に差し替えられないため、新しい言語の反映には再起動が要る。再起動を選べば即座に、選ばなければ次回起動時に反映する。
	private async void OnAppearanceLanguageChanged(object? sender, EventArgs e)
	{
		((App)Application.Current).PersistSettings();

		var dialog = new ContentDialog
		{
			XamlRoot = Content.XamlRoot,
			Title = Loc.Get("Restart_Title"),
			Content = Loc.Get("Restart_Message"),
			PrimaryButtonText = Loc.Get("Restart_Now"),
			CloseButtonText = Loc.Get("Restart_Later"),
			DefaultButton = ContentDialogButton.Primary,
		};

		ContentDialogResult result = await dialog.ShowAsync();

		if (result == ContentDialogResult.Primary)
		{
			((App)Application.Current).RestartApplication();
		}
	}




	// タイトルバーのキャプションボタン(最小化・最大化・閉じる)の前景色を、現在の実効テーマに合わせる。ExtendsContentIntoTitleBar 下でもこれらはシステムのキャプションボタンのままで、ルート要素の RequestedTheme では追従しないため、AppWindow のタイトルバー越しに配色を当てて視認性を保つ。背景はシステム既定(閉じるボタンの赤いホバー等)に委ねる。
	private void UpdateCaptionButtonColors()
	{
		if (this.Content is not FrameworkElement root)
		{
			return;
		}

		bool dark = root.ActualTheme == ElementTheme.Dark;
		Color foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
		Color inactiveForeground = dark ? Color.FromArgb(0xFF, 0x77, 0x77, 0x77) : Color.FromArgb(0xFF, 0x99, 0x99, 0x99);

		var titleBar = AppWindow.TitleBar;
		titleBar.ButtonForegroundColor = foreground;
		titleBar.ButtonHoverForegroundColor = foreground;
		titleBar.ButtonPressedForegroundColor = foreground;
		titleBar.ButtonInactiveForegroundColor = inactiveForeground;
	}




	// 実効テーマが変わったら(設定での選択変更、または Default 時の OS テーマ変更)、キャプションボタンの配色も追従させる。
	private void OnRootActualThemeChanged(FrameworkElement sender, object args)
	{
		UpdateCaptionButtonColors();
	}




	// 役選択パネルの入れ替えボタン。文字色と背景色の役を入れ替える。
	private void OnSwapRolesClick(object sender, RoutedEventArgs e)
	{
		ViewModel.SwapContrastRoles();
	}




	// 表示メニューの「コントラストマトリックス」。全色×全色の比の一覧を別ウィンドウで開く。
	private void OnContrastMatrixMenuClick(object sender, RoutedEventArgs e)
	{
		OpenContrastMatrix();
	}




	// Ctrl+M の割り当て。コントラストマトリックスを開く。テキスト入力中でも開けるよう、フォーカスに依らず受け取る。
	private void OnContrastMatrixAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		args.Handled = true;
		OpenContrastMatrix();
	}




	// コントラストマトリックスのウィンドウを表示して前面へ出す。初回に生成して保存済みの配置を適用し、以後は同じウィンドウを再表示する。
	private void OpenContrastMatrix()
	{
		_matrixWindow ??= new ContrastMatrixWindow(ViewModel, Appearance, _matrixPlacement);
		_matrixWindow.ShowAndActivate();
	}




	// コントラストマトリックスのウィンドウを隠す。トレイ退避時に App から呼ばれ、メインウィンドウと一緒に画面から消える。
	public void HideContrastMatrix()
	{
		_matrixWindow?.HideWindow();
	}




	// コントラストマトリックスの現在の配置を取り出す。このセッションで一度も開いていなければ null を返し、呼び出し側は保存済みの配置を温存する。隠れている間もウィンドウは生きているため、最後に見えていた位置・寸法が捕捉できる。
	public WindowPlacement? CaptureContrastMatrixPlacement()
	{
		return _matrixWindow is null ? null : WindowPlacementService.Capture(_matrixWindow);
	}




	// X の割り当て。テキスト入力にフォーカスがあるときはその入力欄への文字入力に譲るため受け取らない。テキストモードが表示されている間だけ、文字色と背景色の役の入れ替えとして受け取る。
	private void OnSwapRolesAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (IsTextInputFocused() || !ViewModel.EffectiveContrastTextMode)
		{
			args.Handled = false;
			return;
		}

		args.Handled = true;
		ViewModel.SwapContrastRoles();
	}




	// A の割り当て。テキスト入力にフォーカスがあるときはその入力欄への文字入力(16進欄の A–F 等)に譲るため受け取らない。それ以外では「実際の色で表示」を切り替える。
	private void OnToggleActualColorAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (IsTextInputFocused())
		{
			args.Handled = false;
			return;
		}

		args.Handled = true;
		ViewModel.ShowActualColor = !ViewModel.ShowActualColor;
	}




	// L の割り当て。テキスト入力にフォーカスがあるときはその入力欄への文字入力に譲るため受け取らない。それ以外では色制限の解除と直前モードへの復帰を切り替える。
	private void OnToggleLimitAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (IsTextInputFocused())
		{
			args.Handled = false;
			return;
		}

		args.Handled = true;
		ViewModel.ToggleLimit();
	}




	// 色メニューの「黒白」。文字色の役を黒、背景色の役を白にする。コントラスト確認(テキストモード)でないときは何もしない。
	private void OnBlackWhiteMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.SetBlackAndWhite();
	}




	// 色メニューの「すべての色を不透明にする」。全色の不透明度を最大へ揃え、RGB成分は変えない。
	private void OnMakeOpaqueMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.MakeAllOpaque();
	}




	// 色メニューの「完全ランダム」。全色を独立に完全ランダムな色へ変更する。1色ずつの操作は各色パネルの右クリックメニューが担うため、こちらは全色を一度に振る。
	private void OnRandomMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.RandomizeAllColors();
	}




	// 色メニューの「明度を揃えてランダム」。全色を、共有する明度のもとで色相・彩度を無作為に振る。
	private void OnRandomLightnessMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.RandomizeAllUniformLightness();
	}




	// 色メニューの「彩度を揃えてランダム」。全色を、共有する彩度のもとで明度・色相を無作為に振る。
	private void OnRandomChromaMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.RandomizeAllUniformChroma();
	}




	// 色メニューの「色相を揃えてランダム」。全色を、共有する色相のもとで明度・彩度を無作為に振る。
	private void OnRandomHueMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.RandomizeAllSingleHue();
	}




	// 色メニューの「コントラストを確保してランダム」。テキストモードで、全色をランダムにしつつ文字色・背景色の役が WCAG AA を満たすようにする。
	private void OnRandomContrastMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.RandomizeAllContrastSafe();
	}




	// 色覚シミュレーション副メニューの「全て解除」。P型・D型・T型・1色覚の4トグルをまとめてオフにする。
	private void OnVisionClearAllClick(object sender, RoutedEventArgs e)
	{
		ViewModel.ShowProtan = false;
		ViewModel.ShowDeutan = false;
		ViewModel.ShowTritan = false;
		ViewModel.ShowMonochromacy = false;
	}




	// コマンドバー全体の幅が変わったとき、表示レンズ系のボタン群が収まるか測り直す。
	private void ToolbarContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateToolbarOverflow();
	}




	// 操作系・表示レンズ系それぞれのボタン群の寸法が変わったとき(キャプションの表示切り替えなど)に、収まり判定をやり直す。
	private void ToolbarGroup_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateToolbarOverflow();
	}




	// 表示レンズ系のボタン群を、横幅に収まる範囲で1つずつ畳む/戻す(段階的オーバーフロー)。… ボタンに近い右端(色覚→色制限→アルファの順)から先に畳み、畳んだものだけを「…」のメニューへ出す。インラインのボタンとメニュー項目は VM の同じプロパティを双方向束縛するため、どちらから操作しても状態は一致する。畳む数を 0 から増やしながら都度測り、最初に収まった数で確定する。可視性の差し替えは同じレイアウト処理内で済ませ、間に描画を挟まないためちらつきは出ない。差し替えが SizeChanged を呼び戻すので、再入を _updatingToolbarOverflow で抑える。
	private void UpdateToolbarOverflow()
	{
		if (_updatingToolbarOverflow)
		{
			return;
		}

		if (ToolbarContentGrid is null || ToolbarLeftGroup is null || ToolbarRightGroup is null || ToolbarOverflowButton is null)
		{
			return;
		}

		if (InlineAlpha is null || InlineLimit is null || InlineVision is null
			|| OverflowAlphaItem is null || OverflowLimitItem is null || OverflowVisionItem is null)
		{
			return;
		}

		double available = ToolbarContentGrid.ActualWidth;
		if (available <= 0)
		{
			return;
		}

		_updatingToolbarOverflow = true;
		try
		{
			Size infinity = new(double.PositiveInfinity, double.PositiveInfinity);

			ToolbarLeftGroup.Measure(infinity);
			double leftWidth = ToolbarLeftGroup.DesiredSize.Width;

			// … ボタンに近い右端から先に畳む。各表示レンズのインライン表現とメニュー表現を対にして、畳む順に並べる。
			(FrameworkElement Inline, MenuFlyoutItemBase Menu)[] order =
			{
				(InlineVision, OverflowVisionItem),
				(InlineLimit, OverflowLimitItem),
				(InlineAlpha, OverflowAlphaItem),
			};

			// 左群・右群・… ボタンの幅の合計に少しの余白を足して、収まるかを判定する。
			const double margin = 12;

			for (int overflowCount = 0; overflowCount <= order.Length; overflowCount++)
			{
				for (int i = 0; i < order.Length; i++)
				{
					bool overflowed = i < overflowCount;
					order[i].Inline.Visibility = overflowed ? Visibility.Collapsed : Visibility.Visible;
					order[i].Menu.Visibility = overflowed ? Visibility.Visible : Visibility.Collapsed;
				}

				ToolbarOverflowButton.Visibility = overflowCount > 0 ? Visibility.Visible : Visibility.Collapsed;

				ToolbarRightGroup.Measure(infinity);
				double rightWidth = ToolbarRightGroup.DesiredSize.Width;

				double overflowWidth = 0;
				if (overflowCount > 0)
				{
					ToolbarOverflowButton.Measure(infinity);
					overflowWidth = ToolbarOverflowButton.DesiredSize.Width;
				}

				if (leftWidth + rightWidth + overflowWidth + margin <= available)
				{
					break;
				}
			}
		}
		finally
		{
			_updatingToolbarOverflow = false;
		}
	}




	// 編集メニューの「元に戻す」。色の状態を一段戻す。
	private void OnUndoMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.Undo();
	}




	// 編集メニューの「やり直し」。戻した色の状態を一段やり直す。
	private void OnRedoMenuClick(object sender, RoutedEventArgs e)
	{
		ViewModel.Redo();
	}




	// Ctrl+Z の割り当て。テキスト入力にフォーカスがあるときはその入力欄の元に戻すに譲るため受け取らない。それ以外では色の状態を一段戻す。
	private void OnUndoAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (IsTextInputFocused())
		{
			args.Handled = false;
			return;
		}

		args.Handled = true;
		ViewModel.Undo();
	}




	// Ctrl+Y の割り当て。テキスト入力にフォーカスがあるときはその入力欄のやり直しに譲るため受け取らない。それ以外では色の状態を一段やり直す。
	private void OnRedoAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (IsTextInputFocused())
		{
			args.Handled = false;
			return;
		}

		args.Handled = true;
		ViewModel.Redo();
	}




	// 編集メニューの「貼り付け」。クリップボードの文字列を色として解釈し、アクティブな色へ反映して履歴へ積む。
	private async void OnPasteMenuClick(object sender, RoutedEventArgs e)
	{
		await PasteColorFromClipboardAsync();
	}




	// Ctrl+V の割り当て。テキスト入力にフォーカスがあるときは、その入力欄への文字の貼り付けに譲るため受け取らない。それ以外では色の貼り付けとして受け取る。
	private void OnPasteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (IsTextInputFocused())
		{
			args.Handled = false;
			return;
		}

		args.Handled = true;
		_ = PasteColorFromClipboardAsync();
	}




	// テキストを編集できる入力欄にフォーカスがあるか。元に戻す・やり直し・コピー・貼り付けのショートカットを、その入力欄の文字操作や文字入力に譲るかどうかの判断に使う。
	private bool IsTextInputFocused()
	{
		object? focused = FocusManager.GetFocusedElement(Content.XamlRoot);
		return focused is TextBox or PasswordBox or RichEditBox;
	}




	// クリップボードの文字列を色として読み取り、解釈する。テキストが無い・色として解釈できない・クリップボードを読めないときは ok を偽で返す。インデックス色の逆引きには現在の参照テーマを使う。
	private async Task<(bool Ok, ParsedColor Color, string Text)> ReadClipboardColorAsync()
	{
		string? text;

		try
		{
			DataPackageView view = Clipboard.GetContent();

			if (!view.Contains(StandardDataFormats.Text))
			{
				return (false, default, string.Empty);
			}

			text = await view.GetTextAsync();
		}
		catch
		{
			// 他のアプリがクリップボードを握っている等で読めないときは何もしない。
			return (false, default, string.Empty);
		}

		if (!TryParseColorText(text, out ParsedColor color))
		{
			return (false, default, string.Empty);
		}

		return (true, color, text!);
	}




	// 文字列を色として解釈する共通経路。まず CSS 形式(#hex・0x・rgb() 等・名前付き)として解釈し、当てはまらなければ ANSI エスケープ列(ターミナルの色指定)として解釈する。インデックス色の逆引きには現在の参照テーマを使う。クリップボードの貼り付けと色パネルの16進編集で共通の解釈にする。
	private bool TryParseColorText(string? text, out ParsedColor color)
	{
		return ColorStringParser.TryParse(text, out color)
			|| TerminalEscapeParser.TryParse(text, ViewModel.CurrentSnap.Theme, out color);
	}




	// 色パネルの16進ラベルの編集での解釈。共通経路に加え、先頭の # を省いた裸の16進に備えて # を補って再度試す。小さな編集欄へ素の16進を打ち込む用途のための補完で、クリップボードの貼り付けでは語(face・beef 等の正しい16進になる単語)を色と誤解しないよう行わない。
	private bool TryParseHexEditText(string text, out ParsedColor color)
	{
		if (TryParseColorText(text, out color))
		{
			return true;
		}

		string trimmed = text.Trim();

		if (trimmed.Length > 0 && !trimmed.StartsWith('#') && !trimmed.Contains('('))
		{
			return ColorStringParser.TryParse("#" + trimmed, out color);
		}

		return false;
	}




	// クリップボードの文字列を色として解釈し、アクティブな色(と、不透明度が明示されていればアルファ)へ反映して履歴へ積む。テキストが無い・色として解釈できない・クリップボードを読めないときは何もしない。
	private async Task PasteColorFromClipboardAsync()
	{
		(bool ok, ParsedColor color, string text) = await ReadClipboardColorAsync();

		if (!ok)
		{
			return;
		}

		try
		{
			// hwb() 由来なら、正規化オフのとき貼った白み・黒み(退化域=和>1 を含む)をそのまま保てるよう専用経路で反映する。lch()/oklch() 由来なら、色域制限オフのとき貼った明度・彩度・色相(色域外を含む)をそのまま保てるよう専用経路で反映する。それ以外は RGB で反映する。
			if (color.IsHwb)
			{
				ViewModel.ApplyPastedHwb(color.Hue, color.Whiteness, color.Blackness, color.HasAlpha ? color.A : (byte?)null);
			}
			else if (color.IsLch)
			{
				ViewModel.ApplyPastedLch(color.LchSpace, color.LchL, color.LchC, color.LchH, color.HasAlpha ? color.A : (byte?)null);
			}
			else if (color.IsLab)
			{
				ViewModel.ApplyPastedLab(color.LchSpace, color.LabL, color.LabA, color.LabB, color.HasAlpha ? color.A : (byte?)null);
			}
			else
			{
				ViewModel.ApplyColor(color.R, color.G, color.B, color.HasAlpha ? color.A : (byte?)null);
			}

			_history.AddPaste(color.R, color.G, color.B, color.A, color.HasAlpha, text.Trim());

			// 反映した色の書式に合わせて、対応するタブ(と HSV/HSL の副モード)を前面にする。
			SwitchTabForPastedColor(color.Format);
		}
		catch
		{
			// 色の反映や履歴追加で想定外の例外が起きても、メニュー・Ctrl+V の貼り付け経路は結果を待ち受けないため、ここで止めてアプリを落とさない。
		}
	}




	// ツールバーのスポイトボタン。画面カラーピッカーを開く。
	private void OnPickScreenColorClick(object sender, RoutedEventArgs e)
	{
		_ = PickScreenColorAsync();
	}




	// 画面カラーピッカーを開き、利用者が確定したらその色をアクティブな色へ反映して履歴へ積む。中止・採色不能のときは何もしない。確定して反映したかを返し、トレイからの呼び出しはその結果でウィンドウを前面に出すかを決める。クリップボードの貼り付けと同じ配線で色を流す。
	public async Task<bool> PickScreenColorAsync()
	{
		PickedColor? picked = await _screenPicker.PickAsync();

		if (picked is null)
		{
			return false;
		}

		PickedColor c = picked.Value;

		try
		{
			ViewModel.ApplyColor(c.R, c.G, c.B, null);
			_history.AddPick(c.R, c.G, c.B, 255, false);
		}
		catch
		{
			// 色の反映や履歴追加で想定外の例外が起きても、アプリを落とさない。
		}

		return true;
	}




	// クリップボードの文字列を色として解釈し、指定位置の色へ反映して履歴へ積む。サイドバーの色パネルの右クリックメニューが、編集対象を切り替えずにその色へ貼り付けるのに使う。アクティブな色が対象なら、作業値の追従や貼り付けタブ連動が働くよう通常の貼り付け経路へ委譲する。それ以外は RGB と不透明度だけを反映し、タブは切り替えない。
	private async Task PasteColorToIndexAsync(int index)
	{
		if (index == ViewModel.ActiveColorIndex)
		{
			await PasteColorFromClipboardAsync();
			return;
		}

		(bool ok, ParsedColor color, string text) = await ReadClipboardColorAsync();

		if (!ok)
		{
			return;
		}

		try
		{
			ViewModel.ApplyColorToIndex(index, color.R, color.G, color.B, color.HasAlpha ? color.A : (byte?)null);
			_history.AddPaste(color.R, color.G, color.B, color.A, color.HasAlpha, text.Trim());
		}
		catch
		{
			// 色の反映や履歴追加で想定外の例外が起きても、メニューの貼り付け経路は結果を待ち受けないため、ここで止めてアプリを落とさない。
		}
	}




	// 貼り付けた色の書式に応じて、対応するタブ(と HSV/HSL・LCH・Lab の副モード)を前面にする。連動が無効なら何もしない。書式と切替先の対応は次の通り: hwb() は HSV/HSL タブ+HWB 副モード、hsl()/hsla() は HSV/HSL タブ+HSL 副モード、oklch() は LCH タブ+OKLCH 副モード、lch() は LCH タブ+CIE LCH 副モード、oklab() は Lab タブ+OKLab 副モード、lab() は Lab タブ+CIE Lab 副モード、rgb()/rgba()・#hex・ANSI トゥルーカラーは RGB/CMYK タブ、transparent はアルファのスライダー領域を表示。名前付きカラー・ANSI インデックス色は切替先が自明でないため切り替えない。タブが既に前面でも副モードの指定は効く。
	private void SwitchTabForPastedColor(ColorSourceFormat format)
	{
		if (!ViewModel.SwitchTabOnPaste)
		{
			return;
		}

		switch (format)
		{
			case ColorSourceFormat.Hwb:
				SelectTabByTag("hsvhsl");
				_hsvHslTab?.ShowHwbMode();
				break;

			case ColorSourceFormat.Hsl:
				SelectTabByTag("hsvhsl");
				_hsvHslTab?.ShowHslMode();
				break;

			case ColorSourceFormat.Oklch:
				SelectTabByTag("lch");
				_lchTab?.ShowOklchMode();
				break;

			case ColorSourceFormat.Lch:
				SelectTabByTag("lch");
				_lchTab?.ShowLchMode();
				break;

			case ColorSourceFormat.Oklab:
				SelectTabByTag("lab");
				_labTab?.ShowOklabMode();
				break;

			case ColorSourceFormat.Lab:
				SelectTabByTag("lab");
				_labTab?.ShowLabMode();
				break;

			case ColorSourceFormat.Rgb:
			case ColorSourceFormat.Hex:
			case ColorSourceFormat.TerminalTrueColor:
				SelectTabByTag("rgbcmyk");
				break;

			case ColorSourceFormat.Transparent:
				ViewModel.ShowAlpha = true;
				break;
		}
	}




	// 「形式を選択してコピー」の項目。選んだ形式でアクティブな色をクリップボードへ書き出す。どの形式かは項目の Tag で判別する。
	private void OnCopyFormatClick(object sender, RoutedEventArgs e)
	{
		if (sender is not MenuFlyoutItem item || item.Tag is not string key)
		{
			return;
		}

		CopyColorAsFormat(key);
	}




	// 編集メニューの「コピー」。設定で選んだ既定の形式でアクティブな色をコピーする。
	private void OnCopyMenuClick(object sender, RoutedEventArgs e)
	{
		CopyColorAsFormat(ViewModel.CopyFormatKey);
	}




	// Ctrl+C の割り当て。テキスト入力にフォーカスがあるときは、その入力欄の文字コピーに譲るため受け取らない。それ以外では設定で選んだ既定の形式でのアクティブな色のコピーとして受け取る。
	private void OnCopyAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (IsTextInputFocused())
		{
			args.Handled = false;
			return;
		}

		args.Handled = true;
		CopyColorAsFormat(ViewModel.CopyFormatKey);
	}




	// 編集メニューの2つ目の「コピー」。設定で選んだ2つ目の形式とそのアルファ表記でアクティブな色をコピーする。
	private void OnCopyMenu2Click(object sender, RoutedEventArgs e)
	{
		CopyColorAsFormat(ViewModel.CopyFormatKey2, ViewModel.CopyAlphaUnit2);
	}




	// Ctrl+Shift+C の割り当て。テキスト入力にフォーカスがあるときは、その入力欄の文字コピーに譲るため受け取らない。それ以外では設定で選んだ2つ目の形式とそのアルファ表記でのアクティブな色のコピーとして受け取る。
	private void OnCopy2Accelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (IsTextInputFocused())
		{
			args.Handled = false;
			return;
		}

		args.Handled = true;
		CopyColorAsFormat(ViewModel.CopyFormatKey2, ViewModel.CopyAlphaUnit2);
	}




	// 指定の形式でアクティブな色をクリップボードへ書き出し、コピー履歴へ積む。alphaUnit を渡すとその表記でアルファを書き出し、null のときは既定(1つ目のコピー形式)の表記を使う。
	private void CopyColorAsFormat(string key, WebAlphaUnit? alphaUnit = null)
	{
		(string text, bool hasAlpha, byte r, byte g, byte b, byte a) = CopyFormats.Resolve(key, alphaUnit);
		WriteColorToClipboard(text, hasAlpha, r, g, b, a);
	}




	// 指定の形式で指定位置の色をクリップボードへ書き出し、コピー履歴へ積む。サイドバーの色パネルの右クリックメニューが、編集対象を切り替えずにその色をコピーするのに使う。基にする表示色は色制限の丸めを反映した表示色を渡す。alphaUnit を渡すとその表記でアルファを書き出し、null のときは既定(1つ目のコピー形式)の表記を使う。
	private void CopyColorAsFormatFor(int index, string key, WebAlphaUnit? alphaUnit = null)
	{
		Color disp = ViewModel.DisplayedColorAt(index);
		byte alpha = (byte)Math.Round(ViewModel.Colors[index].Alpha);
		(string text, bool hasAlpha, byte r, byte g, byte b, byte a) = CopyFormats.ResolveFor(disp, alpha, key, alphaUnit);
		WriteColorToClipboard(text, hasAlpha, r, g, b, a);
	}




	// コピー文字列をクリップボードへ書き出し、コピー履歴へ積む。クリップボードへ書き込めなければ履歴にも残さない。文字列にならない形式 (色が CSS 名に一致しない名前付きカラー) のときは何もしない。
	private void WriteColorToClipboard(string text, bool hasAlpha, byte r, byte g, byte b, byte a)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		try
		{
			var package = new DataPackage();
			package.SetText(text);
			Clipboard.SetContent(package);
		}
		catch
		{
			// クリップボードへ書き込めないときは履歴にも残さず何もしない。
			return;
		}

		// 常駐アプリのため、コピー後に本体を終了してから貼り付ける流れがありうる。SetContent だけでは内容がこのプロセスに委ねられたままで、終了するとクリップボードから失われる。Flush で内容を確定させ、終了後も残るようにする。書き込み自体は済んでいるため、確定に失敗しても続行する。
		try
		{
			Clipboard.Flush();
		}
		catch
		{
		}

		// 履歴への追加で想定外の例外が起きても、コピー自体は済んでいるためアプリを落とさず無視する。
		try
		{
			_history.AddCopy(r, g, b, a, hasAlpha, text);
		}
		catch
		{
		}
	}
}
