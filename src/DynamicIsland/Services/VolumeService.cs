using System.Windows.Threading;
using DynamicIsland.Core.SystemState;
using DynamicIsland.Native;

namespace DynamicIsland.Services;

sealed class VolumeService : IDisposable
{
    readonly Dispatcher _dispatcher;
    readonly AudioEndpoint _endpoint = new();
    VolumeReading? _last;

    public VolumeService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public event EventHandler<VolumeReading>? Changed;
    public bool Available { get; private set; }

    public void Start()
    {
        _endpoint.VolumeChanged += (_, volume) => _dispatcher.BeginInvoke(() => Deliver(new VolumeReading(volume.Level, volume.Muted)));
        _endpoint.DeviceChanged += (_, _) => _dispatcher.BeginInvoke(Rebind);
        _endpoint.Start();
        Available = true;
        if (_endpoint.TryRead(out var current))
            _last = new VolumeReading(current.Level, current.Muted);
    }

    public float? ReadPeak() => _endpoint.ReadPeak();

    void Rebind()
    {
        try
        {
            _endpoint.BindDefault();
            if (_endpoint.TryRead(out var current))
                Deliver(new VolumeReading(current.Level, current.Muted));
        }
        catch (Exception ex)
        {
            Log.Error("volume-rebind", ex);
        }
    }

    void Deliver(VolumeReading reading)
    {
        if (!VolumeLogic.ShouldAnnounce(_last, reading))
        {
            _last = reading;
            return;
        }

        _last = reading;
        Changed?.Invoke(this, reading);
    }

    public void Dispose() => _endpoint.Dispose();
}
