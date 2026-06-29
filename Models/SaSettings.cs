// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

/// <summary>
/// 焼きなまし法による画像パレット抽出の調整値。
/// 上級者向け設定として UI から変えられる。既定値は標準的な品質に合わせてある。
/// 彩度重み・希少度重み(いずれも 0–8)は0で均等抽出、上げるほど高彩度・希少な色を学習標本へ多く採って面積の小さい差し色を拾いやすくする。
/// </summary>
public readonly struct SaSettings
{
	public SaSettings(int iterations, double move, double alpha, double initialTempFactor, bool seedFromMedianCut, int saturationWeight, int rarityWeight)
	{
		Iterations = iterations;
		Move = move;
		Alpha = alpha;
		InitialTempFactor = initialTempFactor;
		SeedFromMedianCut = seedFromMedianCut;
		SaturationWeight = saturationWeight;
		RarityWeight = rarityWeight;
	}

	/// <summary>
	/// 反復回数。多いほど煮詰まるが重くなる。
	/// </summary>
	public int Iterations { get; }

	/// <summary>
	/// 1手あたりの代表色の移動幅(Lab)。探索の歩幅。
	/// </summary>
	public double Move { get; }

	/// <summary>
	/// 冷却率。1手ごとに温度へ掛ける係数(1未満)。
	/// </summary>
	public double Alpha { get; }

	/// <summary>
	/// 初期温度係数。試行手の平均悪化量へ掛けて初期温度を決める。
	/// </summary>
	public double InitialTempFactor { get; }

	/// <summary>
	/// 種を中央値分割法の結果から起こすか。偽なら学習標本から無作為に種を採る。
	/// </summary>
	public bool SeedFromMedianCut { get; }

	/// <summary>
	/// 彩度重み(0–8)。高彩度の色を学習標本へ多めに採る度合い。
	/// </summary>
	public int SaturationWeight { get; }

	/// <summary>
	/// 希少度重み(0–8)。色空間で疎(希少)な色を学習標本へ多めに採る度合い。
	/// </summary>
	public int RarityWeight { get; }

	/// <summary>
	/// 標準的な品質に合わせた既定値。
	/// </summary>
	public static SaSettings Default => new SaSettings(18000, 18.0, 0.9997, 0.5, true, 0, 0);
}
