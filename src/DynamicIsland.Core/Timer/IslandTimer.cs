using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Timer;

public enum TimerMode
{
    Stopwatch,
    Countdown
}

public sealed class IslandTimer
{
    readonly Func<DateTime> _clock;
    DateTime? _runningSince;
    TimeSpan _accumulated;
    bool _completed;

    public IslandTimer(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public TimerMode Mode { get; private set; } = TimerMode.Stopwatch;
    public TimeSpan CountdownLength { get; private set; } = TimeSpan.FromMinutes(25);
    public bool IsRunning => _runningSince != null;
    public bool IsCompleted => _completed;

    DateTime Now => _clock();

    public TimeSpan Elapsed
    {
        get
        {
            var t = _accumulated;
            if (_runningSince is DateTime since)
                t += Now - since;
            return t < TimeSpan.Zero ? TimeSpan.Zero : t;
        }
    }

    public TimeSpan Remaining
    {
        get
        {
            var r = CountdownLength - Elapsed;
            return r < TimeSpan.Zero ? TimeSpan.Zero : r;
        }
    }

    public TimeSpan Display => Mode == TimerMode.Stopwatch ? Elapsed : Remaining;

    public void SetMode(TimerMode mode)
    {
        if (Mode == mode)
            return;
        Pause();
        Mode = mode;
        _accumulated = TimeSpan.Zero;
        _runningSince = null;
        _completed = false;
    }

    public void SetCountdownMinutes(double minutes)
    {
        if (minutes < 0.25) minutes = 0.25;
        if (minutes > 600) minutes = 600;
        CountdownLength = TimeSpan.FromMinutes(minutes);
        if (_accumulated > CountdownLength)
            _accumulated = CountdownLength;
        if (Remaining > TimeSpan.Zero)
            _completed = false;
    }

    public void Start()
    {
        if (IsRunning)
            return;
        if (Mode == TimerMode.Countdown && Remaining <= TimeSpan.Zero)
        {
            _accumulated = TimeSpan.Zero;
            _completed = false;
        }

        _runningSince = Now;
    }

    public void Pause()
    {
        if (_runningSince is DateTime since)
        {
            _accumulated += Now - since;
            _runningSince = null;
        }

        if (Mode == TimerMode.Countdown && _accumulated >= CountdownLength)
        {
            _accumulated = CountdownLength;
            _completed = true;
        }
    }

    public void Toggle()
    {
        if (IsRunning) Pause();
        else Start();
    }

    public void Reset()
    {
        _runningSince = null;
        _accumulated = TimeSpan.Zero;
        _completed = false;
    }

    public void AddMinutes(double minutes)
    {
        if (Mode != TimerMode.Countdown)
            return;
        SetCountdownMinutes(CountdownLength.TotalMinutes + minutes);
    }

    /// <summary>Returns true exactly once, on the tick where a countdown reaches zero.</summary>
    public bool ConsumeCompletion()
    {
        if (Mode != TimerMode.Countdown || !IsRunning)
            return false;
        if (Elapsed < CountdownLength)
            return false;

        _accumulated = CountdownLength;
        _runningSince = null;
        if (_completed)
            return false;
        _completed = true;
        return true;
    }

    public TimerState Export() => new()
    {
        Mode = Mode == TimerMode.Countdown ? "countdown" : "stopwatch",
        CountdownMinutes = CountdownLength.TotalMinutes,
        AccumulatedSeconds = _accumulated.TotalSeconds,
        RunningSinceUnixMs = _runningSince is DateTime since
            ? new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : null,
        Completed = _completed
    };

    /// <summary>
    /// Restores a saved timer. Returns true when a countdown finished while the app was closed
    /// and that moment was recent enough to be worth a toast.
    /// </summary>
    public bool Import(TimerState? state, DateTime now, TimeSpan recent = default)
    {
        if (recent == default)
            recent = TimeSpan.FromSeconds(30);
        if (state == null)
            return false;

        Mode = state.Mode == "countdown" ? TimerMode.Countdown : TimerMode.Stopwatch;
        SetCountdownMinutes(state.CountdownMinutes <= 0 ? 25 : state.CountdownMinutes);
        _accumulated = TimeSpan.FromSeconds(Math.Max(0, state.AccumulatedSeconds));
        _completed = state.Completed;
        _runningSince = null;

        if (state.RunningSinceUnixMs is long unix)
        {
            var since = DateTimeOffset.FromUnixTimeMilliseconds(unix).UtcDateTime;
            if (since > now)
                since = now;
            _runningSince = since;
        }

        if (Mode == TimerMode.Countdown && IsRunning && Elapsed >= CountdownLength)
        {
            var finishedAt = _runningSince!.Value + (CountdownLength - _accumulated);
            _accumulated = CountdownLength;
            _runningSince = null;
            _completed = true;
            return now - finishedAt <= recent && now >= finishedAt;
        }

        return false;
    }
}
