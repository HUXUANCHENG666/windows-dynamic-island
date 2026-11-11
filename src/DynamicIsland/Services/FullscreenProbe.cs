using System.Text;
using DynamicIsland.Core.Island;
using DynamicIsland.Native;

namespace DynamicIsland.Services;

/// <summary>
/// 从上往下看可见窗口，跳过桌面、Shell 和本进程。
/// 再问一次系统是不是正处在全屏、独占 D3D 或放映。判断本身在 FullscreenPolicy 里。
/// </summary>
static class FullscreenProbe
{
    public static bool Covers(PixelRect monitor)
    {
        try
        {
            return FullscreenPolicy.ShouldHide(monitor, Snapshot(), Attention());
        }
        catch
        {
            return false;
        }
    }

    static ShellAttention Attention()
    {
        if (NativeMethods.SHQueryUserNotificationState(out var state) != 0)
            return ShellAttention.None;
        return state switch
        {
            NativeMethods.QueryUserNotificationState.Busy => ShellAttention.Busy,
            NativeMethods.QueryUserNotificationState.RunningD3DFullScreen => ShellAttention.Direct3D,
            NativeMethods.QueryUserNotificationState.PresentationMode => ShellAttention.Presentation,
            _ => ShellAttention.None
        };
    }

    static List<TopWindow> Snapshot()
    {
        uint self = (uint)Environment.ProcessId;
        var list = new List<TopWindow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            list.Add(Describe(hwnd, self));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static TopWindow Describe(IntPtr hwnd, uint self)
    {
        bool visible = NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd) && !IsCloaked(hwnd);
        var className = ClassName(hwnd);
        bool shell = FullscreenPolicy.IsShellOrDesktop(className);
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        bool own = pid != 0 && pid == self;
        bool ignore = !visible || shell || own;
        bool borderless = false;
        var rect = new PixelRect(0, 0, 0, 0);
        if (!ignore && NativeMethods.GetWindowRect(hwnd, out var bounds))
        {
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            rect = new PixelRect(bounds.Left, bounds.Top, Math.Max(0, width), Math.Max(0, height));
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            borderless = (style & NativeMethods.WS_CAPTION) == 0;
        }
        else
        {
            ignore = true;
        }

        return new TopWindow(rect, ignore, borderless);
    }

    static bool IsCloaked(IntPtr hwnd)
    {
        int cloaked = 0;
        int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        int length = NativeMethods.GetClassName(hwnd, name, name.Capacity);
        return length > 0 ? name.ToString() : "";
    }
}
