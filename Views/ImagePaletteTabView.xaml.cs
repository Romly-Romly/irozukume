// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Irozukume.Helpers;
using Irozukume.Models;
using Irozukume.ViewModels;

namespace Irozukume.Views;

/// <summary>
/// 「画像から抽出」タブの中身。
/// 画像をドロップするかファイル選択で読み込み、選んだアルゴリズムと色数で代表色のパレットを起こして、スウォッチの一覧で見せる。各色はクリックで色1(編集中)へ反映し、一覧はまとめてお気に入りへ保存できる。
/// 復号(BGRA8 への変換)と画素の間引き・抽出はバックグラウンドで行い、その間は操作子を抑えて進捗を見せる。焼きなまし法など重い手法でも UI が固まらないようにする。
/// 編集対象の色は色1・色2を束ねる共有モデルへ反映し、色制限の警告はその設定に追従する。
/// </summary>
public sealed partial class ImagePaletteTabView : UserControl
{
	/// <summary>
	/// 画素の間引きの上限。これ以上の画素はパレット設計の前に等間隔へ減らす。大きな画像でも抽出を軽く保つ。
	/// </summary>
	private const int MaxSamples = 16384;

	/// <summary>
	/// 復号時に画像の長辺をこの大きさまで縮める上限。配色を得るだけなら原寸は要らず、抽出用の画素列もプレビューも巨大なメモリを抱えずに済む。
	/// </summary>
	private const int MaxDecodeDimension = 1024;

	/// <summary>
	/// 色1への反映と色制限(警告)の参照に使う、色編集の共有モデル。表示の間だけ色制限の変更を購読する。
	/// </summary>
	private readonly ColorEditorViewModel _editor;

	/// <summary>
	/// 直近に読み込んだ画像から間引いた画素標本。色数やアルゴリズムを変えて再抽出するとき、復号をやり直さず使い回す。
	/// </summary>
	private List<(byte R, byte G, byte B)>? _samples;

	/// <summary>
	/// 構築中のスライダーの初期化イベントを VM へ伝えないためのフラグ。InitializeComponent 時の既定値の当たりを無視する。
	/// </summary>
	private bool _initializing = true;

	public ImagePaletteViewModel ViewModel { get; }




	public ImagePaletteTabView(ColorEditorViewModel editor, FavoritePalettes favorites)
	{
		_editor = editor;
		ViewModel = new ImagePaletteViewModel(editor, favorites);
		this.InitializeComponent();

		// 復元すべき色数を VM の既定からスライダーへ当てる。ここまでの ValueChanged は _initializing で無視し、以降の操作だけ VM へ伝える。
		CountSlider.Value = ViewModel.ColorCount;
		_initializing = false;

		// 冷却率 α は 0.9997 のように小数第4位まで意味があるため、NumberBox の表示桁を整える。既定の整形では下位桁が落ちて見える。
		var alphaFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
		{
			IntegerDigits = 1,
			FractionDigits = 4,
		};
		alphaFormatter.NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder
		{
			Increment = 0.0001,
			RoundingAlgorithm = Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfToEven,
		};
		SaAlphaBox.NumberFormatter = alphaFormatter;

		this.Loaded += OnLoaded;
		this.Unloaded += OnUnloaded;
	}




	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		// 色制限の切替で警告を塗り替えるための購読。タブの表示・非表示と対にして解除し、寿命の長い共有モデルへ購読を残さない。
		_editor.PropertyChanged -= OnEditorPropertyChanged;
		_editor.PropertyChanged += OnEditorPropertyChanged;

