namespace DynamicIsland.Core.Island;

public readonly record struct IslandFrame(double Width, double Height, double Radius, double CornerN);

/// <summary>
/// 所有尺寸的唯一来源，单位是 DIP（96 DPI 下的像素）。
/// 紧凑态是真胶囊（半径 = 高度的一半，n = 2）。
/// 展开的音乐卡用更大的连续圆角。音量和电量保持胶囊，像 iOS 的小型提示。
/// 顶边锚定：窗口里的岛从 TopMargin 往下长，左右从中心张开。
/// </summary>
public static class IslandMetrics
{
    public const double WindowWidth = 760;
    public const double WindowHeight = 360;
    public const double TopMargin = 8;
    public const double HitSlop = 14;

    public const double IdleCompactW = 130;
    public const double IdleCompactH = 37;

    public const double IdleExpandedW = 312;
    public const double IdleExpandedH = 140;

    public const double MediaCompactW = 188;
    public const double MediaCompactH = 44;

    public const double MediaExpandedW = 428;
    public const double MediaExpandedH = 184;

    public const double TimerCompactW = 168;
    public const double TimerCompactH = 44;

    public const double TimerExpandedW = 340;
    public const double TimerExpandedH = 172;

    public const double VolumeW = 316;
    public const double VolumeH = 76;

    public const double BatteryW = 316;
    public const double BatteryH = 76;

    public const double ToastW = 372;
    public const double ToastH = 96;

    public static IslandFrame FrameFor(IslandMode mode, bool expanded) => mode switch
    {
        IslandMode.Idle => expanded
            ? Card(IdleExpandedW, IdleExpandedH, 46, 4.25)
            : Pill(IdleCompactW, IdleCompactH),
        IslandMode.Media => expanded
            ? Card(MediaExpandedW, MediaExpandedH, 52, 4.5)
            : Pill(MediaCompactW, MediaCompactH),
        IslandMode.Timer => expanded
            ? Card(TimerExpandedW, TimerExpandedH, 50, 4.4)
            : Pill(TimerCompactW, TimerCompactH),
        IslandMode.Volume => Pill(VolumeW, VolumeH),
        IslandMode.Battery => Pill(BatteryW, BatteryH),
        IslandMode.Toast => Card(ToastW, ToastH, 40, 3.3),
        _ => Pill(IdleCompactW, IdleCompactH)
    };

    static IslandFrame Pill(double w, double h) => new(w, h, h / 2.0, 2.0);

    static IslandFrame Card(double w, double h, double radius, double n) =>
        new(w, h, Math.Min(radius, h / 2.0), n);
}
