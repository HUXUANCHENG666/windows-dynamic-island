namespace DynamicIsland.Core.Island;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

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

    public static double SnapDip(double dip, double dpiScale)
    {
        if (dpiScale < 0.5 || double.IsNaN(dpiScale))
            dpiScale = 1;
        return Math.Round(dip * dpiScale) / dpiScale;
    }
}
