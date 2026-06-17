// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Windows.Input;

namespace Irozukume.Helpers;

// XAML を介さずコードからコマンドを割り当てるための軽量 ICommand。パラメータは扱わず、常に実行可能とする。
internal sealed class RelayCommand : ICommand
{
	private readonly Action _execute;




	public RelayCommand(Action execute)
	{
		_execute = execute;
	}




	// 実行可否は変化しないため、購読を保持せず空実装とする。これにより CS0067（未使用イベント）の警告も避ける。
	public event EventHandler? CanExecuteChanged
	{
		add { }
		remove { }
	}




	public bool CanExecute(object? parameter)
	{
		return true;
	}




	public void Execute(object? parameter)
	{
		_execute();
	}
}
