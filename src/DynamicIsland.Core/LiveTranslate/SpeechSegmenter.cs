namespace DynamicIsland.Core.LiveTranslate;

/// <summary>
/// 用短时能量把连续的系统声音切成一句一句。太短的噪声丢掉，太长的在上限处切开，
/// 这样字幕可以边说边更新，而不必等整段节目结束。
/// </summary>
public sealed class SpeechSegmenter
{
    public const int SampleRate = StreamResampler.TargetRate;
    public const int FrameSamples = 320;
    public const float SpeechRms = 0.015f;
    public const int HangoverFrames = 18; // 20 ms/frame ≈ 360 ms of trailing silence
    public const int MinSpeechFrames = 20;
    public const int MaxSpeechFrames = 200; // 20 ms/frame ≈ 4 s, so long speech updates sooner
    public const int PrerollFrames = 10;

    readonly List<float> _pending = new();
    readonly List<float> _utterance = new();
    readonly Queue<float> _preroll = new();
    bool _inSpeech;
    int _hangover;
    int _speechFrames;

    public IReadOnlyList<float[]> Push(ReadOnlySpan<float> samples)
    {
        if (samples.Length > 0)
            _pending.AddRange(samples.ToArray());

        List<float[]>? segments = null;
        while (_pending.Count >= FrameSamples)
        {
            var frame = _pending.GetRange(0, FrameSamples);
            _pending.RemoveRange(0, FrameSamples);
            var segment = Accept(frame);
            if (segment is null)
                continue;
            segments ??= new List<float[]>();
            segments.Add(segment);
        }

        return segments ?? (IReadOnlyList<float[]>)Array.Empty<float[]>();
    }

    public float[]? Flush()
    {
        float[]? tail = null;
        if (_inSpeech && _speechFrames >= MinSpeechFrames)
            tail = _utterance.ToArray();
        Reset();
        return tail;
    }

    public void Reset()
    {
        _pending.Clear();
        _utterance.Clear();
        _preroll.Clear();
        _inSpeech = false;
        _hangover = 0;
        _speechFrames = 0;
    }

    float[]? Accept(List<float> frame)
    {
        bool voiced = Rms(frame) >= SpeechRms;
        if (!_inSpeech)
        {
            RememberPreroll(frame);
            if (!voiced)
                return null;
            _inSpeech = true;
            _hangover = HangoverFrames;
            _speechFrames = 1;
            foreach (var sample in _preroll)
                _utterance.Add(sample);
            _preroll.Clear();
            return MaybeCut();
        }

        _utterance.AddRange(frame);
        if (voiced)
        {
            _hangover = HangoverFrames;
            _speechFrames++;
        }
        else
        {
            _hangover--;
        }

        if (UtteranceFrames() >= MaxSpeechFrames)
            return Finish(force: true);
        if (!voiced && _hangover <= 0)
            return Finish(force: false);
        return null;
    }

    float[]? MaybeCut()
    {
        if (UtteranceFrames() < MaxSpeechFrames)
            return null;
        return Finish(force: true);
    }

    int UtteranceFrames() => _utterance.Count / FrameSamples;

    float[]? Finish(bool force)
    {
        var data = _utterance.ToArray();
        bool keep = _speechFrames >= MinSpeechFrames;
        _utterance.Clear();
        _preroll.Clear();
        if (force)
        {
            _inSpeech = true;
            _hangover = HangoverFrames;
            _speechFrames = 0;
        }
        else
        {
            _inSpeech = false;
            _hangover = 0;
            _speechFrames = 0;
        }

        return keep ? data : null;
    }

    void RememberPreroll(List<float> frame)
    {
        foreach (var sample in frame)
            _preroll.Enqueue(sample);
        int limit = PrerollFrames * FrameSamples;
        while (_preroll.Count > limit)
            _preroll.Dequeue();
    }

    static float Rms(List<float> frame)
    {
        double sum = 0;
        for (int i = 0; i < frame.Count; i++)
        {
            float s = frame[i];
            sum += s * s;
        }

        return (float)Math.Sqrt(sum / frame.Count);
    }
}
