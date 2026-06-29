using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romly.WinUI.Common.Settings;

/// <summary>
/// 設定オブジェクトを JSON ファイルとして堅牢に読み書きする汎用配管。保存場所(パス)は呼び出し側が決め、ここは中身の型 T に依存しない。
/// </summary>
/// <remarks>
/// 書き込みは一時ファイルへ出してからディスクへ確定し、原子的に差し替える(旧版は .bak として残す)。読み込みでパースに失敗したファイルは上書き消去せず .bad へ退避してから既定値に落ちる。読み書きの失敗はいずれも例外を外へ出さず、起動・終了を妨げない。
/// キー名はこの配管では命名ポリシーを当てず、各設定型の [JsonPropertyName] 属性に従う。既存の設定ファイルとの互換を保つため。
/// </remarks>
public static class JsonSettingsFile
{
	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};




	/// <summary>
	/// 指定パスから設定を読み込む。
	/// </summary>
	/// <typeparam name="T">読み込む設定オブジェクトの型。引数なしで生成できること。</typeparam>
	/// <param name="path">設定ファイルのパス。</param>
	/// <returns>読み込んだ設定。ファイルが無ければ既定値、パース不能なら .bad へ退避して既定値、その他の失敗でも既定値を返す。</returns>
	public static T Load<T>(string path) where T : new()
	{
		try
		{
			if (!File.Exists(path))
			{
				return new T();
			}

			string json = File.ReadAllText(path);
			T? value = JsonSerializer.Deserialize<T>(json, Options);
			return value ?? new T();
		}
		catch (JsonException)
		{
			TrySidelineCorrupt(path);
			return new T();
		}
		catch
		{
			return new T();
		}
	}




	/// <summary>
	/// 指定パスへ設定を保存する。保存先フォルダが無ければ作り、一時ファイルへ書いてディスクへ確定してから原子的に差し替える。
	/// </summary>
	/// <typeparam name="T">保存する設定オブジェクトの型。</typeparam>
	/// <param name="value">保存する設定オブジェクト。</param>
	/// <param name="path">保存先のパス。</param>
	public static void Save<T>(T value, string path)
	{
		try
		{
			string? dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string temp = path + ".tmp";

			// SerializeToUtf8Bytes は BOM 無しの UTF-8 を返す。一時ファイルへ書き、Flush(true) でディスクへ確定させてから差し替える。
			byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
			using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				fs.Write(bytes, 0, bytes.Length);
				fs.Flush(true);
			}

			if (File.Exists(path))
			{
				File.Replace(temp, path, path + ".bak");
			}
			else
			{
				File.Move(temp, path);
			}
		}
		catch
		{
		}
	}




	/// <summary>
	/// パースに失敗したファイルを .bad へ改名して退避する。既存の .bad があれば消してから上書きする。退避自体の失敗は無視する。
	/// </summary>
	private static void TrySidelineCorrupt(string path)
	{
		try
		{
			string bad = path + ".bad";
			if (File.Exists(bad))
			{
				File.Delete(bad);
			}

			File.Move(path, bad);
		}
		catch
		{
		}
	}
}
