using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIsland.Core.Formatting;
using DynamicIsland.Core.Ipc;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.SystemState;
using DynamicIsland.Core.Timer;
using DynamicIsland.Native;
using DynamicIsland.Services;
using DynamicIsland.Visuals;
using Microsoft.Win32;

namespace DynamicIsland.Shell;

public partial class IslandWindow : Window
{
    readonly AppSettingsHost _settings;
    readonly IslandDirector _director = new();
    readonly IslandAnimator _animator = new();
    readonly IslandTimer _timer = new();
    readonly MediaService _media;
    readonly VolumeService _volume;
    readonly PowerService _power;
    readonly DynamicIsland.Core.Media.TimelineEstimator _timeline = new();
    TrayController? _tray;
    DispatcherTimer? _slow;
    DispatcherTimer? _topmost;
    bool _timerPinned;
    bool _announceFinish;
    bool _renderLive;
    bool _slowLive;
    bool _trackingLeave;
    bool _servicesStarted;
    bool _inPulse;
    bool _positioning;
    PixelRect _placed;
    TimeSpan _lastRender;
    double _wavePhase;
    DateTime _lastTimerSaveUtc;

    public DynamicIsland.Core.Settings.AppSettings Settings => _settings.Settings;
    public StreamGeometry? HitGeometry { get; set; }
    public StreamGeometry? SlopGeometry { get; set; }

    public IslandWindow(DynamicIsland.Core.Settings.AppSettings settings)
    {
        _settings = new AppSettingsHost(settings);
        InitializeComponent();
        Width = IslandMetrics.WindowWidth;
        Height = IslandMetrics.WindowHeight;
        FontFamily = new FontFamily(IslandFonts.Ui);
        Pill.Fill = Theme.IslandFill;
        Pill.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 18,
            ShadowDepth = 7,
            Direction = 270,
            Opacity = 0.48,
            RenderingBias = RenderingBias.Performance
        };
        PillStroke.Stroke = Theme.Hairline;
        PillStroke.StrokeThickness = 1.6;
        ContentHost.Background = Brushes.Transparent;

        _announceFinish = _timer.Import(settings.Timer, DateTime.UtcNow);
        _timerPinned = settings.Timer.Pinned || _timer.IsRunning || _timer.IsCompleted;
        _director.SetAmbient(AmbientPolicy.Choose(false, false, _timerPinned));
        _animator.PlayIntro();
        _animator.SetTarget(_director.Mode, _director.Expanded);

        _media = new MediaService(Dispatcher);
        _volume = new VolumeService(Dispatcher);
        _power = new PowerService(Dispatcher);
        _media.Changed += (_, _) => OnMedia();
        _volume.Changed += (_, reading) =>
        {
            VolumeNotice.SetVolume(reading);
            _director.PushNotice(IslandMode.Volume, DateTime.UtcNow);
            Nudge();
        };
        _power.Announced += (_, announcement) =>
        {
            BatteryNotice.SetBattery(announcement);
            _director.PushNotice(IslandMode.Battery, DateTime.UtcNow);
            Nudge();
        };

