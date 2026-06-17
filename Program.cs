// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Irozukume;

// アプリのエントリポイント。
// トレイ常駐アプリのため単一起動とし、2つ目以降の起動は既存インスタンスへアクティベーションを転送して自身は終了する。これにより、settings.json を共有する複数プロセスが互いの変更(お気に入り・ウィンドウ配置等)を最後の終了時に上書きし合う事故を防ぐ。XAML が自動生成する既定の Main の代わりにこの Main を使うため、csproj で DISABLE_XAML_GENERATED_MAIN を定義している。
public static class Program
{
	// 単一起動の鍵。この文字列で最初のインスタンスを識別し、以後の起動はこの所有者へ転送する。
	private const string SingleInstanceKey = "Irozukume";

	// 管理者として自分を起動し直したときに渡す印。受け側はこの印があるとき転送せず、旧インスタンスの鍵解放を待って自分が握り直す。
	private const string RelaunchFlag = "--relaunch";




	[STAThread]
	private static int Main(string[] args)
	{
		WinRT.ComWrappersSupport.InitializeComWrappers();

		if (DecideRedirection())
		{
			// 既存インスタンスへ転送済み。この2つ目はウィンドウを作らず静かに終了する。
			return 0;
		}

		Application.Start(p =>
		{
			var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
			SynchronizationContext.SetSynchronizationContext(context);
			_ = new App();
		});

		return 0;
	}




	// 自分が最初のインスタンスかを判定する。最初なら鍵を握り、以後の起動からの転送を受ける側になって false を返す。既に他が握っていれば、そちらへ転送して true を返す。ただし管理者再起動(--relaunch)で立て直した直後は、旧インスタンスがまだ鍵を握って終了処理中のことがあるため転送せず、自分が所有者になれるまで短時間リトライする。
	private static bool DecideRedirection()
	{
		bool isRelaunch = Environment.GetCommandLineArgs().Contains(RelaunchFlag);
		AppActivationArguments activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
		AppInstance keyInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);

		if (isRelaunch)
		{
			for (int i = 0; i < 30 && !keyInstance.IsCurrent; i++)
			{
				Thread.Sleep(100);
				keyInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
			}
		}
		else if (!keyInstance.IsCurrent)
		{
			RedirectActivationTo(activationArgs, keyInstance);
			return true;
		}

		keyInstance.Activated += OnActivated;
		return false;
	}




	// 2つ目以降の起動から転送されてきたアクティベーション。最初のインスタンスのウィンドウを前面に出す。
	private static void OnActivated(object? sender, AppActivationArguments args)
	{
		if (Application.Current is App app)
		{
			app.OnReactivated();
		}
	}




	// 非同期の転送を同期の Main から待ち切る。転送中はまだディスパッチャが回っていないため、別スレッドで実行してイベントで完了を待つ。
	private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
	{
		var completed = new ManualResetEvent(false);
		Task.Run(() =>
		{
			keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
			completed.Set();
		});
		completed.WaitOne();
	}
}
