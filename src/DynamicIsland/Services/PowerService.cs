using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Core.SystemState;
using Microsoft.Win32;

namespace DynamicIsland.Services;

sealed class PowerService : IDisposable
{
    readonly Dispatcher _dispatcher;
    PowerReading? _last;
    DispatcherTimer? _timer;
    bool _alive = true;

    public PowerService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public event EventHandler<PowerAnnouncement>? Announced;
    public bool HasBattery { get; private set; }

    public void Start()
    {
        Sample(announce: false);
        SystemEvents.PowerModeChanged += OnPowerMode;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Sample(announce: true);
        _timer.Start();
    }

    void OnPowerMode(object? sender, PowerModeChangedEventArgs e)
    {
        if (!_alive)
            return;
        _dispatcher.BeginInvoke(() => Sample(announce: true));
    }

    void Sample(bool announce)
    {
        if (!GetSystemPowerStatus(out var status))
            return;
        var reading = PowerLogic.Parse(status.ACLineStatus, status.BatteryFlag, status.BatteryLifePercent);
        HasBattery = reading.HasBattery;
        var announcement = announce ? PowerLogic.Diff(_last, reading) : null;
        _last = reading;
        if (announcement is PowerAnnouncement news)
            Announced?.Invoke(this, news);
    }

    public void Dispose()
    {
        _alive = false;
        _timer?.Stop();
        SystemEvents.PowerModeChanged -= OnPowerMode;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