        Wire();
        Opacity = 0;
        Left = SystemParameters.VirtualScreenLeft + Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = SystemParameters.VirtualScreenTop;
    }

    internal void AttachTray(TrayController tray)
    {
        _tray = tray;
        UpdateTooltip();
    }

    public async Task StartServicesAsync()
    {
        _servicesStarted = true;
        try { await _media.StartAsync(); }
        catch (Exception ex) { Log.Error("media", ex); }
        try { _volume.Start(); }
        catch (Exception ex) { Log.Error("volume", ex); }
        try { _power.Start(); }
        catch (Exception ex) { Log.Error("power", ex); }
    }

    public void HandleCommand(ClientCommand command)
    {
        switch (command.Cmd)
        {
            case "toast":
                ShowToast(string.IsNullOrWhiteSpace(command.Title) ? "通知" : command.Title, command.Body);
                break;
            case "timer" when command.Mode == "stopwatch":
                OpenStopwatch();
                break;
            case "timer":
                OpenCountdown(command.Minutes <= 0 ? 25 : command.Minutes);
                break;
            default:
                _director.Click(DateTime.UtcNow);
                Nudge();
                break;
        }
    }

    public void TrayClick()
    {
        _director.TrayClick(DateTime.UtcNow);
        Nudge();
    }

    public void ShowToast(string title, string body)
    {
        ToastNotice.SetToast(title, body);
        _director.PushNotice(IslandMode.Toast, DateTime.UtcNow);
        Nudge();
    }

    public void OpenStopwatch()
    {
        _timer.SetMode(TimerMode.Stopwatch);
        _timer.Start();
        PinTimer(expand: true);
    }

    public void OpenCountdown(int minutes)
    {
        _timer.SetMode(TimerMode.Countdown);
        _timer.Reset();
        _timer.SetCountdownMinutes(minutes);
        _timer.Start();
        PinTimer(expand: true);
    }

    public void SelectMonitor(string? deviceName)
    {
        Settings.MonitorDeviceName = deviceName;
        SettingsStore.Save(Settings);
        Position();
    }

    public void ExitApp()
    {
        SaveTimer();
        _tray?.Dispose();
        _tray = null;
        _media.Dispose();
        _volume.Dispose();
        _power.Dispose();
        Close();
    }

    public bool HitPill(Point windowPoint)
    {
        double x = windowPoint.X - System.Windows.Controls.Canvas.GetLeft(PillHost);
        double y = windowPoint.Y - System.Windows.Controls.Canvas.GetTop(PillHost);
        if (HitGeometry != null && HitGeometry.FillContains(new Point(x, y)))
            return true;
        if (SlopGeometry != null && SlopGeometry.FillContains(new Point(x + IslandMetrics.HitSlop, y + IslandMetrics.HitSlop)))
            return true;
        return false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WndProc);
        ApplyChrome(source.Handle);
        Position();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        _topmost = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _topmost.Tick += (_, _) => ReassertTopmost();
        _topmost.Start();
        _renderLive = true;
        CompositionTarget.Rendering += OnRendering;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!_announceFinish)
            return;
        _announceFinish = false;
        ShowToast("时间到", "你离开的时候，倒计时已经结束");
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Position();
        _animator.Apply(this, newDpi.DpiScaleX);
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveTimer();
        _renderLive = false;
        CompositionTarget.Rendering -= OnRendering;
        _slow?.Stop();
        _topmost?.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        base.OnClosed(e);
        Application.Current?.Shutdown();
    }

    void Wire()
    {
        PreviewMouseLeftButtonDown += (_, _) =>
        {
            _director.Click(DateTime.UtcNow);
            Nudge();
        };
        ExpandedMedia.PlayPauseClicked += (_, _) =>
        {
            _media.OptimisticToggle();
            ExpandedMedia.Apply(_media.Current);
            _ = _media.ToggleAsync();
        };
        ExpandedMedia.NextClicked += (_, _) => _ = _media.NextAsync();
        ExpandedMedia.PreviousClicked += (_, _) => _ = _media.PreviousAsync();
        ExpandedIdle.StopwatchRequested += (_, _) => OpenStopwatch();
        ExpandedIdle.FocusRequested += (_, _) => OpenCountdown(25);
        ExpandedTimer.ToggleRequested += (_, _) =>
        {
            if (_timer.IsCompleted)
                _timer.Reset();
            _timer.Toggle();
            PinTimer(expand: true);
        };
        ExpandedTimer.ResetRequested += (_, _) =>
        {
            _timer.Reset();
            _timerPinned = false;
            _director.SetAmbient(AmbientPolicy.Choose(_media.Current.IsPlaying, _media.Current.IsOpen, false));
            SaveTimer();
            ExpandedTimer.Render(_timer);
            Nudge();
        };
        ExpandedTimer.MinusRequested += (_, _) => AdjustTimer(-1);
        ExpandedTimer.PlusRequested += (_, _) => AdjustTimer(1);
        ExpandedTimer.StopwatchRequested += (_, _) =>
        {
            _timer.SetMode(TimerMode.Stopwatch);
            PinTimer(expand: true);
        };
        ExpandedTimer.CountdownRequested += (_, _) =>
        {
            _timer.SetMode(TimerMode.Countdown);
            PinTimer(expand: true);
        };
    }

    void PinTimer(bool expand)
    {
        _timerPinned = true;
        _director.SetAmbient(AmbientKind.Timer);
        if (expand)
            _director.Click(DateTime.UtcNow);
        SaveTimer();
        ExpandedTimer.Render(_timer);
        CompactTimer.SetText(TimerLabel());
        Nudge();
    }

    void AdjustTimer(double minutes)
    {
        _timer.AddMinutes(minutes);
        _timerPinned = true;
        SaveTimer();
        ExpandedTimer.Render(_timer);
    }

    void OnMedia()
    {
        var now = _media.Current;
        CompactMedia.SetArt(now.Art);
        ExpandedMedia.Apply(now);
        _timeline.Update(
            now.Position,
            now.Start,
            now.End,
            now.IsPlaying,
            now.TimelineUpdatedUtc == default ? DateTime.UtcNow : now.TimelineUpdatedUtc);
        _director.SetAmbient(AmbientPolicy.Choose(now.IsPlaying, now.IsOpen, _timerPinned));
        UpdateTooltip();
        Nudge();
    }

    void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Position);

    void Nudge()
    {
        if (_renderLive || _inPulse)
            return;
        Pulse(1.0 / 60.0);
    }

    void OnRendering(object? sender, EventArgs e)
    {
        if (!_renderLive || e is not RenderingEventArgs args || args.RenderingTime == _lastRender)
            return;
        double dt = _lastRender == TimeSpan.Zero ? 1.0 / 60.0 : (args.RenderingTime - _lastRender).TotalSeconds;
        _lastRender = args.RenderingTime;
        Pulse(dt);
    }

    void OnSlow(object? sender, EventArgs e)
    {
        if (_renderLive)
        {
            CheckPointer();
            return;
        }

        Pulse(0.08);
    }

    void Pulse(double dt)
    {
        if (_inPulse)
            return;
        _inPulse = true;
        try
        {
            PulseCore(dt);
        }
        finally
        {
            _inPulse = false;
        }
    }

    void PulseCore(double dt)
    {
        CheckPointer();
        var now = DateTime.UtcNow;
        _director.Tick(now);
        if (_timer.ConsumeCompletion())
        {
            SaveTimer();
            ShowToast("时间到", "专注结束");
        }

        _animator.SetTarget(_director.Mode, _director.Expanded);
        _animator.Step(dt);
        _animator.Apply(this, CurrentDpi());
        UpdateLive(dt);
        MaybeSaveTimer(now);
        SyncLoops();
    }

    void UpdateLive(double dt)
    {
        if (_director.Mode == IslandMode.Media && _media.Current.IsOpen)
        {
            var duration = _media.Current.End - _media.Current.Start;
            if (duration < TimeSpan.Zero)
                duration = TimeSpan.Zero;
            if (_director.Expanded)
                ExpandedMedia.SetProgress(_timeline.Estimate(DateTime.UtcNow), duration, _timeline.HasDuration);
            _wavePhase += dt;
            CompactMedia.SetWave(_wavePhase, _servicesStarted ? _volume.ReadPeak() ?? 0.62 : 0.62, _media.Current.IsPlaying);
        }

        if (_director.Mode == IslandMode.Timer)
        {
            if (_director.Expanded)
                ExpandedTimer.Render(_timer);
            else
                CompactTimer.SetText(TimerLabel());
        }

        if (_director.Mode == IslandMode.Idle && _director.Expanded)
            ExpandedIdle.SetClock(DateTime.Now);

        if (_director.Mode == IslandMode.Volume)
            VolumeNotice.RefreshBar();
    }

    string TimerLabel()
    {
        if (_timer.IsCompleted)
            return "时间到";
        return _timer.Mode == TimerMode.Countdown
            ? TimeFormat.Countdown(_timer.Remaining)
            : TimeFormat.Stopwatch(_timer.Display);
    }

    void SyncLoops()
    {
        bool waveform = _director.Mode == IslandMode.Media && _media.Current.IsPlaying;
        bool clock = _director.Mode == IslandMode.Idle && _director.Expanded;
        bool animate = !_animator.IsSettled || waveform;
        if (animate && !_renderLive)
        {
            _renderLive = true;
            _lastRender = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }
        else if (!animate && _renderLive)
        {
            _renderLive = false;
            Dispatcher.BeginInvoke(() =>
            {
                if (!_renderLive)
                    CompositionTarget.Rendering -= OnRendering;
            });
        }

        bool slow = _director.HasScheduledWork || _timer.IsRunning || waveform || clock || _director.Mode == IslandMode.Timer;
        if ((slow || animate) && !_slowLive)
        {
            _slowLive = true;
            _slow ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _slow.Tick -= OnSlow;
            _slow.Tick += OnSlow;
            _slow.Start();
        }
        else if (!slow && !animate && _slowLive)
        {
            _slowLive = false;
            _slow?.Stop();
        }
    }

    void CheckPointer()
    {
        if (!_director.PointerInside || !NativeMethods.GetCursorPos(out var point))
            return;
        try
        {
            if (!HitPill(PointFromScreen(new Point(point.X, point.Y))))
                _director.PointerExited(DateTime.UtcNow);
        }
        catch
        {
            // The window can be between DPI changes for a frame.
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                handled = true;
                return (IntPtr)NativeMethods.MA_NOACTIVATE;
            case NativeMethods.WM_NCHITTEST:
                handled = true;
                try
                {
                    var local = PointFromScreen(new Point(NativeMethods.SignedLow(lParam), NativeMethods.SignedHigh(lParam)));
                    return (IntPtr)(HitPill(local) ? NativeMethods.HTCLIENT : NativeMethods.HTTRANSPARENT);
                }
                catch
                {
                    return (IntPtr)NativeMethods.HTCLIENT;
                }
            case NativeMethods.WM_MOUSEMOVE:
                if (!_trackingLeave)
                {
                    var track = new NativeMethods.TRACKMOUSEEVENT
                    {
                        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
                        dwFlags = NativeMethods.TME_LEAVE,
                        hwndTrack = hwnd
                    };
                    NativeMethods.TrackMouseEvent(ref track);
                    _trackingLeave = true;
                }

                _director.PointerEntered(DateTime.UtcNow);
                if (!_renderLive)
                    SyncLoops();
                break;
            case NativeMethods.WM_MOUSELEAVE:
                _trackingLeave = false;
                _director.PointerExited(DateTime.UtcNow);
                if (!_renderLive)
                    SyncLoops();
                break;
        }

        return IntPtr.Zero;
    }

    void Position()
    {
        if (_positioning)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var monitors = MonitorEnumerator.Enumerate();
        MonitorDesc? monitor = null;
        if (!string.IsNullOrWhiteSpace(Settings.MonitorDeviceName))
        {
            monitor = monitors.FirstOrDefault(item =>
                string.Equals(item.DeviceName, Settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
        }

        monitor ??= monitors.FirstOrDefault(item => item.IsPrimary) ?? monitors.FirstOrDefault();
        if (monitor == null)
            return;

        var rect = WindowPlacement.CenterTop(
            monitor.X, monitor.Y, monitor.Width, monitor.Height,
            monitor.DpiX, monitor.DpiY, Width, Height);
        if (rect == _placed)
            return;
        _positioning = true;
        try
        {
            _placed = rect;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, rect.X, rect.Y, rect.Width, rect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            Log.Info($"place {monitor.Label} @ {rect.X},{rect.Y} {rect.Width}x{rect.Height} dpi {monitor.DpiX}");
        }
        finally
        {
            _positioning = false;
        }
    }

    static void ApplyChrome(IntPtr hwnd)
    {
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        style &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    void ReassertTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    double CurrentDpi()
    {
        try { return VisualTreeHelper.GetDpi(this).DpiScaleX; }
        catch { return 1; }
    }

    void UpdateTooltip()
    {
        var now = _media.Current;
        _tray?.SetTooltip(now.IsOpen ? "灵动岛 · " + now.DisplayTitle : "灵动岛");
    }

    void SaveTimer()
    {
        var state = _timer.Export();
        state.Pinned = _timerPinned;
        Settings.Timer = state;
        SettingsStore.Save(Settings);
        _lastTimerSaveUtc = DateTime.UtcNow;
    }

    void MaybeSaveTimer(DateTime now)
    {
        if (!_timer.IsRunning || now - _lastTimerSaveUtc < TimeSpan.FromSeconds(15))
            return;
        SaveTimer();
    }

    sealed class AppSettingsHost
    {
        public AppSettingsHost(DynamicIsland.Core.Settings.AppSettings settings) => Settings = settings;
        public DynamicIsland.Core.Settings.AppSettings Settings { get; }
    }
}
