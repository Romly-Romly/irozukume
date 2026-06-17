// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Romly

using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Graphics.Effects;
using Windows.UI;
using Irozukume.Glass;
using Irozukume.Helpers;
using Irozukume.Models;

namespace Irozukume.Controls;

// ドラッグ中のスライダーに重ねるガラスレンズ(ルーペ)の描画器。レンズの背後にあたる内容(トラック帯と上下のカード背景)を Win2D で再構成し、拡大・縁の屈折・色収差を掛けて円形に描く。CompositionBackdropBrush は座標を動かすエフェクト(拡大・変位)が効かないため、実体のある画像を組んで変形できる Win2D で描く。手法は _spikes/LiquidGlassSpike の屈折・色収差をそのまま使い、背景だけ再構成へ差し替えたもの。
internal sealed class GlassLens : IDisposable
{
	private readonly CanvasControl _canvas = new();
	private CanvasBitmap? _displacementMap;

	// 焼いた鏡面ハイライト(乗算済みアルファの白)。レンズ径ぶんの1枚で、円の中心まわりに回して光源追従を表す。LensTuning でハイライトが切られているとき・効果総スイッチがオフのときは焼かず null のままにする。
	private CanvasBitmap? _highlight;

	// 鏡面ハイライトを本体中心まわりに回す角度(度)。光源追従の向き。利用側が UpdateField/Update で毎フレーム渡す。円のドーム法線は中心対称のため回転だけで厳密に成り立つ。
	private double _highlightRotDeg;

	// レンズの形と効きを決める固定パラメータ。Build で受け取る。
	private float _diameter;
	private float _magnify;
	private bool _chroma;
	private float _chromaSpread;
	private float _edgeAmount;
	private double _bevelFraction;
	private Color _cardColor;

	// トラックの背後に透明度表現の市松模様を敷くか。不透明度スライダーのように半透明のグラデーションを再構成するとき、帯の背後へ市松を描いて透け感を出す。
	private bool _showCheckerboard;

	// サンプラー方式の色面にジャギー対策のガウスぼかしを掛けるか。掛けるとつまみの白い輪などの細部もぼやけるため、既定では掛けない。ジャギーが気になるときに true にし、SamplerFieldBlur で強さを調整する。
	private const bool EnableSamplerFieldBlur = false;

	// サンプラー方式の色面に掛けるガウスぼかしの量(標準偏差, DIP)。EnableSamplerFieldBlur が true のときだけ効く。色面を1画素ずつ標本化するため、つまみの輪や色面の境目に1ドットのジャギーが出る。それをなめる程度の弱いぼかし。トラック方式(スライダー)は滑らかな再構成のため掛けない。
	private const float SamplerFieldBlur = 0.75f;

	// 毎フレーム更新する場面の状態。トラックの向き・沿軸のつまみ位置・沿軸のトラック全長・帯の太さ・グラデーションの停止点。水平は沿軸=x、垂直は沿軸=y。
	private bool _vertical;
	private double _centerAlong;
	private double _trackLength = 1.0;
	private double _bandThickness = 24.0;
	private double _cornerRadius;
	private CanvasGradientStop[] _stops = Array.Empty<CanvasGradientStop>();

	// トラック上で sRGB 色域を外れる区間(値最小端 0・値最大端 1 の割合)。トラックと同じ色域外の見せ方を再構成へ重ねるために使う。空・null のときは何も描かない。
	private IReadOnlyList<GamutSegment>? _segments;

	// 色域外区間の見せ方。トラック(GradientSlider)と同じ設定を映し、白塗り・斜線・境界線を切り替える。
	private GamutOutOfRangeStyle _oogStyle = GamutOutOfRangeStyle.FillBoundaryHatch;

	// サンプラー方式の状態。2次元の色面(彩度明度パッド・色相環など)では、トラックの再構成では足りないため、コントロール局所座標を色へ写す関数からレンズ像を直に標本化する。_useSampler が真のときこちらを使う。_thumbX/_thumbY はレンズ中心が指すコントロール局所座標(画素)。
	private bool _useSampler;
	private Func<double, double, Color>? _sampler;
	private double _thumbX;
	private double _thumbY;




