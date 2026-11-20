namespace DynamicIsland.Core.Island;

/// <summary>
/// 四周捕获比较贵，不能挡在指针前面；但拖动时视觉仍要跟着走。
/// 先更新位置和流体，捕获放到后面、可以降频，并用上一帧周围图按当前形状再扭曲。
/// </summary>
public static class GlassWork
{
    /// <summary>拖动、甩出时完整屏幕捕获的最短间隔（秒）。</summary>
    public const double MovingCaptureSeconds = 0.05;

    /// <summary>停稳后完整捕获的最短间隔（秒）。</summary>
    public const double IdleCaptureSeconds = 0.016;

    public const int IdleOutputLongSide = 280;
    public const int MovingOutputLongSide = 168;

    /// <summary>位置必须先 1:1 更新，绝不能等捕获做完。</summary>
    public static bool PositionDependsOnCapture => false;

    /// <summary>拖动时仍然要刷新流体、高光和折射（可用缓存的周围图）。</summary>
    public static bool ShouldRefreshVisuals(bool moving) => true;

    /// <summary>完整屏幕捕获可以降频，但不能在移动时完全关掉；已有捕获在飞则不再开新的。</summary>
    public static bool ShouldCapture(bool moving, double secondsSinceCapture, bool captureInFlight)
    {
        if (captureInFlight)
            return false;
        double interval = moving ? MovingCaptureSeconds : IdleCaptureSeconds;
        return secondsSinceCapture >= interval;
    }

    /// <summary>没有新画面时，用上一帧周围图按当前形状和速度再扭曲一次。</summary>
    public static bool ShouldReuseCapture(bool hasCachedCapture) => hasCachedCapture;

    public static int OutputLongSide(bool moving) =>
        moving ? MovingOutputLongSide : IdleOutputLongSide;
}
