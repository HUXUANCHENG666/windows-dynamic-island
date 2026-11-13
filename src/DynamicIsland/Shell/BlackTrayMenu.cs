using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core.Island;
using DynamicIsland.Native;
using DynamicIsland.Services;
using DynamicIsland.Visuals;

namespace DynamicIsland.Shell;

/// <summary>
/// 托盘右键。系统菜单染不成岛的黑色，这里用一块同样的黑胶囊代替。
/// 只放每天会碰到的几项，其余进设置窗口。
/// 位置用物理像素 SetWindowPos。Left/Top 是 DIP，直接写光标坐标会在缩放屏上落到屏幕外。
/// </summary>
sealed class BlackTrayMenu : Window
{
    readonly FontFamily _font = new(IslandFonts.Ui);
    readonly Border _card;
    TrayAnchor _anchor;
    double _dipW = TrayMenuPlacement.FallbackDipWidth;
    double _dipH = TrayMenuPlacement.FallbackDipHeight;
    long _shownAt;
    bool _armed;
    bool _closing;
    bool _captureReady;
    bool _logged;
    DispatcherTimer? _arm;

    public BlackTrayMenu(IslandWindow island)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = _font;

        _card = new Border
        {
            Background = Theme.IslandFill,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(8),
            MinWidth = 196,
            BorderBrush = Theme.Hairline,
            BorderThickness = new Thickness(1)
        };
        _card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 22,
            ShadowDepth = 8,
            Direction = 270,
            Opacity = 0.5
        };
        var stack = new StackPanel();
        _card.Child = stack;
        Content = _card;

        AddCheck(stack, "隐身", island.Settings.Stealth, () => island.ToggleStealth());
        AddCheck(stack, "开机时启动", StartupRegistration.IsEnabled(), () =>
        {
            try { StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled()); }
            catch (Exception ex) { Log.Error("startup", ex); }
        });

        var monitors = MonitorEnumerator.Enumerate();
        if (monitors.Count > 0)
        {
            stack.Children.Add(Rule());
            foreach (var mon in monitors)
            {
                bool selected = island.Settings.MonitorDeviceName == null
                    ? mon.IsPrimary
                    : string.Equals(mon.DeviceName, island.Settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase);
                string? device = mon.IsPrimary ? null : mon.DeviceName;
                AddCheck(stack, mon.Label, selected, () => island.SelectMonitor(device));
            }
        }

        stack.Children.Add(Rule());
        AddCommand(stack, "设置", () => island.OpenSettings());
        AddCommand(stack, "退出", () => island.ExitApp());

        Deactivated += (_, _) => Dismiss(TrayDismiss.Deactivated);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Dismiss(TrayDismiss.Escape);
        };
    }

    public void ShowNear(TrayAnchor anchor)
    {
        _anchor = anchor;
        _shownAt = Environment.TickCount64;
        (_dipW, _dipH) = MeasureCard();
        Width = _dipW;
        Height = _dipH;
        Opacity = 0;
        try
        {
            Show();
            ApplyPlacement();
        }
        finally
        {
            Opacity = 1;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeForeground(hwnd);
        Activate();
        Focus();
        TryCapture();
        _arm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _arm.Tick += (_, _) => OnArmTick();
        _arm.Start();
        Dispatcher.BeginInvoke(ApplyPlacement, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW;
        style &= ~NativeMethods.WS_EX_APPWINDOW;
        style &= ~NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        ApplyPlacement();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_card.ActualWidth < 1 || _card.ActualHeight < 1)
            return;
        if (Math.Abs(_card.ActualWidth - _dipW) < 1 && Math.Abs(_card.ActualHeight - _dipH) < 1)
            return;
        _dipW = _card.ActualWidth;
        _dipH = _card.ActualHeight;
        Width = _dipW;
        Height = _dipH;
        ApplyPlacement();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyPlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        _arm?.Stop();
        ReleaseCapture();
        base.OnClosed(e);
    }

    void OnArmTick()
    {
        if (_closing)
        {
            _arm?.Stop();
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        TryCapture();
        // 隐藏图标的浮层关掉时会把前台抢走。这里过一小会儿再拿回来，
        // 等这次抢走结束，才开始把“失焦”当成用户点到了外面。
        long age = Environment.TickCount64 - _shownAt;
        if (age >= 200 && age < 260)
            NativeMethods.MakeForeground(hwnd);
        if (age < TrayMenuDismiss.DeactivateGraceMs)
            return;
        NativeMethods.MakeForeground(hwnd);
        Activate();
        TryCapture();
        _arm?.Stop();
        Dispatcher.BeginInvoke(() =>
        {
            if (!_closing)
                _armed = true;
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    void ApplyPlacement()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _closing)
            return;
        var screens = new List<TrayScreen>();
        foreach (var monitor in MonitorEnumerator.Enumerate())
        {
            screens.Add(new TrayScreen(
                monitor.X, monitor.Y, monitor.Width, monitor.Height,
                monitor.WorkX, monitor.WorkY, monitor.WorkWidth, monitor.WorkHeight,
                monitor.DpiX, monitor.DpiY, monitor.IsPrimary));
        }

        var rect = TrayMenuPlacement.Place(_anchor, _dipW, _dipH, screens);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, rect.X, rect.Y, rect.Width, rect.Height,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
        if (_logged)
            return;
        _logged = true;
        Log.Info($"tray menu @ {rect.X},{rect.Y} {rect.Width}x{rect.Height}");
    }

    (double Width, double Height) MeasureCard()
    {
        _card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = _card.DesiredSize.Width;
        double height = _card.DesiredSize.Height;
        if (width < 1)
            width = TrayMenuPlacement.FallbackDipWidth;
        if (height < 1)
            height = TrayMenuPlacement.FallbackDipHeight;
        return (width, height);
    }

    void TryCapture()
    {
        if (_captureReady || _closing)
            return;
        if (!Mouse.Capture(_card, CaptureMode.SubTree))
            return;
        _captureReady = true;
        Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(_card, OnOutside);
    }

    void OnOutside(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
            return;
        e.Handled = true;
        Dismiss(TrayDismiss.Outside);
    }

    void ReleaseCapture()
    {
        if (_captureReady)
        {
            Mouse.RemovePreviewMouseDownOutsideCapturedElementHandler(_card, OnOutside);
            _captureReady = false;
        }

        if (Mouse.Captured != null)
            Mouse.Capture(null);
    }

    void Dismiss(TrayDismiss reason)
    {
        if (_closing)
            return;
        long age = Environment.TickCount64 - _shownAt;
        if (!TrayMenuDismiss.ShouldDismiss(_armed, reason, age))
            return;
        _closing = true;
        _arm?.Stop();
        Close();
    }

    void AddCheck(StackPanel stack, string label, bool on, Action action)
    {
        AddRow(stack, (on ? "✓  " : "    ") + label, action);
    }

    void AddCommand(StackPanel stack, string label, Action action) => AddRow(stack, "    " + label, action);

    void AddRow(StackPanel stack, string label, Action action)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 8, 12, 8),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = label,
                Foreground = Theme.Title,
                FontFamily = _font,
                FontSize = 13
            }
        };
        row.MouseEnter += (_, _) => row.Background = Theme.Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Dismiss(TrayDismiss.ItemChosen);
            action();
        };
        stack.Children.Add(row);
    }

    static Border Rule() => new()
    {
        Height = 1,
        Background = Theme.Hairline,
        Margin = new Thickness(8, 4, 8, 4)
    };
}