	// 全体のレンズ設定(LensTuning)を基準パラメータ p へ掛け合わせ、実効パラメータを返す。レンズ効果オフなら屈折を切ってただの拡大にし、屈折オフなら縁の歪み量を 0 にする。強さ・色収差・ベベルは基準値への倍率で掛ける。色収差は屈折の一部(同じ縁の歪みを R/G/B へ分ける)のため、屈折中かつ色収差の倍率が正のときだけ掛ける。拡大率は基準の等倍からの増分へ係数を掛け、係数0で等倍(拡大なし)・1.0で基準どおりにする。径は設定の対象外でそのまま通す。各コントロールがドラッグ開始時にこれを通してレンズを組み、設定の変更を次のドラッグから反映する。
	public static GlassLensParams ApplyTuning(GlassLensParams p)
	{
		bool lens = LensTuning.LensEffect;
		bool refract = lens && LensTuning.Refraction;
		double spread = p.ChromaSpread * LensTuning.ChromaSpread;

		return new GlassLensParams
		{
			Diameter = p.Diameter,
			Magnify = 1.0 + ((p.Magnify - 1.0) * LensTuning.Magnify),
			EdgeAmount = refract ? p.EdgeAmount * LensTuning.RefractionStrength : 0.0,
			Chroma = refract && spread > 0.0,
			ChromaSpread = spread,
			BevelFraction = p.BevelFraction * LensTuning.Bevel,
		};
	}




	// 光源追従で鏡面ハイライトへ与える回転(度)を求める。仮想光源をレンズの置き先(最前面オーバーレイ)の左上寄りに固定し、レンズ中心から光源への方位と焼き付け方位(HighlightAzim)との差を返す。回した結果、艶が光源の側へ向く。ハイライト無効・効果総スイッチオフ・置き先の寸法が未確定のときは 0(焼き付けた向きのまま)を返す。lensCenterInTarget は置き先の座標系でのレンズ中心、targetWidth/targetHeight は置き先の寸法。
	public static double ComputeHighlightRotation(Point lensCenterInTarget, double targetWidth, double targetHeight)
	{
		if (!LensTuning.LensEffect || !LensTuning.ShowHighlight || targetWidth <= 0.0 || targetHeight <= 0.0)
		{
			return 0.0;
		}

		double lightX = targetWidth * LensTuning.HighlightLightFracX;
		double lightY = targetHeight * LensTuning.HighlightLightFracY;
		double dx = lightX - lensCenterInTarget.X;
		double dy = lightY - lensCenterInTarget.Y;

		return (Math.Atan2(dy, dx) * 180.0 / Math.PI) - LensTuning.HighlightDesignAzim;
	}




	// レンズの描画コントロール(1次元トラック方式)を組んで返す。p はレンズの効き、cardColor は帯の上下に敷く背景色、showCheckerboard は半透明トラックの背後へ透明度表現の市松を敷くか。トラックの内容は Update で渡す。
	public CanvasControl Build(GlassLensParams p, Color cardColor, bool showCheckerboard)
	{
		ApplyParams(p);
		_cardColor = cardColor;
		_showCheckerboard = showCheckerboard;

		_canvas.Width = p.Diameter;
		_canvas.Height = p.Diameter;
		_canvas.CreateResources += OnCreateResources;
		_canvas.Draw += OnDraw;
		return _canvas;
	}




	// レンズの効きをフィールドへ写す。内部計算は float で行うため寸法以外を float へ落とす。
	private void ApplyParams(GlassLensParams p)
	{
		_diameter = (float)p.Diameter;
		_magnify = (float)p.Magnify;
		_chroma = p.Chroma;
		_chromaSpread = (float)p.ChromaSpread;
		_edgeAmount = (float)p.EdgeAmount;
		_bevelFraction = p.BevelFraction;
	}




