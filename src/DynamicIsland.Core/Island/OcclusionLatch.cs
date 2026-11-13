namespace DynamicIsland.Core.Island;

/// <summary>
/// 全屏要稳住一会儿才隐身。打开窗口、切换窗口时会有一两下“看起来铺满了屏幕”，
/// 那一下不能收成小点，否则岛会弹出去再缩回来。
/// 退出全屏则马上恢复，不做延迟。
/// </summary>
public sealed class OcclusionLatch
{
    /// <summary>比窗口打开动画长，比人眼等待全屏短。</summary>
    public static readonly TimeSpan EngageAfter = TimeSpan.FromMilliseconds(420);

    public bool Engaged { get; private set; }

    DateTime? _coverSince;

    public bool Observe(bool rawCover, DateTime now)
    {
        if (!rawCover)
        {
            _coverSince = null;
            Engaged = false;
            return false;
        }

        _coverSince ??= now;
        if (Engaged || now - _coverSince.Value >= EngageAfter)
            Engaged = true;
        return Engaged;
    }
}
