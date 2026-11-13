using System.Runtime.InteropServices;
using System.Windows.Interop;
using DynamicIsland.Core.Island;
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
    long _openedAt = -1;

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
        if (msg == NativeMethods.WM_CONTEXTMENU || msg == NativeMethods.WM_RBUTTONUP)
        {
            ShowMenu();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != NativeMethods.WM_TRAY)
            return IntPtr.Zero;
        switch (TrayMenuEvents.Decode(lParam.ToInt64()))
        {
            case TrayPointerAction.OpenMenu:
                ShowMenu();
                handled = true;
                break;
            case TrayPointerAction.Primary:
                _window.TrayClick();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        long now = Environment.TickCount64;
        if (!TrayMenuEvents.ShouldOpen(_openedAt < 0 ? -1 : now - _openedAt))
            return;
        _openedAt = now;
        // 还在这次点击的回调里，系统才允许我们变成前台。晚了菜单会闪一下就没。
        NativeMethods.MakeForeground(_source.Handle);

        var previous = _menu;
        _menu = null;
        previous?.Close();

        var menu = new BlackTrayMenu(_window);
        _menu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu))
                _menu = null;
        };
        menu.ShowNear(ResolveAnchor());
        NativeMethods.PostMessage(_source.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
    }

    TrayAnchor ResolveAnchor()
    {
        bool hasIcon = false;
        var icon = default(TrayAnchor);
        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _source.Handle,
            uID = 1
        };
        if (NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect) == 0)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width > 0 && height > 0 && width <= 256 && height <= 256)
            {
                hasIcon = true;
                icon = new TrayAnchor(rect.Left, rect.Top, width, height);
            }
        }

        bool hasCursor = NativeMethods.GetCursorPos(out var point);
        var cursor = hasCursor ? TrayAnchor.Point(point.X, point.Y) : default;
        return TrayMenuPlacement.ChooseAnchor(hasIcon, icon, hasCursor, cursor);
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