	// 現在のトラックの向き・寸法・グラデーション・色域外区間・その見せ方を更新して再描画を促す。値が変わるたびに呼ぶ。segments を与えると、トラックと同じ白塗り・斜線・境界線を再構成へ重ねる。highlightRotDeg は鏡面ハイライトの回転(光源追従の向き)。
	public void Update(GlassTrack track, IReadOnlyList<GlassGradientStop> stops, IReadOnlyList<GamutSegment>? segments, GamutOutOfRangeStyle oogStyle, double highlightRotDeg)
	{
		_highlightRotDeg = highlightRotDeg;
		_vertical = track.Vertical;
		_centerAlong = track.CenterAlong;
		_trackLength = Math.Max(1.0, track.TrackLength);
		_bandThickness = Math.Max(1.0, track.BandThickness);
		_cornerRadius = Math.Max(0.0, track.CornerRadius);

		var converted = new CanvasGradientStop[stops.Count];

		for (int i = 0; i < stops.Count; i++)
		{
			converted[i] = new CanvasGradientStop { Position = stops[i].Offset, Color = stops[i].Color };
		}

		_stops = converted;
		_segments = segments;
		_oogStyle = oogStyle;
		_canvas.Invalidate();
	}




	// サンプラー方式で描画コントロールを組んで返す。2次元の色面(彩度明度パッド・色相環など)向け。背景はトラック再構成ではなく UpdateField で渡す色関数から標本化する。cardColor は色面の無い箇所(色面の外・色相環の穴や帯の外)に敷く不透明な下地で、レンズの円内を埋めて背後の実物(等倍の色相環・パッド)が透けて二重に見えるのを防ぐ。
	public CanvasControl BuildSampler(GlassLensParams p, Color cardColor)
	{
		ApplyParams(p);
		_useSampler = true;
		_cardColor = cardColor;

		_canvas.Width = p.Diameter;
		_canvas.Height = p.Diameter;
		_canvas.CreateResources += OnCreateResources;
		_canvas.Draw += OnDraw;
		return _canvas;
	}




	// サンプラーと、レンズ中心が指すコントロール局所座標(画素)を更新して再描画を促す。値が変わるたびに呼ぶ。sampler はコントロール局所座標(画素)を受け、その点の色面の色を返す(色面の外は透明を返してよい)。highlightRotDeg は鏡面ハイライトの回転(光源追従の向き)。
	public void UpdateField(Func<double, double, Color> sampler, double thumbX, double thumbY, double highlightRotDeg)
	{
		_highlightRotDeg = highlightRotDeg;
		_sampler = sampler;
		_thumbX = thumbX;
		_thumbY = thumbY;
		_canvas.Invalidate();
	}




	private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
	{
		_displacementMap?.Dispose();
		int size = (int)Math.Ceiling(_diameter);
		byte[] pixels = BuildRadialDisplacement(size, _bevelFraction);
		_displacementMap = CanvasBitmap.CreateFromBytes(sender.Device, pixels, size, size, DirectXPixelFormat.B8G8R8A8UIntNormalized);

		BakeHighlight(sender.Device, size);
	}




	// レンズ径ぶんの鏡面ハイライトを LensTuning の灯で一度だけ焼く。光源追従は方位の回転だけで表すため焼き直しは要らず、ここで焼いた1枚を回して使う。効果総スイッチかハイライトがオフ・灯が無いときは焼かず、艶を乗せない。レンズはドラッグごとに組み直されるため、設定変更は次のドラッグから反映される。
	private void BakeHighlight(CanvasDevice device, int size)
	{
		_highlight?.Dispose();
		_highlight = null;

		IReadOnlyList<Highlight> lights = LensTuning.Highlights;

		if (!LensTuning.LensEffect || !LensTuning.ShowHighlight || lights.Count == 0)
		{
			return;
		}

		HighlightBaker.NormalSet normals = HighlightBaker.BuildNormalSet(size, size, size / 2.0, lights);
		_highlight = HighlightBaker.Bake(device, size, size, size / 2.0, normals, lights, 0);
	}




