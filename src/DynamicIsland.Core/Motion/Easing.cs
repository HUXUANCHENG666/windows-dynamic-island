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

    public static double ExpandedShift(double expand) => (1 - Clamp01(expand)) * 10;

    public static double CompactShift(double expand) => Clamp01(expand) * -5;
}
