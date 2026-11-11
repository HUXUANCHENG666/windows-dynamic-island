using System.Text;
using DynamicIsland.Core.Island;
using DynamicIsland.Native;

namespace DynamicIsland.Services;

readonly record struct ForegroundSample(
    bool HasForeignWindow,
    bool OnIslandMonitor,
    bool CoversIslandMonitor,
    string ProcessName);

static class ForegroundProbe
{
    public static ForegroundSample Read(IntPtr self, PixelRect islandMonitor)
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == self)
            return default;
        if (IsShell(hwnd))
            return default;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return default;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 40 || height < 40)
            return default;
        var window = new PixelRect(rect.Left, rect.Top, width, height);
        bool covers = HidePolicy.Covers(window, islandMonitor, 8);
        bool on = covers || HidePolicy.CenterOn(window, islandMonitor);
        if (!on && !covers)
            return new ForegroundSample(true, false, false, "");
        return new ForegroundSample(true, on, covers, ProcessName(hwnd));
    }

    static bool IsShell(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        int length = NativeMethods.GetClassName(hwnd, name, name.Capacity);
        if (length <= 0)
            return false;
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    static string ProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
            return "";
        IntPtr process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
            return "";
        try
        {
            var buffer = new StringBuilder(520);
            int size = buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size))
                return "";
            return HidePolicy.NormalizeProcess(buffer.ToString());
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
