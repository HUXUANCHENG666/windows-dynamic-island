using DynamicIsland.Core.LiveTranslate;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace DynamicIsland.Services;

/// <summary>
/// 捕获当前默认播放设备正在播出的声音（WASAPI loopback），不是麦克风。
/// 蓝牙耳机和扬声器都走正在使用的播放设备，声音不必从扬声器出来。
/// 默认设备或常见混音格式打不开时，会改用 32 位浮点再试，并遍历其他正在使用的播放设备。
/// 默认设备切换时会自己重开。打不开就报一句中文，不要把岛带崩。
/// </summary>
sealed class LoopbackCapture : IDisposable
{
    const int BufferMilliseconds = 200;

    readonly object _gate = new();
    WasapiCapture? _capture;
    MMDeviceEnumerator? _devices;
    MMDevice? _device;
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
        var leftovers = new List<MMDevice>();
        try
        {
            _devices = new MMDeviceEnumerator();
            var candidates = ListRenderDevices(_devices);
            leftovers.AddRange(candidates);
            if (candidates.Count == 0)
                throw new InvalidOperationException(LiveTranslateCopy.CaptureNoDevice);

            Exception? last = null;
            foreach (var device in candidates)
            {
                if (!TryOpenDevice(device, out last))
                    continue;

                _device = device;
                leftovers.Remove(device);
                DisposeDevices(leftovers);
                leftovers.Clear();
                _running = true;
                HookNotifications();
                Log.Info($"live-translate capture start device={DeviceLabel(device)}");
                return;
            }

            throw last ?? new InvalidOperationException(LiveTranslateCopy.CaptureFailedGeneric);
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-capture", ex);
            DisposeDevices(leftovers);
            CloseLocked();
            _running = false;
            Failed?.Invoke(LiveTranslateCopy.MessageForCaptureException(ex));
        }
    }

    bool TryOpenDevice(MMDevice device, out Exception? error)
    {
        error = null;
        WaveFormat? mix = null;
        try
        {
            using var probe = new MixLoopbackCapture(device, BufferMilliseconds);
            mix = probe.WaveFormat;
        }
        catch (Exception ex)
        {
            error = ex;
            Log.Error("live-translate-capture", ex);
            Log.Info($"live-translate capture probe failed device={DeviceLabel(device)}");
        }

        int rate = mix is { SampleRate: >= 8000 } ? mix.SampleRate : 48000;
        var attempts = new List<WaveFormat?>();
        if (mix != null)
            attempts.Add(null);
        attempts.Add(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2));
        attempts.Add(WaveFormat.CreateIeeeFloatWaveFormat(rate, 1));
        if (rate != 48000)
            attempts.Add(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        if (rate != 44100)
            attempts.Add(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var format in attempts)
        {
            string key = format == null
                ? "mix"
                : $"ieee-{format.SampleRate}-{format.Channels}";
            if (!seen.Add(key))
                continue;
            if (TryStart(device, format, out error))
                return true;
        }

        return false;
    }

    bool TryStart(MMDevice device, WaveFormat? overrideFormat, out Exception? error)
    {
        error = null;
        MixLoopbackCapture? created = null;
        try
        {
            created = overrideFormat == null
                ? new MixLoopbackCapture(device, BufferMilliseconds)
                : new MixLoopbackCapture(device, overrideFormat, BufferMilliseconds);
            var format = created.WaveFormat;
            Guid? subFormat = format is WaveFormatExtensible extensible ? extensible.SubFormat : null;
            Log.Info(
                $"live-translate capture try device={DeviceLabel(device)} " +
                $"encoding={(int)format.Encoding} rate={format.SampleRate} " +
                $"ch={format.Channels} bits={format.BitsPerSample} " +
                $"override={(overrideFormat == null ? "mix" : "ieee-float")}");
            if (format.Channels <= 0 || format.SampleRate < 8000 ||
                !WaveFormatKind.TryDetect((int)format.Encoding, format.BitsPerSample, subFormat, out var kind))
            {
                throw new InvalidOperationException(LiveTranslateCopy.CaptureFormatUnsupported);
            }

            var resampler = new StreamResampler(format.SampleRate, format.Channels, kind);
            created.DataAvailable += OnData;
            created.RecordingStopped += OnStopped;
            created.StartRecording();
            _capture = created;
            _resampler = resampler;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            Log.Error("live-translate-capture", ex);
            if (created != null)
            {
                created.DataAvailable -= OnData;
                created.RecordingStopped -= OnStopped;
                try { created.Dispose(); }
                catch { }
            }

            return false;
        }
    }

    static List<MMDevice> ListRenderDevices(MMDeviceEnumerator enumerator)
    {
        var list = new List<MMDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(MMDevice? device)
        {
            if (device == null)
                return;
            try
            {
                if (!seen.Add(device.ID))
                {
                    device.Dispose();
                    return;
                }

                list.Add(device);
                Log.Info($"live-translate capture candidate device={DeviceLabel(device)}");
            }
            catch (Exception ex)
            {
                Log.Error("live-translate-device", ex);
                try { device.Dispose(); }
                catch { }
            }
        }

        try { Add(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)); }
        catch (Exception ex) { Log.Error("live-translate-device", ex); }
        try { Add(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications)); }
        catch (Exception ex) { Log.Error("live-translate-device", ex); }
        try
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                Add(device);
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-device", ex);
        }

        return list;
    }

    static void DisposeDevices(IEnumerable<MMDevice> devices)
    {
        foreach (var device in devices)
        {
            try { device.Dispose(); }
            catch { }
        }
    }

    static string DeviceLabel(MMDevice device)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(device.FriendlyName))
                return device.FriendlyName;
        }
        catch { }

        try { return device.ID; }
        catch { return "(unknown)"; }
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
        var device = _device;
        _device = null;
        try { device?.Dispose(); }
        catch { }

        var devices = _devices;
        _devices = null;
        try { devices?.Dispose(); }
        catch { }
    }

    public void Dispose() => Stop();

    sealed class MixLoopbackCapture : WasapiCapture
    {
        public MixLoopbackCapture(MMDevice device, int bufferMilliseconds)
            : base(device, false, bufferMilliseconds)
        {
        }

        public MixLoopbackCapture(MMDevice device, WaveFormat format, int bufferMilliseconds)
            : base(device, false, bufferMilliseconds)
        {
            WaveFormat = format;
        }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags()
        {
            return AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
        }
    }

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
