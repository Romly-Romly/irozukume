// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Irozukume.Models;

namespace Irozukume.Helpers;

// Mix タブの多点グラデーション平面を描く。各ポッチ(正規化座標 0–1・左上原点)に置いた色を、選んだ空間補間方式(逆距離加重・ガウス・最近傍)で重み付けし、選んだ色空間(OKLCH・OKLab・CIE LCH・CIE Lab・HSL・Linear sRGB・sRGB)で混ぜて画素を塗る。色相を持つ色空間は回り方(近い側・遠回り)を選べる。シャープネスは重みの効き具合(とろける⇔くっきり)を連続で変える。色制限が有効なら丸めも掛ける。色の空間座標への展開はポッチ数ぶんだけ一度行い、画素ごとには重みと混色だけを回す。
public static class MixField
{
	// 1つのポッチ。位置(正規化 0–1)とフルカラーの色を持つ。
	public readonly struct Stop
	{
		public Stop(double x, double y, Color color)
		{
			X = x;
			Y = y;
			Color = color;
		}

		public double X { get; }
		public double Y { get; }
		public Color Color { get; }
	}




	// 逆距離加重の指数の範囲。シャープネス0でやわらかく(指数小)、1でくっきり(指数大)へ。
	private const double MinPower = 1.0;
	private const double MaxPower = 4.0;

	// ガウス重みの広がり(正規化距離)の範囲。シャープネス0で広く、1で狭く。
	private const double MaxSigma = 0.45;
	private const double MinSigma = 0.12;

	// ポッチへ十分近い画素は、そのポッチの色をそのまま採るための距離しきい値。逆距離加重で重みが発散する近傍を安定させ、ポッチ位置で色が正確に一致するようにする。
	private const double SnapDistance = 1e-4;




	// 指定の画素サイズ・ポッチ列・空間補間方式・色空間・色相の回り方・シャープネス・色制限設定でグラデーション平面を描いた WriteableBitmap を作る。全面を不透明で塗る。ポッチが空のときは全面を透明にする。
	public static WriteableBitmap Create(int pixelWidth, int pixelHeight, IReadOnlyList<Stop> stops, MixInterpolation method, MixColorSpace space, MixHueDirection hueDir, double sharpness, SnapSettings snap)
	{
		var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
		byte[] pixels = new byte[pixelWidth * pixelHeight * 4];

		int count = stops.Count;

		if (count == 0)
		{
			using (Stream emptyStream = bitmap.PixelBuffer.AsStream())
			{
				emptyStream.Write(pixels, 0, pixels.Length);
			}

			bitmap.Invalidate();
			return bitmap;
		}

		double[] px = new double[count];
		double[] py = new double[count];
		double[] c0 = new double[count];
		double[] c1 = new double[count];
		double[] c2 = new double[count];

		for (int i = 0; i < count; i++)
		{
			px[i] = stops[i].X;
			py[i] = stops[i].Y;
			Decompose(space, stops[i].Color, out c0[i], out c1[i], out c2[i]);
		}

		double power = MinPower + (Math.Clamp(sharpness, 0.0, 1.0) * (MaxPower - MinPower));
		double sigma = MaxSigma - (Math.Clamp(sharpness, 0.0, 1.0) * (MaxSigma - MinSigma));
		(double scaleX, double scaleY) = AspectScale(pixelWidth, pixelHeight);

		// 最近傍探索の前計算表を、並列ループの前に単一スレッドで温めておく。
		ColorConversion.Snap(snap, 0, 0, 0);

		Parallel.For(0, pixelHeight, y =>
		{
			double v = (y + 0.5) / pixelHeight;
			int rowBase = (y * pixelWidth) * 4;
			double[] weights = new double[count];

			for (int x = 0; x < pixelWidth; x++)
			{
				double u = (x + 0.5) / pixelWidth;
				ComputeWeights(method, power, sigma, scaleX, scaleY, px, py, u, v, weights);
				Color color = Blend(space, hueDir, c0, c1, c2, weights);
				(byte r, byte g, byte b) = ColorConversion.Snap(snap, color.R, color.G, color.B);

				int index = rowBase + (x * 4);
				pixels[index] = b;
				pixels[index + 1] = g;
				pixels[index + 2] = r;
				pixels[index + 3] = 0xFF;
			}
		});

		using (Stream stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(pixels, 0, pixels.Length);
		}

		bitmap.Invalidate();
		return bitmap;
	}




