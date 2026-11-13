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
/// Maximized 是常规最大化——它也会铺满工作区，但那不是全屏，不能用来决定隐身。
/// </summary>
public readonly record struct TopWindow(PixelRect Rect, bool Ignore, bool Borderless, bool Maximized = false);

/// <summary>
/// 只在岛自己这块屏被盖住时收成小点。
/// 带标题栏的最大化窗口不算，哪怕任务栏自动隐藏、矩形盖住了整块屏。
/// 无边框窗口盖住整块屏就算，包括还留着 WS_MAXIMIZE 的直接全屏。差一点没盖住任务栏的无边框窗口也算。
/// 独占 D3D 时，这块屏上那扇够大、且不是普通最大化的前台窗口也算。别的屏幕上的全屏不算。
/// </summary>
public static class FullscreenPolicy
{
    public const int CoverTolerancePx = 8;

    /// <summary>
    /// 桌面、任务栏，以及切换窗口时一闪而过的全屏壳层。
    /// 这些窗口会在打开或切走普通窗口时铺满屏幕，不能拿来决定隐身。
    /// </summary>
    public static bool IsShellOrDesktop(string? className) => className switch
    {
        "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" => true,
        "Shell_LightDismissOverlay" or "MultitaskingViewFrame" or "ForegroundStaging" => true,
        "Windows.Internal.Shell.TabProxyWindow" or "XamlExplorerHostIslandWindow" => true,
        "ApplicationManager_ImmersiveShellWindow" or "NotifyIconOverflowWindow" => true,
        // Win11 隐藏图标浮层。窗口矩形经常被报成整块屏，类名只给资源管理器用。
        "TopLevelWindowForOverflowXamlIsland" => true,
        _ => false
    };

    /// <summary>
    /// 开始菜单、搜索和任务中心。它们的窗口类（CoreWindow）普通 UWP 程序也用，
    /// 所以只认这几个壳层进程。Kazumi 这类播放器不在里面。
    /// </summary>
    public static bool IsShellHostProcess(string? processName)
    {
        var process = HidePolicy.NormalizeProcess(processName);
        return process is "startmenuexperiencehost" or "searchhost" or "searchapp" or "shellexperiencehost";
    }

    /// <summary>壳层浮层：类名已经能确定，或者进程是开始菜单 / 搜索 / 任务中心。</summary>
    public static bool IsShellOverlay(string? className, string? processName) =>
        IsShellOrDesktop(className) || IsShellHostProcess(processName);

    /// <summary>点得穿的窗口是遮罩，不是用户正在看的全屏。</summary>
    public static bool IsClickThrough(long extendedStyle) => (extendedStyle & 0x20) != 0;

    /// <summary>
    /// 样式里还留着标题栏，但客户区已经铺满窗口时，按无边框算。
    /// Kazumi 一类播放器切全屏后，WS_CAPTION 有时要等下一次激活才清掉。
    /// </summary>
    public static bool IsVisuallyBorderless(bool captionStyle, int nonClientWidth, int nonClientHeight) =>
        !captionStyle || (nonClientWidth <= 2 && nonClientHeight <= 4);

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

        // 带标题栏的最大化窗口会铺满工作区，自动隐藏任务栏时甚至盖住整块屏。那仍是最大化，不是全屏。
        if (windowOnTop.Maximized && !windowOnTop.Borderless)
            return false;
        if (HidePolicy.Covers(windowOnTop.Rect, monitor, CoverTolerancePx))
            return true;
        // 最大化窗口同样会铺满工作区、差一截任务栏。无边框的普通最大化也不算全屏。
        // 无边框又真的盖住整块屏（含过期的 WS_MAXIMIZE）才算，Kazumi 直接全屏是这种。
        if (windowOnTop.Maximized)
            return false;
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
        // 内缩到任务栏高度左右。再大一点，普通最大化窗口也会被算成全屏。
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