	private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
	{
		if (_displacementMap is null)
		{
			return;
		}

		CanvasDrawingSession ds = args.DrawingSession;
		float r = _diameter / 2.0f;

		// 拡大済みの背景(屈折前)を用意する。サンプラー方式は色関数から直に標本化した像(標本化の時点で拡大済み)、トラック方式は再構成した背景を Transform2D で拡大したもの。どちらも後段で屈折・色収差を掛ける。作った中間像は描画後に破棄する。
		ICanvasImage? owned = null;
		ICanvasImage source;

		if (_useSampler)
		{
			if (_sampler is null)
			{
				return;
			}

			CanvasBitmap field = SampleField(sender.Device);
			owned = field;

			// 1画素標本化で出る縁のジャギーを、屈折へ渡す前に弱いガウスぼかしでなめる。EnableSamplerFieldBlur で切り替える(掛けるとつまみの白い輪などもぼやける)。
			source = EnableSamplerFieldBlur
				? new GaussianBlurEffect
				{
					Source = field,
					BlurAmount = SamplerFieldBlur,
					BorderMode = EffectBorderMode.Hard,
				}
				: field;
		}
		else
		{
			// つまみを原点(0,0)、トラックの縦中心を 0 とした素の座標で背景を組み、Transform2D で中心まわりに拡大する。
			ICanvasImage background = BuildBackground(sender.Device);
			owned = background;
			source = new Transform2DEffect
			{
				Source = background,
				TransformMatrix = Matrix3x2.CreateScale(_magnify) * Matrix3x2.CreateTranslation(r, r),
				InterpolationMode = CanvasImageInterpolation.Linear,
			};
		}

		ICanvasImage scene = _chroma ? BuildChromatic(source) : Refract(source, _edgeAmount);
		scene = ApplyHighlight(scene);

		// 円形に切り抜いて描く。サンプラー方式は色面の無い箇所が透明のままだと背後の実物が透けて二重に見えるため、円内をカード色で埋めてからその上に像を重ねる。トラック方式は背景の再構成で既に円内が埋まっているため下地は要らない。
		using (CanvasGeometry clip = CanvasGeometry.CreateCircle(sender.Device, r, r, r))
		using (ds.CreateLayer(1.0f, clip))
		{
			if (_useSampler)
			{
				ds.FillRectangle(0.0f, 0.0f, _diameter, _diameter, _cardColor);
			}

			ds.DrawImage(scene);
		}

		owned?.Dispose();
	}




	// サンプラーから、レンズ径ぶんの色面像を直に標本化して作る。レンズ画素ごとに、中心が _thumbX/_thumbY を指し、まわりが _magnify 倍に拡大されるようコントロール局所座標へ写し、その点の色を得る。標本化の時点で拡大済みのため、後段の Transform2D は要らない。
	private CanvasBitmap SampleField(CanvasDevice device)
	{
		Func<double, double, Color> sampler = _sampler!;
		int d = (int)Math.Ceiling(_diameter);
		var data = new byte[d * d * 4];
		double r = _diameter / 2.0;

		for (int ly = 0; ly < d; ly++)
		{
			for (int lx = 0; lx < d; lx++)
			{
				double sx = _thumbX + ((lx + 0.5 - r) / _magnify);
				double sy = _thumbY + ((ly + 0.5 - r) / _magnify);
				Color c = sampler(sx, sy);
				double a = c.A / 255.0;

				int i = ((ly * d) + lx) * 4;
				data[i + 0] = (byte)(c.B * a);   // 乗算済みアルファの BGRA
				data[i + 1] = (byte)(c.G * a);
				data[i + 2] = (byte)(c.R * a);
				data[i + 3] = c.A;
			}
		}

		return CanvasBitmap.CreateFromBytes(device, data, d, d, DirectXPixelFormat.B8G8R8A8UIntNormalized);
	}




