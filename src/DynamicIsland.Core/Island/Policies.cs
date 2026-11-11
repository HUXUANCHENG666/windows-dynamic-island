namespace DynamicIsland.Core.Island;

public static class AmbientPolicy
{
    /// <summary>
    /// 正在播放的音乐会盖住计时器。暂停的会话会把紧凑位让给已经打开的计时器。
    /// 什么都没有时，岛缩成一颗安静的黑胶囊。
    /// </summary>
    public static AmbientKind Choose(bool mediaPlaying, bool mediaSessionOpen, bool timerPinned)
    {
        if (mediaPlaying)
            return AmbientKind.Media;
        if (timerPinned)
            return AmbientKind.Timer;
        if (mediaSessionOpen)
            return AmbientKind.Media;
        return AmbientKind.Idle;
    }
}

public static class NoticePolicy
{
    public static readonly TimeSpan HoverExpandDelay = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan HoverCollapseDelay = TimeSpan.FromMilliseconds(480);
    public static readonly TimeSpan VolumeHold = TimeSpan.FromMilliseconds(1700);
    public static readonly TimeSpan BatteryHold = TimeSpan.FromMilliseconds(2500);
    public static readonly TimeSpan ToastHold = TimeSpan.FromMilliseconds(3200);
    public static readonly TimeSpan TranslateHold = TimeSpan.FromMilliseconds(8000);
    public static readonly TimeSpan VolumeQueueLife = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan BatteryQueueLife = TimeSpan.FromSeconds(12);
    public static readonly TimeSpan ToastQueueLife = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan TranslateQueueLife = TimeSpan.FromSeconds(12);

    public static TimeSpan Hold(IslandMode mode) => mode switch
    {
        IslandMode.Volume => VolumeHold,
        IslandMode.Battery => BatteryHold,
        IslandMode.Translate => TranslateHold,
        IslandMode.Toast => ToastHold,
        _ => ToastHold
    };

    public static TimeSpan QueueLife(IslandMode mode) => mode switch
    {
        IslandMode.Volume => VolumeQueueLife,
        IslandMode.Battery => BatteryQueueLife,
        IslandMode.Translate => TranslateQueueLife,
        _ => ToastQueueLife
    };

    /// <summary>
    /// 电量不要每掉 1% 都弹一次。只在跌破 20 / 10 / 5，以及每跨过 10% 时说一声。
    /// 充电过程中的百分比上涨不提示，插拔本身会单独提示。
    /// </summary>
    public static bool ShouldAnnounceBatteryPercent(int previous, int current)
    {
        if (previous is < 0 or > 100 || current is < 0 or > 100)
            return false;
        if (current >= previous)
            return false;

        ReadOnlySpan<int> marks = stackalloc int[] { 20, 10, 5 };
        foreach (var mark in marks)
        {
            if (previous > mark && current <= mark)
                return true;
        }

        return current / 10 != previous / 10;
    }
}
