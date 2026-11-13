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

    public const double IdleExpandedW = 392;
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

    public const double ToastW = 400;
    public const double ToastH = 120;

    public const double TranslateW = 400;
    public const double TranslateH = 176;

    public const double CameraW = 392;
    public const double CameraH = 248;

    public const double VerticalWindowWidth = 420;
    public const double VerticalWindowHeight = 560;

    public const double VIdleCompactW = 37;
    public const double VIdleCompactH = 130;
    public const double VIdleExpandedW = 210;
    public const double VIdleExpandedH = 292;

    public const double VMediaCompactW = 56;
    public const double VMediaCompactH = 188;
    public const double VMediaExpandedW = 248;
    public const double VMediaExpandedH = 456;

    public const double VTimerCompactW = 56;
    public const double VTimerCompactH = 156;
    public const double VTimerExpandedW = 230;
    public const double VTimerExpandedH = 392;

    public const double VVolumeW = 176;
    public const double VVolumeH = 210;

    public const double VBatteryW = 176;
    public const double VBatteryH = 196;

    public const double VToastW = 220;
    public const double VToastH = 268;

    public const double VTranslateW = 236;
    public const double VTranslateH = 340;

    public const double VCameraW = 248;
    public const double VCameraH = 420;

    public static readonly IslandFrame StealthDot = new(12, 12, 6, 2);

    public static (double Width, double Height) HostDip(IslandOrientation orientation) =>
        orientation == IslandOrientation.Vertical
            ? (VerticalWindowWidth, VerticalWindowHeight)
            : (WindowWidth, WindowHeight);

    public static IslandFrame FrameFor(IslandMode mode, bool expanded) =>
        FrameFor(mode, expanded, IslandOrientation.Horizontal);

    public static IslandFrame FrameFor(IslandMode mode, bool expanded, IslandOrientation orientation) =>
        orientation == IslandOrientation.Vertical ? Vertical(mode, expanded) : Horizontal(mode, expanded);

    static IslandFrame Horizontal(IslandMode mode, bool expanded) => mode switch
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
        IslandMode.Translate => Card(TranslateW, TranslateH, 42, 4.1),
        IslandMode.Camera => Card(CameraW, CameraH, 48, 4.4),
        _ => Pill(IdleCompactW, IdleCompactH)
    };

    static IslandFrame Vertical(IslandMode mode, bool expanded) => mode switch
    {
        IslandMode.Idle => expanded
            ? Card(VIdleExpandedW, VIdleExpandedH, 40, 4.25)
            : Capsule(VIdleCompactW, VIdleCompactH),
        IslandMode.Media => expanded
            ? Card(VMediaExpandedW, VMediaExpandedH, 44, 4.5)
            : Capsule(VMediaCompactW, VMediaCompactH),
        IslandMode.Timer => expanded
            ? Card(VTimerExpandedW, VTimerExpandedH, 42, 4.4)
            : Capsule(VTimerCompactW, VTimerCompactH),
        IslandMode.Volume => Capsule(VVolumeW, VVolumeH),
        IslandMode.Battery => Capsule(VBatteryW, VBatteryH),
        IslandMode.Toast => Card(VToastW, VToastH, 36, 3.3),
        IslandMode.Translate => Card(VTranslateW, VTranslateH, 40, 4.1),
        IslandMode.Camera => Card(VCameraW, VCameraH, 44, 4.4),
        _ => Pill(VIdleCompactW, VIdleCompactH)
    };

    static IslandFrame Pill(double w, double h) => Capsule(w, h);

    static IslandFrame Capsule(double w, double h) => new(w, h, Math.Min(w, h) / 2.0, 2.0);

    static IslandFrame Card(double w, double h, double radius, double n) =>
        new(w, h, Math.Min(radius, Math.Min(w, h) / 2.0), n);
}
