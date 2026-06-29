// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using Windows.UI;
using Irozukume.Models;

namespace Irozukume.Helpers;

/// <summary>
/// 画像の画素標本から代表色のパレットを起こす純粋ロジック。
/// 中央値分割・k-means・octree・焼きなまし法を同じ入口で切り替えて扱い、最近傍の距離はいずれも CIELAB(CIE76)で測る。
/// WinUI への依存は結果を表す Windows.UI.Color のみで、画素の取り出し(復号)は呼び出し側が担う。利用者の入力を奪わずに突合できるよう、入力の画素列と出力のパレットだけで完結させる。
/// </summary>
public static class ImagePaletteExtractor
{
	/// <summary>
	/// 近接重複とみなす Lab 上の距離(ΔE)。
	/// これ未満に重なった2色は1枠分の無駄とみなし、占有の小さい側を未表現の領域へ移す候補にする。焼きなまし法の双子検出と、他手法後段の近接重複マージの双方で使う。
	/// </summary>
	private const double MergeDeltaE = 4.0;

	/// <summary>
	/// 焼きなまし法の目的関数を測る学習標本の上限。全画素標本からこの数へ間引く。彩度・希少度の重みが有効なら、重みに比例した復元抽出でこの数を引く。
	/// </summary>
	private const int SaSampleMax = 10000;

	/// <summary>
	/// 死に色救済を打ち切る最大誤差のしきい値。標本の最大二乗誤差がこれ以下なら画像は完全に表現済みとみなし、残る担当ゼロ色は避けられない重複として残す。
	/// </summary>
	private const double DeadColorEpsilon = 1e-9;




	/// <summary>
	/// 抽出されたパレットの1色。Color はその代表色、Weight はこの色へ割り当てられた画素の割合(0–1)で、主要色を見分けたり並べ替えたりするのに使う。
	/// </summary>
	public readonly struct ExtractedSwatch
	{
		public ExtractedSwatch(Color color, double weight)
		{
			Color = color;
			Weight = weight;
		}

		public Color Color { get; }
		public double Weight { get; }
	}




	/// <summary>
	/// BGRA8(各画素 B・G・R・A の4バイト)の画素配列を、最大 maxSamples 画素へ間引いた RGB の列にする。
	/// 透明に近い画素(アルファが alphaThreshold 未満)は飛ばす。stride は1行のバイト数で、行末の詰め物を跨ぐために使う。
	/// 復号した SoftwareBitmap をそのまま抽出へ渡すための前処理。
	/// </summary>
	public static List<(byte R, byte G, byte B)> SampleBgra(byte[] bgra, int width, int height, int stride, int maxSamples, byte alphaThreshold = 8)
	{
		var result = new List<(byte R, byte G, byte B)>();

		if (bgra is null || width <= 0 || height <= 0 || stride < width * 4)
		{
			return result;
		}

		long totalPixels = (long)width * height;
		int step = 1;

		if (maxSamples > 0 && totalPixels > maxSamples)
		{
			step = (int)(totalPixels / maxSamples);
		}

		if (step < 1)
		{
			step = 1;
		}

		long counter = 0;

		for (int y = 0; y < height; y++)
		{
			int rowBase = y * stride;

			for (int x = 0; x < width; x++)
			{
				if (counter++ % step != 0)
				{
					continue;
				}

				int o = rowBase + (x * 4);

				if (bgra[o + 3] < alphaThreshold)
				{
					continue;
				}

				result.Add((bgra[o + 2], bgra[o + 1], bgra[o + 0]));
			}
		}

		return result;
	}




	/// <summary>
	/// 画素標本から colorCount 色のパレットを抽出する。algorithm で手法を選ぶ。
	/// seed を与えると乱数が決定的になり、突合に使える。progress は反復系の手法(k-means・焼きなまし法)が 0–1 で進捗を報告する。
	/// saSettings は焼きなまし法の調整値で、省略時は既定値を使う。結果は必ず colorCount 色で、画像が許す限り別々の色にし、足りなければ重複で数を満たす。
	/// 各色は割り当て画素の割合の降順(主要色が先頭)に並ぶ。
	/// </summary>
	public static List<ExtractedSwatch> Extract(IReadOnlyList<(byte R, byte G, byte B)> pixels, int colorCount, ImagePaletteAlgorithm algorithm, int? seed = null, IProgress<double>? progress = null, SaSettings? saSettings = null)
	{
		if (pixels is null || pixels.Count == 0 || colorCount < 1)
		{
			return new List<ExtractedSwatch>();
		}

		var rng = seed.HasValue ? new Random(seed.Value) : new Random();
		int n = pixels.Count;

		// 全標本画素の Lab を一度だけ求め、抽出・整え・重み付け・割合計算で共有する。
		var pixLab = new (double L, double A, double B)[n];

		for (int i = 0; i < n; i++)
		{
			pixLab[i] = ColorMetrics.RgbToLab(pixels[i].R, pixels[i].G, pixels[i].B);
		}

		int kAlg = Math.Min(colorCount, n);
		SaSettings sa = saSettings ?? SaSettings.Default;

		List<(byte R, byte G, byte B)> palette = algorithm switch
		{
			ImagePaletteAlgorithm.KMeans => KMeans(pixels, kAlg, rng, progress),
			ImagePaletteAlgorithm.Octree => OctreeQuantize(pixels, kAlg),
			ImagePaletteAlgorithm.SimulatedAnnealing => SimulatedAnnealing(pixels, pixLab, colorCount, sa, rng, progress),
			_ => MedianCut(pixels, kAlg),
		};

		// どの手法の出力も「必ず指定色数・画像が許す限り別色・足りなければ重複」へ整える。
		RefinePalette(palette, pixLab, pixels, colorCount, rng);

		return Finalize(pixels, pixLab, palette);
	}