	// つまみを原点とした素の座標で背景を組む。沿軸(水平は x、垂直は y)にグラデーションの帯を、その左右(垂直なら上下)へカード背景を敷く。グラデーションは offset0 を値の最小端へ写す(水平は左、垂直は下)。
	private ICanvasImage BuildBackground(CanvasDevice device)
	{
		var list = new CanvasCommandList(device);
		float r = _diameter / 2.0f;

		// レンズへ写る素の範囲(屈折のはみ出しを少し足す)。これを覆うようカード背景を敷く。
		double span = ((r + Math.Abs(_edgeAmount) + 2.0) / Math.Max(0.01, _magnify)) + 4.0;
		float bandHalf = (float)(_bandThickness / 2.0);

		using (CanvasDrawingSession ds = list.CreateDrawingSession())
		{
			ds.FillRectangle((float)-span, (float)-span, (float)(span * 2.0), (float)(span * 2.0), _cardColor);

			if (_stops.Length >= 2)
			{
				// トラックの沿軸範囲(原点はつまみ)。軸0側(水平=左, 垂直=上)が axisLow、軸末端が axisHigh。値の最小端(offset0)は水平では左(axisLow)、垂直では下(axisHigh)。
				double axisLow = -_centerAlong;
				double axisHigh = _trackLength - _centerAlong;
				double off0 = _vertical ? axisHigh : axisLow;
				double off1 = _vertical ? axisLow : axisHigh;
				double drawLow = Math.Max(axisLow, -span);
				double drawHigh = Math.Min(axisHigh, span);

				if (drawHigh > drawLow)
				{
					// 帯をトラックの角丸の輪郭で切り抜く。レンズはトラックを矩形で再構成するため、角丸を映さないと端が四角く見える。トラック全体(沿軸 axisLow–axisHigh × 帯の太さ)の角丸矩形でクリップし、端が丸まって見えるようにする。半径は帯の太さの半分を超えないようにする。
					float radius = (float)Math.Min(_cornerRadius, bandHalf);
					Rect trackRect = _vertical
						? new Rect(-bandHalf, axisLow, bandHalf * 2.0, _trackLength)
						: new Rect(axisLow, -bandHalf, _trackLength, bandHalf * 2.0);

					using CanvasGeometry trackClip = CanvasGeometry.CreateRoundedRectangle(device, (float)trackRect.X, (float)trackRect.Y, (float)trackRect.Width, (float)trackRect.Height, radius, radius);
					using (ds.CreateLayer(1.0f, trackClip))
					{
						// 半透明トラックの背後へ市松を敷いてから、その上にグラデーションを重ねる。透明寄りの区間で市松が透けて不透明度が読める。
						if (_showCheckerboard)
						{
							DrawCheckerboard(ds, device, axisLow, bandHalf, drawLow, drawHigh);
						}

						using var brush = new CanvasLinearGradientBrush(device, _stops)
						{
							StartPoint = _vertical ? new Vector2(0.0f, (float)off0) : new Vector2((float)off0, 0.0f),
							EndPoint = _vertical ? new Vector2(0.0f, (float)off1) : new Vector2((float)off1, 0.0f),
						};

						if (_vertical)
						{
							ds.FillRectangle(-bandHalf, (float)drawLow, bandHalf * 2.0f, (float)(drawHigh - drawLow), brush);
						}
						else
						{
							ds.FillRectangle((float)drawLow, -bandHalf, (float)(drawHigh - drawLow), bandHalf * 2.0f, brush);
						}

						DrawGamutOverlay(ds, device, axisLow, bandHalf);
					}
				}
			}
		}

		return list;
	}




