// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.ComponentModel;
using System.Numerics;
using System.Text;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Irozukume.Helpers;
using Irozukume.ViewModels;

namespace Irozukume.Controls;

// サイドバーの色1件分のパネル。丸め反映済みのスウォッチ・右4分の1の透過プレビュー・16進表記を見せ、パネル上部へポインタを寄せた間だけ左上に横並びのツールボタン(削除・下に追加)を出す。
// パネル本体のポインタ操作は押下後の移動量でクリック(アクティブ化)とドラッグ(並べ替え)を見分け、結果をイベントで親へ通知する。並べ替えの見た目と確定は親(MainWindow)が行う。
public sealed partial class ColorSwatchPanel : UserControl
{
	// ドラッグとみなす縦の移動量(DIP)。これ未満で離せばクリック(アクティブ化)として扱う。
	private const double DragThreshold = 6.0;

	// ツールボタンを見せる上端の高さ(DIP)。
	private const double ToolZoneHeight = 48.0;

	// 内容を切り抜く角丸の半径。Root の CornerRadius (8) から枠線の太さ (1) を引いた値で、外枠の角丸の内側に揃える。
	private const float ContentCornerRadius = 7.0f;

	// 縦キャプションの上の余白(DIP)。Canvas の上端に貼り付かないよう少し空ける。
	private const double LabelTopMargin = 8.0;

	// このパネルが表示する色の項目。表示ブラシ・16進表記・ボタンの可否を束縛する。
	public SidebarColorViewModel Item { get; }

	// パネル本体のクリック(アクティブ化)の通知。
	public event EventHandler? Activated;

	// 透過プレビューのクリックの通知。親がアクティブ化した上でアルファタブへ移す。
	public event EventHandler? AlphaPreviewClicked;

	// 「下に追加」ボタンの通知。
	public event EventHandler? AddRequested;

	// 「削除」ボタンの通知。
	public event EventHandler? DeleteRequested;

	// 「お気に入りに追加」ボタンの通知。この色を単体でお気に入りへ加える。
	public event EventHandler? FavoriteRequested;

	// 並べ替えドラッグの開始(押下後の移動量がしきい値を超えた)の通知。
	public event EventHandler? DragStarted;

	// 並べ替えドラッグ中の移動の通知。値は押下位置からの縦の移動量(ウィンドウ座標)。
	public event EventHandler<double>? DragDelta;

	// 並べ替えドラッグの確定(ポインタを離した)の通知。値は押下位置からの縦の移動量。
	public event EventHandler<double>? DragCompleted;

	// 並べ替えドラッグの中断(キャプチャの喪失・システムによる取り消し)の通知。
	public event EventHandler? DragCanceled;

	// 右クリック・長押し・キーボードによるコンテキストメニューの要求の通知。値はパネル本体(Root)を基準とした表示位置で、位置を取れないキーボード起動のときは null。親がこの色に対する右クリックメニューを組み立てて表示する。
	public event EventHandler<Point?>? ContextMenuRequested;

	// 16進ラベルの編集フライアウトで色を確定した通知。値は解釈済みの色。親がこの色をこのパネルの位置へ反映する。
	public event EventHandler<ParsedColor>? HexEditCommitted;

	// 16進ラベルの編集で入力文字列を色へ解釈する関数。テーマ依存のターミナルのエスケープ列を含む解釈は親(MainWindow)が参照テーマを持つため、解釈は親へ委ね、ここは成否と結果を受け取るだけにする。親が設定する。未設定のときは常に解釈失敗とする。
	public Func<string, (bool Ok, ParsedColor Color)>? ColorParser { get; set; }

	// 押下中(クリックかドラッグかの判定中、またはドラッグ中)か。
	private bool _pressed;

	// ドラッグ(並べ替え)中か。
	private bool _dragging;

	// 押下位置の縦座標(ウィンドウ座標)。移動量の基準にする。
	private double _pressY;




	public ColorSwatchPanel(SidebarColorViewModel item)
	{
		Item = item;
		InitializeComponent();

		ApplyVerticalLabels();
		UpdateVisionColumns();

		Item.Protan.PropertyChanged += OnVisionRowPropertyChanged;
		Item.Deutan.PropertyChanged += OnVisionRowPropertyChanged;
		Item.Tritan.PropertyChanged += OnVisionRowPropertyChanged;
		Item.Monochromacy.PropertyChanged += OnVisionRowPropertyChanged;

		MainAlphaChecker.SizeChanged += OnAlphaCheckerSizeChanged;
		ProtanAlphaChecker.SizeChanged += OnAlphaCheckerSizeChanged;
		DeutanAlphaChecker.SizeChanged += OnAlphaCheckerSizeChanged;
		TritanAlphaChecker.SizeChanged += OnAlphaCheckerSizeChanged;
		MonochromacyAlphaChecker.SizeChanged += OnAlphaCheckerSizeChanged;
	}




