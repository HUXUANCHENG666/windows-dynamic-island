namespace DynamicIsland.Core.Motion;

/// <summary>
/// 异步光栅结果与捕获垫边的几何策略。
/// 快拖时用当前几何重采样最近实况垫边，不把旧位图拉伸到新尺寸，也不因位移就把玻璃关掉。
/// </summary>
public readonly record struct GlassFrameGeometry(int Width, int Height, int Left, int Top, double ScaleX, double ScaleY)
{
    public const double SizeTolerancePx = 3;
    /// <summary>垫边内容的硬上限；超过则不再当作当前桌面。位移本身不使玻璃消失。</summary>
    public const double MaxResampleAgeMs = 350;
    public const double RimGuardPx = 12;

    public bool Matches(GlassFrameGeometry other, double tolerance = SizeTolerancePx) =>
        MatchesSize(other, tolerance)
        && Math.Abs(Left - other.Left) <= tolerance && Math.Abs(Top - other.Top) <= tolerance;

    public bool MatchesSize(GlassFrameGeometry other, double tolerance = SizeTolerancePx) =>
        Math.Abs(Width - other.Width) <= tolerance && Math.Abs(Height - other.Height) <= tolerance
        && Math.Abs(Width * (ScaleX - other.ScaleX)) <= tolerance
        && Math.Abs(Height * (ScaleY - other.ScaleY)) <= tolerance;

    /// <summary>已废弃的 100ms 整帧丢弃阈值不再使用。保留名称以免外部误读旧测试含义。</summary>
    public static bool IsFresh(double ageMilliseconds) => CanResample(ageMilliseconds);

    public static bool CanResample(double ageMilliseconds) =>
        ageMilliseconds >= 0 && ageMilliseconds <= MaxResampleAgeMs;

    /// <summary>显示中的位图：仅当输出尺寸/缩放会明显拉伸时才撤下；位置与帧龄不能让玻璃变全透明。</summary>
    public static bool ShouldKeepDisplayedBitmap(GlassFrameGeometry shown, GlassFrameGeometry view, double dpi) =>
        shown.MatchesSize(view, SizeTolerancePx * Math.Max(0.5, dpi));

    /// <summary>提交已按某几何光栅的结果：尺寸必须对上；位置差用下一帧重采样修正，不能因此丢弃。</summary>
    public static bool ShouldPresent(GlassFrameGeometry ready, GlassFrameGeometry view, double dpi) =>
        ready.MatchesSize(view, SizeTolerancePx * Math.Max(0.5, dpi));

    /// <summary>
    /// 捕获矩形（含 pad 边）是否仍盖住当前岛加一圈折射护边。
    /// 不完全覆盖时仍可重采样，越界样本为 0，只是边缘可能缺一块，不应整幅弃用。
    /// </summary>
    public static bool Covers(
        int padLeft, int padTop, int padW, int padH, int padMargin,
        int viewLeft, int viewTop, int viewW, int viewH,
        double scaleX = 1, double scaleY = 1, double guard = RimGuardPx)
    {
        scaleX = scaleX == 0 ? 1 : scaleX;
        scaleY = scaleY == 0 ? 1 : scaleY;
        double visW = Math.Max(1, viewW * scaleX);
        double visH = Math.Max(1, viewH * scaleY);
        return viewLeft - guard >= padLeft - padMargin
            && viewTop - guard >= padTop - padMargin
            && viewLeft + visW + guard <= padLeft + padW + padMargin
            && viewTop + visH + guard <= padTop + padH + padMargin;
    }
}
