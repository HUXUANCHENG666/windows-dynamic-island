using System.Runtime.InteropServices;
using DynamicIsland.Core.LiveTranslate;
using DynamicIsland.Services;

namespace DynamicIsland.Native;

/// <summary>
/// 用音量功能同一套系统接口打开默认播放设备，再以 WASAPI 回路捕获系统正在播放的声音。
/// 不走 NAudio 的设备枚举，避免单文件发布后打开设备时强制转换失败。
/// 打开、读取、关闭都在同一条套间线程上，避免跨线程使用 COM 对象。
/// </summary>
sealed class LoopbackEndpoint : IDisposable, IAsyncDisposable
{
    const int ShareModeShared = 0;
    const int DataFlowRender = 0;
    const uint ClsCtxAll = 23;
    const uint StreamLoopback = 0x00020000;
    const uint StreamEventCallback = 0x00040000;
    const uint StreamAutoConvertPcm = 0x80000000;
    const uint StreamSrcDefaultQuality = 0x08000000;
    const uint StreamFlags = StreamLoopback | StreamEventCallback | StreamAutoConvertPcm | StreamSrcDefaultQuality;
    const uint BufferFlagSilent = 0x2;
    const int BufferDurationHns = 200 * 10_000;
    const int WaitMs = 300;

    static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    static readonly int[] DefaultRoles = [1, 0, 2];

    readonly object _lifetime = new();
    readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource _openReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task? _cleanup;
    readonly ManualResetEventSlim _opened = new(false);
    readonly ManualResetEventSlim _pump = new(false);
    EventWaitHandle? _bufferReady;
    Thread? _thread;
    Exception? _openError;
    WaveFormatKind.WaveLayout _layout;
    string _deviceLabel = "";
    volatile bool _running;
    volatile bool _disposed;
    IAudioClient? _client;
    IAudioCaptureClient? _capture;
    AudioEndpoint.IMMDeviceEnumerator? _enumerator;
    AudioEndpoint.DeviceCallback? _callback;

    public event Action<byte[]>? Data;
    public event Action<Exception>? Failed;
    public event Action? DefaultDeviceChanged;

    public WaveFormatKind.WaveLayout Layout => _layout;
    public string DeviceLabel => _deviceLabel;

    public bool Open(out WaveFormatKind.WaveLayout layout, out string deviceLabel, out Exception? error)
    {
        if (_thread != null || _disposed)
            throw new InvalidOperationException("endpoint is already open or stopped");

        _opened.Reset();
        _pump.Reset();
        _openError = null;
        _running = true;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "live-translate-loopback"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        try { _opened.Wait(); }
        finally { _openReturned.TrySetResult(); }
        error = _openError;
        layout = _layout;
        deviceLabel = _deviceLabel;
        return error == null;
    }

    public void BeginPump() => _pump.Set();

    public void Dispose() => _ = DisposeAsync();
    public ValueTask DisposeAsync()
    {
        lock (_lifetime)
        {
            if (_cleanup != null) return new ValueTask(_cleanup);
            _disposed = true;
            _running = false;
            // Do not close any handle while its STA owner can still Set/Wait/ReleaseBuffer.
            _bufferReady?.Set();
            _pump.Set();
            if (_thread == null) { _finished.TrySetResult(); _openReturned.TrySetResult(); }
            _cleanup = CleanupAsync();
            return new ValueTask(_cleanup);
        }
    }
    async Task CleanupAsync()
    {
        await _finished.Task.ConfigureAwait(false);
        await _openReturned.Task.ConfigureAwait(false);
        _opened.Dispose(); _pump.Dispose(); _bufferReady?.Dispose();
    }

    void Run()
    {
        try { RunOwned(); }
        finally
        {
            try { CloseLocked(); }
            finally { _finished.TrySetResult(); }
        }
    }
    void RunOwned()
    {
        try
        {
            OpenLocked();
        }
        catch (Exception ex)
        {
            _openError = ex;
            _running = false;
        }
        finally
        {
            _opened.Set();
        }

        if (_openError == null && _running)
        {
            _pump.Wait();
            if (_running)
            {
                try
                {
                    Pump();
                }
                catch (Exception ex)
                {
                    if (_running)
                        Failed?.Invoke(ex);
                }
            }
        }

    }

