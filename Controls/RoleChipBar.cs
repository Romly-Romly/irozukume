// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Irozukume.Helpers;
using Irozukume.ViewModels;

namespace Irozukume.Controls;

// テキストモードの役選択バー。一方の役(文字色か背景色)について、色リストの全色を横並びのチップで見せ、役に就いている色のチップだけを横に広げ、ほかは正方形に畳む。選択が動くと幅をアニメーションでつなぐ。通常モードのアクティブ色パネルが他の2倍に広がるのと同じ「選択が大きくなる」見せ方に揃え、サイドバー全体で統一感を出す。
public sealed class RoleChipBar : Grid
{
	// 非選択チップの一辺(DIP)。選択チップはこの高さのまま、残り幅いっぱいへ広がる。
	private const double SquareSize = 26.0;

	// チップ同士の間隔(DIP)。
	private const double ChipSpacing = 2.0;

	// チップの角丸。色パネル(CornerRadius 8)に寄せたやや丸い角にする。
	private const double ChipCornerRadius = 3.0;

	// 選択の広がり/畳みのアニメーションの長さ。色リストの行高アニメ(250ms)に揃える。
	private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(250);

	// 並んでいるチップ(Border)。選択の変化だけのときは作り直さず、この列の幅を動かす。
	private readonly List<Border> _chips = new();

	// 各チップに重ねる、編集フォーカス枠の明暗二重縁。チップの子として持ち、フォーカス中の役だけ厚みを与えて見せる。外側(_focusDarkRings)を暗・内側(_focusLightRings)を明にし、役の色が明るくても暗くてもどちらかが必ずアクセント枠と役の色を分ける。_chips と添字で対応する。
	private readonly List<Border> _focusDarkRings = new();
	private readonly List<Border> _focusLightRings = new();

	// 束ねている色。並びが同じか(選択の変化だけか)を見分けるために控える。
	private List<SidebarColorViewModel> _items = new();

	// 役に就いている色の位置と、編集フォーカスがこの役にあるか。枠の見せ方と広げる列を決める。
	private int _selected = -1;
	private bool _focused;

	// 走らせ中の幅アニメーション。CompositionTarget.Rendering で1フレームごとに列幅を補間する。Storyboard はビジュアルツリー外の DependencyObject を的にできず、列幅(GridLength)も直接は動かせないため、自前で補間する。
	private bool _animating;
	private double[] _fromWidths = Array.Empty<double>();
	private double[] _toWidths = Array.Empty<double>();
	private TimeSpan _animStart;
	private bool _hasAnimStart;

	// OS の「アニメーションを表示する」設定の参照元。オフのときは幅を即座に確定し、利用者の設定を尊重する。
	private static readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();



	public RoleChipBar()
	{
		ColumnSpacing = ChipSpacing;
		Height = SquareSize;
		HorizontalAlignment = HorizontalAlignment.Stretch;
	}




	// この役が文字色側か。クリック通知の宛先(SelectContrastRole の役)を見分けるのに親が読む。
	public bool IsTextRole { get; set; }




	// チップがクリックされた通知。値はその色の位置。
	public event EventHandler<int>? SelectionRequested;




	// 状態を反映する。色の並び・件数が同じ(選択・フォーカスの変化だけ)ならチップは保ち、選択が動いたぶんだけ幅をアニメーションで動かす。並び・件数が変わったらチップを作り直し、幅は即座に確定する。
	public void SetState(IReadOnlyList<SidebarColorViewModel> items, int selected, bool focused)
	{
		if (!SameItems(items))
		{
			RebuildChips(items);
			_selected = selected;
			_focused = focused;
			StopAnimation();
			ApplyRestColumns();
			UpdateBorders();
			return;
		}

		bool selectionMoved = _selected != selected;
		_selected = selected;
		_focused = focused;
		UpdateBorders();

		if (selectionMoved)
		{
			AnimateToSelection();
		}
	}




