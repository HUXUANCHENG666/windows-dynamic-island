namespace DynamicIsland.Core.Island;

public enum GlassRenderPath
{
    Cpu,
    Gpu
}

/// <summary>
/// 四周捕获比较贵，不能挡在指针和展开动画前面；但拖动和变形时视觉仍要跟着走。
/// 先更新位置和形状，捕获放到后面；扭曲和高光优先走 GPU，显卡不可用时回退 CPU。
/// </summary>
public static class GlassWork
{
    /// <summary>拖动、甩出时完整屏幕捕获的最短间隔（秒）。</summary>
    public const double MovingCaptureSeconds = 1.0 / 60;

    /// <summary>停稳后完整捕获的最短间隔（秒）。</summary>
    public const double IdleCaptureSeconds = 1.0 / 60;

    /// <summary>展开/收起改变尺寸时，上一次捕获一结束就再抓。</summary>
    public const double ResizeCaptureSeconds = 0;

    public const double CaptureSlackSeconds = 0.00075; // 避免 16.66ms 时钟抖动误变隔帧捕获。

    public const int IdleOutputLongSide = 640;
    public const int MovingOutputLongSide = 640;
    public const int CpuLiveOutputLongSide = 384; // full interior five-tap cost: bound CPU fallback
    public const int GpuOutputLongSide = 1024;

    /// <summary>有可用显卡时优先走 GPU 折射和高光。</summary>
    public static bool PreferGpu => true;

    /// <summary>位置必须先 1:1 更新，绝不能等捕获做完。</summary>
    public static bool PositionDependsOnCapture => false;

    /// <summary>位置和展开弹簧也不能等 GPU 画完。</summary>
    public static bool PositionDependsOnGpu => false;

    public static GlassRenderPath ChoosePath(bool gpuAvailable) =>
        PreferGpu && gpuAvailable ? GlassRenderPath.Gpu : GlassRenderPath.Cpu;

    /// <summary>拖动、展开或尺寸变化时仍然要刷新流体、高光和折射。</summary>
    public static bool ShouldRefreshVisuals(bool moving) =>
        ShouldRefreshVisuals(moving, resizing: false);

    public static bool ShouldRefreshVisuals(bool moving, bool resizing) => true;

    /// <summary>完整屏幕捕获可以降频，但不能在移动或展开时完全关掉；已有捕获在飞则不再开新的。</summary>
    public static bool ShouldCapture(bool moving, double secondsSinceCapture, bool captureInFlight) =>
        ShouldCapture(moving, resizing: false, secondsSinceCapture, captureInFlight);

    public static bool ShouldCapture(bool moving, bool resizing, double secondsSinceCapture, bool captureInFlight)
    {
        if (captureInFlight)
            return false;
        double interval = resizing
            ? ResizeCaptureSeconds
            : moving ? MovingCaptureSeconds : IdleCaptureSeconds;
        return secondsSinceCapture + CaptureSlackSeconds >= interval;
    }

    /// <summary>没有新画面时，用上一帧周围图按当前形状再扭曲；尺寸不同也可以先映射过去。</summary>
    public static bool ShouldReuseCapture(bool hasCachedCapture) => hasCachedCapture;

    public static int OutputLongSide(bool moving) =>
        moving ? MovingOutputLongSide : IdleOutputLongSide;
}
