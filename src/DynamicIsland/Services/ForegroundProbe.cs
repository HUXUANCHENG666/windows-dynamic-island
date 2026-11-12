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

    public static bool IsShell(IntPtr hwnd)
    {
        var name = ClassName(hwnd);
        return name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    public static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        int length = NativeMethods.GetClassName(hwnd, name, name.Capacity);
        return length > 0 ? name.ToString() : "";
    }

    /// <summary>归一化后的进程名，用来匹配名单。</summary>
    public static string ProcessName(IntPtr hwnd) => HidePolicy.NormalizeProcess(RawProcessName(hwnd));

    /// <summary>原始文件名，保留大小写，用来在提示里显示。</summary>
    public static string RawProcessName(IntPtr hwnd)
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
            var full = buffer.ToString();
            int slash = Math.Max(full.LastIndexOf('\\'), full.LastIndexOf('/'));
            var file = slash >= 0 && slash < full.Length - 1 ? full[(slash + 1)..] : full;
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                file = file[..^4];
            return file;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