		// 非表示の間に色制限が変わっていることがあるため、表示に戻った時点で現状へ合わせる。
		ViewModel.RefreshWarnings(_editor.CurrentSnap);
	}




	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		_editor.PropertyChanged -= OnEditorPropertyChanged;
	}




	private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorEditorViewModel.CurrentSnap))
		{
			ViewModel.RefreshWarnings(_editor.CurrentSnap);
		}
	}




	/// <summary>
	/// 操作ペインの高さが変わるたびに、プレビュー枠の高さをペインへ合わせ直す(下限170px)。
	/// 上段を Auto 行のまま伸縮させると、読み込んだ画像の縦横比でプレビューが青天井に伸びて下段の結果リストを押し出すため、画像ではなくペインで上段の高さを決める。
	/// </summary>
	private void OnControlPaneSizeChanged(object sender, SizeChangedEventArgs e)
	{
		DropZone.Height = Math.Max(170, e.NewSize.Height);
	}




	/// <summary>
	/// 色数スライダーの変更を VM へ伝える。構築中の初期化(_initializing)は無視する。
	/// </summary>
	private void OnCountChanged(object sender, RangeBaseValueChangedEventArgs e)
	{
		if (_initializing)
		{
			return;
		}

		ViewModel.ColorCount = (int)Math.Round(CountSlider.Value);
	}




	/// <summary>
	/// ×(画像を閉じる)ボタン。読み込んだ画像と抽出結果を捨てて案内へ戻す。間引いた画素も忘れ、再びドロップ/選択できるようにする。
	/// </summary>
	private void OnCloseImageClick(object sender, RoutedEventArgs e)
	{
		_samples = null;
		PreviewImage.Source = null;
		DropHint.Visibility = Visibility.Visible;
		ViewModel.Reset();
	}




	/// <summary>
	/// 「画像を選択」ボタン。ファイルピッカーで画像を1枚選んで読み込む。非パッケージアプリのため、ピッカーをメインウィンドウのハンドルへ紐付ける。
	/// </summary>
	private async void OnPickFileClick(object sender, RoutedEventArgs e)
	{
		var picker = new FileOpenPicker
		{
			ViewMode = PickerViewMode.Thumbnail,
			SuggestedStartLocation = PickerLocationId.PicturesLibrary,
		};
		WinRT.Interop.InitializeWithWindow.Initialize(picker, ((App)Application.Current).WindowHandle);

		foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" })
		{
			picker.FileTypeFilter.Add(ext);
		}

		StorageFile? file = await picker.PickSingleFileAsync();

		if (file is not null)
		{
			await LoadImageAsync(file);
		}
	}




	/// <summary>
	/// ドロップ領域の上を画像ファイルが通ったら、コピーとして受け入れることを示す。
	/// </summary>
	private void OnDropZoneDragOver(object sender, DragEventArgs e)
	{
		if (e.DataView.Contains(StandardDataFormats.StorageItems))
		{
			e.AcceptedOperation = DataPackageOperation.Copy;
		}
	}




	/// <summary>
	/// 画像ファイルがドロップされたら、最初に見つかった画像を読み込む。取得は非同期のため遅延(Deferral)で完了を待つ。
	/// </summary>
	private async void OnDropZoneDrop(object sender, DragEventArgs e)
	{
		if (!e.DataView.Contains(StandardDataFormats.StorageItems))
		{
			return;
		}

		DragOperationDeferral deferral = e.GetDeferral();

		try
		{
			IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();

			foreach (IStorageItem item in items)
			{
				if (item is StorageFile file && IsImageExtension(file.FileType))
				{
					await LoadImageAsync(file);
					break;
				}
			}
		}
		finally
		{
			deferral.Complete();
		}
	}




	/// <summary>
	/// 画像を読み込み、プレビューを見せ、現在の設定で抽出する。復号は BGRA8 で取り出し、画素は間引いて覚えておく。失敗したら案内を出す。
	/// </summary>
	private async Task LoadImageAsync(StorageFile file)
	{
		if (ViewModel.IsExtracting)
		{
			return;
		}

		try
		{
			byte[] bgra;
			int width;
			int height;
			int srcWidth;
			int srcHeight;

			using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
			{
				BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
				srcWidth = (int)decoder.PixelWidth;
				srcHeight = (int)decoder.PixelHeight;

				// 配色の抽出に原寸は要らない。長辺を MaxDecodeDimension まで縮めて復号し、巨大画像でも巨大なバイト列を確保しない。縮小は色の標本にほぼ影響しない。
				double scale = Math.Min(1.0, (double)MaxDecodeDimension / Math.Max(srcWidth, srcHeight));
				width = Math.Max(1, (int)Math.Round(srcWidth * scale));
				height = Math.Max(1, (int)Math.Round(srcHeight * scale));

				// 向き(Exif)は無視して復号する。1行のバイト数を幅×4で扱えるよう、画素の寸法を縮小後の値に揃える。色の標本に向きは関係しない。
				var transform = new BitmapTransform
				{
					ScaledWidth = (uint)width,
					ScaledHeight = (uint)height,
					InterpolationMode = BitmapInterpolationMode.Linear,
				};

				PixelDataProvider pixelData = await decoder.GetPixelDataAsync(
					BitmapPixelFormat.Bgra8,
					BitmapAlphaMode.Straight,
					transform,
					ExifOrientationMode.IgnoreExifOrientation,
					ColorManagementMode.DoNotColorManage);
				bgra = pixelData.DetachPixelData();
			}

			int stride = width * 4;
			_samples = await Task.Run(() => ImagePaletteExtractor.SampleBgra(bgra, width, height, stride, MaxSamples));

			// プレビューも原寸では抱えず、長辺を MaxDecodeDimension に抑えてデコードする。縦横比は長い側だけを指定して保つ。
			var preview = new BitmapImage();

			if (srcWidth >= srcHeight)
			{
				if (srcWidth > MaxDecodeDimension)
				{
					preview.DecodePixelWidth = MaxDecodeDimension;
				}
			}
			else
			{
				if (srcHeight > MaxDecodeDimension)
				{
					preview.DecodePixelHeight = MaxDecodeDimension;
				}
			}

			preview.UriSource = new Uri(file.Path);
			PreviewImage.Source = preview;
			DropHint.Visibility = Visibility.Collapsed;
			ViewModel.HasImage = true;

			await ExtractAsync();
		}
		catch (Exception)
		{
			await ShowLoadErrorAsync();
		}
	}




	/// <summary>
	/// 「抽出」ボタン。読み込み済みの画像から、現在の色数・アルゴリズムで取り直す。
	/// </summary>
	private async void OnExtractClick(object sender, RoutedEventArgs e)
	{
		await ExtractAsync();
	}




	/// <summary>
	/// 覚えてある画素標本から、現在の色数・アルゴリズムでパレットを抽出する。重い計算はバックグラウンドで回し、その間は操作子を抑える。
	/// </summary>
	private async Task ExtractAsync()
	{
		if (_samples is null || _samples.Count == 0 || ViewModel.IsExtracting)
		{
			return;
		}

		List<(byte R, byte G, byte B)> samples = _samples;
		ImagePaletteAlgorithm algorithm = ViewModel.CurrentAlgorithm;
		int count = ViewModel.ColorCount;

		ViewModel.IsExtracting = true;

		try
		{
			SaSettings sa = _editor.ImageSaSettings;
			List<ImagePaletteExtractor.ExtractedSwatch> result = await Task.Run(() => ImagePaletteExtractor.Extract(samples, count, algorithm, saSettings: sa));
			ViewModel.PopulateFromExtraction(result, _editor.CurrentSnap);
		}
		finally
		{
			ViewModel.IsExtracting = false;
		}
	}




	/// <summary>
	/// スウォッチがクリックされたら、その色を色1(編集中)へ反映する。ItemsRepeater は行要素へ DataContext を流さないため、Tag に束縛したスウォッチから取り出す。
	/// </summary>
	private void OnSwatchClick(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement element && element.Tag is PaletteSwatch swatch)
		{
			ViewModel.Apply(swatch);
		}
	}




	/// <summary>
	/// 「お気に入りに保存」ボタン。抽出した一覧を1つのお気に入りパレットとして保存する。
	/// 名前は既定の連番を初期値に出し、利用者が編集できる。保存したら設定ファイルへも即時に書き出す。
	/// </summary>
	private async void OnSaveFavoriteClick(object sender, RoutedEventArgs e)
	{
		if (!ViewModel.HasResult)
		{
			return;
		}

		// お気に入りに登録できるのはリストの先頭から MaxColors 色まで。
		// 抽出色がそれを超えるとき、超えた分は保存されない旨を保存ダイアログ上で警告して、気づかぬまま欠けた状態で保存しないようにする。
		// 書式引数は順に「抽出色数・登録できる色数・保存されない色数」。
		int max = ColorEditorViewModel.MaxColors;
		string? warning = ViewModel.Items.Count > max
			? Loc.Get("ImageTab_TooManyWarningFormat", ViewModel.Items.Count, max, ViewModel.Items.Count - max)
			: null;

		string? name = await PromptForNameAsync(Loc.Get("Favorite_SaveDialogTitle"), ViewModel.DefaultFavoriteName, warning);

		if (name is null)
		{
			return;
		}

		if (ViewModel.SaveFavorite(name) is not null)
		{
			(Application.Current as App)?.PersistSettings();
		}
	}




	/// <summary>
	/// 名前を入力するダイアログを出し、確定された名前を返す。
	/// 取り消したときや空白だけのときは null を返す。初期値は全選択した状態で出し、すぐ書き換えられるようにする。
	/// warning が与えられたときは、名前欄の上に警告(復元時に色が欠ける等)を出して保存前に気づけるようにする。
	/// </summary>
	private async Task<string?> PromptForNameAsync(string title, string initialName, string? warning = null)
	{
		var textBox = new TextBox
		{
			Text = initialName,
			SelectionStart = 0,
			SelectionLength = initialName.Length,
		};

		object content = textBox;

		if (warning is not null)
		{
			var bar = new InfoBar
			{
				Severity = InfoBarSeverity.Warning,
				IsOpen = true,
				IsClosable = false,
				Message = warning,
				Margin = new Thickness(0, 0, 0, 12),
			};

			content = new StackPanel { Children = { bar, textBox } };
		}

		var dialog = new ContentDialog
		{
			XamlRoot = this.XamlRoot,
			Title = title,
			Content = content,
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




	/// <summary>
	/// 画像の読み込みに失敗したときの案内を出す。
	/// </summary>
	private async Task ShowLoadErrorAsync()
	{
		var dialog = new ContentDialog
		{
			XamlRoot = this.XamlRoot,
			Title = Loc.Get("ImageTab_LoadErrorTitle"),
			Content = Loc.Get("ImageTab_LoadErrorContent"),
			CloseButtonText = Loc.Get("Favorite_DialogCancel"),
		};

		await dialog.ShowAsync();
	}




	/// <summary>
	/// 拡張子が読み込み対象の画像か。ドロップされた項目の選別に使う。先頭のドット込みの拡張子(".png" 等)を受ける。
	/// </summary>
	private static bool IsImageExtension(string fileType)
	{
		switch (fileType.ToLowerInvariant())
		{
			case ".png":
			case ".jpg":
			case ".jpeg":
			case ".bmp":
			case ".gif":
			case ".webp":
			case ".tif":
			case ".tiff":
				return true;
			default:
				return false;
		}
	}
}
