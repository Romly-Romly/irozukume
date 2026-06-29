// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

/// <summary>
/// 画像から代表色のパレットを起こすときの抽出アルゴリズム。
/// いずれも同じ画素標本から N 色を求め、最近傍の距離は CIELAB(CIE76)で測る。結果の傾向や速さを見比べられるよう利用者が選ぶ。
/// </summary>
public enum ImagePaletteAlgorithm
{
	/// <summary>
	/// 色空間を分散最大の軸で再帰分割する中央値分割法。速く決定的で、各箱の平均色を代表色にする。
	/// </summary>
	MedianCut,

	/// <summary>
	/// 色を3次元クラスタリングする k-means。k-means++ で初期化し、CIELAB 距離で各画素を最寄りの重心へ割り当てる。
	/// </summary>
	KMeans,

	/// <summary>
	/// 八分木に画素を積み、葉が色数を超える間は最小の葉から併合していく octree。
	/// </summary>
	Octree,

	/// <summary>
	/// 焼きなまし法。量子化誤差(各画素から最寄りのパレット色までの距離の総和)を目的関数に、温度を下げながらパレットを揺らして最適化する。重いが多目的の拘束を足しやすい。
	/// </summary>
	SimulatedAnnealing,
}
