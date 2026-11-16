using System.Windows.Threading;
using DynamicIsland.Core.LiveTranslate;
using Whisper.net;

namespace DynamicIsland.Services;

enum LiveTranslatePhase
{
    Off,
    Downloading,
    Loading,
    Listening,
    Recognizing,
    Error
}

readonly record struct LiveTranslateSnapshot(
    LiveTranslatePhase Phase,
    string Message,
    double? Progress,
    bool CanCancel);

/// <summary>
/// 把模型下载、系统内录、本地识别串在一起。字幕文本通过 Transcript 交给窗口去翻译。
/// 不把原始音频或识别结果写进磁盘或日志。
/// </summary>
sealed class LiveTranslateService : IDisposable
{
    readonly Dispatcher _dispatcher;
    readonly LoopbackCapture _capture = new();
    readonly SpeechSegmenter _segmenter = new();
    readonly object _gate = new();
    CancellationTokenSource? _cts;
    WhisperFactory? _factory;
    Task? _loop;
    float[]? _queued;
    bool _busy;
    int _generation;

    public LiveTranslateService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _capture.Audio += OnAudio;
        _capture.Failed += message => Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Error, message, null, false));
    }

    public bool IsRunning { get; private set; }
    public event Action<LiveTranslateSnapshot>? Status;
    public event Action<string, string?>? Transcript;

    public void Start()
    {
        if (IsRunning)
            return;
        IsRunning = true;
        _generation++;
        int generation = _generation;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _segmenter.Reset();
        Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Loading, LiveTranslateCopy.LoadingModel, null, false));
        _loop = Task.Run(() => RunAsync(generation, token), token);
    }

    public void CancelDownload()
    {
        if (!IsRunning)
            return;
        _cts?.Cancel();
    }

    public void Stop()
    {
        if (!IsRunning && _cts == null)
            return;
        IsRunning = false;
        _generation++;
        try { _cts?.Cancel(); }
        catch { }
        _capture.Stop();
        _segmenter.Reset();
        lock (_gate)
        {
            _queued = null;
            _busy = false;
        }

        var factory = _factory;
        _factory = null;
        try { factory?.Dispose(); }
        catch { }
        Log.Info("live-translate stop");
        Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Off, "", null, false));
    }

    async Task RunAsync(int generation, CancellationToken token)
    {
        try
        {
            bool retriedLoad = false;
            while (true)
            {
                if (!WhisperModelStore.IsReady())
                {
                    var startMessage = retriedLoad ? LiveTranslateCopy.ModelCorrupt : LiveTranslateCopy.Connecting;
                    Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Downloading, startMessage, 0, true));
                    await WhisperModelStore.DownloadAsync((message, progress) =>
                    {
                        if (generation != _generation)
                            return;
                        Publish(new LiveTranslateSnapshot(
                            LiveTranslatePhase.Downloading,
                            message,
                            progress,
                            true));
                    }, token).ConfigureAwait(false);
                }

                token.ThrowIfCancellationRequested();
                if (generation != _generation)
                    return;
                Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Loading, LiveTranslateCopy.LoadingModel, null, false));
                WhisperFactory factory;
                try
                {
                    factory = WhisperFactory.FromPath(WhisperModelStore.FilePath);
                    // FromPath 在原生库加载成功后仍可能接受一份损坏的模型，真正读文件发生在建处理器时。
                    factory.CreateBuilder();
                }
                catch (Exception ex)
                {
                    Log.Error("live-translate-model", ex);
                    if (LiveTranslateCopy.TryNativeLibraryError(ex, out var nativeMessage))
                    {
                        Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Error, nativeMessage, null, false));
                        return;
                    }

                    WhisperModelStore.TryDeleteCorrupt();
                    if (retriedLoad)
                    {
                        Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Error, LiveTranslateCopy.ModelLoadFailed, null, false));
                        return;
                    }

                    retriedLoad = true;
                    continue;
                }

                if (generation != _generation)
                {
                    factory.Dispose();
                    return;
                }

                _factory = factory;
                if (generation != _generation)
                {
                    _factory = null;
                    factory.Dispose();
                    return;
                }

                Log.Info("live-translate model ready");
                Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Listening, LiveTranslateCopy.Listening, null, false));
                StartCapture();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation)
            {
                IsRunning = false;
                _capture.Stop();
                Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Error, LiveTranslateCopy.Cancelled, null, false));
                Log.Info("live-translate download cancelled");
            }
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-start", ex);
            if (generation == _generation)
            {
                Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Error, LiveTranslateCopy.DownloadFailed, null, false));
            }
        }
    }

    void StartCapture()
    {
        if (_dispatcher.CheckAccess())
        {
            _capture.Start();
            return;
        }

        _dispatcher.Invoke(() => _capture.Start());
    }

    void OnAudio(float[] samples)
    {
        if (!IsRunning || _factory == null)
            return;
        IReadOnlyList<float[]> segments;
        try
        {
            segments = _segmenter.Push(samples);
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-vad", ex);
            return;
        }

        foreach (var segment in segments)
            Enqueue(segment);
    }

    void Enqueue(float[] segment)
    {
        bool start = false;
        lock (_gate)
        {
            if (_busy)
            {
                _queued = segment;
                return;
            }

            _busy = true;
            start = true;
        }

        if (start)
            _ = Task.Run(() => RecognizeAsync(segment));
    }

    async Task RecognizeAsync(float[] segment)
    {
        try
        {
            while (true)
            {
                if (!IsRunning)
                    return;
                Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Recognizing, LiveTranslateCopy.Recognizing, null, false));
                var (text, language) = await TranscribeAsync(segment).ConfigureAwait(false);
                if (!IsRunning)
                    return;
                if (LiveTranslatePolicy.IsUsableTranscript(text))
                    RaiseTranscript(text!, language);
                Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Listening, LiveTranslateCopy.Listening, null, false));

                lock (_gate)
                {
                    if (_queued == null)
                    {
                        _busy = false;
                        return;
                    }

                    segment = _queued;
                    _queued = null;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-whisper", ex);
            lock (_gate)
            {
                _busy = false;
                _queued = null;
            }

            if (IsRunning)
                Publish(new LiveTranslateSnapshot(LiveTranslatePhase.Listening, LiveTranslateCopy.Listening, null, false));
        }
    }

    async Task<(string? Text, string? Language)> TranscribeAsync(float[] segment)
    {
        var factory = _factory;
        if (factory == null || segment.Length < SpeechSegmenter.FrameSamples)
            return (null, null);

        using var processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
            .Build();
        var parts = new List<string>();
        string? language = null;
        await foreach (var item in processor.ProcessAsync(segment))
        {
            if (!string.IsNullOrWhiteSpace(item.Language))
                language ??= item.Language;
            if (!string.IsNullOrWhiteSpace(item.Text))
                parts.Add(item.Text.Trim());
        }

        var text = LiveTranslatePolicy.Collapse(string.Join(" ", parts));
        return (string.IsNullOrWhiteSpace(text) ? null : text, language);
    }

    void RaiseTranscript(string text, string? language)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (IsRunning)
                Transcript?.Invoke(text, language);
        });
    }

    void Publish(LiveTranslateSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(() => Status?.Invoke(snapshot));
    }

    public void Dispose()
    {
        _capture.Audio -= OnAudio;
        Stop();
        _capture.Dispose();
        _cts?.Dispose();
    }
}
