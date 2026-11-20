using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>
/// 水滴边缘的折射采样：只从岛外一圈周围取色，再弯到 Rim 里。
/// 边缘带按圆角矩形 / 跑道形的距离场来算，中心保持透明。
/// </summary>
public static class LiquidRefraction
{
    public const int DefaultPad = 72;
    public const double InnerRadius = 0.78;
    public const double SampleGain = 0.88;
    public const double Chromatic = 0.045;
    public const double HeightWarp = 0.20;
    public const double SlopeWarp = 0.55;

    public static double Smoothstep(double edge0, double edge1, double x) =>
        IslandOutline.Smoothstep(edge0, edge1, x);

    /// <summary>
    /// 岛内像素 (x,y) 若落在边缘带，给出它在「外扩 pad 后的周围图」里的采样点。
    /// 周围图的坐标系：岛本体占 [pad, pad+w) × [pad, pad+h)。
    /// 未给半径时按胶囊（半径 = 短边的一半）处理。
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
        TryMap(x, y, width, height, pad, -1, 0, 0, 0,
            out u, out v, out alpha, out redU, out redV, out blueU, out blueV);

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
        out double blueV) =>
        TryMap(x, y, width, height, pad, -1, slopeX, slopeY, surface,
            out u, out v, out alpha, out redU, out redV, out blueU, out blueV);

    public static bool TryMap(
        double x,
        double y,
        double width,
        double height,
        int pad,
        double radius,
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

        if (radius < 0)
            radius = Math.Min(width, height) * 0.5;
        radius = Squircle.SafeRadius(width, height, radius);

        double d = IslandOutline.SignedDistance(x, y, width, height, radius);
        if (d >= 0.35)
            return false;
        double along = IslandOutline.Along(x, y, width, height, radius);
        if (along < 0.02)
            return false;

        var (ux, uy) = IslandOutline.Normal(x, y, width, height, radius);
        double cx = width * 0.5;
        double cy = height * 0.5;
        double halfW = width * 0.5;
        double halfH = height * 0.5;
        double tx = Math.Abs(ux) < 1e-8 ? double.PositiveInfinity : halfW / Math.Abs(ux);
        double ty = Math.Abs(uy) < 1e-8 ? double.PositiveInfinity : halfH / Math.Abs(uy);
        double tRect = Math.Min(tx, ty);
        if (double.IsInfinity(tRect) || tRect <= 0)
            return false;

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
        alpha = along * (1 - 0.12 * IslandOutline.Smoothstep(0.2, 0.85, along));
        return alpha > 0.01;
    }

    /// <summary>周围图像素是否落在岛外的一圈垫边里（我们只采集这些地方）。</summary>
    public static bool InPad(double u, double v, double width, double height, int pad)
    {
        return u < pad || u >= pad + width || v < pad || v >= pad + height;
    }
}
