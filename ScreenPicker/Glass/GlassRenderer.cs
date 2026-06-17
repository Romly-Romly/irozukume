// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;
using Irozukume.Glass;

namespace Irozukume.ScreenPicker.Glass;

// ガラス効果の合成器。各種マップとハイライトを焼いて保持し、毎描画で Win2D のエフェクトグラフを組んで本体形状へ収める。
// 屈折→可変ぼかし→彩度明度をエフェクトグラフで作り、白み・ハイライト・縁取り・光沢を描画段で重ねる。
internal sealed class GlassRenderer
{
	private GlassGeometry _geo;
	private CanvasBitmap? _dispMap;
	private CanvasBitmap? _blurMask;
	private CanvasBitmap? _enhMask;
	private CanvasBitmap? _tintMask;
	private CanvasBitmap? _gloss;
	private CanvasBitmap? _highlight;

	// 仰角追従の焼き直しで使い回す法線場と、量子化した仰角オフセットのバケットごとに焼いたハイライトのキャッシュ。法線は使い回すので各バケット初回だけ鏡面ループを回せばよい。
	private HighlightBaker.NormalSet? _normalSet;
	private readonly Dictionary<int, CanvasBitmap> _highlightByElev = new();

	// 仰角オフセットを焼き直しのキャッシュバケットへ量子化する刻み(度)。細かすぎるとバケットが増え初回焼き直しが頻発し、粗すぎると追従が階段状になる。
	private const double ElevBucketDeg = 2.0;

	// 弧テキストを焼く中間 command list。効果グラフへ ICanvasImage として合成する。毎描画で作り直すので前回ぶんをここで保持して破棄する。
	private CanvasCommandList? _arcText;

	public GlassGeometry Geometry => _geo;




	// 形状・効き幅・ハイライトが変わったときに、重い各種マップとハイライトを焼き直す。
	public void Rebuild(CanvasDevice device, GlassParams p)
	{
		_geo = GlassGeometry.From(p);

		DisposeMaps();
		_dispMap = GlassMaps.BuildDisplacementMap(device, _geo, p.Bevel);
		_blurMask = GlassMaps.BuildEdgeMask(device, _geo, p.VBlurW);
		_enhMask = GlassMaps.BuildEdgeMask(device, _geo, p.VEnhW);
		_tintMask = GlassMaps.BuildTintMask(device, _geo, p.VTintW);
		_gloss = GlassMaps.BuildGlossMap(device, _geo);
		_normalSet = HighlightBaker.BuildNormalSet(_geo.CardW, _geo.CardH, _geo.Radius, p.Highlights);
		_highlight = HighlightBaker.Bake(device, _geo.CardW, _geo.CardH, _geo.Radius, _normalSet, p.Highlights, 0);
	}




	// localBackground は既にガラスローカル座標へ変換済みの屈折対象。ローカル原点 (0,0) がマップ左上に、本体は (margin,margin) に置かれる前提。
	// destX,destY はローカル原点を描画先(キャンバス)のどこへ置くか。本体は (destX+margin, destY+margin) に出る。
	// 背景をどう局所化するか(静止背景の平行移動か、ライブキャプチャの拡大つき写像か)は呼び手が決め、TransformBackground で作って渡す。
	public void Draw(CanvasDrawingSession ds, CanvasDevice device, GlassParams p, ICanvasImage localBackground, double destX, double destY, int magnify, double highlightRotDeg, double highlightElevOffsetDeg)
	{
		GlassGeometry g = _geo;

		ICanvasImage scene = localBackground;

		if (p.Displace && _dispMap != null)
		{
			scene = BuildRefraction(scene, p);
		}

		if (p.Blur)
		{
			if (p.VBlur && _blurMask != null)
			{
				scene = MaskOver(scene, new GaussianBlurEffect { Source = scene, BlurAmount = (float)p.BlurAmt }, _blurMask);
			}
			else
			{
				scene = new GaussianBlurEffect { Source = scene, BlurAmount = (float)p.BlurAmt };
			}
		}

		if (p.Enhance)
		{
			var enhanced = new ColorMatrixEffect { Source = scene, ColorMatrix = SaturateBrighten(1.8, 1.08) };

			if (p.VEnh && _enhMask != null)
			{
				scene = MaskOver(scene, enhanced, _enhMask);
			}
			else
			{
				scene = enhanced;
			}
		}

		scene = ApplyTint(scene, p, g);
		scene = ApplyArcText(scene, device, g, magnify);
		scene = ApplyGloss(scene, p, g);
		CanvasBitmap? highlight = ResolveHighlight(device, p, highlightElevOffsetDeg);
		scene = ApplyHighlight(scene, p, g, highlight, highlightRotDeg);

		double bodyX = destX + g.Margin;
		double bodyY = destY + g.Margin;

		// ガラス本体の角丸クリップ。屈折結果と装飾をこの形に収める。
		CanvasGeometry clip = CanvasGeometry.CreateRoundedRectangle(device, (float)bodyX, (float)bodyY, g.CardW, g.CardH, (float)g.Radius, (float)g.Radius);

		using (ds.CreateLayer(1f, clip))
		{
			ds.DrawImage(scene, new Vector2((float)destX, (float)destY));

			DrawBorder(ds, p, g, bodyX, bodyY);
		}
	}