	/// <summary>
	/// パレットの各色へ全画素を最近傍(CIELAB)で割り当てて割合を求め、割合の降順に並べた結果を組む。
	/// 色数は整えた後のパレットのまま保ち、担当画素の付かない色(乏しい画像で生じる重複)も枠として残す。
	/// どの手法の出力も同じ尺度で重み付けするため、抽出の後段に共通で通す。
	/// </summary>
	private static List<ExtractedSwatch> Finalize(IReadOnlyList<(byte R, byte G, byte B)> pixels, (double L, double A, double B)[] pixLab, List<(byte R, byte G, byte B)> palette)
	{
		var result = new List<ExtractedSwatch>();

		if (palette.Count == 0)
		{
			return result;
		}

		var palLab = new (double L, double A, double B)[palette.Count];

		for (int i = 0; i < palette.Count; i++)
		{
			palLab[i] = ColorMetrics.RgbToLab(palette[i].R, palette[i].G, palette[i].B);
		}

		var counts = new long[palette.Count];

		for (int i = 0; i < pixLab.Length; i++)
		{
			counts[NearestLab(palLab, pixLab[i])]++;
		}

		double total = pixels.Count;

		for (int i = 0; i < palette.Count; i++)
		{
			Color color = Color.FromArgb(0xFF, palette[i].R, palette[i].G, palette[i].B);
			result.Add(new ExtractedSwatch(color, total > 0.0 ? counts[i] / total : 0.0));
		}

		result.Sort((a, b) => b.Weight.CompareTo(a.Weight));
		return result;
	}




	/// <summary>
	/// 中央値分割法。
	/// 全画素を1つの箱に入れ、色数に届くまで「いずれかのチャンネルの値域が最大の箱」を選び、そのチャンネルで並べて中央で2つに割る。
	/// 各箱の平均色を代表色にする。割れる箱(2画素以上で値域のある箱)が尽きたら打ち切る。
	/// </summary>
	private static List<(byte R, byte G, byte B)> MedianCut(IReadOnlyList<(byte R, byte G, byte B)> pixels, int count)
	{
		var boxes = new List<List<(byte R, byte G, byte B)>>
		{
			new List<(byte R, byte G, byte B)>(pixels),
		};

		while (boxes.Count < count)
		{
			int targetBox = -1;
			int targetChannel = 0;
			int targetRange = 0;

			for (int i = 0; i < boxes.Count; i++)
			{
				List<(byte R, byte G, byte B)> box = boxes[i];

				if (box.Count < 2)
				{
					continue;
				}

				ChannelRanges(box, out int rRange, out int gRange, out int bRange);
				int channel = 0;
				int range = rRange;

				if (gRange > range)
				{
					channel = 1;
					range = gRange;
				}

				if (bRange > range)
				{
					channel = 2;
					range = bRange;
				}

				if (range > targetRange)
				{
					targetBox = i;
					targetChannel = channel;
					targetRange = range;
				}
			}

			// 値域のある箱が無い(全画素が単色か、各箱が1画素)。これ以上は割れないため打ち切る。
			if (targetBox < 0)
			{
				break;
			}

			List<(byte R, byte G, byte B)> src = boxes[targetBox];
			src.Sort((x, y) => Channel(x, targetChannel).CompareTo(Channel(y, targetChannel)));
			int mid = src.Count / 2;
			boxes[targetBox] = src.GetRange(0, mid);
			boxes.Add(src.GetRange(mid, src.Count - mid));
		}

		var result = new List<(byte R, byte G, byte B)>(boxes.Count);

		foreach (List<(byte R, byte G, byte B)> box in boxes)
		{
			if (box.Count == 0)
			{
				continue;
			}

			long sr = 0;
			long sg = 0;
			long sb = 0;

			foreach ((byte R, byte G, byte B) p in box)
			{
				sr += p.R;
				sg += p.G;
				sb += p.B;
			}

			result.Add(((byte)(sr / box.Count), (byte)(sg / box.Count), (byte)(sb / box.Count)));
		}

		return result;
	}




