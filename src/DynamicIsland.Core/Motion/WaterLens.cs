using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>
/// 只用于有效实况底图的清水透镜。中心约 1.18× 水滴放大，在约 1.15 倍边带处平滑接到
/// 贴轮廓的 9 DIP 折射/高光带。无时钟抖动、无色散，不是全圆夸张鱼眼。
/// </summary>
public static class WaterLens
{
    public const double Magnification = 1.18;
    /// <summary>从岛内多少倍边带开始混向边缘折射。</summary>
    public const double BlendStartBands = 2.0; // 约 9 DIP 内渐隐，避免在边带内沿只剩 1～2 像素的硬缝
    /// <summary>混到纯边缘折射的位置（负边带的很小一部分，靠近外沿）。</summary>
    /// <summary>在边带内沿（-band）结束混合，9 DIP 轮廓折射带由原边缘路径单独绘制。</summary>
    public const double BlendEndBands = 1.0;

    public static void Map(double x, double y, double w, double h, double radius, double dpi,
        double distance, LiquidOpticsFrame optics, double slopeX, double slopeY, double surface,
        out double u, out double v, out double alpha)
    {
        const int pad = LiquidRefraction.DefaultPad;
        double cx = w * 0.5, cy = h * 0.5;
        double nx = x - cx, ny = y - cy;
        double dome = Math.Max(0, (1 - (nx / cx) * (nx / cx)) * (1 - (ny / cy) * (ny / cy)));
        // 平方穹顶：中心有一段较均匀的放大，近边缘更快落下，把阅读区和边带分开。
        double contraction = (1 - 1 / Magnification) * dome * dome;
        LiquidOptics.DisplacedUv(x, y, w, h, dpi, optics, slopeX, slopeY, out double dx, out double dy);
        double band = IslandOutline.RimBand(w, h, dpi);
        double blend = IslandOutline.Smootherstep(-BlendStartBands * band, -BlendEndBands * band, distance);
        double bx = x - nx * contraction + (dx - x) * 0.18 * (1 - blend);
        double by = y - ny * contraction + (dy - y) * 0.18 * (1 - blend);
        // blend 走到 1（正好是 RimBand 内边界）时不能归零，否则透镜让位给边缘折射的瞬间
        // 会空出一段什么都不画的距离。落点和边缘折射的起点取同一个值，两边直接接上。
        double lensAlpha = 0.96 * (1 - blend) + LiquidRefraction.InnerAlpha * blend;
        if (blend <= 0)
        {
            u = pad + bx;
            v = pad + by;
            alpha = 0.96;
            return;
        }
        var normal = IslandOutline.Normal(x, y, w, h, radius);
        double along = IslandOutline.Smootherstep(0, 1, 1 + distance / band);
        double depth = (4 + 3.5 * along + Math.Clamp(surface, -1, 1) * LiquidRefraction.HeightWarp) * dpi;
        double ex = x + normal.Nx * (depth - distance) + slopeX / (1 + Math.Abs(slopeX)) * LiquidRefraction.SlopeWarp * dpi;
        double ey = y + normal.Ny * (depth - distance) + slopeY / (1 + Math.Abs(slopeY)) * LiquidRefraction.SlopeWarp * dpi;
        u = pad + bx + (ex - bx) * blend;
        v = pad + by + (ey - by) * blend;
        alpha = lensAlpha;
    }
}
