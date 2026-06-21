// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.Interop;
using Irozukume.Models;
using Irozukume.Services;
using Irozukume.ViewModels;

namespace Irozukume.Views;

// 全色×全色の WCAG コントラスト比を一覧する補助ウィンドウ。行が文字色、列が背景色で、各セルは文字色のサンプル字を主体に見せ、下端1行に比と AA/AAA の適合バッジをまとめる。
// ヘッダーの色をクリックすると、メインの役選択と同じ規則でその色を役(行=文字色、列=背景色)へ就けて編集対象にする。アクティブな色のヘッダーはアクセント色の枠で示す。
// 同じ色同士の交差セルは比較として意味を持たないため、色を敷かず斜線だけで塗りつぶす。ツールバーのトグルで AA を満たさない組み合わせにも斜線を重ねられる。斜線の見た目は色域外表示のハッチと同じ。
// メインウィンドウと共有モデルを参照し、色の編集・リストの変化・色制限や不透明度反映の設定に追従してその場で更新される。編集の作業面とは別のウィンドウにすることで、タブで色を編集しながら全組み合わせを監視できる。
// ×ボタンでは破棄せず隠して使い回し、トレイ退避時はメインウィンドウと一緒に隠れる。配置は保存され、次回はディスプレイ構成の変化に合わせてクランプした位置・寸法で開く。
public sealed partial class ContrastMatrixWindow : Window
{
	// ウィンドウの最低サイズ (DIP)。ツールバーと行列が潰れない下限で、ドラッグ縮小の下限と保存配置を復元する際のクランプの双方で基準にする。
	public const int MinWidthDip = 420;
	public const int MinHeightDip = 360;