	// 透明度表現の市松模様を帯へ敷く。半透明のグラデーション(不透明度スライダー)の背後で透け感が出るよう、帯の領域に明色の下地と暗色の升を描く。升はトラックの原点(沿軸0側の端・帯の上端)に合わせて並べ、トラック本体の市松と位相をそろえる。升の寸法は CheckerboardGeometry に合わせ、レンズの拡大は後段の Transform2D が色面ごと一様に掛ける。引数の axisLow は沿軸0側のトラック端、bandHalf は帯の太さの半分、drawLow/drawHigh はレンズに写る沿軸の範囲。
	private void DrawCheckerboard(CanvasDrawingSession ds, CanvasDevice device, double axisLow, float bandHalf, double drawLow, double drawHigh)
	{
		float left;
		float top;
		float width;
		float height;
		double anchorX;
		double anchorY;

		if (_vertical)
		{
			left = -bandHalf;
			top = (float)drawLow;
			width = bandHalf * 2.0f;
			height = (float)(drawHigh - drawLow);
			anchorX = -bandHalf;
			anchorY = axisLow;
		}
		else
		{
			left = (float)drawLow;
			top = -bandHalf;
			width = (float)(drawHigh - drawLow);
			height = bandHalf * 2.0f;
			anchorX = axisLow;
			anchorY = -bandHalf;
		}

		if (width <= 0.0f || height <= 0.0f)
		{
			return;
		}

		double cell = CheckerboardGeometry.CellSize;

		using CanvasGeometry clip = CanvasGeometry.CreateRectangle(device, left, top, width, height);
		using (ds.CreateLayer(1.0f, clip))
		{
			ds.FillRectangle(left, top, width, height, CheckerboardGeometry.LightColor);

			int columnStart = (int)Math.Floor((left - anchorX) / cell);
			int columnEnd = (int)Math.Ceiling(((left + width) - anchorX) / cell);
			int rowStart = (int)Math.Floor((top - anchorY) / cell);
			int rowEnd = (int)Math.Ceiling(((top + height) - anchorY) / cell);

			for (int row = rowStart; row < rowEnd; row++)
			{
				for (int column = columnStart; column < columnEnd; column++)
				{
					// 升の位相をトラック原点に合わせる。負の行・列でも偶奇が正しく出るようビット積で偶奇を取る。
					if (((row + column) & 1) == 1)
					{
						ds.FillRectangle((float)(anchorX + (column * cell)), (float)(anchorY + (row * cell)), (float)cell, (float)cell, CheckerboardGeometry.DarkColor);
					}
				}
			}
		}
	}




	// 色域外区間に、トラックと同じ見せ方(白塗り・斜線・境界線)を帯へ重ねる。白塗りは区間を不透明な白で覆い実色を隠す。斜線は各区間の x 範囲(原点はつまみ)へ矩形クリップした 45 度の黒白2本。境界線は区間の内側の端へ、色域内側に黒・外側に白のタテ線を引く。暗い背景では白が、明るい背景では黒が効き、どこでも消えない。寸法・色は GradientSlider のトラック(UpdateGamutOverlay)に揃える。色域外区間を持つ縦スライダー(Lab の明度レール)のルーペは元から色域外を描かないため、水平向きのみ描く。
	private void DrawGamutOverlay(CanvasDrawingSession ds, CanvasDevice device, double trackLeft, float bandHalf)
	{
		if (_vertical || _segments is null || _segments.Count == 0)
		{
			return;
		}

		bool showHatch = _oogStyle == GamutOutOfRangeStyle.FillBoundaryHatch || _oogStyle == GamutOutOfRangeStyle.WhiteHatch;
		bool showBoundary = _oogStyle != GamutOutOfRangeStyle.WhiteHatch;
		bool fillWhite = _oogStyle == GamutOutOfRangeStyle.WhiteHatch;

		var black = Color.FromArgb(0x80, 0x00, 0x00, 0x00);
		var white = Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF);
		var whiteFill = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
		const float spacing = 10.0f;
		const float lineWidth = 0.7f;
		const float tickWidth = 1.0f;
		const double eps = 1e-4;
		float whiteOffset = lineWidth * (float)Math.Sqrt(2.0);
		float height = bandHalf * 2.0f;
		float top = -bandHalf;