	/// <summary>
	/// k-means。各画素を CIELAB へ移し、k-means++ で初期の重心を選んでから、最寄りの重心への割り当てと重心の更新(割り当てられた画素の RGB 平均)を繰り返す。
	/// 距離は CIELAB、更新は RGB で行い、Lab→RGB の逆変換を経ずに代表色を得る。割り当てが動かなくなるか上限回数で止める。
	/// </summary>
	private static List<(byte R, byte G, byte B)> KMeans(IReadOnlyList<(byte R, byte G, byte B)> pixels, int count, Random rng, IProgress<double>? progress)
	{
		int n = pixels.Count;
		var lab = new (double L, double A, double B)[n];

		for (int i = 0; i < n; i++)
		{
			lab[i] = ColorMetrics.RgbToLab(pixels[i].R, pixels[i].G, pixels[i].B);
		}

		var centLab = new (double L, double A, double B)[count];
		var centR = new double[count];
		var centG = new double[count];
		var centB = new double[count];

		// k-means++ の初期化。最初の重心を無作為に選び、以降は既存の重心からの最短距離の2乗に比例した確率で選んで散らす。
		int first = rng.Next(n);
		SetCentroid(centLab, centR, centG, centB, 0, pixels[first], lab[first]);
		var d2 = new double[n];

		for (int i = 0; i < n; i++)
		{
			d2[i] = Dist2(lab[i], centLab[0]);
		}

		for (int c = 1; c < count; c++)
		{
			int chosen = WeightedPick(d2, rng);
			SetCentroid(centLab, centR, centG, centB, c, pixels[chosen], lab[chosen]);

			for (int i = 0; i < n; i++)
			{
				double d = Dist2(lab[i], centLab[c]);

				if (d < d2[i])
				{
					d2[i] = d;
				}
			}
		}

		var assign = new int[n];
		const int maxIter = 20;

		for (int iter = 0; iter < maxIter; iter++)
		{
			bool changed = false;

			for (int i = 0; i < n; i++)
			{
				int nearest = NearestLab(centLab, lab[i]);

				if (nearest != assign[i])
				{
					assign[i] = nearest;
					changed = true;
				}
			}

			var cnt = new long[count];
			var sumR = new double[count];
			var sumG = new double[count];
			var sumB = new double[count];

			for (int i = 0; i < n; i++)
			{
				int a = assign[i];
				cnt[a]++;
				sumR[a] += pixels[i].R;
				sumG[a] += pixels[i].G;
				sumB[a] += pixels[i].B;
			}

			for (int c = 0; c < count; c++)
			{
				if (cnt[c] > 0)
				{
					var color = ((byte)Math.Round(sumR[c] / cnt[c]), (byte)Math.Round(sumG[c] / cnt[c]), (byte)Math.Round(sumB[c] / cnt[c]));
					SetCentroid(centLab, centR, centG, centB, c, color, ColorMetrics.RgbToLab(color.Item1, color.Item2, color.Item3));
				}
				else
				{
					// 画素の付かなかった重心は無作為な画素へ置き直し、空のまま居座らせない。
					int r = rng.Next(n);
					SetCentroid(centLab, centR, centG, centB, c, pixels[r], lab[r]);
				}
			}

			progress?.Report((iter + 1) / (double)maxIter);

			if (!changed && iter > 0)
			{
				break;
			}
		}

		var result = new List<(byte R, byte G, byte B)>(count);

		for (int c = 0; c < count; c++)
		{
			result.Add(((byte)Math.Round(centR[c]), (byte)Math.Round(centG[c]), (byte)Math.Round(centB[c])));
		}

		return result;
	}