	// 指定の1点(正規化座標)での補間色を返す。Mix タブのつまみが、その点の混色を編集色へ反映するのに使う。丸めは掛けない。距離を画面(ピクセル)空間で測るため、平面の表示寸法(aspectWidth・aspectHeight)を渡し、下地と同じ縦横比で重み付けする。
	public static Color Sample(IReadOnlyList<Stop> stops, double u, double v, MixInterpolation method, MixColorSpace space, MixHueDirection hueDir, double sharpness, double aspectWidth, double aspectHeight)
	{
		int count = stops.Count;

		if (count == 0)
		{
			return Color.FromArgb(0, 0, 0, 0);
		}

		double[] px = new double[count];
		double[] py = new double[count];
		double[] c0 = new double[count];
		double[] c1 = new double[count];
		double[] c2 = new double[count];

		for (int i = 0; i < count; i++)
		{
			px[i] = stops[i].X;
			py[i] = stops[i].Y;
			Decompose(space, stops[i].Color, out c0[i], out c1[i], out c2[i]);
		}

		double power = MinPower + (Math.Clamp(sharpness, 0.0, 1.0) * (MaxPower - MinPower));
		double sigma = MaxSigma - (Math.Clamp(sharpness, 0.0, 1.0) * (MaxSigma - MinSigma));
		(double scaleX, double scaleY) = AspectScale(aspectWidth, aspectHeight);

		double[] weights = new double[count];
		ComputeWeights(method, power, sigma, scaleX, scaleY, px, py, u, v, weights);
		return Blend(space, hueDir, c0, c1, c2, weights);
	}




	// 画素(u, v)に対する各ポッチの重みを weights へ書き込む。総和が 1 になるよう正規化する。power・sigma はシャープネスから決まる効き具合。距離は正規化座標の差へ scaleX・scaleY を掛けて画面(ピクセル)空間で測り、平面が非正方形でも重みの落ち方や最近傍の境が向きで歪まないようにする。
	private static void ComputeWeights(MixInterpolation method, double power, double sigma, double scaleX, double scaleY, double[] px, double[] py, double u, double v, double[] weights)
	{
		int count = weights.Length;

		if (method == MixInterpolation.Nearest)
		{
			int best = 0;
			double bestDist = double.MaxValue;

			for (int i = 0; i < count; i++)
			{
				double dx = (u - px[i]) * scaleX;
				double dy = (v - py[i]) * scaleY;
				double dist = (dx * dx) + (dy * dy);

				if (dist < bestDist)
				{
					bestDist = dist;
					best = i;
				}
			}

			for (int i = 0; i < count; i++)
			{
				weights[i] = i == best ? 1.0 : 0.0;
			}

			return;
		}

		double sum = 0.0;

		for (int i = 0; i < count; i++)
		{
			double dx = (u - px[i]) * scaleX;
			double dy = (v - py[i]) * scaleY;
			double distSq = (dx * dx) + (dy * dy);

			// ポッチへ十分近い画素は、そのポッチの色をそのまま採る。重みの発散を避け、ポッチ位置で色が正確に一致する。
			if (distSq <= SnapDistance * SnapDistance)
			{
				for (int j = 0; j < count; j++)
				{
					weights[j] = j == i ? 1.0 : 0.0;
				}

				return;
			}

			double w = method == MixInterpolation.Gaussian
				? Math.Exp(-distSq / (2.0 * sigma * sigma))
				: 1.0 / Math.Pow(distSq, power / 2.0);

			weights[i] = w;
			sum += w;
		}

		if (sum <= 0.0)
		{
			// すべての重みが事実上 0(ガウスで全ポッチが遠い)のときは等分にして破綻を避ける。
			for (int i = 0; i < count; i++)
			{
				weights[i] = 1.0 / count;
			}

			return;
		}

		for (int i = 0; i < count; i++)
		{
			weights[i] /= sum;
		}
	}




