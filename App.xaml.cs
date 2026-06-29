// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.Interop;
using Irozukume.Models;
using Irozukume.Services;
using Romly.WinUI.Common.Windowing;

namespace Irozukume;

/// <summary>
/// アプリのエントリ。タスクトレイに常駐し、アイコンの左クリックでメインウィンドウを表示、右クリックメニューから「開く」「終了」を行う。
/// ウィンドウの×ボタンは終了ではなくトレイへの退避として扱い、常駐を継続する。プロセスを畳むのは「終了」のみ。
/// </summary>
public partial class App : Application
{
	private MainWindow? _window;
	private TaskbarIcon? _trayIcon;

	/// <summary>
	/// トレイの右クリックメニュー。SecondWindow の別ウィンドウで描かれるため、アプリ テーマの変更時にこのメニューへ配色を当て直す必要があり、参照を保持する。
	/// </summary>
	private MenuFlyout? _trayMenu;

	/// <summary>
	/// アプリの永続設定。起動時に読み込み、ウィンドウ配置を保存する際に書き戻す。
	/// </summary>
	private Settings _settings = new();

	/// <summary>
	/// ウィンドウを一度でも表示したか。未表示のまま終了する場合、AppWindow の位置は OS 既定の生成位置のままでユーザーの選択ではないため、その値で既存の保存を上書きしないよう保存を見送る判断に使う。
	/// </summary>
	private bool _hasBeenShown;

	/// <summary>
	/// 設定の削除・全消去リセットの操作中に立てる。立っている間は <see cref="SaveSettings"/> が何もしないため、削除した settings.json が終了時・トレイ退避時の保存で復活しない。
	/// </summary>
	private bool _suppressSave;




	public App()
	{
		this.InitializeComponent();

		// 未処理例外を crash.log へ書き出す。e.Handled は立てず既定のクラッシュ処理に委ねる(壊れた状態で動き続けるより、落として原因に気づける方がよい)。
		this.UnhandledException += OnUnhandledException;
	}




