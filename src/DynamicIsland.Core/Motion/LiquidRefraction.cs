namespace DynamicIsland.Core.Motion;

/// <summary>
/// 水滴边缘的折射采样：只从岛外一圈周围取色，再弯到 Rim 里。
/// 中心保持透明，这样仍能直接透过窗口看到后面的桌面。
/// </summary>
public static class LiquidRefraction
{
    public const int DefaultPad = 72;
    public const double InnerRadius = 0.78;
    public const double SampleGain = 0.88;
    public const double Chromatic = 0.045;
    public const double HeightWarp = 0.20;
    public const double SlopeWarp = 0.55;

    public static double Smoothstep(double edge0, double edge1, double x)
    {
        if (edge1 <= edge0)
            return x >= edge1 ? 1 : 0;
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// 岛内像素 (x,y) 若落在边缘带，给出它在「外扩 pad 后的周围图」里的采样点。
    /// 周围图的坐标系：岛本体占 [pad, pad+w) × [pad, pad+h)。
    /// </summary>
    public static bool TryMap(
        double x,
        double y,
        double width,
        double height,
        int pad,
        out double u,
        out double v,
        out double alpha,
        out double redU,
        out double redV,
        out double blueU,
        out double blueV) =>
        TryMap(x, y, width, height, pad, 0, 0, 0,
            out u, out v, out alpha, out redU, out redV, out blueU, out blueV);

    /// <param name="slopeX">高度场在岛内归一化坐标下的横向斜率，用来把周围多折一点。</param>
    public static bool TryMap(
        double x,
        double y,
        double width,
        double height,
        int pad,
        double slopeX,
        double slopeY,
        double surface,
        out double u,
        out double v,
        out double alpha,
        out double redU,
        out double redV,
        out double blueU,
        out double blueV)
    {
        u = v = alpha = redU = redV = blueU = blueV = 0;
        if (width < 2 || height < 2 || pad < 4)
            return false;

        double cx = width * 0.5;
        double cy = height * 0.5;
        double halfW = width * 0.5;
        double halfH = height * 0.5;
        double nx = (x - cx) / halfW;
        double ny = (y - cy) / halfH;
        double r = Math.Sqrt(nx * nx + ny * ny);
        if (r < InnerRadius || r < 1e-6)
            return false;

        double dx = x - cx;
        double dy = y - cy;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6)
            return false;
        double ux = dx / len;
        double uy = dy / len;

        double tx = Math.Abs(ux) < 1e-8 ? double.PositiveInfinity : halfW / Math.Abs(ux);
        double ty = Math.Abs(uy) < 1e-8 ? double.PositiveInfinity : halfH / Math.Abs(uy);
        double tRect = Math.Min(tx, ty);
        if (double.IsInfinity(tRect) || tRect <= 0)
            return false;

        double along = Smoothstep(InnerRadius, 1, r);
        double offset = 0.6 + along * Math.Max(4, (pad - 1) * SampleGain)
            + surface * pad * HeightWarp;
        double sx = cx + pad + ux * (tRect + offset) + slopeX * pad * SlopeWarp;
        double sy = cy + pad + uy * (tRect + offset) + slopeY * pad * SlopeWarp;
        double paddedW = width + pad * 2;
        double paddedH = height + pad * 2;
        sx = Math.Clamp(sx, 0, paddedW - 1);
        sy = Math.Clamp(sy, 0, paddedH - 1);

        double chroma = Chromatic * pad * along;
        u = sx;
        v = sy;
        redU = Math.Clamp(sx + ux * chroma, 0, paddedW - 1);
        redV = Math.Clamp(sy + uy * chroma, 0, paddedH - 1);
        blueU = Math.Clamp(sx - ux * chroma, 0, paddedW - 1);
        blueV = Math.Clamp(sy - uy * chroma, 0, paddedH - 1);
        alpha = along * (1 - 0.12 * Smoothstep(0.97, 1.04, r));
        return alpha > 0.01;
    }

    /// <summary>周围图像素是否落在岛外的一圈垫边里（我们只采集这些地方）。</summary>
    public static bool InPad(double u, double v, double width, double height, int pad)
    {
        return u < pad || u >= pad + width || v < pad || v >= pad + height;
    }
}
