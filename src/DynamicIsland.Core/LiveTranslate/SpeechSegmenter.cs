namespace DynamicIsland.Core.LiveTranslate;

/// <summary>
/// 从连续的系统声音里取出当前这句话的音频。说话过程中会定期交出一份还在增长的片段，
/// 好让识别像会议记录一样持续出字；停顿够长或达到上限时再交终稿，去翻译。
/// </summary>
public readonly record struct SpeechChunk(int UtteranceId, float[] Samples, bool Final);

public sealed class SpeechSegmenter
{
    public const int SampleRate = StreamResampler.TargetRate;
    public const int FrameSamples = 320;
    public const float SpeechRms = 0.015f;
    public const int HangoverFrames = 32; // 20 ms/frame ≈ 640 ms of trailing silence
    public const int MinSpeechFrames = 12;
    public const int PartialFrames = 13; // 20 ms/frame ≈ 0.26 s between live captions
    public const int MaxSpeechFrames = 750; // 20 ms/frame ≈ 15 s, a longer thought can finish
    public const int PrerollFrames = 10;

    readonly List<float> _pending = new();
    readonly List<float> _utterance = new();
    readonly Queue<float> _preroll = new();
    bool _inSpeech;
    int _hangover;
    int _speechFrames;
    int _lastPartialFrames;
    int _utteranceId = 1;

    public IReadOnlyList<SpeechChunk> Push(ReadOnlySpan<float> samples)
    {
        if (samples.Length > 0)
            _pending.AddRange(samples.ToArray());

        List<SpeechChunk>? chunks = null;
        while (_pending.Count >= FrameSamples)
        {
            var frame = _pending.GetRange(0, FrameSamples);
            _pending.RemoveRange(0, FrameSamples);
            var chunk = Accept(frame);
            if (chunk is null)
                continue;
            chunks ??= new List<SpeechChunk>();
            chunks.Add(chunk.Value);
        }

        return chunks ?? (IReadOnlyList<SpeechChunk>)Array.Empty<SpeechChunk>();
    }

    public SpeechChunk? Flush()
    {
        SpeechChunk? tail = null;
        if (_inSpeech && _speechFrames >= MinSpeechFrames)
            tail = new SpeechChunk(_utteranceId, _utterance.ToArray(), Final: true);
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
        _lastPartialFrames = 0;
        _utteranceId++;
    }

    SpeechChunk? Accept(List<float> frame)
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
            _lastPartialFrames = 0;
            foreach (var sample in _preroll)
                _utterance.Add(sample);
            _preroll.Clear();
            return MaybeEmit(forceFinal: UtteranceFrames() >= MaxSpeechFrames);
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
        return MaybePartial();
    }

    SpeechChunk? MaybeEmit(bool forceFinal) =>
        forceFinal ? Finish(force: true) : MaybePartial();

    SpeechChunk? MaybePartial()
    {
        if (!_inSpeech || _speechFrames < PartialFrames)
            return null;
        if (_speechFrames - _lastPartialFrames < PartialFrames)
            return null;
        _lastPartialFrames = _speechFrames;
        return new SpeechChunk(_utteranceId, _utterance.ToArray(), Final: false);
    }

    int UtteranceFrames() => _utterance.Count / FrameSamples;

    SpeechChunk? Finish(bool force)
    {
        var data = _utterance.ToArray();
        bool keep = _speechFrames >= MinSpeechFrames;
        int id = _utteranceId;
        _utterance.Clear();
        _preroll.Clear();
        _lastPartialFrames = 0;
        _utteranceId++;
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

        return keep ? new SpeechChunk(id, data, Final: true) : null;
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
