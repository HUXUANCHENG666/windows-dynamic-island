namespace DynamicIsland.Core.Motion;

/// <summary>
/// 形状用弹簧走，内容用这些曲线交叉淡入。
/// 在形变最快的那一段，两边都是模糊的；快落到目标时，新内容才变清楚。
/// </summary>
public static class Easing
{
    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    public static double InverseLerp(double a, double b, double value)
    {
        double d = b - a;
        if (Math.Abs(d) < 1e-8)
            return value >= b ? 1 : 0;
        return Clamp01((value - a) / d);
    }

    public static double SmoothStep(double t)
    {
        t = Clamp01(t);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Expanded content opacity from the expand spring (0 compact, 1 expanded, may overshoot).</summary>
    public static double ExpandFade(double expand) => SmoothStep(InverseLerp(0.18, 0.80, expand));

    /// <summary>Compact content opacity. Falls off as the island opens.</summary>
    public static double CollapseFade(double expand) => SmoothStep(InverseLerp(0.82, 0.20, expand));

    public static double BlurFor(double opacity) => (1 - Clamp01(opacity)) * 5.5;

    public static double ExpandedShift(double expand) => (1 - Clamp01(expand)) * 12;

    public static double CompactShift(double expand) => Clamp01(expand) * -6;

    /// <summary>
    /// 内容进来时从 0.94 落到 1。弹簧过冲最多再抬到大约 1.04，不另做一套动画。
    /// </summary>
    public static double EntranceScale(double presence)
    {
        double t = Clamp01(presence);
        double over = presence - 1;
        if (over < 0)
            over = 0;
        else if (over > 0.08)
            over = 0.08;
        return 0.94 + 0.06 * t + over * 0.5;
    }
}
