namespace DynamicIsland.Core.Island;

/// <summary>
/// 四周捕获和折射扭曲比较贵，不能挡在拖动前面。
/// 指针移动时必须先 1:1 更新岛的位置；重计算放到位置更新之后，拖动期间可以先不做。
/// </summary>
public static class GlassWork
{
    /// <summary>拖动、甩出或滑行时不做屏幕捕获，避免卡住指针、把松手速度抹掉。</summary>
    public static bool ShouldCapture(bool moving) => !moving;
}
