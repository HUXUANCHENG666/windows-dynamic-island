using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland.Core.Settings;

public sealed class TimerState
{
    public string Mode { get; set; } = "stopwatch";
    public double CountdownMinutes { get; set; } = 25;
    public double AccumulatedSeconds { get; set; }
    public long? RunningSinceUnixMs { get; set; }
    public bool Completed { get; set; }
    public bool Pinned { get; set; }
}

public sealed class MonitorDock
{
    public string DeviceName { get; set; } = "";
    public string Anchor { get; set; } = "TopCenter";
}

public sealed class AppSettings
{
    public string? MonitorDeviceName { get; set; }
    public TimerState Timer { get; set; } = new();
    public bool Stealth { get; set; }
    public List<string> HideProcesses { get; set; } = new();
    public List<MonitorDock> Docks { get; set; } = new();
    public string TranslateDirection { get; set; } = "auto";

    /// <summary>用户希望在岛上显示通知中心。默认关，允许之后才会去读。</summary>
    public bool NotificationCenter { get; set; }

    /// <summary>第一次打开时已经在岛上说明过权限。拒绝之后不再重复那句介绍。</summary>
    public bool NotificationCenterExplained { get; set; }

    /// <summary>关注的应用弹出新窗口时在岛上提醒。默认关。</summary>
    public bool PopupAlerts { get; set; }

    /// <summary>要提醒的程序，存归一化后的进程名（小写、不含 .exe）。</summary>
    public List<string> AlertProcesses { get; set; } = new();
}

public static class SettingsJson
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    public static AppSettings Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
