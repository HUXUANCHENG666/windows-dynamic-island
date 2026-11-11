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
    public bool Occluded { get; private set; }
    public bool CameraOpen { get; private set; }

    /// <summary>全屏或隐身时收成小点。提示弹出来的那一会儿不是小点。</summary>
    public bool ShowAsDot => Occluded && !Expanded;

    IslandMode? _notice;
    DateTime _noticeUntil;
    IslandMode? _queued;
    DateTime _queuedAt;
    DateTime? _expandAt;
    DateTime? _collapseAt;

    public bool HasScheduledWork =>
        _expandAt != null || _collapseAt != null || _notice != null || _queued != null;

    public IslandFrame TargetFrame =>
        ShowAsDot ? IslandMetrics.StealthDot : IslandMetrics.FrameFor(Mode, Expanded);

    public static IslandMode Map(AmbientKind kind) => kind switch
    {
        AmbientKind.Media => IslandMode.Media,
        AmbientKind.Timer => IslandMode.Timer,
        _ => IslandMode.Idle
    };

    public void SetAmbient(AmbientKind ambient)
    {
        Ambient = ambient;
        if (_notice is null && Mode != IslandMode.Camera)
            Mode = Map(ambient);
    }

    public void SetOccluded(bool occluded, DateTime now)
    {
        if (Occluded == occluded)
            return;
        Occluded = occluded;
        if (!occluded || _notice is not null || Mode == IslandMode.Camera)
            return;
        Expanded = false;
        _expandAt = null;
        _collapseAt = null;
    }

    public void SetCamera(bool open)
    {
        if (open)
        {
            CameraOpen = true;
            _notice = null;
            Mode = IslandMode.Camera;
            Expanded = true;
            _expandAt = null;
            _collapseAt = null;
            return;
        }

        CameraOpen = false;
        if (Mode != IslandMode.Camera)
            return;
        Mode = Map(Ambient);
        _expandAt = null;
        if (Occluded || !PointerInside)
        {
            Expanded = false;
            _collapseAt = null;
        }
    }

    public void ShowCameraMessage(DateTime now)
    {
        CameraOpen = false;
        _notice = null;
        Mode = IslandMode.Camera;
        Expanded = true;
        _expandAt = null;
        _collapseAt = now + TimeSpan.FromSeconds(6);
    }

    public void PointerEntered(DateTime now)
    {
        PointerInside = true;
        if (Occluded && !Expanded)
            return;
        _collapseAt = null;
        if (Occluded)
            return;
        if (!Expanded)
            _expandAt ??= now + NoticePolicy.HoverExpandDelay;
    }

    public void PointerExited(DateTime now)
    {
        PointerInside = false;
        _expandAt = null;
        if (!Expanded)
            return;
        _collapseAt = now + (Occluded
            ? TimeSpan.FromMilliseconds(240)
            : NoticePolicy.HoverCollapseDelay);
    }

    public void Click(DateTime now)
    {
        if (Occluded && !Expanded)
            return;
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
        if (mode is not (IslandMode.Volume or IslandMode.Battery or IslandMode.Toast or IslandMode.Translate))
            throw new ArgumentOutOfRangeException(nameof(mode));

        if (_notice == mode)
        {
            _noticeUntil = now + NoticePolicy.Hold(mode);
            return;
        }

        bool interacting = !Occluded && PointerInside && Expanded && _notice is null
            && Mode is IslandMode.Media or IslandMode.Timer or IslandMode.Idle or IslandMode.Camera;
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
            if (Occluded || !PointerInside)
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
