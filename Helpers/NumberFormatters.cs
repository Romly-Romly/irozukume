// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using Windows.Globalization.NumberFormatting;

namespace Irozukume.Helpers;

// 各タブの数値入力欄(NumberBox)で使う表示書式。整数表示と小数2桁表示を、生成のたびの作り直しを避けて使い回せるよう静的に持つ。桁区切りは入れず、入力欄の数値をそのまま整数・小数で見せる。FractionDigits は最小表示桁数しか決めないため、端数を丸めて余分な桁を出さないよう IncrementNumberRounder を併せて与える。
public static class NumberFormatters
{
	// 整数表示。RGB・CMYK・色相・彩度・明度・輝度・色差などの入力欄で、小数を出さず整数に丸めて見せる。
	public static DecimalFormatter Integer { get; } = MakeFormatter(0, 1.0);

	// 小数2桁表示。不透明度を 0.0–1.0 で扱う表記や、CIE LCH の彩度の入力欄で使う。
	public static DecimalFormatter TwoDecimal { get; } = MakeFormatter(2, 0.01);

	// 小数3桁表示。OKLCH の小さな彩度(0–0.4 程度)の入力欄で、刻みを細かく見せるために使う。
	public static DecimalFormatter ThreeDecimal { get; } = MakeFormatter(3, 0.001);

	// 16進2桁表示。R・G・B を 00–FF で扱う表記の入力欄で使う。0–255 の値を大文字2桁の16進で見せ、入力も16進で解釈する。
	public static HexByteNumberFormatter Hex { get; } = new HexByteNumberFormatter();




	// 指定の小数桁数と丸め単位で表示する DecimalFormatter を作る。丸め単位ごとに端数を四捨五入し、最小・最大とも fractionDigits 桁にそろえて余分な桁を出さない。
	private static DecimalFormatter MakeFormatter(int fractionDigits, double increment)
	{
		return new DecimalFormatter
		{
			IntegerDigits = 1,
			FractionDigits = fractionDigits,
			IsGrouped = false,
			NumberRounder = new IncrementNumberRounder
			{
				Increment = increment,
				RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero,
			},
		};
	}
}