	/// <summary>
	/// 標本画素から焼きなまし法で count 色のパレットを起こす。
	/// 代表色を Lab 空間で保持し、毎回1色を動かして量子化誤差(標本から最寄りのパレット色までの距離の2乗の平均)を下げる。
	/// 下がれば採用、上がっても確率 exp(−Δ/T) で採用し、温度を幾何冷却する。割り当ては差分更新で保ち、温度は試行手の平均悪化量で較正する。
	/// 一定確率で担当画素の重心へ吸着して k-means 級へ素早く寄せ、担当ゼロの色や近接重複の冗長な側は誤差最大の標本へ飛ばして未表現の領域を担当させる。
	/// 最良解を別に保ち、それを返す。
	/// </summary>
	private static List<(byte R, byte G, byte B)> SimulatedAnnealing(IReadOnlyList<(byte R, byte G, byte B)> pixels, (double L, double A, double B)[] pixLab, int count, SaSettings settings, Random rng, IProgress<double>? progress)
	{
		int N = count;

		if (N < 1)
		{
			return new List<(byte R, byte G, byte B)>();
		}

		double[] sLab = BuildWeightedSaSample(pixels, pixLab, SaSampleMax, settings.SaturationWeight, settings.RarityWeight, rng, out int nS);
		var pal = new double[N * 3];
		int seeded = 0;

		// 種。中央値分割法の代表色を Lab で置き、足りない分は学習標本から無作為に拾う。
		if (settings.SeedFromMedianCut)
		{
			List<(byte R, byte G, byte B)> seed = MedianCut(pixels, Math.Min(N, pixels.Count));

			for (int i = 0; i < seed.Count && i < N; i++)
			{
				(double L, double A, double B) lab = ColorMetrics.RgbToLab(seed[i].R, seed[i].G, seed[i].B);
				pal[i * 3] = lab.L;
				pal[i * 3 + 1] = lab.A;
				pal[i * 3 + 2] = lab.B;
				seeded++;
			}
		}

		for (int i = seeded; i < N; i++)
		{
			int r = rng.Next(nS);
			pal[i * 3] = sLab[r * 3];
			pal[i * 3 + 1] = sLab[r * 3 + 1];
			pal[i * 3 + 2] = sLab[r * 3 + 2];
		}

		var assign = new int[nS];
		var dist = new double[nS];
		double totalCost = 0.0;

		for (int i = 0; i < nS; i++)
		{
			double bestVal = double.MaxValue;
			int bi = 0;

			for (int j = 0; j < N; j++)
			{
				double dL = sLab[i * 3] - pal[j * 3];
				double da = sLab[i * 3 + 1] - pal[j * 3 + 1];
				double db = sLab[i * 3 + 2] - pal[j * 3 + 2];
				double d = (dL * dL) + (da * da) + (db * db);

				if (d < bestVal)
				{
					bestVal = d;
					bi = j;
				}
			}

			assign[i] = bi;
			dist[i] = bestVal;
			totalCost += bestVal;
		}

		double baseMove = settings.Move;
		double alpha = settings.Alpha;
		var changed = new int[nS];
		var changedAssign = new int[nS];
		var changedDist = new double[nS];
		int nc = 0;

		// 色 k を (nL,na,nb) へ動かしたときのコスト変化を求める。
		// changed/nc に再割り当てを控えるが pal は変えない。動かす色の担当画素は最寄りを引き直し、それ以外は新位置が近ければ移る。
		double TrialMove(int k, double nL, double na, double nb)
		{
			double deltaSum = 0.0;
			nc = 0;

			for (int i = 0; i < nS; i++)
			{
				double L = sLab[i * 3];
				double a = sLab[i * 3 + 1];
				double b = sLab[i * 3 + 2];

				if (assign[i] == k)
				{
					double bestVal = double.MaxValue;
					int bi = 0;

					for (int j = 0; j < N; j++)
					{
						double pL = j == k ? nL : pal[j * 3];
						double pa = j == k ? na : pal[j * 3 + 1];
						double pb = j == k ? nb : pal[j * 3 + 2];
						double dL = L - pL;
						double da = a - pa;
						double db = b - pb;
						double d = (dL * dL) + (da * da) + (db * db);

						if (d < bestVal)
						{
							bestVal = d;
							bi = j;
						}
					}

					deltaSum += bestVal - dist[i];
					changed[nc] = i;
					changedAssign[nc] = bi;
					changedDist[nc] = bestVal;
					nc++;
				}
				else
				{
					double dL = L - nL;
					double da = a - na;
					double db = b - nb;
					double d = (dL * dL) + (da * da) + (db * db);

					if (d < dist[i])
					{
						deltaSum += d - dist[i];
						changed[nc] = i;
						changedAssign[nc] = k;
						changedDist[nc] = d;
						nc++;
					}
				}
			}

			return deltaSum;
		}

		// 受理判定は平均コストの変化量で見るため、温度もその尺度へ較正する。試行手の平均悪化量を初期温度の基準にする。
		double calSum = 0.0;
		int calCount = 0;
		int calTrials = Math.Min(150, Math.Max(20, settings.Iterations / 50));

		for (int t = 0; t < calTrials; t++)
		{
			int k = rng.Next(N);
			double nL = pal[k * 3] + (((rng.NextDouble() * 2.0) - 1.0) * baseMove);
			double na = pal[k * 3 + 1] + (((rng.NextDouble() * 2.0) - 1.0) * baseMove);
			double nb = pal[k * 3 + 2] + (((rng.NextDouble() * 2.0) - 1.0) * baseMove);
			double ds = TrialMove(k, nL, na, nb) / nS;

			if (ds > 0.0)
			{
				calSum += ds;
				calCount++;
			}
		}

		double baseDelta = calCount > 0 ? calSum / calCount : 1.0;
		double T = settings.InitialTempFactor * baseDelta;

		if (!(T > 0.0))
		{
			T = baseDelta > 0.0 ? baseDelta : 1.0;
		}

		double T0 = T;
		int iters = settings.Iterations;
		int progStep = Math.Max(1, iters / 60);
		double bestCost = totalCost;
		var bestPal = (double[])pal.Clone();
		double th2 = MergeDeltaE * MergeDeltaE;
		const double pSnap = 0.15;

		for (int it = 0; it < iters; it++)
		{
			int k = rng.Next(N);
			double nL;
			double na;
			double nb;

			if (rng.NextDouble() < pSnap)
			{
				// 重心吸着の一手。担当画素の平均(Lloyd 反復)へ寄せて k-means 級へ素早く収束させる。
				// 担当ゼロや近接重複の冗長な側は、重心ではなく誤差最大の標本へ飛ばして未表現の領域を担当させる。
				double sL = 0.0;
				double sA = 0.0;
				double sB = 0.0;
				int cnt = 0;

				for (int i = 0; i < nS; i++)
				{
					if (assign[i] == k)
					{
						sL += sLab[i * 3];
						sA += sLab[i * 3 + 1];
						sB += sLab[i * 3 + 2];
						cnt++;
					}
				}

				bool twin = false;

				if (cnt > 0)
				{
					for (int j = 0; j < N; j++)
					{
						if (j == k)
						{
							continue;
						}

						double dL = pal[k * 3] - pal[j * 3];
						double da = pal[k * 3 + 1] - pal[j * 3 + 1];
						double db = pal[k * 3 + 2] - pal[j * 3 + 2];

						if ((dL * dL) + (da * da) + (db * db) < th2)
						{
							twin = true;
							break;
						}
					}
				}

				if (cnt > 0 && !twin)
				{
					nL = sL / cnt;
					na = sA / cnt;
					nb = sB / cnt;
				}
				else
				{
					int bi = rng.Next(nS);
					double bd = dist[bi];

					for (int t = 1; t < 8; t++)
					{
						int c = rng.Next(nS);

						if (dist[c] > bd)
						{
							bd = dist[c];
							bi = c;
						}
					}

					nL = sLab[bi * 3];
					na = sLab[bi * 3 + 1];
					nb = sLab[bi * 3 + 2];
				}
			}
			else
			{
				// ランダム移動の一手。温度に比例した幅で揺らし、局所最適からの脱出を担う。
				double amp = baseMove * (T / T0);
				nL = pal[k * 3] + (((rng.NextDouble() * 2.0) - 1.0) * amp);
				na = pal[k * 3 + 1] + (((rng.NextDouble() * 2.0) - 1.0) * amp);
				nb = pal[k * 3 + 2] + (((rng.NextDouble() * 2.0) - 1.0) * amp);
			}

			double deltaSum = TrialMove(k, nL, na, nb);
			double deltaMean = deltaSum / nS;
			bool accept = deltaMean <= 0.0;

			if (!accept && rng.NextDouble() < Math.Exp(-deltaMean / T))
			{
				accept = true;
			}

			if (accept)
			{
				pal[k * 3] = nL;
				pal[k * 3 + 1] = na;
				pal[k * 3 + 2] = nb;

				for (int c = 0; c < nc; c++)
				{
					int i = changed[c];
					assign[i] = changedAssign[c];
					dist[i] = changedDist[c];
				}

				totalCost += deltaSum;

				if (totalCost < bestCost)
				{
					bestCost = totalCost;
					Array.Copy(pal, bestPal, N * 3);
				}
			}

			T *= alpha;

			if (progress is not null && it % progStep == 0)
			{
				progress.Report(it / (double)iters);
			}
		}

		var result = new List<(byte R, byte G, byte B)>(N);

		for (int i = 0; i < N; i++)
		{
			result.Add(ColorMetrics.LabToRgb(bestPal[i * 3], bestPal[i * 3 + 1], bestPal[i * 3 + 2]));
		}

		return result;
	}




