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

    /// <summary>拖动时水滴形变会让缩放每帧变动，尺寸判定跟着放宽，不然位图会被反复撤下。</summary>
    public const double MovingSizeTolerancePx = 16;

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

    /// <summary>
    /// 显示中的位图：只有逻辑宽高变了（展开、收起）才撤下。
    /// 拖动时水滴缩放的细微变化交给 Image 的 Stretch 吸收——把渲染分辨率差当作"该换帧"，
    /// 会让玻璃在拖动中反复消失再出现，那正是抽动和闪烁的来源。
    /// </summary>
    public static bool ShouldKeepDisplayedBitmap(GlassFrameGeometry shown, GlassFrameGeometry view, double dpi)
    {
        double tolerance = SizeTolerancePx * Math.Max(0.5, dpi);
        return Math.Abs(shown.Width - view.Width) <= tolerance
            && Math.Abs(shown.Height - view.Height) <= tolerance;
    }

    public static double PositionError(GlassFrameGeometry a, GlassFrameGeometry b)
    {
        double dx = a.Left - b.Left, dy = a.Top - b.Top;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 提交光栅结果：尺寸必须对上。位置比当前已显示帧更旧（回退）的结果丢弃，但不能因此把玻璃关成全透明。
    /// 无已显示帧时，接受第一张尺寸匹配的图，保证可见。
    /// </summary>
    public static bool ShouldPresent(GlassFrameGeometry ready, GlassFrameGeometry view, double dpi) =>
        ShouldPresent(ready, view, dpi, shown: default, hasShown: false);

    public static bool ShouldPresent(
        GlassFrameGeometry ready, GlassFrameGeometry view, double dpi,
        GlassFrameGeometry shown, bool hasShown,
        bool readyHasLiveInterior = true, bool shownHasLiveInterior = true,
        bool moving = false)
    {
        double tolerance = (moving ? MovingSizeTolerancePx : SizeTolerancePx) * Math.Max(0.5, dpi);
        if (!ready.MatchesSize(view, tolerance))
            return false;
        if (!hasShown)
            return true;
        // 不要用没有实况内景的帧替换已有活体内景，否则中间会露出黑岛身。
        if (shownHasLiveInterior && !readyHasLiveInterior)
            return false;
        // 已显示的是空内景时，只要新帧有实况就该换上（从变黑中恢复）。
        if (readyHasLiveInterior && !shownHasLiveInterior)
            return true;
        // 0.5px 余量避免整数位置来回抖动时互踢。
        return PositionError(ready, view) <= PositionError(shown, view) + 0.5;
    }

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
