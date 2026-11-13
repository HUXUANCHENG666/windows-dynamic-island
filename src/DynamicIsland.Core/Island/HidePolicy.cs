namespace DynamicIsland.Core.Island;

public readonly record struct HideSignals(
    bool Stealth,
    bool HasForeignWindow,
    bool OnIslandMonitor,
    bool CoversIslandMonitor,
    string? ProcessName);

/// <summary>
/// 只看岛所在的那块屏。别的屏幕上的全屏或前台窗口不躲。
/// 隐身是手动的。名单里的程序只要在这块屏上成为前台（含全屏）就收成小点。
/// </summary>
public static class HidePolicy
{
    public static bool Covers(PixelRect window, PixelRect monitor, int tolerancePx)
    {
        if (window.Width <= 0 || window.Height <= 0 || monitor.Width <= 0 || monitor.Height <= 0)
            return false;
        return window.X <= monitor.X + tolerancePx
            && window.Y <= monitor.Y + tolerancePx
            && window.Right >= monitor.Right - tolerancePx
            && window.Bottom >= monitor.Bottom - tolerancePx;
    }

    public static bool CenterOn(PixelRect window, PixelRect monitor)
    {
        if (window.Width <= 0 || window.Height <= 0)
            return false;
        int x = window.X + window.Width / 2;
        int y = window.Y + window.Height / 2;
        return x >= monitor.X && x < monitor.Right && y >= monitor.Y && y < monitor.Bottom;
    }

    public static bool IsOccluded(HideSignals signals, IEnumerable<string> hideList)
    {
        if (signals.Stealth)
            return true;
        if (!signals.HasForeignWindow)
            return false;
        if (signals.CoversIslandMonitor)
            return true;
        if (!signals.OnIslandMonitor || string.IsNullOrWhiteSpace(signals.ProcessName))
            return false;
        var name = NormalizeProcess(signals.ProcessName);
        foreach (var item in hideList)
        {
            if (string.Equals(NormalizeProcess(item), name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static string NormalizeProcess(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var file = name.Trim().Trim('"');
        int slash = Math.Max(file.LastIndexOf('\\'), file.LastIndexOf('/'));
        if (slash >= 0 && slash < file.Length - 1)
            file = file[(slash + 1)..];
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            file = file[..^4];
        return file.ToLowerInvariant();
    }

    /// <summary>Returns true when the process is now on the list.</summary>
    public static bool ToggleListed(IList<string> list, string process)
    {
        var name = NormalizeProcess(process);
        if (name.Length == 0)
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (!string.Equals(NormalizeProcess(list[i]), name, StringComparison.Ordinal))
                continue;
            list.RemoveAt(i);
            return false;
        }

        list.Add(name);
        return true;
    }

    public static bool IsListed(IEnumerable<string> list, string? process)
    {
        var name = NormalizeProcess(process);
        if (name.Length == 0)
            return false;
        foreach (var item in list)
        {
            if (string.Equals(NormalizeProcess(item), name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
