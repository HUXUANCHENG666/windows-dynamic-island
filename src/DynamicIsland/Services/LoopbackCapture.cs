using DynamicIsland.Core.LiveTranslate;
using DynamicIsland.Native;
namespace DynamicIsland.Services;

/// <summary>COM lives on endpoint STA; stop invalidates callbacks immediately and asynchronously joins before handle release.</summary>
sealed class LoopbackCapture : IDisposable, IAsyncDisposable
{
    readonly object _gate = new();
    readonly SemaphoreSlim _lifecycle = new(1, 1);
    LoopbackEndpoint? _endpoint;
    bool _running, _disposed;
    int _id, _restarting;
    public event Action<float[]>? Audio;
    public event Action<string>? Failed;
    public async Task StartAsync(CancellationToken token, int? restartId = null)
    {
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            LoopbackEndpoint? old; int id;
            lock (_gate)
            {
                if (_disposed || (restartId != null && (!_running || restartId != _id))) return;
                old = _endpoint; _endpoint = null; id = ++_id; _running = true;
            }
            if (old != null) await old.DisposeAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var endpoint = new LoopbackEndpoint();
            bool accepted = false;
            try
            {
                if (!endpoint.Open(out var layout, out _, out var error))
                    throw new InvalidOperationException(LiveTranslateCopy.MessageForCaptureException(error ?? new InvalidOperationException()));
                if (layout.Channels <= 0 || layout.SampleRate < 8000 || !WaveFormatKind.TryDetect(layout, out var kind))
                    throw new InvalidOperationException(LiveTranslateCopy.CaptureFormatUnsupported);
                var resampler = new StreamResampler(layout.SampleRate, layout.Channels, kind);
                endpoint.Data += buffer =>
                {
                    lock (_gate) if (_disposed || !_running || id != _id || token.IsCancellationRequested) return;
                    var samples = new List<float>(); resampler.Push(buffer, samples);
                    if (samples.Count > 0 && !token.IsCancellationRequested) Audio?.Invoke(samples.ToArray());
                };
                endpoint.Failed += ex =>
                {
                    lock (_gate) if (_disposed || id != _id) return;
                    Failed?.Invoke(LiveTranslateCopy.MessageForCaptureException(ex));
                };
                endpoint.DefaultDeviceChanged += () => RequestRestart(id, token);
                lock (_gate)
                {
                    if (!_disposed && _running && id == _id && !token.IsCancellationRequested)
                    { _endpoint = endpoint; accepted = true; endpoint.BeginPump(); }
                }
            }
            finally { if (!accepted) await endpoint.DisposeAsync().ConfigureAwait(false); }
        }
        finally { _lifecycle.Release(); }
    }
    void RequestRestart(int id, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _restarting, 1, 0) != 0) return;
        _ = RestartAsync(id, token);
    }
    async Task RestartAsync(int id, CancellationToken token)
    {
        try { await Task.Delay(250, token).ConfigureAwait(false); await StartAsync(token, id).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Info($"live-translate capture-restart type={ex.GetType().Name} hr=0x{ex.HResult:X8}");
            Failed?.Invoke(LiveTranslateCopy.CaptureOpenFailed);
        }
        finally { Interlocked.Exchange(ref _restarting, 0); }
    }
    public void RequestStop()
    {
        LoopbackEndpoint? endpoint;
        lock (_gate) { _disposed = true; _running = false; ++_id; endpoint = _endpoint; }
        endpoint?.Dispose(); // signal only; its owner closes handles after the STA finishes
    }
    public void Dispose() => _ = DisposeAsync();
    public async ValueTask DisposeAsync()
    {
        RequestStop();
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            LoopbackEndpoint? endpoint;
            lock (_gate) { endpoint = _endpoint; _endpoint = null; }
            if (endpoint != null) await endpoint.DisposeAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }
}