	/// <summary>
	/// 焼きなまし法の学習標本を Lab で組む。彩度・希少度の重みが共に0なら全標本から等間隔に間引く。
	/// 重みがあれば、高彩度・低密度(希少)の色を出現頻度を超えて多く採る復元抽出で max 件を引き、面積の小さい差し色を拾わせる。
	/// 返り値は L・a・b を交互に並べた配列で、件数を nS に返す。
	/// </summary>
	private static double[] BuildWeightedSaSample(IReadOnlyList<(byte R, byte G, byte B)> pixels, (double L, double A, double B)[] pixLab, int max, int satW, int rareW, Random rng, out int nS)
	{
		int n = pixLab.Length;

		if (satW <= 0 && rareW <= 0)
		{
			if (n <= max)
			{
				nS = n;
				var flat = new double[n * 3];

				for (int i = 0; i < n; i++)
				{
					flat[i * 3] = pixLab[i].L;
					flat[i * 3 + 1] = pixLab[i].A;
					flat[i * 3 + 2] = pixLab[i].B;
				}

				return flat;
			}

			int stride = (n + max - 1) / max;
			var picked = new List<int>();

			for (int i = 0; i < n; i += stride)
			{
				picked.Add(i);
			}

			nS = picked.Count;
			var arr = new double[nS * 3];

			for (int i = 0; i < nS; i++)
			{
				arr[i * 3] = pixLab[picked[i]].L;
				arr[i * 3 + 1] = pixLab[picked[i]].A;
				arr[i * 3 + 2] = pixLab[picked[i]].B;
			}

			return arr;
		}

		// 密度。粗い Lab ヒストグラム(16^3)の所属ビンの個数を各標本の密度とし、希少度はその逆にする。
		const int bins = 16;
		var hist = new int[bins * bins * bins];

		for (int i = 0; i < n; i++)
		{
			hist[LabBin(pixLab[i], bins)]++;
		}

		int maxDensity = 1;

		for (int i = 0; i < n; i++)
		{
			int d = hist[LabBin(pixLab[i], bins)];

			if (d > maxDensity)
			{
				maxDensity = d;
			}
		}

		var cum = new double[n];
		double acc = 0.0;

		for (int i = 0; i < n; i++)
		{
			double chroma = Math.Sqrt((pixLab[i].A * pixLab[i].A) + (pixLab[i].B * pixLab[i].B));
			double chromaNorm = Math.Min(1.0, chroma / 60.0);
			double rareNorm = 1.0 - (hist[LabBin(pixLab[i], bins)] / (double)maxDensity);
			acc += (1.0 + (satW * chromaNorm)) * (1.0 + (rareW * rareNorm));
			cum[i] = acc;
		}

		nS = max;
		var outLab = new double[max * 3];

		for (int k = 0; k < max; k++)
		{
			double target = rng.NextDouble() * acc;
			int lo = 0;
			int hi = n - 1;

			while (lo < hi)
			{
				int mid = (lo + hi) >> 1;

				if (cum[mid] < target)
				{
					lo = mid + 1;
				}
				else
				{
					hi = mid;
				}
			}

			outLab[k * 3] = pixLab[lo].L;
			outLab[k * 3 + 1] = pixLab[lo].A;
			outLab[k * 3 + 2] = pixLab[lo].B;
		}

		return outLab;
	}




	/// <summary>
	/// Lab を粗い 16^3 ヒストグラムのビン番号へ写す。L は 0–100、a・b は −100–100 を範囲に丸め込む。希少度重みの密度計算に使う。
	/// </summary>
	private static int LabBin((double L, double A, double B) lab, int bins)
	{
		int li = (int)(lab.L / 100.0 * bins);
		li = li < 0 ? 0 : li >= bins ? bins - 1 : li;
		int ai = (int)((lab.A + 100.0) / 200.0 * bins);
		ai = ai < 0 ? 0 : ai >= bins ? bins - 1 : ai;
		int bi = (int)((lab.B + 100.0) / 200.0 * bins);
		bi = bi < 0 ? 0 : bi >= bins ? bins - 1 : bi;
		return ((li * bins) + ai) * bins + bi;
	}




	/// <summary>
	/// どの手法の出力も最終契約へ整える。
	/// まず不足分を誤差最大の標本色で足して指定色数に届かせ、次に担当画素ゼロの色を誤差最大の領域へ移して死に色を解き、最後に近接重複を片側だけ未表現の領域へ移す。
	/// 画像が乏しく移す先が無いときは重複が残るが、色数は必ず保つ。判定はいずれも標本画素の Lab 二乗距離で行う。
	/// </summary>
	private static void RefinePalette(List<(byte R, byte G, byte B)> palette, (double L, double A, double B)[] pixLab, IReadOnlyList<(byte R, byte G, byte B)> pixels, int count, Random rng)
	{
		GrowToCount(palette, pixLab, pixels, count, rng);
		RescueDeadColors(palette, pixLab, pixels, rng);
		MergeNearDuplicates(palette, pixLab, pixels, rng);
	}




