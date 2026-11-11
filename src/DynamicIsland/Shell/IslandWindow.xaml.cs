using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIsland.Core.Camera;
using DynamicIsland.Core.Formatting;
using DynamicIsland.Core.Ipc;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.SystemState;
using DynamicIsland.Core.Timer;
using DynamicIsland.Core.Translation;
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
    readonly NotificationCenterService _notifications;
    readonly Translator _translator = new();
    readonly TranslateSession _translate = new();
    readonly CameraSession _camera;
    readonly FullscreenWatcher _watcher = new();
    readonly DynamicIsland.Core.Media.TimelineEstimator _timeline = new();
    CancellationTokenSource? _translateCts;
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
    bool _deviceOpen;
    bool _dragArmed;
    bool _dragging;
    bool _eatClick;
    NotificationCard? _toastCard;
    IslandMode _trackedMode = IslandMode.Idle;
    bool _clipboardHooked;
    DockAnchor _anchor = DockAnchor.TopCenter;
    IslandOrientation _orientation = IslandOrientation.Horizontal;
    NativeMethods.POINT _pressCursor;
    int _pressX;
    int _pressY;
    PixelRect _placed;
    TimeSpan _lastRender;
    double _wavePhase;
    DateTime _lastTimerSaveUtc;

    public DynamicIsland.Core.Settings.AppSettings Settings => _settings.Settings;
    public string? ForeignProcess { get; private set; }
    public bool CameraIsOpen => _deviceOpen || _director.CameraOpen || _director.Mode == IslandMode.Camera;
    public StreamGeometry? HitGeometry { get; set; }
    public StreamGeometry? SlopGeometry { get; set; }

    public IslandWindow(DynamicIsland.Core.Settings.AppSettings settings)
    {
        settings.Timer ??= new();
        settings.HideProcesses ??= new();
        settings.Docks ??= new();
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
        if (settings.Stealth)
            _director.SetOccluded(true, DateTime.UtcNow);
        _animator.PlayIntro();
        _animator.SetTarget(_director.Mode, _director.Expanded, _director.ShowAsDot, _orientation);
        TranslateNotice.SetDirection(TranslatePolicy.Parse(settings.TranslateDirection));

        _media = new MediaService(Dispatcher);
        _camera = new CameraSession(Dispatcher);
        _camera.Frame += (pixels, width, height) => CameraNotice.SetFrame(pixels, width, height);
        _volume = new VolumeService(Dispatcher);
        _power = new PowerService(Dispatcher);
        _notifications = new NotificationCenterService(Dispatcher);
        _notifications.Arrived += (_, card) => ShowIncoming(card);
        _notifications.AccessLost += (_, access) => ExplainNotificationAccess(access, openSettings: false);
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
        if (Settings.NotificationCenter)
        {
            try { await EnableNotificationCenterAsync(prompt: false); }
            catch (Exception ex) { Log.Info(NotificationLog.Failed(ex.HResult)); }
        }
        EvaluateHide();
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

    public void ShowToast(string title, string body, TimeSpan? hold = null)
    {
        _toastCard = null;
        ToastNotice.SetToast(title, body);
        _director.PushNotice(IslandMode.Toast, DateTime.UtcNow, hold);
        Nudge();
    }

    public void ToggleNotificationCenter()
    {
        if (Settings.NotificationCenter)
        {
            Settings.NotificationCenter = false;
            SettingsStore.Save(Settings);
            _notifications.Stop();
            ShowToast(NotificationCopy.Title, NotificationCopy.Closed);
            return;
        }

        if (!Settings.NotificationCenterExplained)
        {
            Settings.NotificationCenterExplained = true;
            SettingsStore.Save(Settings);
            ShowToast(NotificationCopy.Title, NotificationCopy.FirstUse, NoticePolicy.GuidanceHold);
        }

        _ = EnableNotificationCenterAsync(prompt: true);
    }

    async Task EnableNotificationCenterAsync(bool prompt)
    {
        NotificationAccess access;
        try
        {
            access = prompt
                ? await _notifications.RequestAccessAsync()
                : _notifications.ReadAccess();
        }
        catch (Exception ex)
        {
            Log.Info(NotificationLog.Failed(ex.HResult));
            access = NotificationAccess.Unavailable;
        }

        Log.Info(NotificationLog.Access(access));
        if (!NotificationAccessPolicy.MayListen(true, access))
        {
            _notifications.Stop();
            if (prompt)
            {
                Settings.NotificationCenter = false;
                SettingsStore.Save(Settings);
            }

            ExplainNotificationAccess(access, openSettings: prompt && access == NotificationAccess.Denied);
            return;
        }

        Settings.NotificationCenter = true;
        SettingsStore.Save(Settings);
        _notifications.Start();
        if (prompt)
            ShowToast(NotificationCopy.Title, NotificationCopy.Allowed, NoticePolicy.GuidanceHold);
    }

    void ExplainNotificationAccess(NotificationAccess access, bool openSettings)
    {
        string body = access switch
        {
            NotificationAccess.Denied => NotificationCopy.Denied,
            NotificationAccess.Unavailable => NotificationCopy.Unavailable,
            NotificationAccess.Allowed => NotificationCopy.Allowed,
            _ => NotificationCopy.Unspecified
        };
        ShowToast(NotificationCopy.Title, body, NoticePolicy.GuidanceHold);
        if (openSettings)
            NotificationActivator.OpenPrivacySettings();
    }

    void ShowIncoming(NotificationCard card)
    {
        _toastCard = card;
        ToastNotice.SetIncoming(card.AppName, card.Title, card.Summary);
        _director.PushNotice(IslandMode.Toast, DateTime.UtcNow);
        Nudge();
    }

    async Task OpenNotificationAsync(NotificationCard card)
    {
        bool opened = false;
        try
        {
            opened = await _notifications.OpenAsync(card);
        }
        catch (Exception ex)
        {
            Log.Info(NotificationLog.Failed(ex.HResult));
        }

        if (!opened)
            ShowToast(NotificationCopy.Title, NotificationCopy.OpenFailed);
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
        ReleaseCamera(collapseUi: false);
        _tray?.Dispose();
        _tray = null;
        _media.Dispose();
        _volume.Dispose();
        _power.Dispose();
        _notifications.Dispose();
        Close();
    }

    public void ToggleStealth()
    {
        Settings.Stealth = !Settings.Stealth;
        SettingsStore.Save(Settings);
        EvaluateHide();
    }

    public void ToggleListedProcess(string? process)
    {
        if (string.IsNullOrWhiteSpace(process))
            return;
        Settings.HideProcesses ??= new();
        HidePolicy.ToggleListed(Settings.HideProcesses, process);
        SettingsStore.Save(Settings);
        EvaluateHide();
    }

    public void TranslateClipboard()
    {
        string text;
        try
        {
            text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
        }
        catch (Exception ex)
        {
            Log.Error("clipboard", ex);
            ShowToast("翻译", "现在读不到剪贴板");
            return;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > TranslatePolicy.MaxInviteChars || !TranslatePolicy.ContainsLanguage(trimmed))
        {
            ShowToast("翻译", "剪贴板里没有可翻译的文字");
            return;
        }

        _translate.SetPending(trimmed);
        TranslateNotice.ShowInvite(trimmed);
        _director.PushNotice(IslandMode.Translate, DateTime.UtcNow);
        Nudge();
        ConfirmTranslation();
    }

    public void ToggleCamera()
    {
        if (_deviceOpen || _director.CameraOpen || _director.Mode == IslandMode.Camera)
        {
            ReleaseCamera(collapseUi: true);
            Nudge();
            return;
        }

        if (!CameraPolicy.MayStart(userRequested: true, occluded: _director.Occluded))
        {
            ShowToast("摄像头", "先退出全屏或关闭隐身，再打开摄像头");
            return;
        }

        _ = StartCameraAsync();
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
        _watcher.Start(source.Handle);
        _clipboardHooked = NativeMethods.AddClipboardFormatListener(source.Handle);
        EvaluateHide();
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
        bool transitioning = !_animator.IsSettled;
        _animator.Apply(this, newDpi.DpiScaleX, _anchor, EffectsPolicy.AllowBlur(_director.Expanded && !_director.ShowAsDot, transitioning));
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveTimer();
        _renderLive = false;
        CompositionTarget.Rendering -= OnRendering;
        _slow?.Stop();
        _topmost?.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _translateCts?.Cancel();
        _notifications.Dispose();
        ReleaseCamera(collapseUi: false);
        _watcher.Dispose();
        _translator.Dispose();
        if (_clipboardHooked)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                NativeMethods.RemoveClipboardFormatListener(hwnd);
            _clipboardHooked = false;
        }
        base.OnClosed(e);
        Application.Current?.Shutdown();
    }

    void Wire()
    {
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging && !_eatClick)
                return;
            _eatClick = false;
            e.Handled = true;
        };
        TranslateNotice.ConfirmClicked += (_, _) => ConfirmTranslation();
        TranslateNotice.DirectionClicked += (_, direction) =>
        {
            Settings.TranslateDirection = TranslatePolicy.ToSetting(direction);
            SettingsStore.Save(Settings);
            TranslateNotice.SetDirection(direction);
            if (_translate.SendAuthorized)
                ConfirmTranslation();
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
        var cameraReason = CameraPolicy.Classify(
            _director.CameraSession,
            _director.Expanded,
            _director.Occluded,
            _dragging || _dragArmed);
        if (_deviceOpen && CameraPolicy.ShouldRelease(cameraReason))
            ReleaseCamera(collapseUi: false);
        if (_timer.ConsumeCompletion())
        {
            SaveTimer();
            ShowToast("时间到", "专注结束");
        }

        if (_trackedMode == IslandMode.Toast && _director.Mode != IslandMode.Toast)
            _toastCard = null;
        _trackedMode = _director.Mode;

        bool transitioning = !_animator.IsSettled;
        bool allowBlur = EffectsPolicy.AllowBlur(_director.Expanded && !_director.ShowAsDot, transitioning);
        _animator.SetTarget(_director.Mode, _director.Expanded, _director.ShowAsDot, _orientation);
        _animator.Step(dt);
        _animator.Apply(this, CurrentDpi(), _anchor, allowBlur);
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
        bool transitioning = !_animator.IsSettled;
        bool expanded = _director.Expanded && !_director.ShowAsDot;
        bool animate = EffectsPolicy.UseHighFrameRate(expanded, transitioning, waveform);
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
        if (_dragging || _dragArmed)
            return;
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
            case NativeMethods.WM_APP_CHECKHIDE:
                _watcher.MarkDelivered();
                EvaluateHide();
                handled = true;
                break;
            case NativeMethods.WM_CLIPBOARDUPDATE:
                OnClipboardInvite();
                handled = true;
                break;
            case NativeMethods.WM_LBUTTONDOWN:
                ArmDrag();
                break;
            case NativeMethods.WM_LBUTTONUP:
                FinishDrag();
                break;
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
                MoveWhileDragging();
                if (!_renderLive)
                    SyncLoops();
                break;
            case NativeMethods.WM_MOUSELEAVE:
                _trackingLeave = false;
                if (_dragging || _dragArmed)
                    break;
                _director.PointerExited(DateTime.UtcNow);
                if (!_renderLive)
                    SyncLoops();
                break;
        }

        return IntPtr.Zero;
    }

    void Position()
    {
        if (_positioning || _dragging)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var monitors = MonitorEnumerator.Enumerate();
        var monitor = ResolveMonitor(monitors);
        if (monitor == null)
            return;

        Settings.Docks ??= new();
        _anchor = DockStore.Recall(Settings.Docks, monitor.DeviceName);
        var orientation = Dock.OrientationOf(_anchor);
        var host = IslandMetrics.HostDip(orientation);
        Width = host.Width;
        Height = host.Height;
        if (orientation != _orientation)
        {
            _orientation = orientation;
            ApplyOrientation(orientation == IslandOrientation.Vertical);
        }

        var monitorRect = new PixelRect(monitor.X, monitor.Y, monitor.Width, monitor.Height);
        var reported = new PixelRect(monitor.WorkX, monitor.WorkY, monitor.WorkWidth, monitor.WorkHeight);
        var work = WorkAreaMath.ExcludeBars(monitorRect, reported, TaskbarBounds.Query());
        var pixels = WindowPlacement.ToPixels(Width, Height, monitor.DpiX, monitor.DpiY);
        var rect = SnapMath.Place(_anchor, work, pixels.Width, pixels.Height, monitorRect);
        if (rect == _placed)
            return;
        _positioning = true;
        try
        {
            _placed = rect;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, rect.X, rect.Y, rect.Width, rect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            Log.Info($"place {monitor.Label} {_anchor} @ {rect.X},{rect.Y} {rect.Width}x{rect.Height} dpi {monitor.DpiX}");
        }
        finally
        {
            _positioning = false;
        }
    }

    static MonitorDesc? ResolveMonitor(IReadOnlyList<MonitorDesc> monitors, string? deviceName)
    {
        MonitorDesc? monitor = null;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            monitor = monitors.FirstOrDefault(item =>
                string.Equals(item.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        }

        return monitor ?? monitors.FirstOrDefault(item => item.IsPrimary) ?? monitors.FirstOrDefault();
    }

    MonitorDesc? ResolveMonitor(IReadOnlyList<MonitorDesc> monitors) =>
        ResolveMonitor(monitors, Settings.MonitorDeviceName);

    void ApplyOrientation(bool vertical)
    {
        CompactMedia.SetVertical(vertical);
        CompactTimer.SetVertical(vertical);
        ExpandedIdle.SetVertical(vertical);
        ExpandedMedia.SetVertical(vertical);
        ExpandedTimer.SetVertical(vertical);
        VolumeNotice.SetVertical(vertical);
        BatteryNotice.SetVertical(vertical);
        ToastNotice.SetVertical(vertical);
        TranslateNotice.SetVertical(vertical);
        CameraNotice.SetVertical(vertical);
    }

    void EvaluateHide()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var monitor = ResolveMonitor(MonitorEnumerator.Enumerate());
        if (monitor == null)
            return;
        var sample = ForegroundProbe.Read(hwnd, new PixelRect(monitor.X, monitor.Y, monitor.Width, monitor.Height));
        if (!string.IsNullOrWhiteSpace(sample.ProcessName))
            ForeignProcess = sample.ProcessName;
        Settings.HideProcesses ??= new();
        bool hide = HidePolicy.IsOccluded(
            new HideSignals(Settings.Stealth, sample.HasForeignWindow, sample.OnIslandMonitor, sample.CoversIslandMonitor, sample.ProcessName),
            Settings.HideProcesses);
        if (hide && (_deviceOpen || _director.CameraOpen || _director.Mode == IslandMode.Camera))
            ReleaseCamera(collapseUi: true);
        _director.SetOccluded(hide, DateTime.UtcNow);
        Nudge();
    }

    void OnClipboardInvite()
    {
        string text;
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return;
            text = System.Windows.Clipboard.GetText();
        }
        catch
        {
            return;
        }

        if (!_translate.OfferClipboard(text))
            return;
        TranslateNotice.ShowInvite(_translate.PendingText ?? text);
        _director.PushNotice(IslandMode.Translate, DateTime.UtcNow);
        Nudge();
    }

    void ConfirmTranslation()
    {
        var request = _translate.Confirm(TranslatePolicy.Parse(Settings.TranslateDirection));
        if (request is null)
        {
            TranslateNotice.ShowError("没有可翻译的文字");
            _director.PushNotice(IslandMode.Translate, DateTime.UtcNow);
            Nudge();
            return;
        }

        _ = RunTranslationAsync(request.Value);
    }

    async Task RunTranslationAsync(TranslateRequest request)
    {
        _translateCts?.Cancel();
        _translateCts = new CancellationTokenSource();
        var token = _translateCts.Token;
        TranslateNotice.ShowBusy(request.Clipped);
        _director.PushNotice(IslandMode.Translate, DateTime.UtcNow);
        Nudge();
        try
        {
            Log.Info($"translate send {request.Text.Length} {request.Source}->{request.Target}");
            var result = await _translator.TranslateAsync(request, token);
            if (token.IsCancellationRequested)
                return;
            TranslateNotice.ShowResult(result);
            _director.PushNotice(IslandMode.Translate, DateTime.UtcNow);
            Nudge();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Info("translate failed " + ex.GetType().Name);
            if (!token.IsCancellationRequested)
            {
                TranslateNotice.ShowError(ex is InvalidOperationException ? ex.Message : "翻译没有完成。");
                _director.PushNotice(IslandMode.Translate, DateTime.UtcNow);
                Nudge();
            }
        }
    }

    async Task StartCameraAsync()
    {
        ReleaseCamera(collapseUi: false);
        _director.SetCamera(true);
        CameraNotice.ShowWaiting();
        Nudge();
        var error = await _camera.StartAsync();
        if (!_director.CameraOpen)
        {
            ReleaseCamera(collapseUi: false);
            return;
        }

        if (!string.IsNullOrEmpty(error) || !_camera.IsOpen)
        {
            _deviceOpen = false;
            CameraNotice.ShowError(string.IsNullOrEmpty(error) ? "打不开摄像头。" : error);
            _director.ShowCameraMessage(DateTime.UtcNow);
            Nudge();
            return;
        }

        _deviceOpen = true;
        CameraNotice.SetLive(true);
        Nudge();
    }

    void ReleaseCamera(bool collapseUi)
    {
        _deviceOpen = false;
        if (_camera.IsOpen)
            _camera.Release();
        CameraNotice.SetLive(false);
        if (collapseUi && (_director.CameraOpen || _director.Mode == IslandMode.Camera))
            _director.SetCamera(false);
    }

    void ArmDrag()
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return;
        Point local;
        try { local = PointFromScreen(new Point(point.X, point.Y)); }
        catch { return; }
        if (!HitPill(local))
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
            return;
        _dragArmed = true;
        _dragging = false;
        _pressCursor = point;
        _pressX = rect.Left;
        _pressY = rect.Top;
    }

    void MoveWhileDragging()
    {
        if (!_dragArmed || !NativeMethods.GetCursorPos(out var point))
            return;
        int dx = point.X - _pressCursor.X;
        int dy = point.Y - _pressCursor.Y;
        if (!_dragging && dx * dx + dy * dy < 36)
            return;
        _dragging = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _pressX + dx, _pressY + dy, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    void FinishDrag()
    {
        bool dragged = _dragging;
        _dragArmed = false;
        _dragging = false;
        if (!dragged)
        {
            if (NativeMethods.GetCursorPos(out var point))
            {
                try
                {
                    if (HitPill(PointFromScreen(new Point(point.X, point.Y))))
                    {
                        if (_director.Mode == IslandMode.Toast && _toastCard is { } card
                            && card.Launch.Kind != NotificationLaunchKind.None)
                        {
                            _ = OpenNotificationAsync(card);
                        }
                        else
                        {
                            _director.Click(DateTime.UtcNow);
                            Nudge();
                        }
                    }
                }
                catch
                {
                    // Ignore a hit-test during a DPI change.
                }
            }

            return;
        }

        _eatClick = true;
        SnapFromCursor();
    }

    void SnapFromCursor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect) || !NativeMethods.GetCursorPos(out var point))
            return;
        double dpi = Math.Max(0.5, CurrentDpi());
        double pillLeft = System.Windows.Controls.Canvas.GetLeft(PillHost);
        double pillTop = System.Windows.Controls.Canvas.GetTop(PillHost);
        if (double.IsNaN(pillLeft))
            pillLeft = 0;
        if (double.IsNaN(pillTop))
            pillTop = 0;
        int cx = rect.Left + (int)Math.Round((pillLeft + PillHost.ActualWidth / 2.0) * dpi);
        int cy = rect.Top + (int)Math.Round((pillTop + PillHost.ActualHeight / 2.0) * dpi);
        if (PillHost.ActualWidth < 1)
        {
            cx = point.X;
            cy = point.Y;
        }

        var monitors = MonitorEnumerator.Enumerate();
        var monitor = monitors.FirstOrDefault(item => item.X <= cx && cx < item.X + item.Width && item.Y <= cy && cy < item.Y + item.Height)
            ?? monitors.FirstOrDefault(item => item.IsPrimary)
            ?? monitors.FirstOrDefault();
        if (monitor == null)
            return;
        var monitorRect = new PixelRect(monitor.X, monitor.Y, monitor.Width, monitor.Height);
        var reported = new PixelRect(monitor.WorkX, monitor.WorkY, monitor.WorkWidth, monitor.WorkHeight);
        var work = WorkAreaMath.ExcludeBars(monitorRect, reported, TaskbarBounds.Query());
        int inset = (int)Math.Round(56 * (monitor.DpiX / 96.0));
        var anchor = SnapMath.Nearest(cx, cy, work, inset);
        Settings.Docks ??= new();
        DockStore.Remember(Settings.Docks, monitor.DeviceName, anchor);
        Settings.MonitorDeviceName = monitor.IsPrimary ? null : monitor.DeviceName;
        SettingsStore.Save(Settings);
        _placed = default;
        _dragging = false;
        Position();
        EvaluateHide();
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
