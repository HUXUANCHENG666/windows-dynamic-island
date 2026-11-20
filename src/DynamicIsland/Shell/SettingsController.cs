using System.Diagnostics;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Translation;
using DynamicIsland.Native;
using DynamicIsland.Services;

namespace DynamicIsland.Shell;

internal sealed record SettingsMonitor(string DeviceName, string Label, bool Primary);
internal interface ISettingsController
{
    AppSettings Settings { get; }
    string? SuggestedProcess { get; }
    bool CameraOpen { get; }
    bool LiveOpen { get; }
    bool HasTranslation { get; }
    DockAnchor Anchor { get; }
    IReadOnlyList<SettingsMonitor> Monitors();
    IReadOnlyList<string> RunningProcesses();
    void Scale(double value);
    void Skin(IslandSkin skin);
    void Direction(TranslateDirection direction);
    void Api(TranslateApiKind kind, string? url, string? model, string? key);
    void Notifications();
    void Stealth();
    void Live();
    void Monitor(string? name);
    void Dock(DockAnchor anchor);
    void SaveHideList();
    void Tool(string action);
}

internal sealed class IslandSettingsController(IslandWindow island) : ISettingsController
{
    public AppSettings Settings => island.Settings;
    public string? SuggestedProcess => island.ForeignProcess;
    public bool CameraOpen => island.CameraIsOpen;
    public bool LiveOpen => island.LiveTranslateIsOpen;
    public bool HasTranslation => island.HasStoredTranslation;
    public DockAnchor Anchor => island.CurrentAnchor;
    public void Scale(double value) => island.SetIslandScale(value);
    public void Skin(IslandSkin skin) => island.SetIslandSkin(skin);
    public void Direction(TranslateDirection direction) => island.SetTranslateDirection(direction);
    public void Api(TranslateApiKind kind, string? url, string? model, string? key) => island.SetTranslateApi(kind, url, model, key);
    public void Notifications() => island.ToggleNotificationCenter();
    public void Stealth() => island.ToggleStealth();
    public void Live() => island.ToggleLiveTranslate();
    public void Monitor(string? name) => island.SelectMonitor(name);
    public void Dock(DockAnchor anchor) => island.SetDockAnchor(anchor);
    public void SaveHideList() { SettingsStore.Save(Settings); island.RefreshHideList(); }
    public IReadOnlyList<SettingsMonitor> Monitors() => MonitorEnumerator.Enumerate()
        .Select(m => new SettingsMonitor(m.DeviceName, m.Label, m.IsPrimary)).ToArray();
    public IReadOnlyList<string> RunningProcesses()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try { if (process.MainWindowHandle != IntPtr.Zero) names.Add(process.ProcessName); }
            catch { /* 受保护进程不读取。 */ }
            finally { process.Dispose(); }
        }
        return names;
    }
    public void Tool(string action)
    {
        switch (action)
        {
            case "clipboard": island.TranslateClipboard(); break;
            case "recall": island.ShowStoredTranslation(); break;
            case "camera": island.ToggleCamera(); break;
            case "live": island.ToggleLiveTranslate(); break;
            case "stopwatch": island.OpenStopwatch(); break;
            case "focus": island.OpenCountdown(25); break;
            case "log": island.OpenLog(); break;
        }
    }
}