	/// <summary>
	/// パレットを指定色数まで増やす。
	/// 不足の間、現在のパレットから最も遠い(誤差最大の)標本色を加えていく。豊かな画像では未表現の領域を埋めて別色が増え、乏しい画像では既存色の重複になる。
	/// 多すぎる場合は末尾を切る。
	/// </summary>
	private static void GrowToCount(List<(byte R, byte G, byte B)> palette, (double L, double A, double B)[] pixLab, IReadOnlyList<(byte R, byte G, byte B)> pixels, int count, Random rng)
	{
		int n = pixels.Count;

		while (palette.Count < count && n > 0)
		{
			double[] err = NearestErrors(palette, pixLab);
			int si = PickHighErrorSample(err, rng);
			palette.Add(pixels[si]);
		}

		if (palette.Count > count)
		{
			palette.RemoveRange(count, palette.Count - count);
		}
	}




	/// <summary>
	/// 担当画素ゼロの色を解く。標本を最寄りへ割り当て、誰からも選ばれない色を見つけたら誤差最大の領域(の標本色)へ移す。
	/// 移すたび全体の誤差は下がる。画像が完全に表現済み(最大誤差がほぼ0)なら、残る担当ゼロ色は避けられない重複なので打ち切る。
	/// </summary>
	private static void RescueDeadColors(List<(byte R, byte G, byte B)> palette, (double L, double A, double B)[] pixLab, IReadOnlyList<(byte R, byte G, byte B)> pixels, Random rng)
	{
		int N = palette.Count;
		int n = pixels.Count;

		if (N < 1 || n < 1)
		{
			return;
		}

		int guard = N * 4;

		while (guard-- > 0)
		{
			(double L, double A, double B)[] palLab = ToLab(palette);
			AssignUsage(palLab, pixLab, out double[] err, out int[] usage);
			int dead = -1;

			for (int k = 0; k < N; k++)
			{
				if (usage[k] == 0)
				{
					dead = k;
					break;
				}
			}

			if (dead < 0)
			{
				break;
			}

			double maxErr = 0.0;

			for (int i = 0; i < n; i++)
			{
				if (err[i] > maxErr)
				{
					maxErr = err[i];
				}
			}

			if (maxErr <= DeadColorEpsilon)
			{
				break;
			}

			int si = PickHighErrorSample(err, rng);
			palette[dead] = pixels[si];
		}
	}




	/// <summary>
	/// 近接重複を除く。Lab 上で <see cref="MergeDeltaE"/> 未満に重なった2色は1枠分の働きしかしないため、占有の小さい側を誤差最大の標本へ移す。
	/// 移送は全体の二乗誤差が実際に下がるときだけ確定し、両色とも多数の画素を担うペアは却下されて配色は壊れない。移す先の無い乏しい画像では重複が残る。
	/// </summary>
	private static void MergeNearDuplicates(List<(byte R, byte G, byte B)> palette, (double L, double A, double B)[] pixLab, IReadOnlyList<(byte R, byte G, byte B)> pixels, Random rng)
	{
		int N = palette.Count;
		int n = pixels.Count;

		if (N < 3 || n < 1)
		{
			return;
		}

		double th2 = MergeDeltaE * MergeDeltaE;
		var failed = new HashSet<long>();
		int guard = N * 2;

		while (guard-- > 0)
		{
			(double L, double A, double B)[] palLab = ToLab(palette);
			AssignUsage(palLab, pixLab, out double[] err, out int[] usage);
			int pa = -1;
			int pb = -1;
			double pbest = th2;

			for (int i = 0; i < N; i++)
			{
				if (usage[i] == 0)
				{
					continue;
				}

				for (int j = i + 1; j < N; j++)
				{
					if (usage[j] == 0 || failed.Contains(((long)i * N) + j))
					{
						continue;
					}

					double dL = palLab[i].L - palLab[j].L;
					double da = palLab[i].A - palLab[j].A;
					double db = palLab[i].B - palLab[j].B;
					double d = (dL * dL) + (da * da) + (db * db);

					if (d < pbest)
					{
						pbest = d;
						pa = i;
						pb = j;
					}
				}
			}

			if (pa < 0)
			{
				break;
			}

			int move = usage[pa] <= usage[pb] ? pa : pb;
			double before = 0.0;

			for (int i = 0; i < n; i++)
			{
				before += err[i];
			}

			int si = PickHighErrorSample(err, rng);
			double after = TotalErrorWithReplacement(palLab, move, pixels[si], pixLab);

			if (after < before)
			{
				palette[move] = pixels[si];
			}
			else
			{
				failed.Add(((long)pa * N) + pb);
			}
		}
	}




	/// <summary>
	/// パレットの各色を Lab へ写した配列を作る。整え処理の最近傍計算で使い回す。
	/// </summary>
	private static (double L, double A, double B)[] ToLab(List<(byte R, byte G, byte B)> palette)
	{
		var arr = new (double L, double A, double B)[palette.Count];

		for (int i = 0; i < palette.Count; i++)
		{
			arr[i] = ColorMetrics.RgbToLab(palette[i].R, palette[i].G, palette[i].B);
		}

		return arr;
	}




	/// <summary>
	/// 各標本画素を最寄りのパレット色へ割り当て、最近傍までの二乗距離(err)と各色の担当画素数(usage)を求める。死に色・近接重複の判定に使う。
	/// </summary>
	private static void AssignUsage((double L, double A, double B)[] palLab, (double L, double A, double B)[] pixLab, out double[] err, out int[] usage)
	{
		int n = pixLab.Length;
		err = new double[n];
		usage = new int[palLab.Length];

		for (int i = 0; i < n; i++)
		{
			int bi = NearestLab(palLab, pixLab[i]);
			err[i] = Dist2(palLab[bi], pixLab[i]);
			usage[bi]++;
		}
	}




