namespace DynamicIsland.Core.LiveTranslate;

/// <summary>
/// 字幕列表要不要跟着滚到最新一句。用户已经在底部或接近底部时跟上；
/// 已经上滑回看历史时不要抢视图。行内更新（同一句变长）也走同一条规则。
/// </summary>
public static class CaptionScrollPolicy
{
    /// <summary>离底部还有这么多像素时，仍当作贴在最新一句上。</summary>
    public const double StickSlack = 28;

    public static bool IsAtEnd(double offset, double viewport, double extent, double slack = StickSlack)
    {
        if (viewport <= 0)
            return true;
        if (extent <= viewport + 0.5)
            return true;
        return offset + viewport >= extent - slack;
    }

    public static double EndOffset(double viewport, double extent) =>
        Math.Max(0, extent - Math.Max(0, viewport));
}
