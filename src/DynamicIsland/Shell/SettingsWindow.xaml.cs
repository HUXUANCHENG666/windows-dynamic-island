using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Translation;
using DynamicIsland.Native;
using DynamicIsland.Services;

namespace DynamicIsland.Shell;

public partial class SettingsWindow : Window
{
    readonly ISettingsController _host;
    readonly string _suggested;
    readonly Dictionary<TranslateApiKind, TranslateApiOptions> _apiDrafts = new();
    TranslateApiKind _draftKind;
    bool _loading = true;
    bool _apiDirty;
    bool _allowClose;

    public SettingsWindow(IslandWindow island) : this(new IslandSettingsController(island)) { }

    // CI 使用纯内存控制器加载同一份 BAML，不启动岛、网络、音频、摄像头或设置文件写入。
    internal SettingsWindow(ISettingsController host)
    {
        _host = host;
        var suggested = HidePolicy.NormalizeProcess(host.SuggestedProcess);
        _suggested = HideListEditor.CanList(suggested) ? suggested : "";
        InitializeComponent();
        BuildPlacement();
        RefreshFromIsland();
        Navigation.SelectedIndex = 0;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            long style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE,
                new IntPtr((style | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW));
            FitToWorkArea();
        };
        DpiChanged += (_, _) => FitToWorkArea();
        Activated += (_, _) => RefreshFromIsland();
        Closing += OnClosing;
    }

    void FitToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        double width = work.Width, height = work.Height;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var rect))
        {
            double cx = (rect.Left + rect.Right) / 2.0, cy = (rect.Top + rect.Bottom) / 2.0;
            var monitor = MonitorEnumerator.Enumerate().OrderBy(m =>
                Math.Pow(Math.Clamp(cx, m.X, m.X + m.Width) - cx, 2) +
                Math.Pow(Math.Clamp(cy, m.Y, m.Y + m.Height) - cy, 2)).FirstOrDefault();
            if (monitor != null)
            {
                width = monitor.WorkWidth * 96.0 / monitor.DpiX;
                height = monitor.WorkHeight * 96.0 / monitor.DpiY;
            }
        }
        // 当前显示器物理工作区换算为 DIP；导航固定，长内容独立滚动。
        var size = SettingsPagePolicy.Fit(width, height);
        MinWidth = Math.Min(620, size.Width);
        MinHeight = Math.Min(400, size.Height);
        MaxWidth = size.Width;
        MaxHeight = size.Height;
        Width = Math.Min(Width, size.Width);
        Height = Math.Min(Height, size.Height);
    }

    void BuildPlacement()
    {
        foreach (var monitor in _host.Monitors())
        {
            var choice = new RadioButton { Content = monitor.Label, Tag = monitor, GroupName = "Monitors", MaxWidth = 300 };
            choice.Checked += Monitor_Changed;
            MonitorOptions.Children.Add(choice);
        }
        foreach (var (anchor, label) in new[] {
            (DockAnchor.TopLeft, "左上"), (DockAnchor.TopCenter, "顶部"), (DockAnchor.TopRight, "右上"),
            (DockAnchor.LeftCenter, "左侧"), (DockAnchor.RightCenter, "右侧"),
            (DockAnchor.BottomLeft, "左下"), (DockAnchor.BottomCenter, "底部"), (DockAnchor.BottomRight, "右下") })
        {
            var choice = new RadioButton { Content = label, Tag = anchor, GroupName = "Dock" };
            choice.Checked += Dock_Changed;
            DockOptions.Children.Add(choice);
        }
    }

    public void RefreshFromIsland()
    {
        _loading = true;
        try
        {
            var settings = _host.Settings;
            ScaleSlider.Value = AppearancePolicy.ToPercent(settings.IslandScale);
            ScaleValue.Text = $"{ScaleSlider.Value:0}%";
            SkinGlass.IsChecked = AppearancePolicy.ParseSkin(settings.IslandSkin) == IslandSkin.Glass;
            SkinDefault.IsChecked = SkinGlass.IsChecked != true;
            var direction = TranslatePolicy.Parse(settings.TranslateDirection);
            DirAuto.IsChecked = direction == TranslateDirection.Auto;
            DirZhEn.IsChecked = direction == TranslateDirection.ChineseToEnglish;
            DirEnZh.IsChecked = direction == TranslateDirection.EnglishToChinese;
            NoticeToggle.IsChecked = settings.NotificationCenter;
            StealthToggle.IsChecked = settings.Stealth;
            LiveToggle.IsChecked = _host.LiveOpen;
            ActLiveTranslate.Content = _host.LiveOpen ? "停止实时翻译" : "实时翻译";
            ActCamera.Content = _host.CameraOpen ? "关闭摄像头" : "摄像头";
            ActRecall.IsEnabled = _host.HasTranslation;
            HideCurrent.IsEnabled = _suggested.Length > 0;
            HideCurrent.Content = _suggested.Length > 0 ? $"添加 {_suggested}" : "无先前应用";
            foreach (RadioButton choice in MonitorOptions.Children)
            {
                var monitor = (SettingsMonitor)choice.Tag;
                choice.IsChecked = string.IsNullOrEmpty(settings.MonitorDeviceName) ? monitor.Primary
                    : string.Equals(settings.MonitorDeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase);
            }
            foreach (RadioButton choice in DockOptions.Children)
                choice.IsChecked = (DockAnchor)choice.Tag == _host.Anchor;
            // 激活、切页和即时操作绝不覆盖未保存接口草稿。
            if (!_apiDirty) ReadApi();
            if (AppsPage.Visibility == Visibility.Visible) FillApps();
        }
        finally { _loading = false; }
    }

    void ReadApi()
    {
        var previous = _loading;
        _loading = true;
        try
        {
            foreach (var provider in Enum.GetValues<TranslateApiKind>()) _apiDrafts[provider] = TranslateProviderSettings.Read(_host.Settings, provider);
            _draftKind = TranslateApiPolicy.Parse(_host.Settings.TranslateApi);
            foreach (var button in new[] { ApiDefault, ApiCustom, ApiLibre, ApiAzure, ApiDeepL })
                button.IsChecked = (string)button.Tag == _draftKind.ToString();
            FillApiDraft();
            _apiDirty = false;
            ApiStatus.Text = "接口配置已保存。切换或编辑后请点击保存。";
            UpdateDirtyStatus();
        }
        finally { _loading = previous; }
    }

    void Navigation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AppearancePage == null || Navigation.SelectedItem is not ListBoxItem item) return;
        var section = (string)item.Tag;
        foreach (var (key, page, title, description) in new[] {
            ("Appearance", AppearancePage, "外观与停靠", "调整岛的比例、材质与桌面位置。"),
            ("Translation", TranslationPage, "翻译", "实时字幕与翻译接口，各自清晰掌控。"),
            ("Notifications", NotificationsPage, "通知", "只接收你选择开启的提醒。"),
            ("Apps", AppsPage, "隐身与应用", "控制何时收起，保持专注。"),
            ("Tools", ToolsPage, "快捷工具", "常用动作，一步直达。") })
        {
            page.Visibility = section == key ? Visibility.Visible : Visibility.Collapsed;
            if (section == key) { PageTitle.Text = title; PageDescription.Text = description; }
        }
        BodyScroll.ScrollToTop();
        if (section == "Apps") FillApps();
    }

    void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _host.Scale(AppearancePolicy.FromPercent(e.NewValue));
        ScaleValue.Text = $"{e.NewValue:0}%";
    }
    void ScaleReset_Click(object sender, RoutedEventArgs e) => ScaleSlider.Value = 100;
    void Skin_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) _host.Skin(SkinGlass.IsChecked == true ? IslandSkin.Glass : IslandSkin.Default);
    }
    void Direction_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && sender is RadioButton { Tag: string tag } && Enum.TryParse<TranslateDirection>(tag, out var direction))
            _host.Direction(direction);
    }
    void Monitor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: SettingsMonitor monitor }) return;
        _host.Monitor(monitor.Primary ? null : monitor.DeviceName);
        RefreshFromIsland();
    }
    void Dock_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && sender is RadioButton { Tag: DockAnchor anchor }) _host.Dock(anchor);
    }
    void Live_Click(object sender, RoutedEventArgs e) { _host.Live(); RefreshFromIsland(); }
    void Notice_Click(object sender, RoutedEventArgs e) { _host.Notifications(); RefreshFromIsland(); }
    void Stealth_Click(object sender, RoutedEventArgs e) { _host.Stealth(); RefreshFromIsland(); }
    void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action }) { _host.Tool(action); RefreshFromIsland(); }
    }

    void CacheApiDraft() => _apiDrafts[_draftKind] = new(_draftKind, ApiUrl.Text, ApiModel.Text, ApiKey.Password, ApiRegion.Text);
    void FillApiDraft()
    {
        var previous = _loading; _loading = true;
        try
        {
            var api = _apiDrafts[_draftKind];
            ApiUrl.Text = api.BaseUrl ?? ""; ApiModel.Text = api.Model ?? ""; ApiKey.Password = api.ApiKey ?? ""; ApiRegion.Text = api.Region ?? "";
            ProviderHint.Text = TranslateProviderSettings.Description(_draftKind);
            ApiFields.Visibility = _draftKind == TranslateApiKind.Default ? Visibility.Collapsed : Visibility.Visible;
            ApiUrl.Visibility = ApiUrlLabel.Visibility = ApiUrlHelp.Visibility = _draftKind is TranslateApiKind.Custom or TranslateApiKind.Libre ? Visibility.Visible : Visibility.Collapsed;
            ApiModel.Visibility = ApiModelLabel.Visibility = _draftKind == TranslateApiKind.Custom ? Visibility.Visible : Visibility.Collapsed;
            ApiRegion.Visibility = ApiRegionLabel.Visibility = _draftKind == TranslateApiKind.Azure ? Visibility.Visible : Visibility.Collapsed;
            ApiKeyLabel.Content = _draftKind == TranslateApiKind.Libre ? "API 密钥（实例未启用密钥时可空）" : "API 密钥";
        }
        finally { _loading = previous; }
    }
    void Api_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag } || !Enum.TryParse<TranslateApiKind>(tag, out var kind)) return;
        CacheApiDraft(); _draftKind = kind; FillApiDraft(); MarkApiDirty();
    }
    void ApiText_Changed(object sender, TextChangedEventArgs e) { if (!_loading) MarkApiDirty(); }
    void ApiKey_Changed(object sender, RoutedEventArgs e) { if (!_loading) MarkApiDirty(); }
    void MarkApiDirty()
    {
        _apiDirty = true;
        ApiStatus.Text = "接口有未保存更改；切换分类不会丢失。";
        UpdateDirtyStatus();
    }
    void UpdateDirtyStatus()
    {
        ApiSave.IsEnabled = ApiRevert.IsEnabled = _apiDirty;
        FooterStatus.Text = _apiDirty ? "接口有未保存更改 · 其他偏好已即时生效" : "偏好更改立即生效 · 接口配置需单独保存";
    }
    bool SaveApi()
    {
        CacheApiDraft();
        var api = _apiDrafts[_draftKind];
        string error = SettingsPagePolicy.ValidateApi(api.Kind, api.BaseUrl, api.Model, api.ApiKey);
        if (error.Length > 0)
        {
            ApiStatus.Text = error;
            Navigation.SelectedIndex = 1;
            BodyScroll.ScrollToBottom();
            return false;
        }
        _host.Api(api, _apiDrafts.Values.ToArray());
        _apiDirty = false;
        ReadApi();
        return true;
    }
    void ApiSave_Click(object sender, RoutedEventArgs e) => SaveApi();
    void ApiRevert_Click(object sender, RoutedEventArgs e) => ReadApi();

    void FillApps()
    {
        HideList.Children.Clear();
        RunningApps.Children.Clear();
        var listed = _host.Settings.HideProcesses ?? [];
        foreach (var process in listed.ToArray())
        {
            var name = HidePolicy.NormalizeProcess(process);
            var button = Chip($"{name}  ×", $"从隐身名单移除 {name}");
            button.Click += (_, _) =>
            {
                if (HideListEditor.Remove(listed, name) != HideListOutcome.Removed) return;
                _host.SaveHideList(); FillApps(); HideStatus.Text = $"已移除 {name}。";
            };
            HideList.Children.Add(button);
        }
        if (listed.Count == 0) HideList.Children.Add(new TextBlock { Text = "尚未添加应用。", Opacity = 0.6 });
        try
        {
            var candidates = HideListEditor.Candidates(_host.RunningProcesses(), listed);
            foreach (var name in candidates)
            {
                var button = Chip(name, $"将 {name} 加入隐身名单");
                button.Click += (_, _) => AddProcess(name);
                RunningApps.Children.Add(button);
            }
            if (candidates.Count == 0) RunningApps.Children.Add(new TextBlock { Text = "暂无可添加的运行窗口。", Opacity = 0.6 });
        }
        catch { HideStatus.Text = "运行列表暂不可用；仍可手动添加。"; }
    }
    static Button Chip(string text, string accessibleName)
    {
        var button = new Button { Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap }, MaxWidth = 280, Margin = new Thickness(0, 0, 8, 8), ToolTip = accessibleName };
        System.Windows.Automation.AutomationProperties.SetName(button, accessibleName);
        return button;
    }
    void AddProcess(string raw)
    {
        var list = _host.Settings.HideProcesses ??= [];
        var outcome = HideListEditor.Add(list, raw);
        HideStatus.Text = outcome switch
        {
            HideListOutcome.Added => $"已加入 {HidePolicy.NormalizeProcess(raw)}。成为本屏前台应用时岛会收成小点。",
            HideListOutcome.AlreadyListed => "此应用已在名单中。",
            _ => "请填写有效进程名（例如 chrome），不能添加灵动岛自己。"
        };
        if (outcome != HideListOutcome.Added) return;
        _host.SaveHideList(); HideName.Clear(); FillApps();
    }
    void HideName_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { AddProcess(HideName.Text); e.Handled = true; } }
    void HideAdd_Click(object sender, RoutedEventArgs e) => AddProcess(HideName.Text);
    void HideCurrent_Click(object sender, RoutedEventArgs e) { if (_suggested.Length > 0) AddProcess(_suggested); }
    void HideRefresh_Click(object sender, RoutedEventArgs e) => FillApps();
    void HideBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择要加入隐身名单的程序", Filter = "程序 (*.exe)|*.exe", CheckFileExists = true };
        try { if (dialog.ShowDialog(this) == true) AddProcess(dialog.FileName); }
        catch { HideStatus.Text = "无法打开文件选择器；请手动填写进程名。"; }
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_apiDirty || _allowClose) return;
        e.Cancel = true;
        UnsavedPrompt.Visibility = Visibility.Visible;
        Navigation.IsEnabled = BodyScroll.IsEnabled = CloseButton.IsEnabled = false;
        ContinueEditing.Focus();
    }
    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void SaveClose_Click(object sender, RoutedEventArgs e)
    {
        if (SaveApi()) { _allowClose = true; Close(); }
        else Continue_Click(sender, e);
    }
    void DiscardClose_Click(object sender, RoutedEventArgs e) { _allowClose = true; Close(); }
    void Continue_Click(object sender, RoutedEventArgs e)
    {
        UnsavedPrompt.Visibility = Visibility.Collapsed;
        Navigation.IsEnabled = BodyScroll.IsEnabled = CloseButton.IsEnabled = true;
        Navigation.SelectedIndex = 1;
        ApiSave.Focus();
    }
    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (UnsavedPrompt.Visibility == Visibility.Visible) Continue_Click(sender, e); else Close();
        e.Handled = true;
    }
    void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || CloseButton.IsMouseOver) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        var screens = MonitorEnumerator.Enumerate().Select(m => new TrayScreen(m.X, m.Y, m.Width, m.Height,
            m.WorkX, m.WorkY, m.WorkWidth, m.WorkHeight, m.DpiX, m.DpiY, m.IsPrimary)).ToArray();
        var placed = WindowDrag.KeepVisible(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, screens);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, placed.X, placed.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
