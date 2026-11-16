using DynamicIsland.Core.LiveTranslate;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace DynamicIsland.Services;

/// <summary>
/// 捕获当前默认播放设备正在播出的声音（WASAPI loopback），不是麦克风。
/// 默认设备切换时会自己重开。打不开就报一句中文，不要把岛带崩。
/// </summary>
sealed class LoopbackCapture : IDisposable
{
    readonly object _gate = new();
    readonly List<float> _resampled = new();
    WasapiLoopbackCapture? _capture;
    MMDeviceEnumerator? _devices;
    NotificationClient? _notify;
    StreamResampler? _resampler;
    bool _running;
    int _restarting;

    public event Action<float[]>? Audio;
    public event Action<string>? Failed;

    public bool IsRunning
    {
        get { lock (_gate) return _running && _capture != null; }
    }

    public void Start()
    {
        lock (_gate)
        {
            OpenLocked();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            CloseLocked();
        }
    }

    void OpenLocked()
    {
        CloseLocked();
        try
        {
            _devices = new MMDeviceEnumerator();
            var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var capture = new WasapiLoopbackCapture(device);
            var format = capture.WaveFormat;
            var kind = format.Encoding == WaveFormatEncoding.IeeeFloat
                ? WaveKind.Float32
                : format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16
                    ? WaveKind.Pcm16
                    : throw new InvalidOperationException(LiveTranslateCopy.CaptureFailed);
            if (format.Channels <= 0 || format.SampleRate < 8000)
                throw new InvalidOperationException(LiveTranslateCopy.CaptureFailed);

            _resampler = new StreamResampler(format.SampleRate, format.Channels, kind);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnStopped;
            _capture = capture;
            _running = true;
            capture.StartRecording();
            HookNotifications();
            Log.Info("live-translate capture start");
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-capture", ex);
            CloseLocked();
            _running = false;
            Failed?.Invoke(LiveTranslateCopy.CaptureFailed);
        }
    }

    void HookNotifications()
    {
        if (_devices == null || _notify != null)
            return;
        try
        {
            _notify = new NotificationClient(this);
            _devices.RegisterEndpointNotificationCallback(_notify);
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-device", ex);
            _notify = null;
        }
    }

    void OnData(object? sender, WaveInEventArgs args)
    {
        StreamResampler? resampler;
        bool running;
        lock (_gate)
        {
            running = _running;
            resampler = _resampler;
        }

        if (!running || resampler == null || args.BytesRecorded <= 0)
            return;

        var chunk = new List<float>(Math.Max(16, args.BytesRecorded / 8));
        try
        {
            resampler.Push(args.Buffer.AsSpan(0, args.BytesRecorded), chunk);
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-resample", ex);
            return;
        }

        if (chunk.Count > 0)
            Audio?.Invoke(chunk.ToArray());
    }

    void OnStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception != null)
            Log.Error("live-translate-capture", args.Exception);
        bool restart;
        lock (_gate)
        {
            restart = _running;
        }

        if (restart)
            RequestRestart();
    }

    internal void OnDefaultDeviceChanged()
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
                lock (_gate)
                {
                    if (_running)
                    {
                        Log.Info("live-translate capture restart");
                        OpenLocked();
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _restarting, 0);
            }
        });
    }

    void CloseLocked()
    {
        var capture = _capture;
        _capture = null;
        _resampler = null;
        if (capture != null)
        {
            capture.DataAvailable -= OnData;
            capture.RecordingStopped -= OnStopped;
            try
            {
                if (capture.CaptureState != CaptureState.Stopped)
                    capture.StopRecording();
            }
            catch
            {
                // 停不下来也要把对象丢掉。
            }

            try { capture.Dispose(); }
            catch { }
        }

        if (_devices != null && _notify != null)
        {
            try { _devices.UnregisterEndpointNotificationCallback(_notify); }
            catch { }
        }

        _notify = null;
        var devices = _devices;
        _devices = null;
        try { devices?.Dispose(); }
        catch { }
    }

    public void Dispose() => Stop();

    sealed class NotificationClient : IMMNotificationClient
    {
        readonly LoopbackCapture _owner;

        public NotificationClient(LoopbackCapture owner) => _owner = owner;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render)
                _owner.OnDefaultDeviceChanged();
        }

        public void OnDeviceAdded(string deviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }
}