    void OpenLocked()
    {
        _enumerator = (AudioEndpoint.IMMDeviceEnumerator)new AudioEndpoint.MMDeviceEnumerator();
        _callback = new AudioEndpoint.DeviceCallback(OnDefaultDeviceChanged);
        try { _enumerator.RegisterEndpointNotificationCallback(_callback); }
        catch (Exception ex) { Log.Error("live-translate-device", ex); }

        Exception? last = null;
        foreach (var role in DefaultRoles)
        {
            var hr = _enumerator.GetDefaultAudioEndpoint(DataFlowRender, role, out var device);
            if (hr < 0 || device is null)
            {
                last = Marshal.GetExceptionForHR(hr) ?? last;
                Log.Info($"live-translate capture default role={role} hr=0x{unchecked((uint)hr):X8}");
                continue;
            }

            try
            {
                if (TryInitialize(device, role, out var layout, out var label, out last))
                {
                    _layout = layout;
                    _deviceLabel = label;
                    return;
                }
            }
            finally
            {
                ReleaseCom(device);
            }
        }

        throw last ?? new InvalidOperationException(LiveTranslateCopy.CaptureNoDevice);
    }

    bool TryInitialize(
        AudioEndpoint.IMMDevice device,
        int role,
        out WaveFormatKind.WaveLayout layout,
        out string label,
        out Exception? error)
    {
        layout = default;
        label = "";
        error = null;
        string id = ReadDeviceId(device);
        Log.Info($"live-translate capture candidate role={role} id={id}");

        IAudioClient? client = ActivateClient(device, out error);
        if (client is null)
            return false;

        IntPtr mixPtr = IntPtr.Zero;
        try
        {
            var hr = client.GetMixFormat(out mixPtr);
            if (hr < 0 || mixPtr == IntPtr.Zero)
            {
                error = Marshal.GetExceptionForHR(hr);
                ReleaseCom(client);
                return false;
            }

            var blob = CopyWaveFormat(mixPtr);
            if (!WaveFormatKind.TryReadLayout(blob, out layout))
            {
                error = new InvalidOperationException(LiveTranslateCopy.CaptureFormatUnsupported);
                ReleaseCom(client);
                return false;
            }

            label = $"role={role} id={id} mix encoding=0x{layout.Encoding:X} {layout.SampleRate}Hz {layout.Channels}ch {layout.BitsPerSample}bit";
            Log.Info($"live-translate capture try {label}");

            hr = client.Initialize(ShareModeShared, StreamFlags, BufferDurationHns, 0, mixPtr, IntPtr.Zero);
            if (hr < 0)
            {
                Log.Info($"live-translate capture mix init hr=0x{unchecked((uint)hr):X8} id={id}");
                ReleaseCom(client);
                client = null;
                if (!TryInitializeIeeeFloat(device, layout, out client, out layout, out var floatHr))
                {
                    error = Marshal.GetExceptionForHR(hr < 0 ? hr : floatHr);
                    return false;
                }

                label = $"role={role} id={id} fallback-float {layout.SampleRate}Hz {layout.Channels}ch {layout.BitsPerSample}bit";
                Log.Info($"live-translate capture try {label}");
            }

            if (client is null)
            {
                error = new InvalidCastException("无法激活播放设备的音频接口。");
                return false;
            }

            _bufferReady = new EventWaitHandle(false, EventResetMode.AutoReset);
            hr = client.SetEventHandle(_bufferReady.SafeWaitHandle.DangerousGetHandle());
            if (hr < 0)
            {
                error = Marshal.GetExceptionForHR(hr);
                ReleaseCom(client);
                return false;
            }

            var captureIid = IidAudioCaptureClient;
            hr = client.GetService(ref captureIid, out var svc);
            if (hr < 0 || svc is not IAudioCaptureClient capture)
            {
                error = Marshal.GetExceptionForHR(hr) ?? new InvalidCastException("无法取得捕获接口。");
                ReleaseCom(client);
                return false;
            }

            hr = client.Start();
            if (hr < 0)
            {
                error = Marshal.GetExceptionForHR(hr);
                ReleaseCom(capture);
                ReleaseCom(client);
                return false;
            }

            _client = client;
            _capture = capture;
            return true;
        }
        finally
        {
            if (mixPtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(mixPtr);
        }
    }

    static IAudioClient? ActivateClient(AudioEndpoint.IMMDevice device, out Exception? error)
    {
        error = null;
        var iid = IidAudioClient;
        var hr = device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var obj);
        if (hr < 0 || obj is not IAudioClient client)
        {
            error = Marshal.GetExceptionForHR(hr) ?? new InvalidCastException("无法激活播放设备的音频接口。");
            return null;
        }

        return client;
    }

