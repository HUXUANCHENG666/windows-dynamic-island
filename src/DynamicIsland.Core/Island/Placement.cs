using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Island;

public enum DockAnchor
{
    TopCenter = 0,
    BottomCenter,
    LeftCenter,
    RightCenter,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public enum IslandOrientation
{
    Horizontal,
    Vertical
}

public readonly record struct Shift(double X, double Y);

/// <summary>
/// 停靠、吸附、工作区避让。左右居中是竖胶囊，四角和上下居中保持横胶囊。
/// 窗口必须落在工作区里；放不进时往没有任务栏的那一侧溢出。
/// </summary>
public static class Dock
{
    public static IslandOrientation OrientationOf(DockAnchor anchor) =>
        anchor is DockAnchor.LeftCenter or DockAnchor.RightCenter
            ? IslandOrientation.Vertical
            : IslandOrientation.Horizontal;

    public static DockAnchor Parse(string? value) =>
        Enum.TryParse<DockAnchor>(value, ignoreCase: true, out var anchor)
            ? anchor
            : DockAnchor.TopCenter;
}

public static class DockStore
{
    public static DockAnchor Recall(IEnumerable<MonitorDock>? docks, string? deviceName)
    {
        if (docks == null || string.IsNullOrWhiteSpace(deviceName))
            return DockAnchor.TopCenter;
        foreach (var dock in docks)
        {
            if (string.Equals(dock.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                return Dock.Parse(dock.Anchor);
        }

        return DockAnchor.TopCenter;
    }

    public static void Remember(IList<MonitorDock> docks, string deviceName, DockAnchor anchor)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return;
        for (int i = 0; i < docks.Count; i++)
        {
            if (!string.Equals(docks[i].DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                continue;
            docks[i].Anchor = anchor.ToString();
            return;
        }

        docks.Add(new MonitorDock { DeviceName = deviceName, Anchor = anchor.ToString() });
    }
}

public static class PillLayout
{
    public static (double Left, double Top) Offset(
        DockAnchor anchor,
        double windowW,
        double windowH,
        double pillW,
        double pillH,
        double margin)
    {
        double x = anchor switch
        {
            DockAnchor.TopLeft or DockAnchor.BottomLeft or DockAnchor.LeftCenter => margin,
            DockAnchor.TopRight or DockAnchor.BottomRight or DockAnchor.RightCenter => windowW - pillW - margin,
            _ => (windowW - pillW) / 2.0
        };
        double y = anchor switch
        {
            DockAnchor.TopLeft or DockAnchor.TopCenter or DockAnchor.TopRight => margin,
            DockAnchor.BottomLeft or DockAnchor.BottomCenter or DockAnchor.BottomRight => windowH - pillH - margin,
            _ => (windowH - pillH) / 2.0
        };
        return (x, y);
    }
}

public static class MotionLayout
{
    /// <summary>
    /// along 是内容沿“长出来的方向”的位移。上沿用正值往下，下沿反过来，左右沿水平长。
    /// </summary>
    public static Shift ContentShift(DockAnchor anchor, double along) => anchor switch
    {
        DockAnchor.BottomLeft or DockAnchor.BottomCenter or DockAnchor.BottomRight => new Shift(0, -along),
        DockAnchor.LeftCenter => new Shift(along, 0),
        DockAnchor.RightCenter => new Shift(-along, 0),
        _ => new Shift(0, along)
    };
}

public static class WorkAreaMath
{
    public static PixelRect ExcludeBars(PixelRect monitor, PixelRect reportedWork, IReadOnlyList<PixelRect> bars)
    {
        var area = PixelRect.Intersect(monitor, reportedWork);
        if (area.Width < 80 || area.Height < 80)
            area = monitor;
        foreach (var bar in bars)
            area = Shrink(area, bar);
        if (area.Width < 80 || area.Height < 80)
        {
            var fallback = PixelRect.Intersect(monitor, reportedWork);
            return fallback.Width < 80 || fallback.Height < 80 ? monitor : fallback;
        }

        return area;
    }

    public static PixelRect Shrink(PixelRect area, PixelRect bar)
    {
        var overlap = PixelRect.Intersect(area, bar);
        if (overlap.Width < 8 || overlap.Height < 8)
            return area;

        bool onLeft = overlap.X <= area.X + 4;
        bool onRight = overlap.Right >= area.Right - 4;
        bool onTop = overlap.Y <= area.Y + 4;
        bool onBottom = overlap.Bottom >= area.Bottom - 4;

        if (onBottom && overlap.Height < area.Height / 2 && overlap.Width > area.Width / 2)
            return new PixelRect(area.X, area.Y, area.Width, Math.Max(0, overlap.Y - area.Y));
        if (onTop && overlap.Height < area.Height / 2 && overlap.Width > area.Width / 2)
            return new PixelRect(area.X, overlap.Bottom, area.Width, Math.Max(0, area.Bottom - overlap.Bottom));
        if (onLeft && overlap.Width < area.Width / 2 && overlap.Height > area.Height / 2)
            return new PixelRect(overlap.Right, area.Y, Math.Max(0, area.Right - overlap.Right), area.Height);
        if (onRight && overlap.Width < area.Width / 2 && overlap.Height > area.Height / 2)
            return new PixelRect(area.X, area.Y, Math.Max(0, overlap.X - area.X), area.Height);
        return area;
    }
}

public static class SnapMath
{
    public readonly record struct Point(int X, int Y);

    public static Point AnchorPoint(DockAnchor anchor, PixelRect work, int inset)
    {
        int left = work.X + inset;
        int right = work.Right - inset;
        int top = work.Y + inset;
        int bottom = work.Bottom - inset;
        int cx = work.X + work.Width / 2;
        int cy = work.Y + work.Height / 2;
        return anchor switch
        {
            DockAnchor.BottomCenter => new Point(cx, bottom),
            DockAnchor.LeftCenter => new Point(left, cy),
            DockAnchor.RightCenter => new Point(right, cy),
            DockAnchor.TopLeft => new Point(left, top),
            DockAnchor.TopRight => new Point(right, top),
            DockAnchor.BottomLeft => new Point(left, bottom),
            DockAnchor.BottomRight => new Point(right, bottom),
            _ => new Point(cx, top)
        };
    }

    public static DockAnchor Nearest(int x, int y, PixelRect work, int inset)
    {
        DockAnchor best = DockAnchor.TopCenter;
        long bestDistance = long.MaxValue;
        foreach (DockAnchor anchor in Enum.GetValues<DockAnchor>())
        {
            var point = AnchorPoint(anchor, work, inset);
            long dx = x - (long)point.X;
            long dy = y - (long)point.Y;
            long distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = anchor;
            }
        }

        return best;
    }

    public static PixelRect Place(DockAnchor anchor, PixelRect work, int windowW, int windowH, PixelRect monitor)
    {
        int x = anchor switch
        {
            DockAnchor.TopLeft or DockAnchor.BottomLeft or DockAnchor.LeftCenter => work.X,
            DockAnchor.TopRight or DockAnchor.BottomRight or DockAnchor.RightCenter => work.Right - windowW,
            _ => work.X + (work.Width - windowW) / 2
        };
        int y = anchor switch
        {
            DockAnchor.TopLeft or DockAnchor.TopCenter or DockAnchor.TopRight => work.Y,
            DockAnchor.BottomLeft or DockAnchor.BottomCenter or DockAnchor.BottomRight => work.Bottom - windowH,
            _ => work.Y + (work.Height - windowH) / 2
        };
        return Fit(x, y, windowW, windowH, work, monitor);
    }

    public static PixelRect Fit(int x, int y, int w, int h, PixelRect work, PixelRect monitor)
    {
        if (w <= work.Width)
            x = Math.Clamp(x, work.X, work.Right - w);
        else if (work.X > monitor.X + 1)
            x = work.X;
        else
            x = work.Right - w;

        if (h <= work.Height)
            y = Math.Clamp(y, work.Y, work.Bottom - h);
        else if (work.Y > monitor.Y + 1)
            y = work.Y;
        else
            y = work.Bottom - h;

        return new PixelRect(x, y, w, h);
    }
}