	/// <summary>
	/// 各標本画素から最寄りのパレット色までの二乗距離を求める。色を増やす際、最も遠い(未表現の)標本を選ぶのに使う。パレットが空のときは全標本を最大距離にする。
	/// </summary>
	private static double[] NearestErrors(List<(byte R, byte G, byte B)> palette, (double L, double A, double B)[] pixLab)
	{
		int n = pixLab.Length;
		var err = new double[n];

		if (palette.Count == 0)
		{
			for (int i = 0; i < n; i++)
			{
				err[i] = double.MaxValue;
			}

			return err;
		}

		(double L, double A, double B)[] palLab = ToLab(palette);

		for (int i = 0; i < n; i++)
		{
			err[i] = Dist2(palLab[NearestLab(palLab, pixLab[i])], pixLab[i]);
		}

		return err;
	}




	/// <summary>
	/// 誤差の大きい標本を1つ選ぶ。候補を8点引いて最大誤差のものを採る。死に色や冗長色の移送先(未表現の領域)を散らして選ぶのに使う。
	/// </summary>
	private static int PickHighErrorSample(double[] err, Random rng)
	{
		int n = err.Length;
		int bi = rng.Next(n);
		double bd = err[bi];

		for (int t = 1; t < 8; t++)
		{
			int c = rng.Next(n);

			if (err[c] > bd)
			{
				bd = err[c];
				bi = c;
			}
		}

		return bi;
	}




	/// <summary>
	/// パレットの move 番の色を cand へ差し替えたときの、全標本の二乗誤差の総和を求める。近接重複の移送が純益(誤差の純減)になるかの判定に使う。
	/// </summary>
	private static double TotalErrorWithReplacement((double L, double A, double B)[] palLab, int move, (byte R, byte G, byte B) cand, (double L, double A, double B)[] pixLab)
	{
		(double L, double A, double B) c = ColorMetrics.RgbToLab(cand.R, cand.G, cand.B);
		int N = palLab.Length;
		double total = 0.0;

		for (int i = 0; i < pixLab.Length; i++)
		{
			double bestVal = double.MaxValue;

			for (int j = 0; j < N; j++)
			{
				(double L, double A, double B) pl = j == move ? c : palLab[j];
				double d = Dist2(pl, pixLab[i]);

				if (d < bestVal)
				{
					bestVal = d;
				}
			}

			total += bestVal;
		}

		return total;
	}




	/// <summary>
	/// 八分木による減色。各画素を RGB の上位ビットから順に8段の木へ落とし込み、葉(色)の数が色数を超える間は最も画素の少ない内部ノードを葉へ畳んで減らす。
	/// 残った葉の平均色を代表色にする。
	/// </summary>
	private static List<(byte R, byte G, byte B)> OctreeQuantize(IReadOnlyList<(byte R, byte G, byte B)> pixels, int count)
	{
		var tree = new Octree(count);

		foreach ((byte R, byte G, byte B) p in pixels)
		{
			tree.Add(p.R, p.G, p.B);
		}

		return tree.Palette();
	}




	/// <summary>
	/// 縮約できる八分木。各ノードは部分木の画素数と RGB の総和を持ち、内部ノードは階層ごとの縮約候補一覧に登録する。
	/// 葉が上限を超える間は最も深い階層の最小ノードを畳んで葉数を減らす。最も深い階層から畳むため、畳む対象の子は必ず葉になる。
	/// </summary>
	private sealed class Octree
	{
		private const int Levels = 8;

		private sealed class Node
		{
			public int Level;
			public bool IsLeaf;
			public long Count;
			public long SumR;
			public long SumG;
			public long SumB;
			public int ChildCount;
			public readonly Node?[] Children = new Node?[8];
		}

		private readonly Node _root = new Node { Level = 0 };
		private readonly List<Node>[] _reducible;
		private readonly int _maxColors;
		private int _leafCount;

		public Octree(int maxColors)
		{
			_maxColors = Math.Max(1, maxColors);
			_reducible = new List<Node>[Levels];

			for (int i = 0; i < Levels; i++)
			{
				_reducible[i] = new List<Node>();
			}
		}

		/// <summary>
		/// 1画素を木へ加える。各段で RGB の該当ビットから子を選んで降り、通り道の全ノードへ画素数と色の総和を積む。
		/// 縮約されて葉になったノードに行き当たったらそれ以上は降りず、その葉へ集計だけ足す。
		/// 縮約済みの葉へ降りて枝を作り直すと葉数の計数が壊れて縮約が止まらなくなるため、葉では必ず止める。新たな最下段の葉ができたら葉数を数え、上限を超えていれば縮約する。
		/// </summary>
		public void Add(byte r, byte g, byte b)
		{
			Node node = _root;
			node.Count++;
			node.SumR += r;
			node.SumG += g;
			node.SumB += b;

			for (int level = 0; level < Levels; level++)
			{
				// 縮約された葉。これ以上は降りず、集計はこの葉に積んである(直前の降下で足した)ので何もしない。
				if (node.IsLeaf)
				{
					break;
				}

				int bit = 7 - level;
				int idx = (((r >> bit) & 1) << 2) | (((g >> bit) & 1) << 1) | ((b >> bit) & 1);
				Node? child = node.Children[idx];

				if (child is null)
				{
					// このノードが初めて子を持ち、内部ノード(縮約候補)になった瞬間に登録する。
					if (node.ChildCount == 0)
					{
						_reducible[level].Add(node);
					}

					child = new Node { Level = level + 1 };
					node.Children[idx] = child;
					node.ChildCount++;

					if (child.Level == Levels)
					{
						_leafCount++;
					}
				}

				node = child;
				node.Count++;
				node.SumR += r;
				node.SumG += g;
				node.SumB += b;
			}

			while (_leafCount > _maxColors)
			{
				Reduce();
			}
		}

