namespace DynamicIsland.Core.Formatting;

public static class TimeFormat
{
    public static string Media(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        int total = (int)t.TotalSeconds;
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        return $"{total / 60}:{total % 60:00}";
    }

    public static string Stopwatch(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        int cs = t.Milliseconds / 10;
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{cs:00}";
    }

    /// <summary>Ceil to the next second so a countdown doesn't show 00:00 before it actually ends.</summary>
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds - 1e-9);
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    public static double Progress(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return 0;
        return Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
    }

    public static string PrettyAppId(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
            return "";
        var s = aumid.Replace('/', '\\');
        int slash = s.LastIndexOf('\\');
        if (slash >= 0)
            s = s[(slash + 1)..];
        int bang = s.LastIndexOf('!');
        if (bang >= 0 && bang < s.Length - 1)
            s = s[(bang + 1)..];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            s = s[..^4];
        return s;
    }
}
