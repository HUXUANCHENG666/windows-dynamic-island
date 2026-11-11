using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DynamicIsland.Native;
using DynamicIsland.Shell;

namespace DynamicIsland.Services;

sealed class TrayController : IDisposable
{
    const int IdExit = 1;
    const int IdStartup = 2;
    const int IdStopwatch = 3;
    const int IdFocus = 4;
    const int IdToast = 5;
    const int IdLog = 6;
    const int IdMonitorBase = 100;

    readonly IslandWindow _window;
    readonly HwndSource _source;
    readonly List<MonitorDesc> _monitors = new();
    IntPtr _icon;
    bool _added;

    public TrayController(IslandWindow window)
    {
        _window = window;
        var parameters = new HwndSourceParameters("DynamicIsland.Tray")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _icon = LoadIcon();
        AddIcon("灵动岛");
    }

    public void SetTooltip(string text)
    {
        if (!_added)
            return;
        if (text.Length > 127)
            text = text[..127];
        var data = BaseData();
        data.uFlags = (int)NativeMethods.NIF_TIP;
        data.szTip = text;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_TRAY)
            return IntPtr.Zero;
        int mouse = lParam.ToInt32();
        if (mouse is NativeMethods.WM_RBUTTONUP or NativeMethods.WM_CONTEXTMENU)
        {
            ShowMenu();
            handled = true;
        }
        else if (mouse == NativeMethods.WM_LBUTTONUP)
        {
            _window.TrayClick();
            handled = true;
        }

        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        _monitors.Clear();
        _monitors.AddRange(MonitorEnumerator.Enumerate());
        IntPtr menu = NativeMethods.CreatePopupMenu();
        IntPtr monitors = NativeMethods.CreatePopupMenu();
        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | NativeMethods.MF_GRAYED, IntPtr.Zero, "灵动岛");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);

            for (int i = 0; i < _monitors.Count; i++)
            {
                var mon = _monitors[i];
                bool selected = _window.Settings.MonitorDeviceName == null
                    ? mon.IsPrimary
                    : string.Equals(mon.DeviceName, _window.Settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase);
                uint flags = NativeMethods.MF_STRING | (selected ? NativeMethods.MF_CHECKED : 0);
                NativeMethods.AppendMenu(monitors, flags, (IntPtr)(IdMonitorBase + i), mon.Label);
            }

            if (_monitors.Count > 0)
                NativeMethods.AppendMenu(menu, NativeMethods.MF_POPUP, monitors, "显示器");

            uint startup = NativeMethods.MF_STRING | (StartupRegistration.IsEnabled() ? NativeMethods.MF_CHECKED : 0);
            NativeMethods.AppendMenu(menu, startup, (IntPtr)IdStartup, "开机时启动");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (IntPtr)IdStopwatch, "打开秒表");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (IntPtr)IdFocus, "开始 25 分钟专注");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (IntPtr)IdToast, "发送一条测试通知");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (IntPtr)IdLog, "打开日志");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (IntPtr)IdExit, "退出");

            NativeMethods.GetCursorPos(out var point);
            NativeMethods.SetForegroundWindow(_source.Handle);
            int command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TPM_RIGHTALIGN | NativeMethods.TPM_BOTTOMALIGN | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                point.X,
                point.Y,
                0,
                _source.Handle,
                IntPtr.Zero);
            NativeMethods.PostMessage(_source.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            Dispatch(command);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    void Dispatch(int command)
    {
        switch (command)
        {
            case IdExit:
                _window.ExitApp();
                break;
            case IdStartup:
                try
                {
                    StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
                }
                catch (Exception ex)
                {
                    Log.Error("startup", ex);
                }
                break;
            case IdStopwatch:
                _window.OpenStopwatch();
                break;
            case IdFocus:
                _window.OpenCountdown(25);
                break;
            case IdToast:
                _window.ShowToast("灵动岛", "这是一条测试通知");
                break;
            case IdLog:
                OpenLog();
                break;
            default:
                if (command >= IdMonitorBase && command - IdMonitorBase < _monitors.Count)
                {
                    var mon = _monitors[command - IdMonitorBase];
                    _window.SelectMonitor(mon.IsPrimary ? null : mon.DeviceName);
                }
                break;
        }
    }

    static void OpenLog()
    {
        try
        {
            Log.Info("log opened from tray");
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("log-open", ex);
        }
    }

    void AddIcon(string tip)
    {
        var data = BaseData();
        data.uFlags = (int)(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_TIP | (_icon != IntPtr.Zero ? NativeMethods.NIF_ICON : 0));
        data.uCallbackMessage = NativeMethods.WM_TRAY;
        data.hIcon = _icon;
        data.szTip = tip;
        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
    }

    NativeMethods.NOTIFYICONDATA BaseData() => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _source.Handle,
        uID = 1
    };

    static IntPtr LoadIcon()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            return IntPtr.Zero;
        NativeMethods.ExtractIconEx(path, 0, out var large, out var small, 1);
        if (small != IntPtr.Zero && large != IntPtr.Zero)
            NativeMethods.DestroyIcon(large);
        return small != IntPtr.Zero ? small : large;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = BaseData();
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        _source.Dispose();
    }
}