	// 背景画像をガラスローカル座標へ写す変換を適用する。静止背景なら平行移動、ライブキャプチャなら拡大つきの写像を呼び手が組んで渡す。
	// カラーピッカーとして拡大時にデスクトップの各物理画素をくっきりしたドットで見せ、採色対象を画素単位で見極められるよう、補間は最近傍とする。
	public static ICanvasImage TransformBackground(ICanvasImage background, Matrix3x2 matrix)
	{
		return new Transform2DEffect
		{
			Source = background,
			TransformMatrix = matrix,
			InterpolationMode = CanvasImageInterpolation.NearestNeighbor,
		};
	}




	// 屈折段。色収差ありのときは R/G/B を別量で変位して各チャンネルを取り出し screen 合成する。
	private ICanvasImage BuildRefraction(ICanvasImage src, GlassParams p)
	{
		if (!p.Chroma)
		{
			return new DisplacementMapEffect
			{
				Source = src,
				Displacement = _dispMap,
				Amount = (float)p.Scale,
				XChannelSelect = EffectChannelSelect.Red,
				YChannelSelect = EffectChannelSelect.Green,
			};
		}

		double r = p.Scale * (1 + p.Spread);
		double gg = p.Scale;
		double b = p.Scale * (1 - p.Spread);

		ICanvasImage cr = IsolateChannel(Displace(src, r), 0);
		ICanvasImage cg = IsolateChannel(Displace(src, gg), 1);
		ICanvasImage cb = IsolateChannel(Displace(src, b), 2);

		var crg = new BlendEffect { Background = cr, Foreground = cg, Mode = BlendEffectMode.Screen };
		return new BlendEffect { Background = crg, Foreground = cb, Mode = BlendEffectMode.Screen };
	}




	private DisplacementMapEffect Displace(ICanvasImage src, double amount)
	{
		return new DisplacementMapEffect
		{
			Source = src,
			Displacement = _dispMap,
			Amount = (float)amount,
			XChannelSelect = EffectChannelSelect.Red,
			YChannelSelect = EffectChannelSelect.Green,
		};
	}




	// 指定チャンネル(0=R,1=G,2=B)だけを残し、他を 0 にする。アルファは保つ。
	private static ColorMatrixEffect IsolateChannel(ICanvasImage src, int channel)
	{
		var m = new Matrix5x4();
		m.M44 = 1;  // アルファは通す

		if (channel == 0) m.M11 = 1;
		else if (channel == 1) m.M22 = 1;
		else m.M33 = 1;

		return new ColorMatrixEffect { Source = src, ColorMatrix = m };
	}




	// 強い版(over)を元(baseImg)の上にエッジマスクのアルファで切り抜いて重ねる。縁ほど over が、中央では元が見える勾配になる。
	private static ICanvasImage MaskOver(ICanvasImage baseImg, ICanvasImage over, CanvasBitmap mask)
	{
		var edge = new AlphaMaskEffect { Source = over, AlphaMask = mask };
		var comp = new CompositeEffect { Mode = CanvasComposite.SourceOver };
		comp.Sources.Add(baseImg);
		comp.Sources.Add(edge);
		return comp;
	}




	// 白みをローカル座標の屈折結果へ重ねる。色は不透明の白/黒を本体形状(可変時は縁マスク)で切り抜き、濃さは OpacityEffect で乗算済み空間のまま縮めてから通常合成で上に乗せる。
	// 色自体を半透明にすると、乗算前の RGB が全開のまま流れて超過輝度の白になり値が効かなくなるため、濃さは色のアルファでなく OpacityEffect で与える。
	private ICanvasImage ApplyTint(ICanvasImage scene, GlassParams p, GlassGeometry g)
	{
		if (!p.Tint || p.TintAmt == 0)
		{
			return scene;
		}

		byte col = p.TintAmt > 0 ? (byte)255 : (byte)0;
		float amt = (float)Math.Abs(p.TintAmt);
		var color = new ColorSourceEffect { Color = Color.FromArgb(255, col, col, col) };

		ICanvasImage shaped;

		if (p.VTint && _tintMask != null)
		{
			shaped = new AlphaMaskEffect { Source = color, AlphaMask = _tintMask };
		}
		else
		{
			shaped = new CropEffect { Source = color, SourceRectangle = new Rect(0, 0, g.CardW, g.CardH) };
		}

		var layer = new OpacityEffect { Source = shaped, Opacity = amt };

		var comp = new CompositeEffect { Mode = CanvasComposite.SourceOver };
		comp.Sources.Add(scene);
		comp.Sources.Add(Offset(layer, g.Margin, g.Margin));
		return comp;
	}




