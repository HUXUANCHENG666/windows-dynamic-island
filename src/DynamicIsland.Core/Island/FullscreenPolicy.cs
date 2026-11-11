namespace DynamicIsland.Core.Island;

/// <summary>
/// Windows 认为用户正忙着：全屏应用、独占 D3D，或放映模式。这是整机状态，不能单独决定哪块屏要躲。
/// </summary>
public enum ShellAttention
{
    None,
    Busy,
    Direct3D,
    Presentation
}

/// <summary>
/// 从上到下的一扇窗口。Ignore 用来丢掉桌面、Shell、本进程自己的窗口，以及看不见的窗口。
/// Borderless 是没有标题栏的无边框窗口，Kazumi 这类全屏就是这样盖住屏幕的。
/// </summary>
public readonly record struct TopWindow(PixelRect Rect, bool Ignore, bool Borderless);

/// <summary>
/// 只在岛自己这块屏被盖住时收成小点。
/// 严格盖住 rcMonitor 的窗口都算，不管有没有标题栏。
/// 无边框窗口差一点没盖住任务栏（Kazumi 常见）也算。普通最大化窗口有标题栏，而且只占工作区，不算。
/// 独占 D3D 时，这块屏上那扇够大的前台窗口也算。别的屏幕上的全屏不算。
/// </summary>
public static class FullscreenPolicy
{
    public const int CoverTolerancePx = 8;

    public static bool IsShellOrDesktop(string? className) => className switch
    {
        "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" => true,
        _ => false
    };

    public static bool ShouldHide(PixelRect monitor, IReadOnlyList<TopWindow> zOrderTopFirst, ShellAttention attention)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0)
            return false;

        TopWindow? top = null;
        foreach (var window in zOrderTopFirst)
        {
            if (window.Ignore || !IsSubstantial(window.Rect, monitor))
                continue;
            top = window;
            break;
        }

        if (top is not TopWindow windowOnTop)
            return false;
        if (!Overlaps(windowOnTop.Rect, monitor))
            return false;
        if (HidePolicy.Covers(windowOnTop.Rect, monitor, CoverTolerancePx))
            return true;
        if (windowOnTop.Borderless && NearlyCovers(windowOnTop.Rect, monitor))
            return true;
        if (attention == ShellAttention.Direct3D
            && HidePolicy.CenterOn(windowOnTop.Rect, monitor)
            && OverlapRatio(windowOnTop.Rect, monitor) >= 0.85)
            return true;
        return false;
    }

    public static bool NearlyCovers(PixelRect window, PixelRect monitor)
    {
        if (!HidePolicy.CenterOn(window, monitor))
            return false;
        int insetX = Math.Min(72, Math.Max(1, monitor.Width / 12));
        int insetY = Math.Min(72, Math.Max(1, monitor.Height / 12));
        if (monitor.Width <= insetX * 2 || monitor.Height <= insetY * 2)
            return false;
        var inner = new PixelRect(
            monitor.X + insetX,
            monitor.Y + insetY,
            monitor.Width - insetX * 2,
            monitor.Height - insetY * 2);
        return HidePolicy.Covers(window, inner, 0);
    }

    public static bool IsSubstantial(PixelRect window, PixelRect monitor)
    {
        if (window.Width < 80 || window.Height < 80 || monitor.Width <= 0 || monitor.Height <= 0)
            return false;
        long area = (long)window.Width * window.Height;
        long screen = (long)monitor.Width * monitor.Height;
        return area * 100 >= screen * 15;
    }

    public static bool Overlaps(PixelRect window, PixelRect monitor) =>
        PixelRect.Intersect(window, monitor).Width > 0;

    public static double OverlapRatio(PixelRect window, PixelRect monitor)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0)
            return 0;
        var overlap = PixelRect.Intersect(window, monitor);
        if (overlap.Width <= 0 || overlap.Height <= 0)
            return 0;
        return ((long)overlap.Width * overlap.Height) / (double)((long)monitor.Width * monitor.Height);
    }
}
