namespace DynamicIsland.Core.Media;

/// <summary>
/// SMTC 不会每个画面都推一次进度。这里记下上次的位置和时间，播放中按真实时间外推，暂停时停住。
/// </summary>
public sealed class TimelineEstimator
{
    public TimeSpan Start { get; private set; }
    public TimeSpan End { get; private set; }
    public bool IsPlaying { get; private set; }
    TimeSpan _position;
    DateTime _updatedUtc;

    public bool HasDuration => End > Start && End - Start > TimeSpan.FromMilliseconds(800);

    public void Update(TimeSpan position, TimeSpan start, TimeSpan end, bool isPlaying, DateTime updatedUtc)
    {
        _position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        Start = start < TimeSpan.Zero ? TimeSpan.Zero : start;
        End = end < TimeSpan.Zero ? TimeSpan.Zero : end;
        IsPlaying = isPlaying;
        _updatedUtc = updatedUtc == default ? DateTime.UtcNow : updatedUtc;
    }

    public TimeSpan Estimate(DateTime nowUtc)
    {
        var p = _position;
        if (IsPlaying && nowUtc > _updatedUtc)
            p += nowUtc - _updatedUtc;
        if (p < Start)
            p = Start;
        if (HasDuration && p > End)
            p = End;
        return p;
    }
}