	// 与えられた仰角オフセットに合うハイライトを返す。オフセットがゼロなら焼き済みの基準をそのまま使い、非ゼロならバケットへ量子化し、無ければその場で焼いてキャッシュする。
	// 仰角は鏡面の当たり方そのものを変えるため拡大では擬似できず、その仰角で焼き直す必要がある。法線は使い回すので焼き直しは鏡面ループだけで済み、同じバケットの二度目以降はキャッシュ即返しになる。
	private CanvasBitmap? ResolveHighlight(CanvasDevice device, GlassParams p, double elevOffsetDeg)
	{
		if (_highlight == null || _normalSet == null)
		{
			return _highlight;
		}

		int bucket = (int)Math.Round(elevOffsetDeg / ElevBucketDeg);
		if (bucket == 0)
		{
			return _highlight;
		}

		if (!_highlightByElev.TryGetValue(bucket, out CanvasBitmap? bmp))
		{
			bmp = HighlightBaker.Bake(device, _geo.CardW, _geo.CardH, _geo.Radius, _normalSet, p.Highlights, bucket * ElevBucketDeg);
			_highlightByElev[bucket] = bmp;
		}

		return bmp;
	}




	// ハイライトを screen 合成で重ねる。焼いた白(乗算済み)を margin だけずらして本体位置へ合わせる。明るさは白へ向けて補間され、加算のような白飛びにならない。
	// highlightRotDeg は焼いたハイライトを本体中心まわりに回す角度で、光源追従の向きを与える。円形シェイプはドーム法線が中心対称なので回転だけで厳密に成り立つ。角丸長方形では近似となる。仰角追従は焼き直した highlight ビットマップ自体が担う。
	private ICanvasImage ApplyHighlight(ICanvasImage scene, GlassParams p, GlassGeometry g, CanvasBitmap? highlight, double highlightRotDeg)
	{
		if (!p.Light || highlight == null)
		{
			return scene;
		}

		ICanvasImage img;

		if (highlightRotDeg != 0)
		{
			var center = new Vector2(g.CardW / 2f, g.CardH / 2f);
			Matrix3x2 m = Matrix3x2.CreateRotation((float)(highlightRotDeg * Math.PI / 180.0), center) * Matrix3x2.CreateTranslation(g.Margin, g.Margin);
			img = new Transform2DEffect
			{
				Source = highlight,
				TransformMatrix = m,
				InterpolationMode = CanvasImageInterpolation.Linear,
			};
		}
		else
		{
			img = Offset(highlight, g.Margin, g.Margin);
		}

		return new BlendEffect
		{
			Background = scene,
			Foreground = img,
			Mode = BlendEffectMode.Screen,
		};
	}




	private static ICanvasImage Offset(ICanvasImage img, int dx, int dy)
	{
		return new Transform2DEffect
		{
			Source = img,
			TransformMatrix = Matrix3x2.CreateTranslation(dx, dy),
		};
	}




	// 光沢を屈折結果へ重ねる。焼いた面取りの明るみを margin だけずらして本体位置へ合わせ、通常合成で上に乗せる。
	private ICanvasImage ApplyGloss(ICanvasImage scene, GlassParams p, GlassGeometry g)
	{
		if (!p.Gloss || _gloss == null)
		{
			return scene;
		}

		var comp = new CompositeEffect { Mode = CanvasComposite.SourceOver };
		comp.Sources.Add(scene);
		comp.Sources.Add(Offset(_gloss, g.Margin, g.Margin));
		return comp;
	}




	// 弧テキストを scene へ合成する。文字を command list へ描いてから ICanvasImage として光沢の手前に重ねるので、光沢とハイライトが文字の上を流れる。
	// テキストは本体ローカル座標(原点=本体左上)で組み、効果グラフのマップローカル座標へ合わせるため margin だけずらす。
	private ICanvasImage ApplyArcText(ICanvasImage scene, CanvasDevice device, GlassGeometry g, int magnify)
	{
		_arcText?.Dispose();
		_arcText = new CanvasCommandList(device);
		using (CanvasDrawingSession ds = _arcText.CreateDrawingSession())
		{
			DrawArcText(ds, g, $"×{magnify}");
		}

		var comp = new CompositeEffect { Mode = CanvasComposite.SourceOver };
		comp.Sources.Add(scene);
		comp.Sources.Add(Offset(_arcText, g.Margin, g.Margin));
		return comp;
	}




