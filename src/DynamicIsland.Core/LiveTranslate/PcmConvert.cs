using System.Buffers.Binary;

namespace DynamicIsland.Core.LiveTranslate;

public enum WaveKind
{
    Float32,
    Pcm16,
    Pcm24,
    Pcm32
}

/// <summary>
/// 把播放设备交出来的交错 PCM 收成 16 kHz 单声道浮点，供语音识别使用。
/// 相位在多次回调之间保留，避免块与块之间出现接缝。
/// </summary>
public sealed class StreamResampler
{
    public const int TargetRate = 16000;

    readonly int _sourceRate;
    readonly int _channels;
    readonly WaveKind _kind;
    readonly List<float> _mono = new();
    double _phase;

    public StreamResampler(int sourceRate, int channels, WaveKind kind)
    {
        if (sourceRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRate));
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));
        _sourceRate = sourceRate;
        _channels = channels;
        _kind = kind;
    }

    public int SourceRate => _sourceRate;
    public int Channels => _channels;
    public WaveKind Kind => _kind;

    public static int BytesPerFrame(WaveKind kind, int channels) => kind switch
    {
        WaveKind.Float32 => 4 * channels,
        WaveKind.Pcm16 => 2 * channels,
        WaveKind.Pcm24 => 3 * channels,
        WaveKind.Pcm32 => 4 * channels,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public void Push(ReadOnlySpan<byte> raw, List<float> destination)
    {
        DecodeMono(raw);
        if (_mono.Count < 2)
            return;

        double step = _sourceRate / (double)TargetRate;
        int last = _mono.Count - 1;
        while (_phase < last)
        {
            int index = (int)_phase;
            double frac = _phase - index;
            float a = _mono[index];
            float b = _mono[index + 1];
            destination.Add((float)(a + (b - a) * frac));
            _phase += step;
        }

        int consumed = Math.Min((int)_phase, _mono.Count - 1);
        if (consumed > 0)
        {
            _mono.RemoveRange(0, consumed);
            _phase -= consumed;
        }
    }

    public void Reset()
    {
        _mono.Clear();
        _phase = 0;
    }

    void DecodeMono(ReadOnlySpan<byte> raw)
    {
        int frame = BytesPerFrame(_kind, _channels);
        if (frame <= 0)
            return;
        int frames = raw.Length / frame;
        for (int i = 0; i < frames; i++)
        {
            int offset = i * frame;
            float mix = 0;
            int used = _channels <= 2 ? _channels : 2;
            for (int c = 0; c < used; c++)
                mix += Sample(raw, offset, c);
            _mono.Add(mix / used);
        }
    }

    float Sample(ReadOnlySpan<byte> raw, int frameOffset, int channel)
    {
        switch (_kind)
        {
            case WaveKind.Float32:
                return BinaryPrimitives.ReadSingleLittleEndian(raw.Slice(frameOffset + channel * 4, 4));
            case WaveKind.Pcm16:
                return BinaryPrimitives.ReadInt16LittleEndian(raw.Slice(frameOffset + channel * 2, 2)) / 32768f;
            case WaveKind.Pcm24:
            {
                int at = frameOffset + channel * 3;
                int sample = raw[at] | (raw[at + 1] << 8) | (raw[at + 2] << 16);
                if ((sample & 0x800000) != 0)
                    sample |= unchecked((int)0xFF000000);
                return sample / 8388608f;
            }
            case WaveKind.Pcm32:
                return BinaryPrimitives.ReadInt32LittleEndian(raw.Slice(frameOffset + channel * 4, 4)) / 2147483648f;
            default:
                return 0;
        }
    }
}