	// 平面の表示寸法から、距離計算へ掛ける軸ごとのスケールを返す。短辺を 1 とし、長辺側をその比率ぶん引き伸ばすことで、距離を画面(ピクセル)空間で測る。正方形では (1, 1) を返し、正規化座標での距離と一致する。寸法が不正(0 以下)なら等方の (1, 1) を返す。
	private static (double X, double Y) AspectScale(double width, double height)
	{
		double min = Math.Min(width, height);

		if (min <= 0.0)
		{
			return (1.0, 1.0);
		}

		return (width / min, height / min);
	}




	// 色を、選んだ色空間の3成分へ展開する。色相を持つ色空間(OKLCH・CIE LCH・HSL)は明度相当・彩度相当・色相(度)を、直交系(OKLab・CIE Lab)は明度と2軸を、Linear sRGB は線形光の各チャンネル(0–1)を、sRGB は各チャンネル(0–255)を持つ。
	private static void Decompose(MixColorSpace space, Color color, out double a, out double b, out double c)
	{
		switch (space)
		{
			case MixColorSpace.Oklch:
			{
				(double l, double chroma, double hueRad) = OklabColor.ToOklch(color);
				a = l;
				b = chroma;
				c = NormalizeDeg(hueRad * 180.0 / Math.PI);
				return;
			}

			case MixColorSpace.Lch:
			{
				(double l, double chroma, double hueDeg) = LchColor.FromRgb(LchSpace.CieLch, color.R, color.G, color.B);
				a = l;
				b = chroma;
				c = NormalizeDeg(hueDeg);
				return;
			}

			case MixColorSpace.Oklab:
			{
				(double l, double chroma, double hueRad) = OklabColor.ToOklch(color);
				a = l;
				b = chroma * Math.Cos(hueRad);
				c = chroma * Math.Sin(hueRad);
				return;
			}

			case MixColorSpace.Lab:
			{
				(double l, double labA, double labB) = LabColor.FromRgb(LchSpace.CieLch, color.R, color.G, color.B);
				a = l;
				b = labA;
				c = labB;
				return;
			}

			case MixColorSpace.Hsl:
			{
				(double hueDeg, double sat, double light) = ColorConversion.RgbToHsl(color.R, color.G, color.B);
				a = light;
				b = sat;
				c = NormalizeDeg(hueDeg);
				return;
			}

			case MixColorSpace.LinearRgb:
			{
				a = SrgbToLinear(color.R / 255.0);
				b = SrgbToLinear(color.G / 255.0);
				c = SrgbToLinear(color.B / 255.0);
				return;
			}

			default:
			{
				a = color.R;
				b = color.G;
				c = color.B;
				return;
			}
		}
	}




