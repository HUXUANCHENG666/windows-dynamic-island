namespace DynamicIsland.Core.Motion;

/// <summary>
/// 周围画面的取色：始终做双线性插值，颜色交界是渐变而不是硬切。
/// 屏幕捕获常常没有 alpha，有颜色的像素按不透明处理。
/// </summary>
public static class GlassSample
{
    public static int Prepare(int color)
    {
        if (color == 0)
            return 0;
        if (((color >> 24) & 0xFF) == 0)
            return unchecked((int)0xFF000000) | (color & 0x00FFFFFF);
        return color;
    }

    public static int Lerp(int a, int b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        a = Prepare(a);
        b = Prepare(b);
        int aa = (a >> 24) & 0xFF;
        int ba = (b >> 24) & 0xFF;
        int ar = (a >> 16) & 0xFF;
        int br = (b >> 16) & 0xFF;
        int ag = (a >> 8) & 0xFF;
        int bg = (b >> 8) & 0xFF;
        int ab = a & 0xFF;
        int bb = b & 0xFF;
        return (((int)Math.Round(aa + (ba - aa) * t)) << 24)
            | (((int)Math.Round(ar + (br - ar) * t)) << 16)
            | (((int)Math.Round(ag + (bg - ag) * t)) << 8)
            | (int)Math.Round(ab + (bb - ab) * t);
    }

    public static int Bilinear(int[] pixels, int width, int height, double u, double v)
    {
        if (pixels == null || width < 1 || height < 1 || pixels.Length < width * height)
            return 0;

        u = Math.Clamp(u, 0, Math.Max(0, width - 1.001));
        v = Math.Clamp(v, 0, Math.Max(0, height - 1.001));
        int x0 = (int)Math.Floor(u);
        int y0 = (int)Math.Floor(v);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);
        double fx = u - x0;
        double fy = v - y0;
        int row0 = y0 * width;
        int row1 = y1 * width;
        return Lerp(
            Lerp(pixels[row0 + x0], pixels[row0 + x1], fx),
            Lerp(pixels[row1 + x0], pixels[row1 + x1], fx),
            fy);
    }

    /// <summary>
    /// 把当前岛尺寸下的采样点，映射到另一帧（尺寸不同）的周围图上。
    /// 垫边宽度不变，只缩放岛洞，展开动画期间也能用上一帧周围图。
    /// </summary>
    public static (double U, double V) MapPad(
        double u,
        double v,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        int pad)
    {
        return (MapAxis(u, sourceWidth, targetWidth, pad), MapAxis(v, sourceHeight, targetHeight, pad));
    }

    static double MapAxis(double x, int source, int target, int pad)
    {
        if (x < pad)
            return x;
        if (x >= pad + target)
            return x - target + source;
        if (target <= 0)
            return pad;
        return pad + (x - pad) * source / (double)target;
    }
}
