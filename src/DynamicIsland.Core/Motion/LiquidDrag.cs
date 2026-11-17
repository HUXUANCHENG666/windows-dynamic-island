namespace DynamicIsland.Core.Motion;

/// <summary>
/// 把拖动速度变成水滴一样的拉伸：沿运动方向拉长、垂直方向压扁，面积近似不变。
/// 松手时额外给弹簧一个回弹速度，才能看出 Q 弹，而不是只有几个像素的过冲。
/// </summary>
public static class LiquidDrag
{
    /// <summary>低于这个速度（像素/秒）时保持原形，免得轻轻挪一下也变形。</summary>
    public const double MinSpeed = 90;

    /// <summary>大约到这个速度时接近最大拉伸。</summary>
    public const double ReferenceSpeed = 1600;

    /// <summary>沿运动方向最多拉到这么长。</summary>
    public const double MaxStretch = 1.16;

    /// <summary>
    /// 比岛本体更有弹性：ζ ≈ 0.50，过冲大约 16%。
    /// 再配合 <see cref="ReleaseKick"/>，松手后会明显地弹一下再落定。
    /// </summary>
    public static SpringConfig Bounce { get; } = new(190, 13.8);

    public static double Speed(double vx, double vy) => Math.Sqrt(vx * vx + vy * vy);

    public static (double Along, double Perp) Stretch(double vx, double vy, double maxAlong)
    {
        double speed = Speed(vx, vy);
        if (speed < MinSpeed)
            return (1, 1);

        double cap = Math.Clamp(maxAlong, 1.01, MaxStretch);
        double t = Math.Clamp((speed - MinSpeed) / (ReferenceSpeed - MinSpeed), 0, 1);
        t = 1 - (1 - t) * (1 - t);
        double along = 1 + (cap - 1) * t;
        return (along, 1 / along);
    }

    /// <summary>
    /// 把沿运动方向的拉伸拆成窗口的宽、高缩放。
    /// 岛是停靠在边上的，不做旋转拉伸，免得从边缘甩出去。
    /// </summary>
    public static (double ScaleX, double ScaleY) AxisScale(double vx, double vy, double maxX, double maxY)
    {
        double maxAlong = Math.Min(MaxStretch, Math.Min(maxX, maxY));
        var (along, perp) = Stretch(vx, vy, maxAlong);
        if (Math.Abs(along - 1) < 0.0005)
            return (1, 1);

        double speed = Speed(vx, vy);
        double ax = Math.Abs(vx) / speed;
        double ay = Math.Abs(vy) / speed;
        double scaleX = perp + (along - perp) * ax;
        double scaleY = perp + (along - perp) * ay;
        scaleX = Math.Min(scaleX, maxX);
        scaleY = Math.Min(scaleY, maxY);
        return (scaleX, scaleY);
    }

    /// <summary>
    /// 松手时朝静止目标额外推一把，让从 1.16 拉回 1 时能越过目标大约 8%～12%。
    /// </summary>
    public static double ReleaseKick(double value, double target = 1) =>
        (target - value) * 11;

    /// <summary>
    /// 以岛中心为原点缩放时，窗口里还能留出多少余量。
    /// 实际使用时会改成从停靠边生长，这里给出每个轴的上限。
    /// </summary>
    public static (double MaxX, double MaxY) MaxFit(double pillW, double pillH, double roomX, double roomY)
    {
        double maxX = pillW <= 1 ? MaxStretch : Math.Max(1.01, (pillW + Math.Max(0, roomX)) / pillW);
        double maxY = pillH <= 1 ? MaxStretch : Math.Max(1.01, (pillH + Math.Max(0, roomY)) / pillH);
        return (Math.Min(MaxStretch, maxX), Math.Min(MaxStretch, maxY));
    }

    public static (double OriginX, double OriginY) TransformOrigin(bool top, bool bottom, bool left, bool right)
    {
        double x = 0.5;
        double y = 0.5;
        if (left && !right)
            x = 0;
        else if (right && !left)
            x = 1;
        if (top && !bottom)
            y = 0;
        else if (bottom && !top)
            y = 1;
        return (x, y);
    }
}
