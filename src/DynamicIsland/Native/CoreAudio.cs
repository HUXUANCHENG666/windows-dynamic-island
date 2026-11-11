using System.Runtime.InteropServices;

namespace DynamicIsland.Native;

/// <summary>
/// 默认播放设备的主音量回调和峰值表。音量键、系统滑块都会进到这里。
/// 设备被换掉（插上耳机）时重新绑定，否则回调会停在旧设备上。
/// </summary>
public sealed class AudioEndpoint : IDisposable
{
    IMMDeviceEnumerator? _enumerator;
    IAudioEndpointVolume? _volume;
    IAudioMeterInformation? _meter;
    VolumeCallback? _volumeCallback;
    DeviceCallback? _deviceCallback;
    bool _started;

    public event EventHandler<EndpointVolume>? VolumeChanged;
    public event EventHandler? DeviceChanged;

    public void Start()
    {
        if (_started)
            return;
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _deviceCallback = new DeviceCallback(() => DeviceChanged?.Invoke(this, EventArgs.Empty));
        _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
        BindDefault();
        _started = true;
    }

    public void BindDefault()
    {
        if (_enumerator == null)
            return;
        if (_volume != null && _volumeCallback != null)
        {
            try { _volume.UnregisterControlChangeNotify(_volumeCallback); } catch { /* device already gone */ }
        }

        _volume = null;
        _meter = null;
        int hr = _enumerator.GetDefaultAudioEndpoint(0, 0, out var device);
        if (hr != 0 || device == null)
            return;

        var volumeId = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        var meterId = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
        if (device.Activate(ref volumeId, 23, IntPtr.Zero, out var volumeObj) == 0 && volumeObj is IAudioEndpointVolume volume)
        {
            _volume = volume;
            _volumeCallback = new VolumeCallback(data =>
            {
                VolumeChanged?.Invoke(this, new EndpointVolume(data.fMasterVolume, data.bMuted != 0));
            });
            _volume.RegisterControlChangeNotify(_volumeCallback);
        }

        if (device.Activate(ref meterId, 23, IntPtr.Zero, out var meterObj) == 0 && meterObj is IAudioMeterInformation meter)
            _meter = meter;
    }

    public bool TryRead(out EndpointVolume volume)
    {
        volume = default;
        if (_volume == null)
            return false;
        if (_volume.GetMasterVolumeLevelScalar(out float level) != 0)
            return false;
        if (_volume.GetMute(out int muted) != 0)
            return false;
        volume = new EndpointVolume(level, muted != 0);
        return true;
    }

    public float? ReadPeak()
    {
        if (_meter == null)
            return null;
        return _meter.GetPeakValue(out float peak) == 0 ? peak : null;
    }

    public void Dispose()
    {
        try
        {
            if (_volume != null && _volumeCallback != null)
                _volume.UnregisterControlChangeNotify(_volumeCallback);
        }
        catch { /* shutting down */ }

        try
        {
            if (_enumerator != null && _deviceCallback != null)
                _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback);
        }
        catch { /* shutting down */ }
    }

    public readonly record struct EndpointVolume(float Level, bool Muted);

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioVolumeNotificationData
    {
        public Guid guidEventContext;
        public int bMuted;
        public float fMasterVolume;
        public uint nChannels;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int GetChannelCount(out uint channels);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute(int mute, IntPtr eventContext);
        [PreserveSig] int GetMute(out int mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(IntPtr eventContext);
        [PreserveSig] int VolumeStepDown(IntPtr eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint support);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notify);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out uint channels);
        [PreserveSig] int GetChannelsPeakValues(uint count, IntPtr peaks);
        [PreserveSig] int QueryHardwareSupport(out uint support);
    }

    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [ComVisible(true)]
    public sealed class VolumeCallback : IAudioEndpointVolumeCallback
    {
        readonly Action<AudioVolumeNotificationData> _onNotify;
        public VolumeCallback(Action<AudioVolumeNotificationData> onNotify) => _onNotify = onNotify;

        public int OnNotify(IntPtr notify)
        {
            try
            {
                var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notify);
                _onNotify(data);
            }
            catch
            {
                return unchecked((int)0x80004005);
            }

            return 0;
        }
    }

    [ComVisible(true)]
    public sealed class DeviceCallback : IMMNotificationClient
    {
        readonly Action _onDefaultChanged;
        public DeviceCallback(Action onDefaultChanged) => _onDefaultChanged = onDefaultChanged;
        public void OnDeviceStateChanged(string deviceId, int newState) { }
        public void OnDeviceAdded(string deviceId) { }
        public void OnDeviceRemoved(string deviceId) { }

        public void OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId)
        {
            if (flow == 0)
                _onDefaultChanged();
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }
}