	/// <summary>
	/// アプリ全体で捕まえ損ねた未処理例外の最後の受け皿。WinUI の UI スレッドへ漏れた例外がここへ来る。crash.log へ残すだけで Handled は立てず、既定のクラッシュ処理に委ねる。
	/// </summary>
	private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		CrashLog.Write(e.Exception);
	}




	/// <summary>
	/// 2つ目以降の起動から転送されてきたアクティベーションを受けて、既存インスタンスのウィンドウを前面に出す。
	/// Activated は UI スレッド外で発火しうるため、ウィンドウのディスパッチャへ載せ替えてから表示する。
	/// </summary>
	public void OnReactivated()
	{
		_window?.DispatcherQueue.TryEnqueue(ShowWindow);
	}




	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		// 保存済み配置があれば復元したうえでウィンドウを生成し、起動直後に表示する。×ボタンでトレイへ退避した後は、トレイアイコン操作で再表示する。
		_settings = SettingsStore.Load();

		// 保存済みの表示言語を、ウィンドウ生成より前に適用する。x:Uid は UI の読み込み時に解決されるため、ここで設定しないと初回表示が目的の言語にならない。
		ApplyLanguageOverride(_settings.Appearance?.Language);

		_window = new MainWindow(_settings.Window, _settings.Editor, _settings.Appearance, _settings.MatrixWindow);
		_window.Closed += OnWindowClosed;

		InitializeTrayIcon();
		ShowWindow();
	}




	/// <summary>
	/// 保存済みの表示言語をアプリへ適用する。
	/// 非パッケージアプリでは WinAppSDK 版の ApplicationLanguages を使う(WinRT 版は非パッケージで効かない)。"ja"/"en" はその言語へ固定し、"system"・未指定は空文字で OS の表示言語に従う。ウィンドウ生成より前に呼ぶことで、x:Uid が初回読み込み時に目的の言語で解決される。
	/// </summary>
	private static void ApplyLanguageOverride(string? language)
	{
		string primaryLanguage = language switch
		{
			"ja" => "ja",
			"en" => "en",
			_ => "",
		};

		try
		{
			Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = primaryLanguage;
		}
		catch
		{
			// 言語の上書きに失敗しても、OS の既定言語で起動を続ける。
		}
	}




	/// <summary>
	/// トレイアイコンとコンテキストメニューをコードで組み立てる。
	/// アイコンは exe・ウィンドウと同じ絵柄に揃えるため、出力フォルダへ置いた ico を実行フォルダからの絶対パスで読む。
	/// 非パッケージ配布では ms-appx 等の資産URIが解決できないため、file URI になる絶対パスを使う。
	/// </summary>
	private void InitializeTrayIcon()
	{
		var menu = new MenuFlyout { AreOpenCloseAnimationsEnabled = false };

		// メニュー先頭に「アプリ名 バージョン」を表示する。情報提示のみのため Click を割り当てず、選択しても何も起こらない。
		var versionItem = new MenuFlyoutItem { Text = BuildAppNameVersionText() };

		// 左クリックでも同じ動作をすることをメニュー右側のショートカット表示で示す。実際のキー割り当ては伴わない表示専用。
		var openItem = new MenuFlyoutItem { Text = Loc.Get("Tray_Open"), KeyboardAcceleratorTextOverride = Loc.Get("Tray_LeftClick") };
		openItem.Click += (_, _) => ShowWindow();

		// 画面から色を拾う。ウィンドウ非表示でも採色でき、確定したらその色を反映したうえでウィンドウを前面に出して結果を見せる。中止時はウィンドウを出さない。
		// アイコンはコマンドバー・編集メニュー・パレットの画面ピックと同じ Segoe Fluent Icons のスポイト(EF3C)に揃える。
		var pickItem = new MenuFlyoutItem { Text = Loc.Get("Tray_PickColor"), Icon = new FontIcon { Glyph = "\uEF3C" } };
		pickItem.Click += async (_, _) =>
		{
			if (_window is null)
			{
				return;
			}

			if (await _window.PickScreenColorAsync())
			{
				ShowWindow();
			}
		};

		// 管理者として再起動。通常権限では昇格ウィンドウ(管理者アプリ)の上で採色フックが OS に握られ採色できないので、明示的に昇格して拾えるようにするためのもの。既に管理者なら現状を示すだけで無効化する。
		var adminItem = new MenuFlyoutItem();
		if (ElevationHelper.IsElevated)
		{
			adminItem.Text = Loc.Get("Tray_RunningAsAdmin");
			adminItem.IsEnabled = false;
		}
		else
		{
			adminItem.Text = Loc.Get("Tray_RestartAsAdmin");
			adminItem.Click += (_, _) => RestartAsAdministrator();
		}

		var exitItem = new MenuFlyoutItem { Text = Loc.Get("Tray_Exit") };
		exitItem.Click += (_, _) => ExitApplication();

		menu.Items.Add(versionItem);
		menu.Items.Add(new MenuFlyoutSeparator());
		menu.Items.Add(openItem);
		menu.Items.Add(pickItem);
		menu.Items.Add(adminItem);
		menu.Items.Add(new MenuFlyoutSeparator());
		menu.Items.Add(exitItem);
		_trayMenu = menu;

		_trayIcon = new TaskbarIcon
		{
			ToolTipText = Loc.Get("AppName"),
			// メニューを親ウィンドウに依存しない専用ウィンドウで描く。ウィンドウ非表示でも右クリックメニューが正しく表示される。
			ContextMenuMode = ContextMenuMode.SecondWindow,
			// 左クリックの確定を遅延させず、押下に対してすぐ LeftClickCommand を発火する。
			NoLeftClickDelay = true,
			LeftClickCommand = new RelayCommand(ShowWindow),
			IconSource = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Irozukume.ico"))),
			ContextFlyout = menu,
		};

		_trayIcon.ForceCreate();

		// トレイメニューは SecondWindow の別ウィンドウで描かれ、メインウィンドウのルートに当てたテーマが届かない。現在のテーマをメニューへ当て、設定での選択変更にも追従させる。
		ApplyTrayMenuTheme();

		if (_window is not null)
		{
			_window.Appearance.ThemeChanged += OnAppearanceThemeChanged;
		}
	}




	/// <summary>
	/// トレイメニュー先頭に出す「アプリ名 バージョン」の文字列を組み立てる。バージョンはアセンブリのバージョンから取り、取得できない場合はアプリ名だけを返す。
	/// </summary>
	private static string BuildAppNameVersionText()
	{
		System.Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
		string appName = Loc.Get("AppName");
		return version is null ? appName : $"{appName} {version.Major}.{version.Minor}.{version.Build}";
	}




	/// <summary>
	/// トレイの右クリックメニューに現在のアプリテーマを適用する。
	/// SecondWindow の別ウィンドウで描かれメインウィンドウの RequestedTheme が届かないため、プレゼンターのスタイル経由で配色を合わせる。Default のときはシステム設定に従う。
	/// </summary>
	private void ApplyTrayMenuTheme()
	{
		if (_trayMenu is null || _window is null)
		{
			return;
		}

		var style = new Style(typeof(MenuFlyoutPresenter));
		style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, _window.Appearance.Theme));
		_trayMenu.MenuFlyoutPresenterStyle = style;
	}




	/// <summary>
	/// アプリ テーマが変わったら、トレイメニューの配色も当て直す。
	/// </summary>
	private void OnAppearanceThemeChanged(object? sender, EventArgs e)
	{
		ApplyTrayMenuTheme();
	}




	/// <summary>
	/// メインウィンドウを表示して前面へ出す。Show は H.NotifyIcon が提供する Window 拡張で、トレイへ退避した非表示状態からの復帰に対応する。
	/// </summary>
	private void ShowWindow()
	{
		if (_window is null)
		{
			return;
		}

		_window.Show();

		// 最小化されていれば元のサイズへ戻す。
		if (_window.AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
		{
			presenter.Restore();
		}

		_window.Activate();

		// 既に表示済みで他のウィンドウの背面にある場合、Show と Activate だけでは前面奪取制限で前へ出ない。トレイクリック直後はシステムが前面化を一時的に許可するので、SetForegroundWindow を明示して前面化を確定させる。
		NativeMethods.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window));

		_hasBeenShown = true;
	}




	/// <summary>
	/// 「終了」だけがプロセスを畳む唯一の経路。トレイアイコンを即座に取り除いてから終了する。トレイメニューとメインメニューの双方から呼ばれる。
	/// </summary>
	internal void ExitApplication()
	{
		SaveSettings();
		_trayIcon?.Dispose();
		Environment.Exit(0);
	}




	/// <summary>
	/// 現在の状態を設定ファイルへ即時保存する。表示言語の変更時など、終了を待たずに保存したい場面でメインウィンドウから呼ぶ。
	/// </summary>
	internal void PersistSettings()
	{
		SaveSettings();
	}




	/// <summary>
	/// メインウィンドウの Win32 ハンドル。非パッケージアプリで FileOpenPicker 等の WinRT のダイアログを所有ウィンドウへ紐付けるのに使う。ウィンドウがまだ無いときは IntPtr.Zero を返す。
	/// </summary>
	internal nint WindowHandle => _window is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(_window);




	/// <summary>
	/// アプリを再起動する。表示言語の変更を読み込み済み UI へ反映するには、いったん終了して起動し直す必要がある。
	/// 終了前に状態を保存し、トレイアイコンを片付けてから、同じ実行ファイルを起動して現在のプロセスを終える。
	/// </summary>
	internal void RestartApplication()
	{
		SaveSettings();
		_trayIcon?.Dispose();

		string? path = Environment.ProcessPath;

		if (path is not null)
		{
			System.Diagnostics.Process.Start(path);
		}

		Environment.Exit(0);
	}




	/// <summary>
	/// 管理者権限で自分を起動し直す。通常権限では UIPI により昇格ウィンドウ上で採色フックが効かないため、利用者の明示操作で昇格セッションへ移る。
	/// Verb = "runas" で起動時に一度だけ UAC を出す(マニフェストは asInvoker のままなので通常起動では UAC は出ない)。
	/// UAC を拒否・キャンセルした場合は昇格起動が失敗するので、トレイアイコンを保持したまま通常権限で動作を続ける。成功して新プロセスへ引き継げたときだけ現在のプロセスを畳む。
	/// </summary>
	internal void RestartAsAdministrator()
	{
		string? path = Environment.ProcessPath;
		if (path is null)
		{
			return;
		}

		var psi = new System.Diagnostics.ProcessStartInfo
		{
			FileName = path,
			UseShellExecute = true,
			Verb = "runas",
			// 単一起動の番人に弾かれず確実に立て直すための印。受け側(Program)はこの印があるとき転送せず、旧インスタンスの鍵解放を待って自分が握り直す。
			Arguments = "--relaunch",
		};

		try
		{
			SaveSettings();
			System.Diagnostics.Process.Start(psi);
		}
		catch (System.ComponentModel.Win32Exception)
		{
			return;
		}

		_trayIcon?.Dispose();
		Environment.Exit(0);
	}




	/// <summary>
	/// 設定ファイルのある実行フォルダをエクスプローラーで開く。
	/// 設定ファイルがあればそれを選択した状態で、無ければフォルダだけを開く。バックアップ等はそこから手動で行ってもらう。開けなくてもアプリの動作は妨げない。
	/// </summary>
	internal void OpenSettingsFolder()
	{
		try
		{
			var dir = AppContext.BaseDirectory ?? System.IO.Directory.GetCurrentDirectory();
			var settingsPath = System.IO.Path.Combine(dir, "settings.json");

			var psi = System.IO.File.Exists(settingsPath)
				? new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{settingsPath}\"", UseShellExecute = true }
				: new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true };

			System.Diagnostics.Process.Start(psi);
		}
		catch
		{
			// フォルダを開けなくてもアプリの動作は妨げない。
		}
	}




	/// <summary>
	/// 設定をすべて削除し、初期状態で起動し直す。配布前に既定の見え方を確認するための操作。終了時保存で削除が無に帰さないよう保存を抑止してから消し、再起動する。
	/// </summary>
	internal void ResetSettingsAndRestart()
	{
		_suppressSave = true;
		SettingsStore.Delete();
		RestartApplication();
	}




	/// <summary>
	/// 設定ファイルを削除してアプリを終了する。
	/// アンインストール前の後始末用。終了処理 (<see cref="ExitApplication"/>) の保存が削除を無に帰さないよう、抑止フラグを立ててから消し、そのまま終了する。
	/// </summary>
	internal void DeleteSettingsAndExit()
	{
		_suppressSave = true;
		SettingsStore.Delete();
		ExitApplication();
	}




	/// <summary>
	/// 現在のウィンドウ配置と色編集状態を設定へ取り込んで保存する。
	/// × でのトレイ退避時と終了時に呼び、次回起動へ引き継ぐ。一度も表示していないウィンドウは位置が OS 既定の生成位置のままなので保存しない。これで未表示のまま終了しても、既存の保存値を既定位置で汚さない。保存に失敗してもアプリの動作・終了を妨げないよう、例外は飲み込む。
	/// </summary>
	private void SaveSettings()
	{
		if (_window is null || !_hasBeenShown || _suppressSave)
		{
			return;
		}

		try
		{
			_settings.Window = WindowPlacementService.Capture(_window.AppWindow);
			_settings.Editor = _window.CaptureEditorState();
			_settings.Appearance = _window.CaptureAppearanceState();

			// マトリックスのウィンドウはこのセッションで一度も開いていなければ null が返るため、保存済みの配置を温存する。
			_settings.MatrixWindow = _window.CaptureContrastMatrixPlacement() ?? _settings.MatrixWindow;

			SettingsStore.Save(_settings);
		}
		catch
		{
			// 終了時の状態保存はベストエフォート。書き込みに失敗してもアプリの終了を妨げない。
		}
	}




	/// <summary>
	/// ×ボタンで閉じられた時は終了させず、ウィンドウをトレイへ退避させて常駐を続ける。
	/// Handled を立てることで実際のクローズを取り消す。退避の本体はメニューの「閉じる」(Ctrl+W)と共通の <see cref="HideWindowToTray"/> が担う。
	/// </summary>
	private void OnWindowClosed(object sender, WindowEventArgs args)
	{
		args.Handled = true;
		HideWindowToTray();
	}




	/// <summary>
	/// ウィンドウを終了させずトレイへ退避させる。×ボタン・メニューの「閉じる」(Ctrl+W)から呼ばれる。
	/// 退避でウィンドウ位置が失われる前にこの時点の配置を保存し、次回起動へ持ち越す。
	/// 設定ページを開いたまま退避した場合でも次の再表示は色ピッカー(本体)で開くよう、退避前に本体表示へ戻しておく。
	/// コントラストマトリックスとプレビューの補助ウィンドウも一緒に隠す。
	/// </summary>
	internal void HideWindowToTray()
	{
		SaveSettings();
		_window?.CloseSettings();
		_window?.HideContrastMatrix();
		_window?.HideColorPreview();
		_window?.Hide();
	}
}
