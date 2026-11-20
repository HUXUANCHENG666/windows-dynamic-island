using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Translation;
using DynamicIsland.Shell;

internal static class Program
{
    static int _checks;
    static T Get<T>(SettingsWindow window, string name) where T : class =>
        window.FindName(name) as T ?? throw new Exception($"Missing actual WPF control: {name}");
    static void Check(bool passed, string name)
    {
        if (!passed) throw new Exception("Settings UI regression: " + name);
        _checks++;
    }
    static void Click(SettingsWindow w, string name) => Get<ButtonBase>(w, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    static void Layout(SettingsWindow w, double width, double height)
    {
        var root = Get<FrameworkElement>(w, "FrameRoot");
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        root.UpdateLayout();
    }
    [STAThread]
    public static int Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var host = new MemorySettings();
            var initial = JsonSerializer.Serialize(host.Settings);
            var w = new SettingsWindow(host);
            var nav = Get<ListBox>(w, "Navigation");
            Check(nav.SelectedIndex == 0, "default appearance");
            Check(host.Writes == 0 && host.Actions.Count == 0, "construction performs no writes/actions");
            Check(JsonSerializer.Serialize(host.Settings) == initial, "initialization preserves all settings including API credentials");
            foreach (double dpi in new[] { 1.0, 1.25, 1.5 })
            {
                var size = SettingsPagePolicy.Fit(1024 / dpi, 720 / dpi);
                for (int page = 0; page < 5; page++)
                {
                    nav.SelectedIndex = page;
                    Layout(w, size.Width, size.Height);
                    var scroll = Get<ScrollViewer>(w, "BodyScroll");
                    Check(scroll.ActualWidth >= 260 && scroll.ViewportHeight >= 150, $"usable content viewport page={page}, logical dpi={dpi}");
                    Check(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"no horizontal overflow page={page}, logical dpi={dpi}");
                    var navigationBounds = nav.TransformToAncestor(Get<FrameworkElement>(w, "FrameRoot")).TransformBounds(new Rect(nav.RenderSize));
                    scroll.ScrollToEnd(); Layout(w, size.Width, size.Height);
                    Check(nav.TransformToAncestor(Get<FrameworkElement>(w, "FrameRoot")).TransformBounds(new Rect(nav.RenderSize)) == navigationBounds,
                        $"navigation stays fixed page={page}, logical dpi={dpi}");
                }
            }
            Check(JsonSerializer.Serialize(host.Settings) == initial && host.Writes == 0 && host.Actions.Count == 0, "visiting all pages never mutates settings or starts services");
            nav.SelectedIndex = 1;
            var url = Get<TextBox>(w, "ApiUrl");
            url.Text = "https://draft.invalid";
            Get<TextBox>(w, "ApiModel").Text = "draft-model";
            Get<PasswordBox>(w, "ApiKey").Password = "FAKE-KEY-NOT-A-CREDENTIAL";
            nav.SelectedIndex = 3; w.RefreshFromIsland(); nav.SelectedIndex = 1;
            Check(url.Text == "https://draft.invalid" && host.Writes == 0, "draft survives navigation and activation refresh without autosave");
            url.Text = "http://remote.invalid";
            Click(w, "ApiSave");
            Check(host.Writes == 0 && Get<Button>(w, "ApiSave").IsEnabled, "invalid API cannot save");
            url.Text = "https://draft.invalid";
            Click(w, "ApiSave");
            Check(host.Settings.TranslateApiUrl == url.Text && host.ApiWrites == 1 && !Get<Button>(w, "ApiSave").IsEnabled, "explicit valid API save writes once");
            Get<RadioButton>(w, "ApiDefault").IsChecked = true;
            Click(w, "ApiSave");
            Check(host.Settings.TranslateApi == "default" && host.Settings.TranslateApiKey == "FAKE-KEY-NOT-A-CREDENTIAL", "default mode preserves stored custom fields");
            Get<RadioButton>(w, "ApiCustom").IsChecked = true;
            Click(w, "ApiRevert");
            Check(Get<RadioButton>(w, "ApiDefault").IsChecked == true, "revert restores saved mode");
            Get<Slider>(w, "ScaleSlider").Value = 125;
            Get<RadioButton>(w, "SkinGlass").IsChecked = true;
            Get<RadioButton>(w, "DirZhEn").IsChecked = true;
            Check(host.Settings.IslandScale == 1.25 && host.Settings.IslandSkin == "glass" && host.Settings.TranslateDirection == TranslatePolicy.ToSetting(TranslateDirection.ChineseToEnglish), "appearance and direction apply immediately");
            Click(w, "ScaleReset"); Check(host.Settings.IslandScale == 1, "scale reset only resets scale");
            foreach (RadioButton button in Get<WrapPanel>(w, "DockOptions").Children) button.IsChecked = true;
            Check(host.Anchor == DockAnchor.BottomRight, "all eight docking choices dispatch");
            ((RadioButton)Get<WrapPanel>(w, "MonitorOptions").Children[1]).IsChecked = true;
            Check(host.Settings.MonitorDeviceName == "DISPLAY2", "monitor selection dispatch");
            Click(w, "NoticeToggle"); Click(w, "StealthToggle"); Click(w, "LiveToggle");
            Check(host.Settings.NotificationCenter && host.Settings.Stealth && host.LiveOpen, "existing toggles dispatch");
            nav.SelectedIndex = 3;
            Get<TextBox>(w, "HideName").Text = "NEWAPP.exe"; Click(w, "HideAdd");
            Check(host.Settings.HideProcesses.Contains("newapp"), "add process normalizes and preserves list");
            Get<TextBox>(w, "HideName").Text = "newapp"; Click(w, "HideAdd");
            Check(host.Settings.HideProcesses.Count(p => p == "newapp") == 1, "duplicate rejected");
            var chips = Get<WrapPanel>(w, "HideList").Children.OfType<Button>().ToArray();
            chips.Single(b => ((TextBlock)b.Content).Text.StartsWith("newapp ")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(!host.Settings.HideProcesses.Contains("newapp") && host.Settings.HideProcesses.Contains("existing"), "remove only selected process");
            foreach (string name in new[] { "ActTranslate", "ActRecall", "ActCamera", "ActLiveTranslate", "ActStopwatch", "ActFocus", "ActLog" }) Click(w, name);
            Check(host.Actions.SequenceEqual(new[] { "clipboard", "recall", "camera", "live", "stopwatch", "focus", "log" }), "all seven tool actions retained");
            Check(Get<Button>(w, "ActLog").Focusable && Get<Slider>(w, "ScaleSlider").Focusable && Get<ToggleButton>(w, "LiveToggle").Focusable, "native controls keyboard-focusable");
            url.Text = "https://unsaved.invalid";
            w.Close();
            Check(Get<FrameworkElement>(w, "UnsavedPrompt").Visibility == Visibility.Visible, "dirty close requires explicit decision");
            Click(w, "ContinueEditing");
            Check(url.Text == "https://unsaved.invalid" && nav.IsEnabled, "continue editing preserves draft");
            w.Close(); Click(w, "SaveAndClose");
            Check(host.Settings.TranslateApiUrl == "https://unsaved.invalid", "save-and-close persists draft explicitly");
            var discardHost = new MemorySettings(); var discard = new SettingsWindow(discardHost);
            Get<TextBox>(discard, "ApiUrl").Text = "https://discard.invalid";
            discard.Close(); Click(discard, "DiscardAndClose");
            Check(discardHost.ApiWrites == 0, "explicit discard never writes API");
            app.Shutdown();
            Console.WriteLine($"Settings actual WPF BAML/layout/behavior PASS: {_checks} checks; 100/125/150% equivalent logical workareas. No window shown, no real DPI/input/desktop preview, no network/permissions/services/disk writes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

internal sealed class MemorySettings : ISettingsController
{
    public AppSettings Settings { get; } = new()
    {
        IslandScale = 1.1, IslandSkin = "default", TranslateApi = "custom", TranslateApiUrl = "https://stored.invalid",
        TranslateApiModel = "stored-model", TranslateApiKey = "TEST-KEY-NOT-REAL", HideProcesses = ["existing"]
    };
    public string? SuggestedProcess => "editor";
    public bool CameraOpen => false;
    public bool LiveOpen { get; private set; }
    public bool HasTranslation => true;
    public DockAnchor Anchor { get; private set; }
    public int Writes { get; private set; }
    public int ApiWrites { get; private set; }
    public List<string> Actions { get; } = [];
    public IReadOnlyList<SettingsMonitor> Monitors() => [new("DISPLAY1", "主显示器", true), new("DISPLAY2", "第二显示器", false)];
    public IReadOnlyList<string> RunningProcesses() => ["editor", "browser", "existing"];
    public void Scale(double value) { Settings.IslandScale = value; Writes++; }
    public void Skin(IslandSkin skin) { Settings.IslandSkin = AppearancePolicy.ToSetting(skin); Writes++; }
    public void Direction(TranslateDirection value) { Settings.TranslateDirection = TranslatePolicy.ToSetting(value); Writes++; }
    public void Api(TranslateApiKind kind, string? url, string? model, string? key)
    {
        Settings.TranslateApi = TranslateApiPolicy.ToSetting(kind); Settings.TranslateApiUrl = url;
        Settings.TranslateApiModel = model; Settings.TranslateApiKey = key; Writes++; ApiWrites++;
    }
    public void Notifications() { Settings.NotificationCenter = !Settings.NotificationCenter; Writes++; }
    public void Stealth() { Settings.Stealth = !Settings.Stealth; Writes++; }
    public void Live() { LiveOpen = !LiveOpen; }
    public void Monitor(string? name) { Settings.MonitorDeviceName = name; Writes++; }
    public void Dock(DockAnchor anchor) { Anchor = anchor; Writes++; }
    public void SaveHideList() { Writes++; }
    public void Tool(string action) { Actions.Add(action); }
}
