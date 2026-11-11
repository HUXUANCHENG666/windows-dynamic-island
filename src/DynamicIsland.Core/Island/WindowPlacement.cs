namespace DynamicIsland.Core.Island;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.Right, b.Right);
        int y2 = Math.Min(a.Bottom, b.Bottom);
        if (x2 <= x1 || y2 <= y1)
            return new PixelRect(x1, y1, 0, 0);
        return new PixelRect(x1, y1, x2 - x1, y2 - y1);
    }
}

/// <summary>
/// 把 DIP 尺寸换成某块屏幕上的物理像素，并水平居中、贴在那块屏的顶边。
/// 多屏 DPI 不一致时不能拿主屏的缩放去摆副屏。
/// </summary>
public static class WindowPlacement
{
    public static PixelRect CenterTop(
        int monitorX,
        int monitorY,
        int monitorWidth,
        int monitorHeight,
        uint dpiX,
        uint dpiY,
        double dipWidth,
        double dipHeight)
    {
        double sx = Math.Max(96, dpiX) / 96.0;
        double sy = Math.Max(96, dpiY) / 96.0;
        int w = Math.Max(1, (int)Math.Round(dipWidth * sx));
        int h = Math.Max(1, (int)Math.Round(dipHeight * sy));
        int x = monitorX + (monitorWidth - w) / 2;
        int y = monitorY;
        return new PixelRect(x, y, w, h);
    }

    public static (int Width, int Height) ToPixels(double dipWidth, double dipHeight, uint dpiX, uint dpiY)
    {
        double sx = Math.Max(96, dpiX) / 96.0;
        double sy = Math.Max(96, dpiY) / 96.0;
        return (Math.Max(1, (int)Math.Round(dipWidth * sx)), Math.Max(1, (int)Math.Round(dipHeight * sy)));
    }

    public static double SnapDip(double dip, double dpiScale)
    {
        if (dpiScale < 0.5 || double.IsNaN(dpiScale))
            dpiScale = 1;
        return Math.Round(dip * dpiScale) / dpiScale;
    }
}
