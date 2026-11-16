namespace DynamicIsland.Core.LiveTranslate;

/// <summary>
/// 识别播放设备交出来的 PCM 格式。Windows 共享模式的混音几乎都是
/// WAVEFORMATEXTENSIBLE，标签是 0xFFFE，真正的类型在子类型里。
/// </summary>
public static class WaveFormatKind
{
    public const int EncodingPcm = 1;
    public const int EncodingIeeeFloat = 3;
    public const int EncodingExtensible = 0xFFFE;

    public static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    public static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    public static bool TryDetect(int encoding, int bitsPerSample, Guid? subFormat, out WaveKind kind)
    {
        kind = default;
        if (bitsPerSample <= 0)
            return false;

        if (IsIeeeFloat(encoding, subFormat) && bitsPerSample == 32)
        {
            kind = WaveKind.Float32;
            return true;
        }

        if (IsPcm(encoding, subFormat) && bitsPerSample == 16)
        {
            kind = WaveKind.Pcm16;
            return true;
        }

        return false;
    }

    static bool IsIeeeFloat(int encoding, Guid? subFormat)
    {
        if (encoding == EncodingIeeeFloat)
            return true;
        if (encoding != EncodingExtensible)
            return false;
        if (subFormat is { } guid && guid != Guid.Empty)
            return guid == SubtypeIeeeFloat;
        return true;
    }

    static bool IsPcm(int encoding, Guid? subFormat)
    {
        if (encoding == EncodingPcm)
            return true;
        if (encoding != EncodingExtensible)
            return false;
        if (subFormat is { } guid && guid != Guid.Empty)
            return guid == SubtypePcm;
        return true;
    }
}
