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

public sealed class AppSettings
{
    public string? MonitorDeviceName { get; set; }
    public TimerState Timer { get; set; } = new();
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
