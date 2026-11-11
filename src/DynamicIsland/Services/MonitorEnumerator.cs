using DynamicIsland.Native;

namespace DynamicIsland.Services;

sealed class MonitorDesc
{
    public string DeviceName { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int WorkX { get; init; }
    public int WorkY { get; init; }
    public int WorkWidth { get; init; }
    public int WorkHeight { get; init; }
    public uint DpiX { get; init; } = 96;
    public uint DpiY { get; init; } = 96;
    public bool IsPrimary { get; init; }
    public string Label { get; init; } = "";
}

static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorDesc> Enumerate()
    {
        var list = new List<MonitorDesc>();
        NativeMethods.MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return true;

            uint dpiX = 96, dpiY = 96;
            try
            {
                if (NativeMethods.GetDpiForMonitor(monitor, 0, out var x, out var y) == 0 && x > 0 && y > 0)
                {
                    dpiX = x;
                    dpiY = y;
                }
            }
            catch
            {
                dpiX = 96;
                dpiY = 96;
            }

            int width = info.rcMonitor.Right - info.rcMonitor.Left;
            int height = info.rcMonitor.Bottom - info.rcMonitor.Top;
            int workW = info.rcWork.Right - info.rcWork.Left;
            int workH = info.rcWork.Bottom - info.rcWork.Top;
            bool primary = (info.dwFlags & 1) != 0;
            string name = info.szDevice ?? "";
            string shortName = name.Replace(@"\\.\", "", StringComparison.Ordinal);
            string label = primary ? $"主显示器 · {width}×{height}" : $"{shortName} · {width}×{height}";
            list.Add(new MonitorDesc
            {
                DeviceName = name,
                X = info.rcMonitor.Left,
                Y = info.rcMonitor.Top,
                Width = width,
                Height = height,
                WorkX = info.rcWork.Left,
                WorkY = info.rcWork.Top,
                WorkWidth = workW > 0 ? workW : width,
                WorkHeight = workH > 0 ? workH : height,
                DpiX = dpiX,
                DpiY = dpiY,
                IsPrimary = primary,
                Label = label
            });
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return list;
    }
}