	// 控えた色の並びと、与えられた並びが参照ごとに一致するか。一致すれば選択・フォーカスの変化だけとみなし、チップを保つ。
	private bool SameItems(IReadOnlyList<SidebarColorViewModel> items)
	{
		if (_items.Count != items.Count)
		{
			return false;
		}

		for (int i = 0; i < _items.Count; i++)
		{
			if (!ReferenceEquals(_items[i], items[i]))
			{
				return false;
			}
		}

		return true;
	}




	// 色リストの並びでチップと列を作り直す。各チップの塗りは項目の表示ブラシへ束縛し、色の編集に追従させる。
	private void RebuildChips(IReadOnlyList<SidebarColorViewModel> items)
	{
		Children.Clear();
		ColumnDefinitions.Clear();
		_chips.Clear();
		_focusDarkRings.Clear();
		_focusLightRings.Clear();
		_items = new List<SidebarColorViewModel>(items);

		for (int i = 0; i < items.Count; i++)
		{
			SidebarColorViewModel item = items[i];
			ColumnDefinitions.Add(new ColumnDefinition());

			var chip = new Border
			{
				Height = SquareSize,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Center,
				CornerRadius = new CornerRadius(ChipCornerRadius),
			};

			chip.SetBinding(Border.BackgroundProperty, new Binding
			{
				Source = item,
				Path = new PropertyPath(nameof(SidebarColorViewModel.FillBrush)),
				Mode = BindingMode.OneWay,
			});

			// 編集フォーカス枠の明暗二重縁。アクセント枠(チップ自身の枠)の内側へ入れ子で重ね、UpdateBorders が厚みと色を与える。役の色が透けて見えるよう塗りは持たせず、当たり判定はチップへ通す。
			var darkRing = new Border
			{
				CornerRadius = new CornerRadius(Math.Max(0.0, ChipCornerRadius - 3.0)),
				IsHitTestVisible = false,
			};
			var lightRing = new Border
			{
				CornerRadius = new CornerRadius(Math.Max(0.0, ChipCornerRadius - 4.0)),
				IsHitTestVisible = false,
			};
			darkRing.Child = lightRing;
			chip.Child = darkRing;
			_focusDarkRings.Add(darkRing);
			_focusLightRings.Add(lightRing);

			Grid.SetColumn(chip, i);
			AutomationProperties.SetName(chip, Loc.Get("SwatchColorN", i + 1));

			int index = i;
			chip.Tapped += (_, args) =>
			{
				args.Handled = true;
				SelectionRequested?.Invoke(this, index);
			};

			Children.Add(chip);
			_chips.Add(chip);
		}
	}




