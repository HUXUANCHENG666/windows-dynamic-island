namespace DynamicIsland.Core.Motion;

/// <summary>
/// 手感只改这里。Island 是岛本体（宽、高、圆角、展开进度）共用的一套物理参数，
/// 这样它始终像同一个物体，而不是每条边各弹各的。
/// ζ ≈ 0.70：过冲大约 4%～5%，大约半秒内落定。
/// Presence 接近临界阻尼，透明度不该来回晃。
/// </summary>
public readonly record struct SpringConfig(double Stiffness, double Damping)
{
    public static SpringConfig Island { get; } = new(170, 18.3);

    public static SpringConfig Presence { get; } = new(240, 31);

    public static SpringConfig Appear { get; } = new(210, 29);
}
