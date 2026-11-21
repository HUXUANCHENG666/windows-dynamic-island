using System.Windows.Threading;
using DynamicIsland.Core.LiveTranslate;
using Whisper.net;
using Whisper.net.LibraryLoader;

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
sealed class LiveTranslateService : IDisposable, IAsyncDisposable
{
    readonly Dispatcher _dispatcher;
    readonly AsyncSessionOwner _owner = new();
    volatile bool _running;
    public LiveTranslateService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _owner.Faulted += ex => Log.Info($"live-translate session fault type={ex.GetType().Name} hr=0x{ex.HResult:X8}");
    }
    public bool IsRunning => _running;
    public event Action<LiveTranslateSnapshot>? Status;
    public event Action<int, string, string?, bool>? Transcript;
    public void Start()
    {
        if (_running) return;
        _running = true;
        _owner.Start(RunSessionAsync);
    }
    public void CancelDownload()
    {
        _dispatcher.VerifyAccess();
        Stop();
        Status?.Invoke(new(LiveTranslatePhase.Error, LiveTranslateCopy.Cancelled, null, false));
    }
    public void Stop() { _running = false; _ = _owner.StopAsync(); }
    public Task StopAsync() { _running = false; return _owner.StopAsync(); }

    async Task RunSessionAsync(int epoch, CancellationToken token)
    {
        WhisperFactory? factory = null;
        WhisperProcessor? stream = null, final = null;
        await using var capture = new LoopbackCapture();
        using var stopCapture = token.Register(capture.RequestStop);
        var segmenter = new SpeechSegmenter();
        var queue = new RecognitionWorkQueue();
        var gate = new object();
        using var wake = new SemaphoreSlim(0, 1);
        void Audio(float[] samples)
        {
            if (token.IsCancellationRequested) return;
            lock (gate)
            {
                if (token.IsCancellationRequested) return;
                int dropped = queue.Dropped;
                foreach (var chunk in segmenter.Push(samples)) queue.Enqueue(chunk);
                if (queue.Dropped != dropped)
                {
                    Log.Info($"live-translate recognition overload dropped={queue.Dropped}");
                    Publish(epoch, new(LiveTranslatePhase.Recognizing, "识别繁忙，已跳过积压音频；已有字幕保留。", null, false));
                }
                if (queue.Count > 0 && wake.CurrentCount == 0) wake.Release();
            }
        }
        void Failed(string message) => Post(epoch, () => Status?.Invoke(new(LiveTranslatePhase.Error, message, null, false)));
        capture.Audio += Audio; capture.Failed += Failed;
        string failureMessage = LiveTranslateCopy.DownloadFailed;
        try
        {
            bool retried = false;
            while (factory == null)
            {
                token.ThrowIfCancellationRequested();
                if (!WhisperModelStore.IsReady())
                {
                    Publish(epoch, new(LiveTranslatePhase.Downloading, LiveTranslateCopy.Connecting, 0, true));
                    await WhisperModelStore.DownloadAsync((message, progress) => Publish(epoch,
                        new(LiveTranslatePhase.Downloading, message, progress, true)), token).ConfigureAwait(false);
                }
                failureMessage = LiveTranslateCopy.ModelLoadFailed;
                Publish(epoch, new(LiveTranslatePhase.Loading, LiveTranslateCopy.LoadingModel, null, false));
                PreferVulkanThenCpu();
                try
                {
                    factory = WhisperFactory.FromPath(WhisperModelStore.FilePath);
                    final = CreateProcessor(factory, accurate: true);
                    stream = CreateProcessor(factory, accurate: false);
                }
                catch (Exception ex)
                {
                    stream?.Dispose(); stream = null; final?.Dispose(); final = null; factory?.Dispose(); factory = null;
                    if (LiveTranslateCopy.TryNativeLibraryError(ex, out var message))
                    { Publish(epoch, new(LiveTranslatePhase.Error, message, null, false)); return; }
                    if (retried) throw;
                    retried = true; WhisperModelStore.TryDeleteCorrupt();
                }
            }
            token.ThrowIfCancellationRequested();
            failureMessage = LiveTranslateCopy.CaptureOpenFailed;
            await capture.StartAsync(token).ConfigureAwait(false);
            failureMessage = "识别中断；历史字幕已保留，请关闭后重试。";
            Publish(epoch, new(LiveTranslatePhase.Listening, LiveTranslateCopy.Listening, null, false));
            while (true)
            {
                await wake.WaitAsync(token).ConfigureAwait(false);
                while (true)
                {
                    SpeechChunk? next;
                    lock (gate) next = queue.Dequeue();
                    if (next is not { } chunk) break;
                    token.ThrowIfCancellationRequested();
                    Publish(epoch, new(LiveTranslatePhase.Recognizing, chunk.Final ? LiveTranslateCopy.Recognizing : LiveTranslateCopy.Recording, null, false));
                    var parts = new List<string>(); string? language = null;
                    var processor = chunk.Final ? final! : stream!;
                    await foreach (var part in processor.ProcessAsync(chunk.Samples, token).ConfigureAwait(false))
                    { language ??= part.Language; if (!string.IsNullOrWhiteSpace(part.Text)) parts.Add(part.Text.Trim()); }
                    string text = LiveTranslatePolicy.Collapse(string.Join(" ", parts));
                    // 空终稿也携带身份收束；不能把它过滤后让下一句覆盖上一句。
                    if (chunk.Final || LiveTranslatePolicy.IsUsableTranscript(text))
                        Post(epoch, () => Transcript?.Invoke(chunk.UtteranceId, text, language, chunk.Final));
                    Publish(epoch, new(LiveTranslatePhase.Listening, LiveTranslateCopy.Listening, null, false));
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Info($"live-translate worker fault type={ex.GetType().Name} hr=0x{ex.HResult:X8}");
            Publish(epoch, new(LiveTranslatePhase.Error, failureMessage, null, false));
        }
        finally
        {
            capture.Audio -= Audio; capture.Failed -= Failed;
            // Join the STA producer before disposing wake/segmenter or native decoder ownership.
            await capture.DisposeAsync().ConfigureAwait(false);
            try { stream?.Dispose(); }
            finally { try { final?.Dispose(); } finally { factory?.Dispose(); } }
            Log.Info($"live-translate session drained queued-dropped={queue.Dropped}");
            // A newer start owns its own state; never mark that session stopped.
            if (_owner.IsCurrent(epoch)) _running = false;
        }
    }
    void Post(int epoch, Action action)
    {
        if (!_owner.IsCurrent(epoch) || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        try { _dispatcher.BeginInvoke(() => { if (_owner.IsCurrent(epoch) && !_dispatcher.HasShutdownStarted) action(); }); }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) { }
    }
    void Publish(int epoch, LiveTranslateSnapshot snapshot) => Post(epoch, () => Status?.Invoke(snapshot));
    static void PreferVulkanThenCpu()
    {
        RuntimeOptions.RuntimeLibraryOrder =
        [
            RuntimeLibrary.Vulkan,
            RuntimeLibrary.Cpu
        ];
    }

    static WhisperProcessor CreateProcessor(WhisperFactory factory, bool accurate)
    {
        var builder = factory.CreateBuilder()
            .WithLanguage("auto")
            .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
            .WithMaxLastTextTokens(256)
            .WithTemperature(0);
        if (accurate)
            builder = builder.WithBeamSearchSamplingStrategy(beam => beam.WithBeamSize(3));
        return builder.Build();
    }


    public void Dispose() { _running = false; _ = _owner.StopAsync(close: true); }
    public async ValueTask DisposeAsync() { _running = false; await _owner.StopAsync(close: true).ConfigureAwait(false); }
}
