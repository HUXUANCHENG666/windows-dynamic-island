using DynamicIsland.Core.LiveTranslate;
using DynamicIsland.Native;

namespace DynamicIsland.Services;

/// <summary>
/// 捕获当前默认播放设备正在播出的声音（WASAPI loopback），不是麦克风。
/// 蓝牙耳机和扬声器都走正在使用的播放设备，声音不必从扬声器出来。
/// 打开设备走音量功能同一套系统接口，在单独的套间线程上捕获。
/// 默认设备切换时会自己重开。打不开就报一句中文，不要把岛带崩。
/// </summary>
sealed class LoopbackCapture : IDisposable
{
    readonly object _gate = new();
    LoopbackEndpoint? _endpoint;
    StreamResampler? _resampler;
    bool _running;
    int _openId;
    int _restarting;

    public event Action<float[]>? Audio;
    public event Action<string>? Failed;

    public bool IsRunning
    {
        get { lock (_gate) return _running && _endpoint != null; }
    }

    public void Start()
    {
        int id;
        LoopbackEndpoint? old;
        lock (_gate)
        {
            id = ++_openId;
            _running = true;
            old = DetachLocked();
        }

        old?.Dispose();

        var endpoint = new LoopbackEndpoint();
        if (!endpoint.Open(out var layout, out var device, out var error))
        {
            var ex = error ?? new InvalidOperationException(LiveTranslateCopy.CaptureFailedGeneric);
            Log.Error("live-translate-capture", ex);
            endpoint.Dispose();
            if (StillCurrent(id))
            {
                lock (_gate)
                {
                    if (id == _openId)
                        _running = false;
                }

                Failed?.Invoke(LiveTranslateCopy.MessageForCaptureException(ex));
            }

            return;
        }

        if (layout.Channels <= 0 || layout.SampleRate < 8000 ||
            !WaveFormatKind.TryDetect(layout, out var kind))
        {
            var ex = new InvalidOperationException(LiveTranslateCopy.CaptureFormatUnsupported);
            Log.Error("live-translate-capture", ex);
            Log.Info($"live-translate capture unsupported {device}");
            endpoint.Dispose();
            if (StillCurrent(id))
            {
                lock (_gate)
                {
                    if (id == _openId)
                        _running = false;
                }

                Failed?.Invoke(LiveTranslateCopy.CaptureFormatUnsupported);
            }

            return;
        }

        var resampler = new StreamResampler(layout.SampleRate, layout.Channels, kind);
        lock (_gate)
        {
            if (id != _openId || !_running)
            {
                endpoint.Dispose();
                return;
            }

            _endpoint = endpoint;
            _resampler = resampler;
            endpoint.Data += OnData;
            endpoint.Failed += OnFailed;
            endpoint.DefaultDeviceChanged += OnDefaultDeviceChanged;
            endpoint.BeginPump();
            Log.Info($"live-translate capture start {device} kind={kind}");
        }
    }

    public void Stop()
    {
        LoopbackEndpoint? old;
        lock (_gate)
        {
            _openId++;
            _running = false;
            old = DetachLocked();
        }

        old?.Dispose();
    }

    bool StillCurrent(int id)
    {
        lock (_gate)
            return id == _openId;
    }

    LoopbackEndpoint? DetachLocked()
    {
        var endpoint = _endpoint;
        _endpoint = null;
        _resampler = null;
        if (endpoint != null)
        {
            endpoint.Data -= OnData;
            endpoint.Failed -= OnFailed;
            endpoint.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        }

        return endpoint;
    }

    void OnData(byte[] buffer)
    {
        StreamResampler? resampler;
        bool running;
        lock (_gate)
        {
            running = _running;
            resampler = _resampler;
        }

        if (!running || resampler == null || buffer.Length == 0)
            return;

        var chunk = new List<float>(Math.Max(16, buffer.Length / 8));
        try
        {
            resampler.Push(buffer, chunk);
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-resample", ex);
            return;
        }

        if (chunk.Count > 0)
            Audio?.Invoke(chunk.ToArray());
    }

    void OnFailed(Exception ex)
    {
        Log.Error("live-translate-capture", ex);
        Failed?.Invoke(LiveTranslateCopy.MessageForCaptureException(ex));
    }

    void OnDefaultDeviceChanged()
    {
        bool running;
        lock (_gate)
        {
            running = _running;
        }

        if (running)
            RequestRestart();
    }

    void RequestRestart()
    {
        if (Interlocked.CompareExchange(ref _restarting, 1, 0) != 0)
            return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Thread.Sleep(250);
                bool running;
                lock (_gate)
                {
                    running = _running;
                }

                if (running)
                {
                    Log.Info("live-translate capture restart");
                    Start();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _restarting, 0);
            }
        });
    }

    public void Dispose() => Stop();
}
