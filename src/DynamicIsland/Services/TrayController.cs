using System.Runtime.InteropServices;
using System.Windows.Interop;
using DynamicIsland.Native;
using DynamicIsland.Shell;

namespace DynamicIsland.Services;

sealed class TrayController : IDisposable
{
    readonly IslandWindow _window;
    readonly HwndSource _source;
    BlackTrayMenu? _menu;
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
        if (_menu != null)
        {
            _menu.Close();
            _menu = null;
        }

        _menu = new BlackTrayMenu(_window);
        _menu.Closed += (_, _) => _menu = null;
        _menu.ShowAtCursor();
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
        _menu?.Close();
        _menu = null;
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
