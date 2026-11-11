namespace DynamicIsland.Core.Island;

/// <summary>
/// 岛上“现在该显示什么”的状态机。时间由调用方传入，方便测试。
/// 悬停或点击展开；鼠标离开后稍等再收回。
/// 音量、电量、通知会暂时撑开岛。如果你正看着展开的内容，新提示先排队，离开后再出现。
/// </summary>
public sealed class IslandDirector
{
    public AmbientKind Ambient { get; private set; } = AmbientKind.Idle;
    public bool Expanded { get; private set; }
    public IslandMode Mode { get; private set; } = IslandMode.Idle;
    public bool PointerInside { get; private set; }

    IslandMode? _notice;
    DateTime _noticeUntil;
    IslandMode? _queued;
    DateTime _queuedAt;
    DateTime? _expandAt;
    DateTime? _collapseAt;

    public bool HasScheduledWork =>
        _expandAt != null || _collapseAt != null || _notice != null || _queued != null;

    public IslandFrame TargetFrame => IslandMetrics.FrameFor(Mode, Expanded);

    public static IslandMode Map(AmbientKind kind) => kind switch
    {
        AmbientKind.Media => IslandMode.Media,
        AmbientKind.Timer => IslandMode.Timer,
        _ => IslandMode.Idle
    };

    public void SetAmbient(AmbientKind ambient)
    {
        Ambient = ambient;
        if (_notice is null)
            Mode = Map(ambient);
    }

    public void PointerEntered(DateTime now)
    {
        PointerInside = true;
        _collapseAt = null;
        if (!Expanded)
            _expandAt ??= now + NoticePolicy.HoverExpandDelay;
    }

    public void PointerExited(DateTime now)
    {
        PointerInside = false;
        _expandAt = null;
        if (Expanded)
            _collapseAt = now + NoticePolicy.HoverCollapseDelay;
    }

    public void Click(DateTime now)
    {
        PointerInside = true;
        _collapseAt = null;
        _expandAt = null;
        Expanded = true;
    }

    /// <summary>
    /// Tray click. The pointer is down by the clock, not on the island, so this must not
    /// pretend the cursor is inside. A second click while it is open and unhovered closes it.
    /// Otherwise it stays open for a few seconds, or until the pointer actually arrives and leaves.
    /// </summary>
    public void TrayClick(DateTime now)
    {
        if (Expanded && !PointerInside)
        {
            _collapseAt = now;
            return;
        }

        Expanded = true;
        _expandAt = null;
        if (!PointerInside)
            _collapseAt = now + TimeSpan.FromSeconds(5);
    }

    public void PushNotice(IslandMode mode, DateTime now)
    {
        if (mode is not (IslandMode.Volume or IslandMode.Battery or IslandMode.Toast))
            throw new ArgumentOutOfRangeException(nameof(mode));

        if (_notice == mode)
        {
            _noticeUntil = now + NoticePolicy.Hold(mode);
            return;
        }

        bool interacting = PointerInside && Expanded && _notice is null
            && Mode is IslandMode.Media or IslandMode.Timer or IslandMode.Idle;
        if (interacting)
        {
            _queued = mode;
            _queuedAt = now;
            return;
        }

        Present(mode, now);
    }

    public void Tick(DateTime now)
    {
        if (_expandAt is DateTime expandAt && now >= expandAt)
        {
            _expandAt = null;
            if (PointerInside)
                Expanded = true;
        }

        if (_notice is not null && now >= _noticeUntil)
        {
            _notice = null;
            Mode = Map(Ambient);
            if (!PointerInside)
            {
                Expanded = false;
                _collapseAt = null;
                PromoteQueue(now);
            }
            else
            {
                Expanded = true;
            }
        }

        if (_collapseAt is DateTime collapseAt && now >= collapseAt && !PointerInside)
        {
            _collapseAt = null;
            if (_notice != null)
            {
                _notice = null;
                Mode = Map(Ambient);
            }

            Expanded = false;
            PromoteQueue(now);
        }
    }

    void Present(IslandMode mode, DateTime now)
    {
        _notice = mode;
        _noticeUntil = now + NoticePolicy.Hold(mode);
        Mode = mode;
        Expanded = true;
        _expandAt = null;
        _collapseAt = null;
    }

    void PromoteQueue(DateTime now)
    {
        if (_queued is not IslandMode queued)
            return;

        var age = now - _queuedAt;
        _queued = null;
        if (age > NoticePolicy.QueueLife(queued))
            return;

        Present(queued, now);
    }
}