		foreach (GamutSegment segment in _segments)
		{
			double startNorm = Math.Clamp(segment.Start, 0.0, 1.0);
			double endNorm = Math.Clamp(segment.End, 0.0, 1.0);
			double x0 = trackLeft + (startNorm * _trackLength);
			double x1 = trackLeft + (endNorm * _trackLength);

			if (x1 <= x0)
			{
				continue;
			}

			if (fillWhite)
			{
				ds.FillRectangle((float)x0, top, (float)(x1 - x0), height, whiteFill);
			}

			if (showHatch)
			{
				using CanvasGeometry clip = CanvasGeometry.CreateRectangle(device, (float)x0, top, (float)(x1 - x0), height);
				using (ds.CreateLayer(1.0f, clip))
				{
					for (double sx = x0 - height; sx < x1; sx += spacing)
					{
						ds.DrawLine((float)sx, top + height, (float)(sx + height), top, black, lineWidth);
						ds.DrawLine((float)(sx + whiteOffset), top + height, (float)(sx + whiteOffset + height), top, white, lineWidth);
					}
				}
			}

			// 境界線。開始端は色域内が左(値の小さい側)、終了端は色域内が右。黒を色域内側、白を色域外側へ密着で置く。トラック端(0 や 1)には引かない。
			if (showBoundary)
			{
				if (startNorm > eps)
				{
					ds.FillRectangle((float)(x0 - tickWidth), top, tickWidth, height, black);
					ds.FillRectangle((float)x0, top, tickWidth, height, white);
				}

				if (endNorm < 1.0 - eps)
				{
					ds.FillRectangle((float)x1, top, tickWidth, height, black);
					ds.FillRectangle((float)(x1 - tickWidth), top, tickWidth, height, white);
				}
			}
		}
	}




	// 焼いた鏡面ハイライトを scene へ screen 合成で重ねる。明るさは白へ向けて補間され、加算のような白飛びにならない。光源追従の向き(_highlightRotDeg)があれば、ハイライトをレンズ中心まわりに回してから重ねる。円のドーム法線は中心対称のため回転だけで厳密に成り立つ。ハイライトを焼いていない(無効時)ときはそのまま返す。
	private ICanvasImage ApplyHighlight(ICanvasImage scene)
	{
		if (_highlight is null)
		{
			return scene;
		}

		float r = _diameter / 2.0f;
		ICanvasImage hi;

		if (_highlightRotDeg != 0.0)
		{
			hi = new Transform2DEffect
			{
				Source = _highlight,
				TransformMatrix = Matrix3x2.CreateRotation((float)(_highlightRotDeg * Math.PI / 180.0), new Vector2(r, r)),
				InterpolationMode = CanvasImageInterpolation.Linear,
			};
		}
		else
		{
			hi = _highlight;
		}

		return new BlendEffect
		{
			Background = scene,
			Foreground = hi,
			Mode = BlendEffectMode.Screen,
		};
	}




	// 拡大済みの背景を縁の変位マップで屈折させる。amount は変位量(画素、負で内向き)。
	private ICanvasImage Refract(IGraphicsEffectSource source, float amount)
	{
		return new DisplacementMapEffect
		{
			Source = source,
			Displacement = _displacementMap,
			Amount = amount,
			XChannelSelect = EffectChannelSelect.Red,
			YChannelSelect = EffectChannelSelect.Green,
		};
	}




	// 色収差つきの屈折。R/G/B を別量で変位して各チャンネルを取り出し、screen で重ねる。縁ほど色がずれてフリンジになる。
	private ICanvasImage BuildChromatic(IGraphicsEffectSource source)
	{
		ICanvasImage r = IsolateChannel(Refract(source, _edgeAmount * (1.0f + _chromaSpread)), 0);
		ICanvasImage g = IsolateChannel(Refract(source, _edgeAmount), 1);
		ICanvasImage b = IsolateChannel(Refract(source, _edgeAmount * (1.0f - _chromaSpread)), 2);

		var rg = new BlendEffect { Background = r, Foreground = g, Mode = BlendEffectMode.Screen };
		return new BlendEffect { Background = rg, Foreground = b, Mode = BlendEffectMode.Screen };
	}




	// 指定チャンネル(0=R,1=G,2=B)だけを残し、他を 0 にする。アルファは通す。
	private static ColorMatrixEffect IsolateChannel(ICanvasImage source, int channel)
	{
		var m = new Matrix5x4 { M44 = 1.0f };

		if (channel == 0)
		{
			m.M11 = 1.0f;
		}
		else if (channel == 1)
		{
			m.M22 = 1.0f;
		}
		else
		{
			m.M33 = 1.0f;
		}

		return new ColorMatrixEffect { Source = source, ColorMatrix = m };
	}




	// 円形の変位マップ(BGRA8)を作る。縁から内側へ bevel 幅のあいだだけ、外向き(放射方向)のずれを二次減衰で R(横)・G(縦)へ書く。中央と円外はずれ0(128)。アルファは不透明。
	private static byte[] BuildRadialDisplacement(int size, double bevelFraction)
	{
		var data = new byte[size * size * 4];
		double center = size / 2.0;
		double radius = size / 2.0;
		double bevel = Math.Max(1.0, bevelFraction * radius);

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				double dx = (x + 0.5) - center;
				double dy = (y + 0.5) - center;
				double dist = Math.Sqrt((dx * dx) + (dy * dy));
				double depth = radius - dist;
				double t = 0.0;

				if (depth >= 0.0 && depth < bevel)
				{
					double k = depth / bevel;
					t = (1.0 - k) * (1.0 - k);
				}

				double nx = dist > 1e-6 ? dx / dist : 0.0;
				double ny = dist > 1e-6 ? dy / dist : 0.0;

				int i = ((y * size) + x) * 4;
				data[i + 0] = 128;                              // B
				data[i + 1] = Clamp(128.0 + (ny * t * 127.0));  // G
				data[i + 2] = Clamp(128.0 + (nx * t * 127.0));  // R
				data[i + 3] = 255;                              // A
			}
		}

		return data;
	}




	private static byte Clamp(double value)
	{
		int n = (int)Math.Round(value);

		if (n < 0)
		{
			n = 0;
		}

		if (n > 255)
		{
			n = 255;
		}

		return (byte)n;
	}




	public void Dispose()
	{
		_canvas.Draw -= OnDraw;
		_canvas.CreateResources -= OnCreateResources;
		_displacementMap?.Dispose();
		_displacementMap = null;
		_highlight?.Dispose();
		_highlight = null;

		// CanvasControl は内部のリソースを解放するため RemoveFromVisualTree を呼んでから捨てる。
		_canvas.RemoveFromVisualTree();
	}
}




