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

        // 前台窗口插到最前。直接全屏的播放器有时被一扇过渡窗压住，枚举顺序会把它漏到后面。
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            var front = Describe(foreground, self);
            if (!front.Ignore)
                list.Insert(0, front);
        }

        return list;
    }

    static TopWindow Describe(IntPtr hwnd, uint self)
    {
        bool visible = NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd) && !IsCloaked(hwnd);
        var className = ClassName(hwnd);
        bool shell = FullscreenPolicy.IsShellOrDesktop(className);
        long extended = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        bool clickThrough = FullscreenPolicy.IsClickThrough(extended);
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        bool own = pid != 0 && pid == self;
        bool ignore = !visible || shell || own || clickThrough;
        bool borderless = false;
        bool maximized = false;
        var rect = new PixelRect(0, 0, 0, 0);
        if (!ignore && NativeMethods.GetWindowRect(hwnd, out var bounds))
        {
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            rect = new PixelRect(bounds.Left, bounds.Top, Math.Max(0, width), Math.Max(0, height));
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            bool caption = (style & NativeMethods.WS_CAPTION) != 0;
            int nonClientW = width;
            int nonClientH = height;
            if (NativeMethods.GetClientRect(hwnd, out var client))
            {
                nonClientW = Math.Max(0, width - Math.Max(0, client.Right - client.Left));
                nonClientH = Math.Max(0, height - Math.Max(0, client.Bottom - client.Top));
            }

            borderless = FullscreenPolicy.IsVisuallyBorderless(caption, nonClientW, nonClientH);
            maximized = (style & NativeMethods.WS_MAXIMIZE) != 0;
        }
        else
        {
            ignore = true;
        }

        return new TopWindow(rect, ignore, borderless, maximized);
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
