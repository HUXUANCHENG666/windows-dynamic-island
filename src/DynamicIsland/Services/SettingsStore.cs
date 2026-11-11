using DynamicIsland.Core.Settings;
using IOPath = System.IO.Path;

namespace DynamicIsland.Services;

static class SettingsStore
{
    public static string FilePath => IOPath.Combine(Log.DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            var settings = SettingsJson.Deserialize(File.ReadAllText(FilePath));
            settings.HideProcesses ??= new();
            settings.Docks ??= new();
            settings.Timer ??= new();
            return settings;
        }
        catch (Exception ex)
        {
            Log.Error("settings", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Log.DirectoryPath);
            File.WriteAllText(FilePath, SettingsJson.Serialize(settings));
        }
        catch (Exception ex)
        {
            Log.Error("settings", ex);
        }
    }
}
