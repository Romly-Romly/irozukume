// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Irozukume.Helpers;

// ドラッグ中の拡大つまみ(ルーペ)を、ウィンドウ最前面の透明オーバーレイへ描くための仲介役。レンズの描画コントロールはタブの中身を包むスクロール領域の内側にあり、そのままでは上下端で切り抜かれる。各ウィンドウが自分の最前面オーバーレイ(Canvas)を XamlRoot とともに登録し、レンズを出すコントロール(スライダー・色相環・パッド)は自分の XamlRoot からそれを引いてレンズをそこへ移す。オーバーレイはどの祖先にもクリップされないため、レンズはどこへはみ出しても切れない。
public static class LensOverlayService
{
	// XamlRoot(ウィンドウ1枚に1つ)から、そのウィンドウの最前面オーバーレイ Canvas を引く対応表。XamlRoot は参照同値で鍵にする。
	private static readonly Dictionary<XamlRoot, Canvas> _overlays = new();




	// ウィンドウの最前面オーバーレイを、その XamlRoot に結び付けて登録する。各ウィンドウが読み込み後に一度呼ぶ。同じ XamlRoot での再登録は新しいオーバーレイで上書きする。
	public static void Register(XamlRoot root, Canvas overlay)
	{
		_overlays[root] = overlay;
	}




	// 指定の XamlRoot に結び付いた最前面オーバーレイを返す。未登録・null のときは null を返し、呼び出し側はレンズの置き場をテンプレート内 Canvas へフォールバックする。
	public static Canvas? Get(XamlRoot? root)
	{
		return root is not null && _overlays.TryGetValue(root, out Canvas? overlay) ? overlay : null;
	}




	// レンズ置き場の局所座標で表したルーペ左上を、配置先の座標系へ写すアフィン行列を作る。toTarget はレンズ置き場から配置先への変換で、祖先の回転・拡大・スクロール量をすべて織り込む。これをルーペの配置トランスフォームに使うことで、レンズを最前面オーバーレイへ移しても、回転したパッドやスクロール中のスライダーで見た目と位置が一致する。単位ベクトルの写り先の差から線形部(回転・拡大)を、左上の写り先を平行移動成分にする。
	public static Matrix ComputePlacement(GeneralTransform toTarget, double topLeftX, double topLeftY)
	{
		Point origin = toTarget.TransformPoint(new Point(0.0, 0.0));
		Point unitX = toTarget.TransformPoint(new Point(1.0, 0.0));
		Point unitY = toTarget.TransformPoint(new Point(0.0, 1.0));
		Point topLeft = toTarget.TransformPoint(new Point(topLeftX, topLeftY));
		return new Matrix(unitX.X - origin.X, unitX.Y - origin.Y, unitY.X - origin.X, unitY.Y - origin.Y, topLeft.X, topLeft.Y);
	}
}
