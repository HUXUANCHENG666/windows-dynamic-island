using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>
/// 水滴边缘的折射采样：只从岛外一圈周围取色，再弯到 Rim 里。
/// 边缘带按圆角矩形 / 跑道形的距离场来算，中心保持透明。
/// </summary>
public static class LiquidRefraction
{
    public const int DefaultPad = 144; // 快拖时仍能用上一张实况垫边按当前位移重采样
    /// <summary>
    /// 内边界（贴着透镜那一侧）的折射强度。边缘折射的起点、内部透镜 blend 区的落点
    /// 都用这一个值，三者在 d=-RimBand 处才接得上，不会裂出第三条"缓冲带"。
    /// </summary>
    public const double InnerAlpha = 0.16;
    public const double InnerRadius = 0.78;
    public const double SampleGain = 0.88;
    public const double Chromatic = 0; // 同一点采完整 RGB，默认无色散。
    public const double HeightWarp = 0.75;
    public const double SlopeWarp = 2;

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
        out double blueV, double dpi = 1)
    {
        u = v = alpha = redU = redV = blueU = blueV = 0;
        if (width < 2 || height < 2 || pad < 4)
            return false;

        if (radius < 0)
            radius = Math.Min(width, height) * 0.5;
        radius = Squircle.SafeRadius(width, height, radius);

        double d = IslandOutline.SignedDistance(x, y, width, height, radius);
        if (d >= 0.5)
            return false;
        // 用距离挡掉中心，而不是用 along：along 把负数都压成 0，
        // 中心和内边界长得一样，之前靠 along<=0 早退，把内边界也一起挡掉了，
        // 于是边缘带的 alpha 在内边界突然归零，和内部透镜之间就裂出一条带。
        double band = IslandOutline.RimBand(width, height, dpi);
        if (d < -band)
            return false;
        double along = IslandOutline.Along(x, y, width, height, radius, dpi);

        var (ux, uy) = IslandOutline.Normal(x, y, width, height, radius);
        // 同一圆角平行曲面：最近外轮廓点 p-d*n，再沿同一法线偏移。
        // 不再延长到矩形四带，也不做 nearest-strip 分支投影。
        double depth = (4 + 3.5 * along + Math.Clamp(surface, -1, 1) * HeightWarp) * dpi;
        double sx = pad + x + ux * (depth - d) + slopeX / (1 + Math.Abs(slopeX)) * SlopeWarp * dpi;
        double sy = pad + y + uy * (depth - d) + slopeY / (1 + Math.Abs(slopeY)) * SlopeWarp * dpi;
        (u, v) = GlassSample.SafeRoundedPoint(sx, sy, width, height, radius, pad, dpi);
        redU = blueU = u;
        redV = blueV = v;
        // 内边界留一个和内部透镜接近的底值，交界处才不会从 0 跳到 0.15 形成一条带。
        // 爬升必须慢于尾部衰减的起点，否则内侧会在 midway 就冲到满值、反超最外圈，
        // 视觉上边缘就"不亮"了。这里让 along=1（贴着轮廓）才是全场峰值。
        double ramp = InnerAlpha + (1 - InnerAlpha) * IslandOutline.Smootherstep(0.04, 0.86, along);
        alpha = ramp * (1 - 0.10 * IslandOutline.Smootherstep(0.86, 1, along));
        return alpha > 0.01;
    }

    /// <summary>周围图像素是否落在岛外的一圈垫边里（我们只采集这些地方）。</summary>
    public static bool InPad(double u, double v, double width, double height, int pad)
    {
        return u < pad || u >= pad + width || v < pad || v >= pad + height;
    }
}