	// 適合・不適合のバッジ色。メインウィンドウのコントラストバッジと同じ配色。
	private static readonly Brush PassBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0x44));
	private static readonly Brush FailBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0x44, 0x44));

	// 斜線ハッチの寸法と色。45 度の右上がりで周期 10 DIP・線幅 0.7 DIP、黒線と白線を密着で並べて暗い背景では白が、明るい背景では黒が効くようにする。色域外表示のハッチ(GradientSlider・LcPlane・AbPlane)と寸法・色をそろえる。
	private const double HatchSpacing = 10.0;
	private const double HatchLineWidth = 0.7;
	private static readonly Brush HatchBlackBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
	private static readonly Brush HatchWhiteBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));

	// 共有の色編集モデル。ツールバーのトグルの x:Bind もこれを参照する。
	public ColorEditorViewModel ViewModel { get; }

	private readonly AppearanceViewModel _appearance;

	// 最低ウィンドウサイズの番人。ネイティブへ渡したサブクラスを生かし続けるためフィールドで保持する。
	private readonly WindowMinSizer _minSizer;

	// 行列の更新先。ヘッダー(左端=文字色、上端=背景色)のチップとセルの部品への参照を組み立て時に控え、値の変化では作り直さずその場で書き換える。
	private readonly List<HeaderChip> _rowHeaders = new();
	private readonly List<HeaderChip> _columnHeaders = new();
	private MatrixCell[,] _cells = new MatrixCell[0, 0];




	public ContrastMatrixWindow(ColorEditorViewModel editor, AppearanceViewModel appearance, WindowPlacement? placement)
	{
		// x:Bind が解決される InitializeComponent より前に共有モデルを差す。
		ViewModel = editor;
		_appearance = appearance;
		InitializeComponent();
		Title = Loc.Get("MatrixWindowTitle");

		// タスクバーと Alt+Tab に出るウィンドウアイコンを明示する。exe 埋め込みアイコンとは別に AppWindow へ当てないと、非パッケージのWinUIウィンドウは既定アイコンのままになる。ico の実体は出力フォルダへコピーしている。
		var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Irozukume.ico");
		AppWindow.SetIcon(iconPath);

		// 保存済みの配置があれば、復元先モニタの DPI とワークエリアへクランプして適用する。ディスプレイ構成が変わって元位置が画面外でも可視域へ引き戻される。無ければ既定のサイズで開く。
		if (placement is not null)
		{
			WindowPlacementService.Apply(this, placement, MinWidthDip, MinHeightDip);
		}
		else
		{
			WindowPlacementService.ResizeToDip(this, 640, 520);
		}

		_minSizer = new WindowMinSizer(this, MinWidthDip, MinHeightDip);

		// メインウィンドウと同じテーマを当て、設定での選択変更にも追従させる。このウィンドウは破棄せず使い回すため購読は解かない。
		ApplyTheme();
		_appearance.ThemeChanged += OnThemeChanged;

		// 並び・件数・アクティブの変化では行列を組み直し、色の値・不透明度・色制限・アルファ反映・斜線トグルの変化では値だけ書き換える。
		ViewModel.ColorListChanged += OnColorListChanged;
		ViewModel.PropertyChanged += OnEditorPropertyChanged;

		// ×ボタンでは破棄せず隠す。次回の呼び出しで同じウィンドウを再表示する。
		Closed += OnClosed;

		// 色覚シミュレーションのメニューへ、トグルの復帰先(保存値、無ければ既定の D型)の項目のチェックを入れ、ボタンの点灯をシミュレーション中かどうかに合わせる。以後の排他は RadioMenuFlyoutItem の GroupName が担う。
		RadioMenuFlyoutItem initialVision = (ViewModel.MatrixVision ?? ColorVisionType.Deutan) switch
		{
			ColorVisionType.Protan => VisionProtanItem,
			ColorVisionType.Tritan => VisionTritanItem,
			ColorVisionType.Monochromacy => VisionMonochromacyItem,
			_ => VisionDeutanItem,
		};
		initialVision.IsChecked = true;
		VisionToggle.IsChecked = ViewModel.MatrixVision is not null;

		// ドラッグ中のルーペを最前面で描く透明オーバーレイを、このウィンドウの XamlRoot に結び付けて登録する。XamlRoot は読み込み後に定まるため Loaded を待つ。
		RootGrid.Loaded += OnRootGridLoaded;

		RebuildMatrix();
	}




	// 読み込みが済んでウィンドウの XamlRoot が定まったら、レンズの最前面オーバーレイを登録する。
	private void OnRootGridLoaded(object sender, RoutedEventArgs e)
	{
		if (RootGrid.XamlRoot is not null)
		{
			LensOverlayService.Register(RootGrid.XamlRoot, LensOverlay);
		}
	}




	// 色覚シミュレーションのトグル(ボタン部)。オンは直前に選んでいた種別へ復帰し、オフはフルカラーへ戻す。共有モデルの変更の通知を受けて行列の値が書き換わる。
	private void OnVisionToggleChanged(ToggleSplitButton sender, ToggleSplitButtonIsCheckedChangedEventArgs args)
	{
		ViewModel.ToggleMatrixVision(sender.IsChecked);
	}




	// 色覚シミュレーションメニューの種別の選択。選んだ色覚を共有モデルへ書き込み、ボタンを点灯させる。変更の通知を受けて行列の値が書き換わる。
	private void OnVisionMenuClick(object sender, RoutedEventArgs e)
	{
		if (sender is RadioMenuFlyoutItem item && item.Tag is string tag)
		{
			ViewModel.MatrixVision = VisionFromTag(tag);
			VisionToggle.IsChecked = true;
		}
	}




	// メニュー項目の Tag の安定キーから色覚シミュレーションを解決する。未知の値はフルカラー(null)として扱う。
	private static ColorVisionType? VisionFromTag(string tag)
	{
		return tag switch
		{
			"protan" => ColorVisionType.Protan,
			"deutan" => ColorVisionType.Deutan,
			"tritan" => ColorVisionType.Tritan,
			"monochromacy" => ColorVisionType.Monochromacy,
			_ => null,
		};
	}




	// ウィンドウを表示して前面へ出す。×で隠した後の再表示にも使う。
	public void ShowAndActivate()
	{
		AppWindow.Show();
		Activate();
	}




	// ウィンドウを隠す。トレイ退避時にメインウィンドウ側から呼ばれる。
	public void HideWindow()
	{
		AppWindow.Hide();
	}




	private void OnClosed(object sender, WindowEventArgs args)
	{
		args.Handled = true;
		AppWindow.Hide();
	}




	private void OnThemeChanged(object? sender, EventArgs e)
	{
		ApplyTheme();
	}




	private void ApplyTheme()
	{
		RootGrid.RequestedTheme = _appearance.Theme;
	}




	// 共有モデルの変更のうち、行列の中身に効くものだけを拾って書き換える。Color1HexText はアクティブ色の値の変化を代表し、Alpha は不透明度、CurrentSnap は色制限、ContrastIncludeAlpha は透かし、MatrixHatchFails は斜線トグル、MatrixVision は色覚シミュレーション、ContrastSampleText はサンプル文字を表す。
	private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(ColorEditorViewModel.Color1HexText)
			or nameof(ColorEditorViewModel.Alpha)
			or nameof(ColorEditorViewModel.CurrentSnap)
			or nameof(ColorEditorViewModel.ContrastIncludeAlpha)
			or nameof(ColorEditorViewModel.MatrixHatchFails)
			or nameof(ColorEditorViewModel.MatrixVision)
			or nameof(ColorEditorViewModel.VisionSeverity)
			or nameof(ColorEditorViewModel.ContrastSampleText))
		{
			RefreshMatrix();
		}
	}




	private void OnColorListChanged(object? sender, EventArgs e)
	{
		RebuildMatrix();
	}




	// ツールバーの定型文メニュー。選んだ見本文をサンプル文字へ流し込む。共有の文字列のため、テキストモードのコントラスト確認欄にも同じ文が入る。見本文は UI の言語に依らず固定で、Lorem ipsum はラテン語のダミー文、fox は全アルファベットを一度ずつ含むパングラム、イーハトーヴォは日本語のフォント見本の定番(宮沢賢治「ポラーノの広場」冒頭)を使う。
	private void OnSamplePresetClick(object sender, RoutedEventArgs e)
	{
		if (sender is not MenuFlyoutItem item || item.Tag is not string key)
		{
			return;
		}

		ViewModel.ContrastSampleText = key switch
		{
			"lorem" => "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
			"fox" => "The quick brown fox jumps over the lazy dog",
			"ihatovo" => "あのイーハトーヴォのすきとおった風、夏でも底に冷たさをもつ青いそら、うつくしい森で飾られたモリーオ市、郊外のぎらぎらひかる草の波。",
			_ => ViewModel.ContrastSampleText,
		};
	}




	// 行列を色リストの現在の件数で組み直す。左上に凡例、上端に背景色のヘッダー、左端に文字色のヘッダー、その内側に件数×件数のセルを置く。ヘッダーとセルは星付けで窓いっぱいに広げる。組み立て後に値を流し込む。
	private void RebuildMatrix()
	{
		MatrixGrid.Children.Clear();
		MatrixGrid.RowDefinitions.Clear();
		MatrixGrid.ColumnDefinitions.Clear();
		_rowHeaders.Clear();
		_columnHeaders.Clear();

		int count = ViewModel.Colors.Count;
		_cells = new MatrixCell[count, count];

		MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		for (int i = 0; i < count; i++)
		{
			MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
			MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		}

		// 左上の凡例。行が文字色、列が背景色であることを示す。
		var corner = new TextBlock
		{
			Text = Loc.Get("MatrixCornerLabel"),
			FontSize = 11,
			TextAlignment = TextAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
		};
		Grid.SetRow(corner, 0);
		Grid.SetColumn(corner, 0);
		MatrixGrid.Children.Add(corner);

		for (int i = 0; i < count; i++)
		{
			HeaderChip column = CreateHeaderChip(isTextRole: false, i);
			Grid.SetRow(column.Root, 0);
			Grid.SetColumn(column.Root, i + 1);
			MatrixGrid.Children.Add(column.Root);
			_columnHeaders.Add(column);

			HeaderChip row = CreateHeaderChip(isTextRole: true, i);
			Grid.SetRow(row.Root, i + 1);
			Grid.SetColumn(row.Root, 0);
			MatrixGrid.Children.Add(row.Root);
			_rowHeaders.Add(row);

			for (int j = 0; j < count; j++)
			{
				MatrixCell cell = CreateCell();
				Grid.SetRow(cell.Root, i + 1);
				Grid.SetColumn(cell.Root, j + 1);
				MatrixGrid.Children.Add(cell.Root);
				_cells[i, j] = cell;
			}
		}

		RefreshMatrix();
	}




	// 行列の値を流し込む。ヘッダーは各色の表示色と16進にアクティブの枠を重ね、セルは背景色の地に文字色のサンプル字・比・AA/AAA の適合。サンプル字は共有のコントラスト確認の文字列で、空のときは言語非依存の Ag に退避する。色制限の丸めと、文字色の不透明度の透かし(アルファ反映の設定)を、メインのコントラスト確認と同じ規則で反映する。同じ色同士の交差セルは色もテキストも見せず斜線だけにし、トグルがオンなら AA を満たさない組み合わせにも斜線を重ねる。
	// 色覚シミュレーションを選んでいる間は、セルの背景と文字を「画面に出る色へその色覚のフィルタを掛けた色」で見せる。文字は透かし合成後の実効色へフィルタを掛けた不透明色にし、合成が先でフィルタが後という順序を保つ。ヘッダーの色チップと、コントラスト比・AA/AAA の判定はフルカラーでの値のまま変えない。
	private void RefreshMatrix()
	{
		SnapSettings snap = ViewModel.CurrentSnap;
		ColorVisionType? vision = ViewModel.MatrixVision;
		double severity = ViewModel.VisionSeverity;
		string sampleText = string.IsNullOrWhiteSpace(ViewModel.ContrastSampleText) ? "Ag" : ViewModel.ContrastSampleText;
		int count = Math.Min(ViewModel.Colors.Count, _rowHeaders.Count);

		for (int i = 0; i < count; i++)
		{
			Color display = ToDisplay(snap, ViewModel.Colors[i].Rgb);
			bool isActive = i == ViewModel.ActiveColorIndex;
			UpdateHeader(_rowHeaders[i], display, isActive);
			UpdateHeader(_columnHeaders[i], display, isActive);
		}

		for (int i = 0; i < count; i++)
		{
			SidebarColorViewModel fg = ViewModel.Colors[i];
			Color fgDisplay = ToDisplay(snap, fg.Rgb);
			byte alpha = ViewModel.ContrastIncludeAlpha ? (byte)Math.Round(fg.Alpha) : (byte)0xFF;
			Color foreground = Color.FromArgb(alpha, fgDisplay.R, fgDisplay.G, fgDisplay.B);

			for (int j = 0; j < count; j++)
			{
				MatrixCell cell = _cells[i, j];

				// 交差セル(同じ色同士)。比較として意味が無く、色やテキストを見せるとごちゃつくため、空のまま斜線だけにする。
				if (i == j)
				{
					cell.Root.Background = null;
					cell.Sample.Visibility = Visibility.Collapsed;
					cell.Bottom.Visibility = Visibility.Collapsed;
					cell.Hatch.Visibility = Visibility.Visible;
					continue;
				}

				Color bgDisplay = ToDisplay(snap, ViewModel.Colors[j].Rgb);
				Color effective = alpha < 0xFF ? ColorMetrics.AlphaComposite(foreground, bgDisplay) : fgDisplay;
				double ratio = ColorMetrics.ContrastRatio(effective, bgDisplay);

				// 見せる色。シミュレーション中は背景をフィルタ済みに、文字は透かし合成後の実効色へフィルタを掛けた不透明色にする。シミュレーションなしなら文字は不透明度を持ったまま重ね、画面側の合成に任せる。
				Color shownBg = bgDisplay;
				Color shownSample = foreground;

				if (vision is ColorVisionType visionType)
				{
					shownBg = ColorVisionSimulation.Simulate(visionType, severity, bgDisplay);
					shownSample = ColorVisionSimulation.Simulate(visionType, severity, effective);
				}

				cell.Root.Background = new SolidColorBrush(shownBg);
				cell.Sample.Visibility = Visibility.Visible;
				cell.Sample.Text = sampleText;
				cell.Sample.Foreground = new SolidColorBrush(shownSample);
				cell.Bottom.Visibility = Visibility.Visible;
				cell.Ratio.Text = $"{ratio:0.00}";
				cell.Ratio.Foreground = new SolidColorBrush(ColorMetrics.ContrastColor(shownBg));
				cell.AaBadge.Background = ratio >= 4.5 ? PassBrush : FailBrush;
				cell.AaaBadge.Background = ratio >= 7.0 ? PassBrush : FailBrush;
				cell.Hatch.Visibility = ViewModel.MatrixHatchFails && ratio < 4.5 ? Visibility.Visible : Visibility.Collapsed;
			}
		}
	}




	// ヘッダーのチップへ表示色・16進表記・読みやすい文字色を流し込む。アクティブな色はアクセント色の枠で示し、それ以外は通常の枠にする。
	private static void UpdateHeader(HeaderChip chip, Color display, bool isActive)
	{
		chip.Root.Background = new SolidColorBrush(display);
		chip.Hex.Text = $"#{display.R:X2}{display.G:X2}{display.B:X2}";
		chip.Hex.Foreground = new SolidColorBrush(ColorMetrics.ContrastColor(display));

		if (isActive)
		{
			chip.Root.BorderThickness = new Thickness(2);
			chip.Root.BorderBrush = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
		}
		else
		{
			chip.Root.BorderThickness = new Thickness(1);
			chip.Root.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
		}
	}




	// 表示用の色を、色制限モードに従って丸めて返す。制限なしなら素の色をそのまま返す。メインの見え方と同じ規則。
	private static Color ToDisplay(SnapSettings snap, Color color)
	{
		if (snap.Mode == ColorLimitMode.None)
		{
			return color;
		}

		(byte r, byte g, byte b) = ColorConversion.Snap(snap, color.R, color.G, color.B);
		return Color.FromArgb(0xFF, r, g, b);
	}




	// 斜線レイヤーの大きさが変わったら線を引き直す。表示へ切り替わった直後もレイアウト確定時にここを通る。
	private static void OnHatchLayerSizeChanged(object sender, SizeChangedEventArgs e)
	{
		RebuildHatchLines((Grid)sender);
	}




	// 斜線レイヤーへ 45 度固定の斜線を引き直す。レイヤーの矩形でクリップし、黒線と、密着するよう位相を線幅×√2 ぶんずらした白線の2本を重ねる。縦横比や大きさに依らず角度と周期が一定になる。
	private static void RebuildHatchLines(Grid layer)
	{
		layer.Children.Clear();
		double width = layer.ActualWidth;
		double height = layer.ActualHeight;

		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}

		layer.Clip = new RectangleGeometry { Rect = new Rect(0.0, 0.0, width, height) };
		layer.Children.Add(new Path
		{
			Data = BuildDiagonalLines(0.0, width, 0.0, height, HatchSpacing, 0.0),
			Stroke = HatchBlackBrush,
			StrokeThickness = HatchLineWidth,
		});
		layer.Children.Add(new Path
		{
			Data = BuildDiagonalLines(0.0, width, 0.0, height, HatchSpacing, HatchLineWidth * Math.Sqrt(2.0)),
			Stroke = HatchWhiteBrush,
			StrokeThickness = HatchLineWidth,
		});
	}




	// 指定の x 範囲を覆う 45 度の斜線(右上がり、x+y=一定)をまとめたジオメトリを作る。縦は top から top+height の帯に収め、左へ height 分ずらした位置から周期ごとに引き、offset で帯の位相をずらす(黒線と白線を密着させるのに使う)。色域外表示のハッチ(GradientSlider)と同じ引き方。
	private static GeometryGroup BuildDiagonalLines(double x, double right, double top, double height, double spacing, double offset)
	{
		var lines = new GeometryGroup();

		for (double sx = x - height + offset; sx < right; sx += spacing)
		{
			lines.Children.Add(new LineGeometry
			{
				StartPoint = new Point(sx, top + height),
				EndPoint = new Point(sx + height, top),
			});
		}

		return lines;
	}




	// ヘッダーのチップを作る。塗り・16進・アクティブの枠は RefreshMatrix が流し込む。クリックでメインの役選択と同じ規則(行=文字色の役、列=背景色の役)でその色を役へ就け、編集対象にする。
	private HeaderChip CreateHeaderChip(bool isTextRole, int index)
	{
		var hex = new TextBlock
		{
			FontFamily = new FontFamily("Consolas"),
			FontSize = 11,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};

		var root = new Border
		{
			CornerRadius = new CornerRadius(4),
			BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
			BorderThickness = new Thickness(1),
			Padding = new Thickness(6, 4, 6, 4),
			Child = hex,
		};

		AutomationProperties.SetName(root, Loc.Get("SwatchColorN", index + 1));
		root.Tapped += (_, args) =>
		{
			args.Handled = true;
			ViewModel.SelectContrastRole(isTextRole, index);
		};

		return new HeaderChip(root, hex);
	}




	// セルを作る。文字色のサンプル字を主体として中央へ大きく置き、比と AA/AAA は下端の1行へまとめる。サンプル字は折り返しつつ、収まらない分は省略記号で切る。斜線はセル全体を覆うレイヤーで、可視は RefreshMatrix が、線の引き直しは大きさの変化が決める。
	private static MatrixCell CreateCell()
	{
		var sample = new TextBlock
		{
			FontSize = 20,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TextAlignment = TextAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
			TextTrimming = TextTrimming.CharacterEllipsis,
		};

		var ratio = new TextBlock
		{
			FontFamily = new FontFamily("Consolas"),
			FontSize = 11,
			VerticalAlignment = VerticalAlignment.Center,
		};

		var aaBadge = new Border
		{
			CornerRadius = new CornerRadius(3),
			Padding = new Thickness(4, 0, 4, 0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = new TextBlock
			{
				Text = "AA",
				FontSize = 9,
				FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
			},
		};

		var aaaBadge = new Border
		{
			CornerRadius = new CornerRadius(3),
			Padding = new Thickness(4, 0, 4, 0),
			VerticalAlignment = VerticalAlignment.Center,
			Child = new TextBlock
			{
				Text = "AAA",
				FontSize = 9,
				FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
			},
		};

		var bottom = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		bottom.Children.Add(ratio);
		bottom.Children.Add(aaBadge);
		bottom.Children.Add(aaaBadge);

		var layout = new Grid { Margin = new Thickness(6) };
		layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		Grid.SetRow(sample, 0);
		Grid.SetRow(bottom, 1);
		layout.Children.Add(sample);
		layout.Children.Add(bottom);

		// 斜線レイヤー。内容の余白に依らずセル全体を覆い、当たり判定は持たせない。線は大きさの変化に合わせて引き直す。
		var hatch = new Grid
		{
			IsHitTestVisible = false,
			Visibility = Visibility.Collapsed,
		};
		hatch.SizeChanged += OnHatchLayerSizeChanged;

		var wrapper = new Grid();
		wrapper.Children.Add(layout);
		wrapper.Children.Add(hatch);

		var root = new Border
		{
			CornerRadius = new CornerRadius(6),
			BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
			BorderThickness = new Thickness(1),
			MinWidth = 64,
			MinHeight = 56,
			Child = wrapper,
		};

		return new MatrixCell(root, sample, ratio, aaBadge, aaaBadge, bottom, hatch);
	}




	// ヘッダーのチップの更新先。
	private sealed record HeaderChip(Border Root, TextBlock Hex);




	// セルの更新先。
	private sealed record MatrixCell(Border Root, TextBlock Sample, TextBlock Ratio, Border AaBadge, Border AaaBadge, StackPanel Bottom, Grid Hatch);
}