	// 各チップの枠を選択状態で変える。未選択は通常の枠、役に就いている色は背景に映える前景色の枠、さらに編集フォーカスの役はアクセント色の太枠にする。
	private void UpdateBorders()
	{
		for (int i = 0; i < _chips.Count; i++)
		{
			Border chip = _chips[i];
			Border darkRing = _focusDarkRings[i];
			Border lightRing = _focusLightRings[i];

			if (i == _selected && _focused)
			{
				chip.BorderThickness = new Thickness(3);
				chip.BorderBrush = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);

				// アクセント色が役の色に近いと枠が埋もれるため、アクセント枠の内側へ暗0.5・明0.5 DIP の縁を重ねる。役の色が明るくても暗くても、どちらかが必ずアクセント枠と役の色を分ける。
				darkRing.BorderThickness = new Thickness(0.5);
				darkRing.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x00, 0x00));
				lightRing.BorderThickness = new Thickness(0.5);
				lightRing.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
			}
			else
			{
				// 役に就いている色は横へ大きく広げて示すため、枠では強調しない。畳まれた候補と同じ細いカード枠で輪郭だけ示す。フォーカス中の役だけは上のアクセント枠で別に示す。
				chip.BorderThickness = new Thickness(1);
				chip.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

				darkRing.BorderThickness = new Thickness(0);
				lightRing.BorderThickness = new Thickness(0);
			}
		}
	}




	// 静止状態の列幅を即座に当てる。役の色(選択)の列は候補の2倍を下限に残り幅を占める星幅(2*)、候補の列は正方形(SquareSize)を上限に縮みうる星幅(1*)にする。星幅にしておくことで、バーの横幅が変わっても配分が追従する。幅が詰まると上限のある候補から先に縮み、上限のない役の色は候補の2倍まで粘る。
	private void ApplyRestColumns()
	{
		for (int i = 0; i < ColumnDefinitions.Count; i++)
		{
			if (i == _selected)
			{
				ColumnDefinitions[i].MaxWidth = double.PositiveInfinity;
				ColumnDefinitions[i].Width = new GridLength(2.0, GridUnitType.Star);
			}
			else
			{
				ColumnDefinitions[i].MaxWidth = SquareSize;
				ColumnDefinitions[i].Width = new GridLength(1.0, GridUnitType.Star);
			}
		}
	}




	// 現在の列幅から、新しい選択に応じた幅へアニメーションで遷移する。広げる/畳む途中はすべての列を画素幅で補間し、終わったら選択列だけ星幅へ戻して横幅変化に追従できるようにする。
	private void AnimateToSelection()
	{
		int count = ColumnDefinitions.Count;

		if (count == 0)
		{
			return;
		}

		// 行き先の画素幅。静止状態と同じ配分を画素で求める。候補は正方形(SquareSize)を上限に、足りなければ役の色が候補の2倍になるところまで一緒に縮む。役の色は残りを占める。
		double available = ActualWidth - ((count - 1) * ChipSpacing);
		double candidate = Math.Min(SquareSize, available / (count + 1));
		double role = available - ((count - 1) * candidate);

		if (!_uiSettings.AnimationsEnabled || ActualWidth <= 0.0 || role <= candidate)
		{
			StopAnimation();
			ApplyRestColumns();
			return;
		}

		_fromWidths = new double[count];
		_toWidths = new double[count];

		for (int i = 0; i < count; i++)
		{
			// 画素で補間する間は上限の切り詰めが邪魔になるため、いったん上限を外す。静止へ戻すときに ApplyRestColumns が上限を引き直す。
			ColumnDefinitions[i].MaxWidth = double.PositiveInfinity;
			_fromWidths[i] = ColumnDefinitions[i].ActualWidth;
			_toWidths[i] = i == _selected ? role : candidate;
			ColumnDefinitions[i].Width = new GridLength(_fromWidths[i], GridUnitType.Pixel);
		}

		_hasAnimStart = false;

		if (!_animating)
		{
			_animating = true;
			CompositionTarget.Rendering += OnRendering;
		}
	}




	// 毎フレームの更新。経過時間から進捗を出し、CubicEase の EaseOut で各列の画素幅を補間する。1に達したら選択列を星幅へ戻して静止状態にし、購読を解く。
	private void OnRendering(object? sender, object e)
	{
		TimeSpan now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;

		if (!_hasAnimStart)
		{
			_animStart = now;
			_hasAnimStart = true;
		}

		double progress = AnimationDuration > TimeSpan.Zero ? (now - _animStart) / AnimationDuration : 1.0;

		if (progress >= 1.0)
		{
			StopAnimation();
			ApplyRestColumns();
			return;
		}

		double t = EaseOut(Math.Max(0.0, progress));
		int count = Math.Min(ColumnDefinitions.Count, Math.Min(_fromWidths.Length, _toWidths.Length));

		for (int i = 0; i < count; i++)
		{
			double width = _fromWidths[i] + ((_toWidths[i] - _fromWidths[i]) * t);
			ColumnDefinitions[i].Width = new GridLength(width, GridUnitType.Pixel);
		}
	}




	// 3次の EaseOut。終わり際で緩やかに収束させ、色リストの行高アニメと同じ曲線にする。
	private static double EaseOut(double t)
	{
		double inverse = 1.0 - t;
		return 1.0 - (inverse * inverse * inverse);
	}




	private void StopAnimation()
	{
		if (_animating)
		{
			CompositionTarget.Rendering -= OnRendering;
			_animating = false;
		}
	}
}
