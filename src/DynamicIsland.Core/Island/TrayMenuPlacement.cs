namespace DynamicIsland.Core.Island;

/// <summary>
/// 托盘图标或鼠标在屏幕上的位置，单位是物理像素。
/// 宽高为 0 时表示只是一个点（拿不到图标矩形时用光标位置）。
/// </summary>
public readonly record struct TrayAnchor(int X, int Y, int Width, int Height)
{
    public static TrayAnchor Point(int x, int y) => new(x, y, 0, 0);
}

/// <summary>
/// 一块屏的全屏矩形、工作区和 DPI。坐标都是物理像素，和 GetCursorPos / GetMonitorInfo 同一套。
/// </summary>
public readonly record struct TrayScreen(
    int X,
    int Y,
    int Width,
    int Height,
    int WorkX,
    int WorkY,
    int WorkWidth,
    int WorkHeight,
    uint DpiX,
    uint DpiY,
    bool IsPrimary);

/// <summary>
/// 把黑色托盘菜单摆到图标附近，并且整块落在所在屏幕的工作区里。
/// 调用方要拿这里返回的物理像素去 SetWindowPos。不能把这些数写进 WPF 的 Left/Top：
/// 那两个属性是 DIP，Per-Monitor V2 下系统会再乘一次缩放，菜单会偏到屏幕外面。
/// </summary>
public static class TrayMenuPlacement
{
    public const double FallbackDipWidth = 196;
    public const double FallbackDipHeight = 180;
    public const double GapDip = 6;
    public const int IconSlopPx = 64;

    public static TrayAnchor ChooseAnchor(bool hasIcon, TrayAnchor icon, bool hasCursor, TrayAnchor cursor)
    {
        if (hasIcon && hasCursor)
            return NearIcon(icon, cursor) ? icon : cursor;
        if (hasIcon)
            return icon;
        if (hasCursor)
            return cursor;
        return TrayAnchor.Point(0, 0);
    }

    public static PixelRect Place(TrayAnchor anchor, double dipWidth, double dipHeight, IReadOnlyList<TrayScreen> screens)
    {
        double dipW = dipWidth >= 1 ? dipWidth : FallbackDipWidth;
        double dipH = dipHeight >= 1 ? dipHeight : FallbackDipHeight;
        int probeX = anchor.X + Math.Max(0, anchor.Width) / 2;
        int probeY = anchor.Y + Math.Max(0, anchor.Height) / 2;
        TrayScreen? screen = FindScreen(probeX, probeY, screens);
        uint dpiX = Dpi(screen?.DpiX ?? 96);
        uint dpiY = Dpi(screen?.DpiY ?? 96);
        var pixels = WindowPlacement.ToPixels(dipW, dipH, dpiX, dpiY);
        int gap = Math.Max(1, (int)Math.Round(GapDip * (dpiY / 96.0)));
        int left = anchor.X + Math.Max(0, anchor.Width) - pixels.Width;
        int top = anchor.Y - pixels.Height - gap;
        if (screen == null)
            return new PixelRect(left, top, pixels.Width, pixels.Height);

        var work = screen.Value;
        int workRight = work.WorkX + work.WorkWidth;
        int workBottom = work.WorkY + work.WorkHeight;
        if (top < work.WorkY)
            top = anchor.Y + Math.Max(0, anchor.Height) + gap;
        left = Clamp(left, work.WorkX, Math.Max(work.WorkX, workRight - pixels.Width));
        top = Clamp(top, work.WorkY, Math.Max(work.WorkY, workBottom - pixels.Height));
        return new PixelRect(left, top, pixels.Width, pixels.Height);
    }

    static bool NearIcon(TrayAnchor icon, TrayAnchor cursor)
    {
        int right = icon.X + Math.Max(icon.Width, 1) + IconSlopPx;
        int bottom = icon.Y + Math.Max(icon.Height, 1) + IconSlopPx;
        return cursor.X >= icon.X - IconSlopPx && cursor.X < right
            && cursor.Y >= icon.Y - IconSlopPx && cursor.Y < bottom;
    }

    static TrayScreen? FindScreen(int x, int y, IReadOnlyList<TrayScreen> screens)
    {
        TrayScreen? inside = null;
        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            if (x >= screen.X && x < screen.X + screen.Width && y >= screen.Y && y < screen.Y + screen.Height)
            {
                inside = screen;
                break;
            }
        }

        if (inside != null)
            return inside;
        if (screens.Count == 0)
            return null;

        var best = screens[0];
        long bestDistance = long.MaxValue;
        for (int i = 0; i < screens.Count; i++)
        {
            long distance = DistanceSquared(x, y, screens[i]);
            if (distance < bestDistance || (distance == bestDistance && screens[i].IsPrimary && !best.IsPrimary))
            {
                bestDistance = distance;
                best = screens[i];
            }
        }

        return best;
    }

    static long DistanceSquared(int x, int y, TrayScreen screen)
    {
        long dx = x < screen.X ? screen.X - x : x >= screen.X + screen.Width ? x - (screen.X + screen.Width - 1) : 0;
        long dy = y < screen.Y ? screen.Y - y : y >= screen.Y + screen.Height ? y - (screen.Y + screen.Height - 1) : 0;
        return dx * dx + dy * dy;
    }

    static uint Dpi(uint dpi) => dpi < 96 ? 96 : dpi;

    static int Clamp(int value, int min, int max)
    {
        if (max < min)
            return min;
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
