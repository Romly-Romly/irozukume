// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

namespace Irozukume.Models;

// Mix タブの自動アレンジで、ポッチをどの形に整列させるか。
// 四隅は左上から時計回りに四隅へ、5個目以降は中央へ置く。正多角形は中央を囲む正 N 角形の頂点へ(頂点の1つは真上)。縦一列・横一列は等間隔に並べる。
public enum MixArrange
{
	Corners,
	Polygon,
	Column,
	Row,
}