	// 各成分配列と重みから、選んだ色空間で混ぜた sRGB の不透明色を返す。色相を持つ空間は彩度で重み付けした色相平均(回り方で近い側/遠回りを切り替え)で混ぜ、無彩色の色相が混色を乱さないようにする。
	private static Color Blend(MixColorSpace space, MixHueDirection hueDir, double[] c0, double[] c1, double[] c2, double[] weights)
	{
		int count = weights.Length;

		switch (space)
		{
			case MixColorSpace.Oklch:
			case MixColorSpace.Lch:
			case MixColorSpace.Hsl:
			{
				double comp0 = 0.0;
				double comp1 = 0.0;

				for (int i = 0; i < count; i++)
				{
					comp0 += weights[i] * c0[i];
					comp1 += weights[i] * c1[i];
				}

				double hueDeg = CombineHue(hueDir, c2, c1, weights);

				if (space == MixColorSpace.Oklch)
				{
					return OklabColor.FromOklch(comp0, comp1, hueDeg * Math.PI / 180.0);
				}

				if (space == MixColorSpace.Lch)
				{
					return LchColor.ToRgb(LchSpace.CieLch, comp0, comp1, hueDeg);
				}

				(byte hr, byte hg, byte hb) = ColorConversion.HslToRgb(hueDeg, Math.Clamp(comp1, 0.0, 1.0), Math.Clamp(comp0, 0.0, 1.0));
				return Color.FromArgb(0xFF, hr, hg, hb);
			}

			case MixColorSpace.Oklab:
			{
				double l = 0.0;
				double oa = 0.0;
				double ob = 0.0;

				for (int i = 0; i < count; i++)
				{
					l += weights[i] * c0[i];
					oa += weights[i] * c1[i];
					ob += weights[i] * c2[i];
				}

				double chroma = Math.Sqrt((oa * oa) + (ob * ob));
				double hue = Math.Atan2(ob, oa);
				return OklabColor.FromOklch(l, chroma, hue);
			}

			case MixColorSpace.Lab:
			{
				double l = 0.0;
				double labA = 0.0;
				double labB = 0.0;

				for (int i = 0; i < count; i++)
				{
					l += weights[i] * c0[i];
					labA += weights[i] * c1[i];
					labB += weights[i] * c2[i];
				}

				return LabColor.ToRgb(LchSpace.CieLch, l, labA, labB);
			}

			case MixColorSpace.LinearRgb:
			{
				double lr = 0.0;
				double lg = 0.0;
				double lb = 0.0;

				for (int i = 0; i < count; i++)
				{
					lr += weights[i] * c0[i];
					lg += weights[i] * c1[i];
					lb += weights[i] * c2[i];
				}

				byte r = ToByte(LinearToSrgb(Math.Clamp(lr, 0.0, 1.0)) * 255.0);
				byte g = ToByte(LinearToSrgb(Math.Clamp(lg, 0.0, 1.0)) * 255.0);
				byte b = ToByte(LinearToSrgb(Math.Clamp(lb, 0.0, 1.0)) * 255.0);
				return Color.FromArgb(0xFF, r, g, b);
			}

			default:
			{
				double r = 0.0;
				double g = 0.0;
				double b = 0.0;

				for (int i = 0; i < count; i++)
				{
					r += weights[i] * c0[i];
					g += weights[i] * c1[i];
					b += weights[i] * c2[i];
				}

				return Color.FromArgb(0xFF, ToByte(r), ToByte(g), ToByte(b));
			}
		}
	}




	// 各ポッチの色相(度)を彩度と空間重みで重み付けして1つの色相(度)へまとめる。近い側は色相ベクトルの合成方向(最短弧)、遠回りは色相の値(度)の直線平均(0 度の継ぎ目を跨がない側)で混ぜる。彩度が事実上 0 のポッチばかりのときは空間重みだけで平均し、色相を保つ。
	private static double CombineHue(MixHueDirection hueDir, double[] hueDeg, double[] chroma, double[] weights)
	{
		int count = weights.Length;

		if (hueDir == MixHueDirection.Longer)
		{
			double acc = 0.0;
			double accW = 0.0;

			for (int i = 0; i < count; i++)
			{
				double w = weights[i] * chroma[i];
				acc += w * hueDeg[i];
				accW += w;
			}

			if (accW <= 0.0)
			{
				acc = 0.0;
				accW = 0.0;

				for (int i = 0; i < count; i++)
				{
					acc += weights[i] * hueDeg[i];
					accW += weights[i];
				}
			}

			return accW > 0.0 ? acc / accW : 0.0;
		}

		double hx = 0.0;
		double hy = 0.0;

		for (int i = 0; i < count; i++)
		{
			double w = weights[i] * chroma[i];
			double radians = hueDeg[i] * Math.PI / 180.0;
			hx += w * Math.Cos(radians);
			hy += w * Math.Sin(radians);
		}

		return NormalizeDeg(Math.Atan2(hy, hx) * 180.0 / Math.PI);
	}




	// 角度を 0–360 度へ正規化する。
	private static double NormalizeDeg(double deg)
	{
		return ((deg % 360.0) + 360.0) % 360.0;
	}




	// sRGB の1チャンネル(0–1)を線形光へ戻す。
	private static double SrgbToLinear(double c)
	{
		return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	}




	// 線形光(0–1)を sRGB の1チャンネル(0–1)へ符号化する。
	private static double LinearToSrgb(double c)
	{
		return c <= 0.0031308 ? 12.92 * c : (1.055 * Math.Pow(c, 1.0 / 2.4)) - 0.055;
	}




	// 0–255 の実数値を 0–255 のバイトへ丸める。
	private static byte ToByte(double v)
	{
		int i = (int)Math.Round(v);
		return (byte)Math.Clamp(i, 0, 255);
	}
}