// 1次元トラックの向きと寸法。レンズがトラックを再構成して映すために渡す。沿軸は水平なら x、垂直なら y。CenterAlong は沿軸上のつまみ位置(画素)、TrackLength は沿軸のトラック全長(画素)、BandThickness は帯の太さ(画素)、CornerRadius は帯の角丸量(画素)。グラデーションは offset0 を値の最小端(水平=左、垂直=下)へ写す。
internal readonly struct GlassTrack
{
	public bool Vertical { get; init; }
	public double CenterAlong { get; init; }
	public double TrackLength { get; init; }
	public double BandThickness { get; init; }
	public double CornerRadius { get; init; }
}




// ガラスレンズの効きをまとめた設定。1次元トラック(GradientSlider)と2次元の色面・色相環(LensController)で同じ項目を使い、値だけ各々で変える。
internal readonly struct GlassLensParams
{
	// レンズの直径(DIP)。つまみより大きくして、ドラッグ中に膨らむルーペとして見せる。
	public double Diameter { get; init; }

	// 背後をどれだけ拡大するか。倍率(無次元)。1 で等倍、上げるほど拡大され、映る範囲が狭まる。
	public double Magnify { get; init; }

	// 縁の屈折の強さ。変位量を画素(DIP)で与える。負で内向き(縁が中心へ引き込まれる)、正で外向き。絶対値が大きいほど縁が強く歪む。
	public double EdgeAmount { get; init; }

	// 色収差(カラーフリンジ)を出すか。真のとき、縁の屈折を R/G/B で別量に分けて色をずらす。
	public bool Chroma { get; init; }

	// 色収差の広がり。屈折量に対する R と B のずらし幅の割合(無次元)。R は (1+spread) 倍、B は (1−spread) 倍の量で屈折させる。0 で色ずれなし、大きいほど縁の色が派手にずれる。
	public double ChromaSpread { get; init; }

	// 屈折が縁から内側へ効く幅。レンズ半径に対する割合(0–1、無次元。画素でも角度でもない)。0.5 なら半径の半分の帯で、縁から中心へ向け二次的に弱まって消える。
	public double BevelFraction { get; init; }
}




// XAML の LinearGradientBrush から取り出したグラデーションの停止点。Win2D 型を呼び手へ出さずに渡すための受け渡し用。
internal readonly struct GlassGradientStop
{
	public GlassGradientStop(Color color, float offset)
	{
		Color = color;
		Offset = offset;
	}

	public Color Color { get; }
	public float Offset { get; }
}
