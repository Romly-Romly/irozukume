using System.Text.Json.Serialization;

namespace Romly.WinUI.Common.Windowing;

/// <summary>
/// ウィンドウの位置とサイズを永続化用に保持する。位置は物理ピクセルのスクリーン絶対座標、サイズはDIP(論理単位)で持つ。サイズをDIPで持つのは、別DPIのモニタへ復元したときに見かけの大きさを保つため。Work* は保存時にウィンドウが乗っていたモニタのワークエリア矩形で、復元時にどのモニタへ戻すかを重なり面積で判定し、そのモニタ内での相対位置を保つために使う。
/// </summary>
public sealed class WindowPlacement
{
	/// <summary>
	/// ウィンドウ左上のスクリーン絶対座標の X(物理ピクセル)。
	/// </summary>
	[JsonPropertyName("x")]
	public int X { get; set; }

	/// <summary>
	/// ウィンドウ左上のスクリーン絶対座標の Y(物理ピクセル)。
	/// </summary>
	[JsonPropertyName("y")]
	public int Y { get; set; }

	/// <summary>
	/// ウィンドウの幅(DIP)。別DPIのモニタへ復元しても見かけの大きさを保つため論理単位で持つ。
	/// </summary>
	[JsonPropertyName("width_dip")]
	public double WidthDip { get; set; }

	/// <summary>
	/// ウィンドウの高さ(DIP)。別DPIのモニタへ復元しても見かけの大きさを保つため論理単位で持つ。
	/// </summary>
	[JsonPropertyName("height_dip")]
	public double HeightDip { get; set; }

	/// <summary>
	/// 保存時にウィンドウが乗っていたモニタのワークエリアの左端 X(物理ピクセル)。
	/// </summary>
	[JsonPropertyName("work_x")]
	public int WorkX { get; set; }

	/// <summary>
	/// 保存時にウィンドウが乗っていたモニタのワークエリアの上端 Y(物理ピクセル)。
	/// </summary>
	[JsonPropertyName("work_y")]
	public int WorkY { get; set; }

	/// <summary>
	/// 保存時にウィンドウが乗っていたモニタのワークエリアの幅(物理ピクセル)。
	/// </summary>
	[JsonPropertyName("work_width")]
	public int WorkWidth { get; set; }

	/// <summary>
	/// 保存時にウィンドウが乗っていたモニタのワークエリアの高さ(物理ピクセル)。
	/// </summary>
	[JsonPropertyName("work_height")]
	public int WorkHeight { get; set; }
}