    static bool TryInitializeIeeeFloat(
        AudioEndpoint.IMMDevice device,
        WaveFormatKind.WaveLayout mix,
        out IAudioClient? client,
        out WaveFormatKind.WaveLayout layout,
        out int hr)
    {
        client = null;
        layout = default;
        hr = -1;
        int rate = mix.SampleRate >= 8000 ? mix.SampleRate : 48000;
        var channels = new List<int>();
        foreach (var ch in new[] { Math.Max(1, mix.Channels), 2, 1 })
        {
            if (!channels.Contains(ch))
                channels.Add(ch);
        }

        foreach (var ch in channels)
        {
            var attempt = ActivateClient(device, out _);
            if (attempt is null)
                continue;

            var ptr = AllocIeeeFloat(rate, ch);
            try
            {
                hr = attempt.Initialize(ShareModeShared, StreamFlags, BufferDurationHns, 0, ptr, IntPtr.Zero);
                if (hr >= 0)
                {
                    client = attempt;
                    layout = new WaveFormatKind.WaveLayout(
                        WaveFormatKind.EncodingIeeeFloat, rate, ch, 32, 4 * ch, null);
                    return true;
                }

                Log.Info($"live-translate capture float fallback hr=0x{unchecked((uint)hr):X8} {rate}Hz {ch}ch");
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }

            ReleaseCom(attempt);
        }

        return false;
    }

    void Pump()
    {
        while (_running)
        {
            _bufferReady!.WaitOne(WaitMs);
            if (!_running)
                break;
            ReadPackets();
        }

        try { _client?.Stop(); }
        catch { }
    }

    void ReadPackets()
    {
        var capture = _capture;
        if (capture is null)
            return;

        while (_running)
        {
            int hr = capture.GetNextPacketSize(out int frames);
            if (hr < 0)
                throw Marshal.GetExceptionForHR(hr)!;
            if (frames <= 0)
                return;

            hr = capture.GetBuffer(out var data, out int ready, out uint flags, out _, out _);
            if (hr < 0)
                throw Marshal.GetExceptionForHR(hr)!;

            try
            {
                if (ready <= 0 || _layout.BlockAlign <= 0)
                    continue;

                int bytes = ready * _layout.BlockAlign;
                var buffer = new byte[bytes];
                if ((flags & BufferFlagSilent) != 0 || data == IntPtr.Zero)
                    Array.Clear(buffer);
                else
                    Marshal.Copy(data, buffer, 0, bytes);
                Data?.Invoke(buffer);
            }
            finally
            {
                capture.ReleaseBuffer(ready);
            }
        }
    }

    void CloseLocked()
    {
        try { _client?.Stop(); } catch { }
        ReleaseCom(_capture);
        _capture = null;
        ReleaseCom(_client);
        _client = null;
        if (_enumerator != null && _callback != null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_callback); }
            catch { }
        }

        _callback = null;
        ReleaseCom(_enumerator);
        _enumerator = null;
    }

    void OnDefaultDeviceChanged()
    {
        if (_running)
            DefaultDeviceChanged?.Invoke();
    }

    static string ReadDeviceId(AudioEndpoint.IMMDevice device)
    {
        try
        {
            if (device.GetId(out var id) >= 0 && !string.IsNullOrWhiteSpace(id))
                return id;
        }
        catch { }

        return "(unknown)";
    }

    static byte[] CopyWaveFormat(IntPtr ptr)
    {
        var header = Marshal.PtrToStructure<WaveFormatEx>(ptr);
        int bytes = 18 + Math.Max(0, (int)header.Size);
        var blob = new byte[bytes];
        Marshal.Copy(ptr, blob, 0, bytes);
        return blob;
    }

    static IntPtr AllocIeeeFloat(int rate, int channels)
    {
        int block = 4 * channels;
        var fmt = new WaveFormatEx
        {
            FormatTag = WaveFormatKind.EncodingIeeeFloat,
            Channels = (ushort)channels,
            SamplesPerSec = (uint)rate,
            AvgBytesPerSec = (uint)(rate * block),
            BlockAlign = (ushort)block,
            BitsPerSample = 32,
            Size = 0
        };
        var ptr = Marshal.AllocCoTaskMem(18);
        Marshal.StructureToPtr(fmt, ptr, false);
        return ptr;
    }

    static void ReleaseCom(object? obj)
    {
        if (obj is null)
            return;
        try
        {
            if (Marshal.IsComObject(obj))
                Marshal.ReleaseComObject(obj);
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferSize);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint currentPadding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out int frames, out uint flags, out long devicePosition, out long qpcPosition);
        [PreserveSig] int ReleaseBuffer(int framesRead);
        [PreserveSig] int GetNextPacketSize(out int frames);
    }
}