	// 内容全体を Root の角丸に合わせて切り抜く。Border の CornerRadius は子要素の塗りを切り抜かないため、透過プレビューの市松や色覚シミュレーションの行の塗りが角からはみ出さないよう、大きさが変わるたびにクリップを作り直す。
	private void OnContentRootSizeChanged(object sender, SizeChangedEventArgs e)
	{
		Visual visual = ElementCompositionPreview.GetElementVisual(ContentRoot);
		CompositionRoundedRectangleGeometry clipGeometry = visual.Compositor.CreateRoundedRectangleGeometry();
		clipGeometry.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
		clipGeometry.CornerRadius = new Vector2(ContentCornerRadius, ContentCornerRadius);
		visual.Clip = visual.Compositor.CreateGeometricClip(clipGeometry);
	}




	// 色覚列の表示トグルが変わったら、その列の幅を出し入れする。市松の横位相は、幅の変化に伴うアルファ帯のセルの SizeChanged で取り直す。
	private void OnVisionRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorVisionRowViewModel.RowVisibility))
		{
			UpdateVisionColumns();
		}
	}




	// 列幅の星(*)比の調整つまみ。主色列の取り分と、表示中の色覚列1つあたりの取り分。
	// 色覚列を太くしたいときは VisionColumnStars を上げ、主色を広く保ちたいときは MainColumnStars を上げる。
	// XAML の ColumnDefinition の Width は起動時に UpdateVisionColumns が全列を上書きするため、実質の幅はこの2つで決まる。
	private const double MainColumnStars = 5.5;
	private const double VisionColumnStars = 1.0;




	// 主色列と色覚の各列の幅を、表示トグルに合わせて出し入れする。主色列は常に MainColumnStars の星幅、色覚列はオフなら幅0に畳んでレイアウトから消し、オンなら VisionColumnStars の星幅に戻す。
	private void UpdateVisionColumns()
	{
		MainColumn.Width = new GridLength(MainColumnStars, GridUnitType.Star);
		ProtanColumn.Width = VisionColumnWidth(Item.Protan);
		DeutanColumn.Width = VisionColumnWidth(Item.Deutan);
		TritanColumn.Width = VisionColumnWidth(Item.Tritan);
		MonochromacyColumn.Width = VisionColumnWidth(Item.Monochromacy);
	}




	// 表示中なら色覚列の星幅(VisionColumnStars)、隠れているなら幅0を返す。
	private static GridLength VisionColumnWidth(ColorVisionRowViewModel row)
	{
		return row.RowVisibility == Visibility.Visible ? new GridLength(VisionColumnStars, GridUnitType.Star) : new GridLength(0.0);
	}




	// アルファ帯のいずれかのセルの幅が変わったら、各色覚の透過プレビューの市松の横位相を取り直す。
	private void OnAlphaCheckerSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RecomputeAlphaCheckerPhase();
	}




	// アルファ帯の各市松の横位相を取り直し、升の並びを左の主色の透過プレビューの市松から右へ切れ目なく繋げる。位相は各セルの左端の、帯の左端(主色のセルの左端)からの距離。主色のセルは帯の原点のため位相0で、各色覚のセルへ左から順に幅を足し込む。隠れている色覚列は幅0のため位相を進めない。
	private void RecomputeAlphaCheckerPhase()
	{
		double phase = MainAlphaChecker.ActualWidth;
		phase = ApplyCheckerPhaseX(ProtanAlphaChecker, phase);
		phase = ApplyCheckerPhaseX(DeutanAlphaChecker, phase);
		phase = ApplyCheckerPhaseX(TritanAlphaChecker, phase);
		ApplyCheckerPhaseX(MonochromacyAlphaChecker, phase);
	}




	// 1つの市松へ横位相を与え、次のセルの位相(この市松の右端)を返す。隠れている市松は位相を進めない。
	private static double ApplyCheckerPhaseX(CheckerboardPanel checker, double phase)
	{
		if (checker.Visibility == Visibility.Collapsed)
		{
			return phase;
		}

		checker.PhaseX = phase;
		return phase + checker.ActualWidth;
	}




	// 色覚列のキャプションの向きを言語に合わせて整える。漢字・かなを含む日本語は1文字ずつ改行して縦書きにし、それ以外(英語等)は文字列を90度回転して縦に倒す。言語の切り替えは再起動で反映するため、x:Uid で読み込み済みの文字列をここで一度整えれば足りる。
	private void ApplyVerticalLabels()
	{
		ApplyVerticalLabel(ProtanLabel);
		ApplyVerticalLabel(DeutanLabel);
		ApplyVerticalLabel(TritanLabel);
		ApplyVerticalLabel(MonochromacyLabel);
	}




	// 1つのキャプションを縦向きにする。日本語は1文字ずつ改行で縦に積む。それ以外(英語等)は左上角を軸に時計回りに90度回転して縦に倒し、上から読み下す形にする。実際の配置(横中央寄せ・上揃え)は、Canvas の大きさが定まってから PositionVerticalLabel が行う。
	private static void ApplyVerticalLabel(TextBlock label)
	{
		if (ContainsJapanese(label.Text))
		{
			label.Text = ToVerticalText(label.Text);
			return;
		}

		label.RenderTransform = new RotateTransform { Angle = 90.0, CenterX = 0.0, CenterY = 0.0 };
	}




	// 色覚列のキャプションを載せた Canvas の大きさが定まったら、その中の TextBlock を配置する。Canvas は子を無限幅で測るため、長い英語でも回転前に切り詰められず、回転後にパネル高を超えた分だけ下端で切れる。
	private void OnLabelCanvasSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (sender is Canvas canvas && canvas.Children.Count > 0 && canvas.Children[0] is TextBlock label)
		{
			PositionVerticalLabel(canvas, label);
		}
	}




	// 縦キャプションを Canvas 上に置く。回転済み(英語等)は、左上角を軸に時計回り90度回転した結果が列の横中央へ来るよう左を決め、上端から余白ぶん下げて下へ読み下させる。回転していない縦書き(日本語)は、積んだ文字列を横中央・上端へ置く。
	private static void PositionVerticalLabel(Canvas canvas, TextBlock label)
	{
		label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
		double textWidth = label.DesiredSize.Width;
		double textHeight = label.DesiredSize.Height;
		double canvasWidth = canvas.ActualWidth;

		if (label.RenderTransform is RotateTransform)
		{
			// +90度回転で文字列は左方向(幅=textHeight)へ倒れて下へ伸びる。回転後の帯を横中央へ寄せるぶん右へずらし、上端から余白ぶん下げて置く。
			Canvas.SetLeft(label, (canvasWidth / 2.0) + (textHeight / 2.0));
			Canvas.SetTop(label, LabelTopMargin);
		}
		else
		{
			Canvas.SetLeft(label, (canvasWidth - textWidth) / 2.0);
			Canvas.SetTop(label, LabelTopMargin);
		}
	}




	// 漢字・ひらがな・カタカナのいずれかを含むか。縦書き(1文字ずつ改行)と90度回転のどちらにするかの判定に使う。
	private static bool ContainsJapanese(string text)
	{
		foreach (Rune rune in text.EnumerateRunes())
		{
			int value = rune.Value;
			bool hiragana = value is >= 0x3040 and <= 0x309F;
			bool katakana = value is >= 0x30A0 and <= 0x30FF;
			bool kanji = value is >= 0x4E00 and <= 0x9FFF;

			if (hiragana || katakana || kanji)
			{
				return true;
			}
		}

		return false;
	}




	// 文字列の各文字の間に改行を挟み、縦積み表示用の文字列にする。サロゲートペアを割らないよう Rune 単位で区切る。
	private static string ToVerticalText(string text)
	{
		var builder = new StringBuilder();

		foreach (Rune rune in text.EnumerateRunes())
		{
			if (builder.Length > 0)
			{
				builder.Append('\n');
			}

			builder.Append(rune.ToString());
		}

		return builder.ToString();
	}




	// パネル本体の押下。ポインタをつかまえて移動量の計測を始める。クリックかドラッグかはこの後の移動量で見分ける。ボタン類(透過プレビュー・ツールボタン)の上の押下は各ボタンが処理済みにするため、ここへは来ない。
	private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
	{
		if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse
			&& !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
		{
			return;
		}

		if (!Root.CapturePointer(e.Pointer))
		{
			return;
		}

		_pressed = true;
		_dragging = false;
		_pressY = e.GetCurrentPoint(null).Position.Y;
		e.Handled = true;
	}




	// ポインタの移動。パネル上部にカーソルがある間だけツールボタンを見せる。押下中は縦の移動量を測り、しきい値を超えたらドラッグ(並べ替え)を始めて親へ通知する。
	private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
	{
		UpdateToolButtonsVisibility(e);

		if (!_pressed)
		{
			return;
		}

		double dy = e.GetCurrentPoint(null).Position.Y - _pressY;

		if (!_dragging)
		{
			if (Math.Abs(dy) < DragThreshold)
			{
				return;
			}

			_dragging = true;
			ToolButtons.Visibility = Visibility.Collapsed;
			DragStarted?.Invoke(this, EventArgs.Empty);
		}

		DragDelta?.Invoke(this, dy);
		e.Handled = true;
	}




	// ポインタを離した。ドラッグ中なら並べ替えの確定、そうでなければクリック(アクティブ化)として親へ通知する。キャプチャの解放が PointerCaptureLost を呼ぶため、先に押下状態を畳んでから解放する。
	private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (!_pressed)
		{
			return;
		}

		double dy = e.GetCurrentPoint(null).Position.Y - _pressY;
		bool dragged = _dragging;
		_pressed = false;
		_dragging = false;
		Root.ReleasePointerCapture(e.Pointer);

		if (dragged)
		{
			DragCompleted?.Invoke(this, dy);
		}
		else
		{
			Activated?.Invoke(this, EventArgs.Empty);
		}

		e.Handled = true;
	}




	// パネルからポインタが離れたらツールボタンを隠す。
	private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
	{
		ToolButtons.Visibility = Visibility.Collapsed;
	}




	private void OnRootPointerCaptureLost(object sender, PointerRoutedEventArgs e)
	{
		CancelPointerOperation();
	}




	private void OnRootPointerCanceled(object sender, PointerRoutedEventArgs e)
	{
		CancelPointerOperation();
	}




	// 押下の途中でキャプチャを失ったときの後始末。ドラッグ中なら並べ替えの中断として親へ通知する。
	private void CancelPointerOperation()
	{
		if (!_pressed)
		{
			return;
		}

		bool dragged = _dragging;
		_pressed = false;
		_dragging = false;

		if (dragged)
		{
			DragCanceled?.Invoke(this, EventArgs.Empty);
		}
	}




	// 上端のしきい高さの内側にポインタがある間だけツールボタンを見せる。ドラッグ中は隠す。
	private void UpdateToolButtonsVisibility(PointerRoutedEventArgs e)
	{
		double y = e.GetCurrentPoint(Root).Position.Y;
		bool show = !_dragging && y <= ToolZoneHeight;
		ToolButtons.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
	}




	private void OnAlphaPreviewClick(object sender, RoutedEventArgs e)
	{
		AlphaPreviewClicked?.Invoke(this, EventArgs.Empty);
	}




	// 右クリック・長押し・キーボードでのコンテキストメニュー要求。位置を取れれば Root を基準とした表示位置を、キーボード起動などで取れなければ null を渡して親へ通知する。親がこの色に対する右クリックメニューを表示する。
	private void OnRootContextRequested(UIElement sender, ContextRequestedEventArgs e)
	{
		Point? position = e.TryGetPosition(Root, out Point point) ? point : null;
		ContextMenuRequested?.Invoke(this, position);
		e.Handled = true;
	}




	private void OnAddClick(object sender, RoutedEventArgs e)
	{
		AddRequested?.Invoke(this, EventArgs.Empty);
	}




	private void OnDeleteClick(object sender, RoutedEventArgs e)
	{
		DeleteRequested?.Invoke(this, EventArgs.Empty);
	}




	private void OnFavoriteClick(object sender, RoutedEventArgs e)
	{
		FavoriteRequested?.Invoke(this, EventArgs.Empty);
	}




	// 16進ラベルの編集フライアウトが開いた。現在の表示色を初期値に入れて全選択し、すぐ打ち替えられるようフォーカスする。前回のエラー表示は消しておく。
	private void OnHexEditFlyoutOpened(object? sender, object e)
	{
		HexEditError.Visibility = Visibility.Collapsed;
		HexEditBox.Text = Item.HexText;
		HexEditBox.SelectAll();
		HexEditBox.Focus(FocusState.Programmatic);
	}




	// 入力欄で Enter を押したら確定する。
	private void OnHexEditBoxKeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Enter)
		{
			e.Handled = true;
			CommitHexEdit();
		}
	}




	// 入力を打ち替えたらエラー表示を消す。
	private void OnHexEditBoxTextChanged(object sender, TextChangedEventArgs e)
	{
		HexEditError.Visibility = Visibility.Collapsed;
	}




	private void OnHexEditApplyClick(object sender, RoutedEventArgs e)
	{
		CommitHexEdit();
	}




	// 入力を色として解釈し、解釈できればフライアウトを閉じて親へ通知する。解釈できなければエラーを出してフライアウトは開いたままにする。解釈は親が設定した ColorParser へ委ね、未設定のときは解釈失敗として扱う。
	private void CommitHexEdit()
	{
		(bool ok, ParsedColor color) = ColorParser is not null ? ColorParser(HexEditBox.Text) : (false, default);

		if (!ok)
		{
			HexEditError.Visibility = Visibility.Visible;
			return;
		}

		HexEditFlyout.Hide();
		HexEditCommitted?.Invoke(this, color);
	}
}
