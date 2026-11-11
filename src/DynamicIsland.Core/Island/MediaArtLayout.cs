namespace DynamicIsland.Core.Island;

/// <summary>
/// 展开的播放器里，封面格子怎么摆。
/// 横着停靠时封面在左边，文字在右边，这是三列：封面、空隙、文字。
/// 竖着停靠时如果文字那一列还留着星号宽度，封面就只在左半边里居中，右边会空出一块。
/// 所以竖排必须把后面的列收成 0，并让封面和文字横跨整行，封面再在整张卡片里居中。
/// </summary>
public static class MediaArtLayout
{
    public const double HorizontalSize = 104;
    public const double VerticalSize = 132;

    public readonly record struct Spec(
        int ArtColumn,
        int ArtSpan,
        int ArtRow,
        int MetaColumn,
        int MetaSpan,
        int MetaRow,
        bool TrailingColumnCollapsed,
        bool CenterArt,
        double ArtSize);

    public static Spec For(IslandOrientation orientation) =>
        orientation == IslandOrientation.Vertical
            ? new(0, 3, 0, 0, 3, 2, true, true, VerticalSize)
            : new(0, 1, 0, 2, 1, 0, false, false, HorizontalSize);

    public static Spec For(DockAnchor anchor) => For(Dock.OrientationOf(anchor));

    /// <summary>封面格子左边缘，相对播放器内容区。竖排居中，横排贴在左边，不往中间偏。</summary>
    public static double ArtLeft(IslandOrientation orientation, double contentWidth)
    {
        var spec = For(orientation);
        if (contentWidth <= 0 || double.IsNaN(contentWidth))
            return 0;
        if (!spec.CenterArt)
            return 0;
        return (contentWidth - spec.ArtSize) / 2.0;
    }

    public static double ArtCenterX(IslandOrientation orientation, double contentWidth)
    {
        var spec = For(orientation);
        return ArtLeft(orientation, contentWidth) + spec.ArtSize / 2.0;
    }
}