	// 文字列を本体下側の円弧に沿って一字ずつ配置する。各文字は接線方向へ回転させるが、グリフ自体の極座標変形はしない(読みやすさを優先)。
	// 角度は下端中心(6時方向)を 0 とし時計回りを正に取る。各文字の幅を測って弧長で字送りし、文字列全体が下端中心へ揃うようにする。
	private static void DrawArcText(CanvasDrawingSession ds, GlassGeometry g, string text)
	{
		float cx = g.CardW / 2f;
		float cy = g.CardH / 2f;
		float radius = Math.Min(g.CardW, g.CardH) / 2f - 24f;

		using CanvasTextFormat fmt = new()
		{
			FontFamily = "Segoe UI",
			FontSize = 15f,
		};

		// 各文字の幅を測り、弧全体の角度と左端の開始角を求める。
		float[] widths = new float[text.Length];
		float totalWidth = 0f;
		for (int i = 0; i < text.Length; i++)
		{
			using CanvasTextLayout one = new(ds, text[i].ToString(), fmt, 0f, 0f);
			// LayoutBounds は末尾の空白を切り落とすため空白文字の幅が 0 になる。字送りには末尾空白を含む幅を使う。
			widths[i] = (float)one.LayoutBoundsIncludingTrailingWhitespace.Width;
			totalWidth += widths[i];
		}

		float angle = -(totalWidth / radius) / 2f;

		Color shadow = Color.FromArgb(180, 0, 0, 0);
		Color fill = Color.FromArgb(235, 255, 255, 255);

		for (int i = 0; i < text.Length; i++)
		{
			// 文字中心の角度。位置は (sin,cos) で下端基準に取り、接線へ向けて -mid だけ回す。
			float mid = angle + (widths[i] / 2f) / radius;
			angle += widths[i] / radius;

			float x = cx + radius * MathF.Sin(mid);
			float y = cy + radius * MathF.Cos(mid);

			ds.Transform = Matrix3x2.CreateRotation(-mid) * Matrix3x2.CreateTranslation(x, y);

			using CanvasTextLayout layout = new(ds, text[i].ToString(), fmt, 0f, 0f);
			Rect b = layout.LayoutBounds;
			float ox = (float)(-b.Width / 2 - b.Left);
			float oy = (float)(-b.Height / 2 - b.Top);

			// 暗い影を先に置いてから白を重ね、明るい背景でも読めるようにする。
			ds.DrawTextLayout(layout, ox + 0.6f, oy + 0.6f, shadow);
			ds.DrawTextLayout(layout, ox, oy, fill);
		}

		ds.Transform = Matrix3x2.Identity;
	}




	private static void DrawBorder(CanvasDrawingSession ds, GlassParams p, GlassGeometry g, double glassX, double glassY)
	{
		if (!p.Border)
		{
			return;
		}

		float t = (float)p.BorderW;
		float r = Math.Max(0, (float)g.Radius - t / 2);
		ds.DrawRoundedRectangle((float)glassX + t / 2, (float)glassY + t / 2, g.CardW - t, g.CardH - t, r, r, Color.FromArgb(153, 255, 255, 255), t);
	}




	// 標準的な輝度保存の彩度行列に明度倍率を掛けた色行列を作る。
	private static Matrix5x4 SaturateBrighten(double s, double k)
	{
		double lr = 0.2126;
		double lg = 0.7152;
		double lb = 0.0722;

		double r0 = lr * (1 - s);
		double g0 = lg * (1 - s);
		double b0 = lb * (1 - s);

		return new Matrix5x4
		{
			M11 = (float)(k * (r0 + s)), M12 = (float)(k * r0),       M13 = (float)(k * r0),       M14 = 0,
			M21 = (float)(k * g0),       M22 = (float)(k * (g0 + s)), M23 = (float)(k * g0),       M24 = 0,
			M31 = (float)(k * b0),       M32 = (float)(k * b0),       M33 = (float)(k * (b0 + s)), M34 = 0,
			M41 = 0,                     M42 = 0,                     M43 = 0,                     M44 = 1,
			M51 = 0,                     M52 = 0,                     M53 = 0,                     M54 = 0,
		};
	}




	private void DisposeMaps()
	{
		_dispMap?.Dispose();
		_blurMask?.Dispose();
		_enhMask?.Dispose();
		_tintMask?.Dispose();
		_gloss?.Dispose();
		_highlight?.Dispose();
		_arcText?.Dispose();

		foreach (CanvasBitmap b in _highlightByElev.Values)
		{
			b.Dispose();
		}
		_highlightByElev.Clear();
	}




	// レンズ窓を畳むときに、焼いた全マップとキャッシュを解放する。
	public void Dispose()
	{
		DisposeMaps();
	}
}
