using System.Text;

namespace DynamicIsland.Core.LiveTranslate;

/// <summary>
/// 用文件大小和文件头判断本地 Whisper 模型是否像一份完整模型，避免半截文件被当成可用。
/// whisper.cpp 把 ggml 魔数写成小端 uint32，磁盘上是 “lmgg”，不是 ASCII “ggml”。
/// </summary>
public static class WhisperModelFile
{
    public const long MinBytes = 40_000_000;
    public const long MaxBytes = 90_000_000;

    static readonly byte[][] Magics =
    {
        Encoding.ASCII.GetBytes("ggml"),
        Encoding.ASCII.GetBytes("lmgg"), // little-endian 'ggml'
        Encoding.ASCII.GetBytes("ggmf"),
        Encoding.ASCII.GetBytes("fmgg"),
        Encoding.ASCII.GetBytes("ggjt"),
        Encoding.ASCII.GetBytes("tjgg"),
        Encoding.ASCII.GetBytes("GGUF")
    };

    public static bool LooksValid(long length, ReadOnlySpan<byte> header)
    {
        if (length < MinBytes || length > MaxBytes || header.Length < 4)
            return false;
        foreach (var magic in Magics)
        {
            if (header.StartsWith(magic))
                return true;
        }

        return false;
    }

    public static bool LooksValid(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[4];
            int read = stream.Read(header);
            return LooksValid(info.Length, header[..read]);
        }
        catch
        {
            return false;
        }
    }
}
