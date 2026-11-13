using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Native;
using DynamicIsland.Services;
using DynamicIsland.Visuals;

namespace DynamicIsland.Shell;

/// <summary>
/// 托盘右键。系统菜单染不成岛的黑色，这里用一块同样的黑胶囊代替。
/// 只放每天会碰到的几项，其余进设置窗口。
/// </summary>
sealed class BlackTrayMenu : Window
{
    readonly FontFamily _font = new(IslandFonts.Ui);
    bool _ready;

    public BlackTrayMenu(IslandWindow island)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = _font;

        var card = new Border
        {
            Background = Theme.IslandFill,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(8),
            MinWidth = 196,
            BorderBrush = Theme.Hairline,
            BorderThickness = new Thickness(1)
        };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 22,
            ShadowDepth = 8,
            Direction = 270,
            Opacity = 0.5
        };
        var stack = new StackPanel();
        card.Child = stack;
        Content = card;

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

        Deactivated += (_, _) =>
        {
            if (_ready)
                Close();
        };
        SourceInitialized += (_, _) =>
        {
            var source = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this)!;
            long style = NativeMethods.GetWindowLongPtr(source.Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            style |= NativeMethods.WS_EX_TOOLWINDOW;
            style &= ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLongPtr(source.Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        };
    }

    public void ShowAtCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            Show();
            return;
        }

        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = DesiredSize.Width;
        double height = DesiredSize.Height;
        var monitors = MonitorEnumerator.Enumerate();
        var monitor = monitors.FirstOrDefault(item =>
                item.X <= point.X && point.X < item.X + item.Width && item.Y <= point.Y && point.Y < item.Y + item.Height)
            ?? monitors.FirstOrDefault(item => item.IsPrimary)
            ?? monitors.FirstOrDefault();
        double left = point.X - width;
        double top = point.Y - height - 6;
        if (monitor != null)
        {
            left = Math.Clamp(left, monitor.WorkX, Math.Max(monitor.WorkX, monitor.WorkX + monitor.WorkWidth - width));
            if (top < monitor.WorkY)
                top = point.Y + 6;
            top = Math.Clamp(top, monitor.WorkY, Math.Max(monitor.WorkY, monitor.WorkY + monitor.WorkHeight - height));
        }

        Left = left;
        Top = top;
        Show();
        Activate();
        Dispatcher.BeginInvoke(() => _ready = true);
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
            Close();
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