		/// <summary>
		/// 葉が上限を超えたとき、最も深い縮約候補の階層から画素数最小のノードを選び、その子(葉)をすべて畳んでノード自身を葉にする。
		/// 通り道で総和を積んであるためノードは既に部分木の集計を持ち、子を切り離して葉にするだけでよい。
		/// </summary>
		private void Reduce()
		{
			int level = Levels - 1;

			while (level >= 0 && _reducible[level].Count == 0)
			{
				level--;
			}

			if (level < 0)
			{
				return;
			}

			List<Node> list = _reducible[level];
			int pick = 0;

			for (int i = 1; i < list.Count; i++)
			{
				if (list[i].Count < list[pick].Count)
				{
					pick = i;
				}
			}

			Node node = list[pick];
			list.RemoveAt(pick);
			_leafCount -= node.ChildCount;

			for (int i = 0; i < 8; i++)
			{
				node.Children[i] = null;
			}

			node.ChildCount = 0;
			node.IsLeaf = true;
			_leafCount++;
		}

		/// <summary>
		/// 残った葉(縮約された内部ノードと最下段の葉)の平均色を集めてパレットにする。
		/// </summary>
		public List<(byte R, byte G, byte B)> Palette()
		{
			var result = new List<(byte R, byte G, byte B)>();
			Collect(_root, result);
			return result;
		}

		private void Collect(Node node, List<(byte R, byte G, byte B)> result)
		{
			if (node.IsLeaf || node.Level == Levels)
			{
				if (node.Count > 0)
				{
					result.Add(((byte)(node.SumR / node.Count), (byte)(node.SumG / node.Count), (byte)(node.SumB / node.Count)));
				}

				return;
			}

			for (int i = 0; i < 8; i++)
			{
				Node? child = node.Children[i];

				if (child is not null)
				{
					Collect(child, result);
				}
			}
		}
	}




	/// <summary>
	/// 重心の Lab と RGB(平均を保つための実数)を一括で設定する。
	/// </summary>
	private static void SetCentroid((double L, double A, double B)[] centLab, double[] centR, double[] centG, double[] centB, int index, (byte R, byte G, byte B) color, (double L, double A, double B) lab)
	{
		centLab[index] = lab;
		centR[index] = color.R;
		centG[index] = color.G;
		centB[index] = color.B;
	}




	/// <summary>
	/// 重みの2乗和に比例した確率で要素番号を1つ選ぶ。k-means++ の種の選択に使う。
	/// </summary>
	private static int WeightedPick(double[] weights, Random rng)
	{
		double sum = 0.0;

		foreach (double w in weights)
		{
			sum += w;
		}

		if (sum <= 0.0)
		{
			return rng.Next(weights.Length);
		}

		double target = rng.NextDouble() * sum;

		for (int i = 0; i < weights.Length; i++)
		{
			target -= weights[i];

			if (target <= 0.0)
			{
				return i;
			}
		}

		return weights.Length - 1;
	}




	/// <summary>
	/// パレットの中で指定の Lab 色に最も近い要素番号を返す。
	/// </summary>
	private static int NearestLab((double L, double A, double B)[] palette, (double L, double A, double B) p)
	{
		int best = 0;
		double bestDist = double.MaxValue;

		for (int i = 0; i < palette.Length; i++)
		{
			double d = Dist2(palette[i], p);

			if (d < bestDist)
			{
				bestDist = d;
				best = i;
			}
		}

		return best;
	}




	/// <summary>
	/// 2つの Lab 色のユークリッド距離の2乗(CIE76)。最近傍の大小判定にのみ使うため平方根は取らない。
	/// </summary>
	private static double Dist2((double L, double A, double B) p, (double L, double A, double B) q)
	{
		double dl = p.L - q.L;
		double da = p.A - q.A;
		double db = p.B - q.B;
		return (dl * dl) + (da * da) + (db * db);
	}




	/// <summary>
	/// 箱の R・G・B 各チャンネルの値域(最大−最小)をまとめて求める。中央値分割法で分割する軸を選ぶのに使う。
	/// </summary>
	private static void ChannelRanges(List<(byte R, byte G, byte B)> box, out int rRange, out int gRange, out int bRange)
	{
		byte rLo = 255;
		byte rHi = 0;
		byte gLo = 255;
		byte gHi = 0;
		byte bLo = 255;
		byte bHi = 0;

		foreach ((byte R, byte G, byte B) p in box)
		{
			if (p.R < rLo) rLo = p.R;
			if (p.R > rHi) rHi = p.R;
			if (p.G < gLo) gLo = p.G;
			if (p.G > gHi) gHi = p.G;
			if (p.B < bLo) bLo = p.B;
			if (p.B > bHi) bHi = p.B;
		}

		rRange = rHi - rLo;
		gRange = gHi - gLo;
		bRange = bHi - bLo;
	}




	/// <summary>
	/// 画素の指定チャンネル(0=R・1=G・2=B)の値を返す。中央値分割法の並べ替えに使う。
	/// </summary>
	private static byte Channel((byte R, byte G, byte B) p, int channel)
	{
		return channel switch
		{
			0 => p.R,
			1 => p.G,
			_ => p.B,
		};
	}
}
