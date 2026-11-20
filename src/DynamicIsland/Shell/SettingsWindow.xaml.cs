using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Translation;
using DynamicIsland.Native;
using DynamicIsland.Services;
using DynamicIsland.Views;
using DynamicIsland.Visuals;

namespace DynamicIsland.Shell;

public partial class SettingsWindow : Window
{
    readonly IslandWindow _island;
    readonly FontFamily _font = new(IslandFonts.Ui);
    readonly string _suggested;

    public SettingsWindow(IslandWindow island)
    {
        _island = island;
        var suggested = HidePolicy.NormalizeProcess(island.ForeignProcess);
        _suggested = HideListEditor.CanList(suggested) ? suggested : "";
        InitializeComponent();
        FontFamily = _font;
        Card.Background = Theme.IslandFill;
        Heading.Foreground = Theme.Title;
        Gear.Data = Geometry.Parse(Glyphs.Gear);
        Gear.Stroke = Theme.Title;
        CloseButton.Background = Theme.Chip;
        CloseButton.Child.SetValue(TextBlock.ForegroundProperty, Theme.Title);
        NoticeLabel.Foreground = Theme.Quiet;
        LookLabel.Foreground = Theme.Quiet;
        DirectionLabel.Foreground = Theme.Quiet;
        HideLabel.Foreground = Theme.Quiet;
        LiveLabel.Foreground = Theme.Quiet;
        ToolLabel.Foreground = Theme.Quiet;
        LookRow.Background = Theme.Chip;
        ScaleTitle.Foreground = Theme.Title;
        ScaleTitle.FontFamily = _font;
        ScaleValue.Foreground = Theme.Artist;
        ScaleValue.FontFamily = _font;
        SkinTitle.Foreground = Theme.Title;
        SkinTitle.FontFamily = _font;
        SkinDetail.Foreground = Theme.Artist;
        SkinDetail.FontFamily = _font;
        ScaleRail.Background = Theme.Track;
        ScaleFill.Background = Theme.Title;
        ScaleKnob.Background = Theme.Title;
        StyleChip(ScaleReset, ScaleResetText);
        StyleText(SkinDefaultText);
        StyleText(SkinGlassText);
        NoticeRow.Background = Theme.Chip;
        NoticeTitle.Foreground = Theme.Title;
        NoticeTitle.FontFamily = _font;
        NoticeDetail.Foreground = Theme.Artist;
        NoticeDetail.FontFamily = _font;
        NoticeKnob.Background = Theme.Title;
        LiveRow.Background = Theme.Chip;
        HideRow.Background = Theme.Chip;
        TranslateRule.Background = Theme.Hairline;
        LiveTitle.Foreground = Theme.Title;
        LiveTitle.FontFamily = _font;
        LiveDetail.Foreground = Theme.Artist;
        LiveDetail.FontFamily = _font;
        LiveKnob.Background = Theme.Title;
        ApiLabel.Foreground = Theme.Quiet;
        ApiUrlLabel.Foreground = Theme.Quiet;
        ApiModelLabel.Foreground = Theme.Quiet;
        ApiKeyLabel.Foreground = Theme.Quiet;
        StyleText(ApiDefaultText);
        StyleText(ApiCustomText);
        ApiUrlBox.Background = Theme.Hover;
        ApiModelBox.Background = Theme.Hover;
        ApiKeyBox.Background = Theme.Hover;
        StyleField(ApiUrl, ApiUrlHint);
        StyleField(ApiModel, ApiModelHint);
        ApiKey.FontFamily = _font;
        ApiKey.Foreground = Theme.Title;
        ApiKey.CaretBrush = Theme.Title;
        ApiKey.Background = Brushes.Transparent;
        ApiKey.BorderThickness = new Thickness(0);
        HideHint.Foreground = Theme.Artist;
        HideHint.FontFamily = _font;
        HideNameBox.Background = Theme.Hover;
        HideName.FontFamily = _font;
        HideName.Foreground = Theme.Title;
        HideName.CaretBrush = Theme.Title;
        HideName.Background = Brushes.Transparent;
        HideName.BorderThickness = new Thickness(0);
        HideNameHint.Foreground = Theme.Quiet;
        HideNameHint.FontFamily = _font;
        HideStatus.Foreground = Theme.Artist;
        HideStatus.FontFamily = _font;
        RunningLabel.Foreground = Theme.Quiet;
        RunningLabel.FontFamily = _font;
        HideCurrent.Background = Theme.Hover;
        HideCurrentText.Foreground = Theme.Title;
        HideCurrentText.FontFamily = _font;
        StyleChip(HideAdd, HideAddText);
        StyleChip(HideBrowse, HideBrowseText);
        StyleChip(HideRefresh, HideRefreshText);

        StyleText(DirAutoText);
        StyleText(DirZhEnText);
        StyleText(DirEnZhText);
        WireDirection(DirAuto, TranslateDirection.Auto);
        WireDirection(DirZhEn, TranslateDirection.ChineseToEnglish);
        WireDirection(DirEnZh, TranslateDirection.EnglishToChinese);
        WireApi(ApiDefault, TranslateApiKind.Default);
        WireApi(ApiCustom, TranslateApiKind.Custom);
        WireSkin(SkinDefault, IslandSkin.Default);
        WireSkin(SkinGlass, IslandSkin.Glass);
        WireScale();
        ScaleReset.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.SetIslandScale(AppearancePolicy.DefaultScale);
            RefreshFromIsland();
        };
        StyleChip(ActTranslate, ActTranslateText);
        StyleChip(ActRecall, ActRecallText);
        StyleChip(ActCamera, ActCameraText);
        StyleChip(ActLiveTranslate, ActLiveTranslateText);
        StyleChip(ActStopwatch, ActStopwatchText);
        StyleChip(ActFocus, ActFocusText);
        StyleChip(ActLog, ActLogText);

        Ui.Hover(CloseButton, Theme.Chip, Theme.ChipHover);
        Ui.Hover(HideCurrent, Theme.Hover, Theme.ChipHover);
        DragHeader.Background = Brushes.Transparent;
        Card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled || e.LeftButton != MouseButtonState.Pressed)
                return;
            if (IsInteractive(e.OriginalSource as DependencyObject))
                return;
            e.Handled = true;
            BeginWindowDrag();
        };
        CloseButton.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        NoticeToggle.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.ToggleNotificationCenter();
            RefreshFromIsland();
        };
        LiveToggle.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.ToggleLiveTranslate();
            RefreshFromIsland();
        };
        ApiUrl.TextChanged += (_, _) =>
        {
            ApiUrlHint.Visibility = string.IsNullOrEmpty(ApiUrl.Text) ? Visibility.Visible : Visibility.Collapsed;
            PersistApi(rememberFields: true);
        };
        ApiModel.TextChanged += (_, _) =>
        {
            ApiModelHint.Visibility = string.IsNullOrEmpty(ApiModel.Text) ? Visibility.Visible : Visibility.Collapsed;
            PersistApi(rememberFields: true);
        };
        ApiKey.PasswordChanged += (_, _) => PersistApi(rememberFields: true);
        ApiUrl.LostFocus += (_, _) => PersistApi(rememberFields: true);
        ApiModel.LostFocus += (_, _) => PersistApi(rememberFields: true);
        ApiKey.LostFocus += (_, _) => PersistApi(rememberFields: true);
        Closed += (_, _) => PersistApi(rememberFields: true);
        HideName.TextChanged += (_, _) =>
            HideNameHint.Visibility = string.IsNullOrEmpty(HideName.Text) ? Visibility.Visible : Visibility.Collapsed;
        HideName.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            AddProcess(HideName.Text);
        };
        HideAdd.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            AddProcess(HideName.Text);
        };
        HideBrowse.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            BrowseProcess();
        };
        HideRefresh.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            RefreshFromIsland();
        };
        HideCurrent.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            AddProcess(_suggested);
        };
        Activated += (_, _) => RefreshFromIsland();
        ActTranslate.MouseLeftButtonUp += (_, e) => { e.Handled = true; _island.TranslateClipboard(); };
        ActRecall.MouseLeftButtonUp += (_, e) => { e.Handled = true; _island.ShowStoredTranslation(); };
        ActCamera.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.ToggleCamera();
            RefreshFromIsland();
        };
        ActLiveTranslate.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.ToggleLiveTranslate();
            RefreshFromIsland();
        };
        ActStopwatch.MouseLeftButtonUp += (_, e) => { e.Handled = true; _island.OpenStopwatch(); };
        ActFocus.MouseLeftButtonUp += (_, e) => { e.Handled = true; _island.OpenCountdown(25); };
        ActLog.MouseLeftButtonUp += (_, e) => { e.Handled = true; _island.OpenLog(); };

        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            long style = NativeMethods.GetWindowLongPtr(source.Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            style |= NativeMethods.WS_EX_TOOLWINDOW;
            style &= ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLongPtr(source.Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
            double work = SystemParameters.WorkArea.Height;
            BodyScroll.MaxHeight = Math.Max(280, work - 140);
        };
        RefreshFromIsland();
    }

    public void RefreshFromIsland()
    {
        bool notes = _island.Settings.NotificationCenter;
        NoticeKnob.HorizontalAlignment = notes ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        NoticeKnob.Margin = notes ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
        NoticeToggle.Background = notes ? Theme.Title : Theme.Track;
        NoticeKnob.Background = notes ? Theme.Ink : Theme.Title;

        PaintAppearance();

        var direction = TranslatePolicy.Parse(_island.Settings.TranslateDirection);
        PaintDirection(DirAuto, direction == TranslateDirection.Auto);
        PaintDirection(DirZhEn, direction == TranslateDirection.ChineseToEnglish);
        PaintDirection(DirEnZh, direction == TranslateDirection.EnglishToChinese);

        var api = TranslateApiPolicy.Parse(_island.Settings.TranslateApi);
        PaintDirection(ApiDefault, api == TranslateApiKind.Default);
        PaintDirection(ApiCustom, api == TranslateApiKind.Custom);
        ApiFields.Visibility = api == TranslateApiKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        if (!ApiUrl.IsKeyboardFocusWithin)
        {
            var url = _island.Settings.TranslateApiUrl ?? "";
            if (ApiUrl.Text != url)
                ApiUrl.Text = url;
        }
        if (!ApiModel.IsKeyboardFocusWithin)
        {
            var model = _island.Settings.TranslateApiModel ?? "";
            if (ApiModel.Text != model)
                ApiModel.Text = model;
        }
        if (!ApiKey.IsKeyboardFocusWithin)
        {
            var key = _island.Settings.TranslateApiKey ?? "";
            if (ApiKey.Password != key)
                ApiKey.Password = key;
        }
        ApiUrlHint.Visibility = string.IsNullOrEmpty(ApiUrl.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApiModelHint.Visibility = string.IsNullOrEmpty(ApiModel.Text) ? Visibility.Visible : Visibility.Collapsed;

        var names = _island.Settings.HideProcesses ?? new List<string>();
        bool suggest = _suggested.Length > 0 && !HidePolicy.IsListed(names, _suggested);
        HideCurrent.Visibility = suggest ? Visibility.Visible : Visibility.Collapsed;
        HideCurrentText.Text = suggest ? $"把刚才的 {_suggested} 加入名单" : "";

        ActCameraText.Text = _island.CameraIsOpen ? "关闭摄像头" : "摄像头";
        ActLiveTranslateText.Text = _island.LiveTranslateIsOpen ? "关闭翻译" : "实时翻译";
        bool live = _island.LiveTranslateIsOpen;
        LiveKnob.HorizontalAlignment = live ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        LiveKnob.Margin = live ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
        LiveToggle.Background = live ? Theme.Title : Theme.Track;
        LiveKnob.Background = live ? Theme.Ink : Theme.Title;
        ActRecall.Opacity = _island.HasStoredTranslation ? 1 : 0.4;
        ActRecall.IsHitTestVisible = _island.HasStoredTranslation;

        FillRunning(names);
        HideList.Children.Clear();
        if (names.Count == 0)
        {
            HideList.Children.Add(new TextBlock
            {
                Text = "还没有程序。在上面输入进程名，或点一个正在运行的程序。",
                Foreground = Theme.Quiet,
                FontFamily = _font,
                FontSize = 13,
                Margin = new Thickness(2, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var name in names.ToArray())
        {
            var row = new Border
            {
                Background = Theme.Hover,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 7, 8, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = name,
                Foreground = Theme.Title,
                FontFamily = _font,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            var remove = new Border
            {
                Background = Theme.Hover,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "移出",
                    Foreground = Theme.Title,
                    FontFamily = _font,
                    FontSize = 12
                }
            };
            string captured = name;
            remove.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                RemoveProcess(captured);
            };
            Grid.SetColumn(remove, 1);
            grid.Children.Add(label);
            grid.Children.Add(remove);
            row.Child = grid;
            HideList.Children.Add(row);
        }
    }

    static bool IsInteractive(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is TextBox || source is PasswordBox)
                return true;
            if (source is FrameworkElement element && element.Cursor == Cursors.Hand)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    void BeginWindowDrag()
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
            return;
        var screens = new List<TrayScreen>();
        foreach (var monitor in MonitorEnumerator.Enumerate())
        {
            screens.Add(new TrayScreen(
                monitor.X, monitor.Y, monitor.Width, monitor.Height,
                monitor.WorkX, monitor.WorkY, monitor.WorkWidth, monitor.WorkHeight,
                monitor.DpiX, monitor.DpiY, monitor.IsPrimary));
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        var placed = WindowDrag.KeepVisible(rect.Left, rect.Top, width, height, screens);
        if (placed.X == rect.Left && placed.Y == rect.Top)
            return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, placed.X, placed.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    void AddProcess(string raw)
    {
        var name = HidePolicy.NormalizeProcess(raw);
        if (!HideListEditor.CanList(raw))
        {
            HideStatus.Text = name == "dynamicisland"
                ? "这是灵动岛自己，不用加入。"
                : "这个名字没法加入。请填任务管理器里的进程名，例如 chrome，不用带 .exe。";
            return;
        }

        var list = _island.Settings.HideProcesses ??= new List<string>();
        if (HidePolicy.IsListed(list, name))
        {
            HideStatus.Text = $"{name} 已经在名单里。";
            return;
        }

        var outcome = HideListEditor.Add(list, raw);
        if (outcome != HideListOutcome.Added)
            return;
        SettingsStore.Save(_island.Settings);
        _island.RefreshHideList();
        HideName.Clear();
        RefreshFromIsland();
        HideStatus.Text = $"已加入 {name}。它在这块屏幕上成为前台时，岛会收成小点。";
    }

    void RemoveProcess(string name)
    {
        var list = _island.Settings.HideProcesses ??= new List<string>();
        if (HideListEditor.Remove(list, name) != HideListOutcome.Removed)
            return;
        SettingsStore.Save(_island.Settings);
        _island.RefreshHideList();
        RefreshFromIsland();
        HideStatus.Text = $"已移出 {HidePolicy.NormalizeProcess(name)}。";
    }

    void BrowseProcess()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要加入隐身名单的程序",
            Filter = "程序 (*.exe)|*.exe",
            CheckFileExists = true
        };
        try
        {
            if (dialog.ShowDialog(this) == true)
                AddProcess(dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("hide-list", ex);
            HideStatus.Text = "没有打开文件选择窗口。可以直接输入进程名。";
        }
    }

    void FillRunning(IReadOnlyCollection<string> listed)
    {
        RunningApps.Children.Clear();
        IReadOnlyList<string> shown;
        try
        {
            shown = HideListEditor.Candidates(RunningWindowProcesses(), listed);
        }
        catch (Exception ex)
        {
            Log.Error("hide-list", ex);
            shown = Array.Empty<string>();
        }

        if (shown.Count == 0)
        {
            RunningApps.Children.Add(new TextBlock
            {
                Text = "现在没有可以点的窗口。可以直接输入进程名。",
                Foreground = Theme.Quiet,
                FontFamily = _font,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var name in shown)
        {
            var chip = new Border
            {
                Background = Theme.Hover,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = name,
                    Foreground = Theme.Title,
                    FontFamily = _font,
                    FontSize = 12
                }
            };
            Ui.Hover(chip, Theme.Hover, Theme.ChipHover);
            string captured = name;
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                AddProcess(captured);
            };
            RunningApps.Children.Add(chip);
        }
    }

    static List<string> RunningWindowProcesses()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                    continue;
                names.Add(process.ProcessName);
            }
            catch
            {
                // 有的系统进程不让看窗口句柄。跳过即可。
            }
            finally
            {
                process.Dispose();
            }
        }

        return names;
    }

    bool _scaleDragging;

    void WireSkin(Border chip, IslandSkin skin)
    {
        chip.MouseEnter += (_, _) => chip.Background = Theme.ChipHover;
        chip.MouseLeave += (_, _) => PaintDirection(chip, SelectedSkin() == skin);
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.SetIslandSkin(skin);
            RefreshFromIsland();
        };
    }

    IslandSkin SelectedSkin() => AppearancePolicy.ParseSkin(_island.Settings.IslandSkin);

    void WireScale()
    {
        ScaleTrack.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _scaleDragging = true;
            ScaleTrack.CaptureMouse();
            ApplyScaleFromPointer();
        };
        ScaleTrack.MouseMove += (_, _) =>
        {
            if (_scaleDragging)
                ApplyScaleFromPointer();
        };
        ScaleTrack.MouseLeftButtonUp += (_, e) =>
        {
            if (!_scaleDragging)
                return;
            e.Handled = true;
            _scaleDragging = false;
            if (ScaleTrack.IsMouseCaptured)
                ScaleTrack.ReleaseMouseCapture();
            ApplyScaleFromPointer();
        };
        ScaleTrack.LostMouseCapture += (_, _) => _scaleDragging = false;
        ScaleTrack.SizeChanged += (_, _) =>
        {
            if (!_scaleDragging)
                PaintScale(_island.Settings.IslandScale);
        };
    }

    void ApplyScaleFromPointer()
    {
        double width = Math.Max(18, ScaleTrack.ActualWidth);
        double rail = Math.Max(1, width - 18);
        double x = Mouse.GetPosition(ScaleTrack).X - 9;
        double scale = AppearancePolicy.FromPercent(85 + (Math.Clamp(x, 0, rail) / rail) * 55);
        PaintScale(scale);
        _island.SetIslandScale(scale);
    }

    void PaintAppearance()
    {
        if (!_scaleDragging)
            PaintScale(_island.Settings.IslandScale);
        var skin = SelectedSkin();
        PaintDirection(SkinDefault, skin == IslandSkin.Default);
        PaintDirection(SkinGlass, skin == IslandSkin.Glass);
    }

    void PaintScale(double scale)
    {
        scale = AppearancePolicy.ClampScale(scale);
        ScaleValue.Text = $"{AppearancePolicy.ToPercent(scale)}%";
        double width = Math.Max(18, ScaleTrack.ActualWidth);
        double rail = Math.Max(1, width - 18);
        double t = (scale - AppearancePolicy.MinScale) / (AppearancePolicy.MaxScale - AppearancePolicy.MinScale);
        double left = t * rail;
        ScaleKnob.Margin = new Thickness(left, 0, 0, 0);
        ScaleFill.Width = left;
    }

    void WireApi(Border chip, TranslateApiKind kind)
    {
        chip.MouseEnter += (_, _) => chip.Background = Theme.ChipHover;
        chip.MouseLeave += (_, _) => PaintDirection(chip, SelectedApi() == kind);
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            PersistApi(kind, rememberFields: true);
            RefreshFromIsland();
        };
    }

    void PersistApi(bool rememberFields) => PersistApi(SelectedApi(), rememberFields);

    void PersistApi(TranslateApiKind kind, bool rememberFields)
    {
        string? url = rememberFields ? ApiUrl.Text : _island.Settings.TranslateApiUrl;
        string? model = rememberFields ? ApiModel.Text : _island.Settings.TranslateApiModel;
        string? key = rememberFields ? ApiKey.Password : _island.Settings.TranslateApiKey;
        _island.SetTranslateApi(kind, url, model, key);
    }

    TranslateApiKind SelectedApi() => TranslateApiPolicy.Parse(_island.Settings.TranslateApi);

    void StyleField(TextBox box, TextBlock hint)
    {
        box.FontFamily = _font;
        box.Foreground = Theme.Title;
        box.CaretBrush = Theme.Title;
        box.Background = Brushes.Transparent;
        box.BorderThickness = new Thickness(0);
        hint.Foreground = Theme.Quiet;
        hint.FontFamily = _font;
    }

    void WireDirection(Border chip, TranslateDirection direction)
    {
        chip.MouseEnter += (_, _) => chip.Background = Theme.ChipHover;
        chip.MouseLeave += (_, _) => PaintDirection(chip, SelectedDirection() == direction);
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.SetTranslateDirection(direction);
            RefreshFromIsland();
        };
    }

    TranslateDirection SelectedDirection() => TranslatePolicy.Parse(_island.Settings.TranslateDirection);

    void StyleText(TextBlock text)
    {
        text.FontFamily = _font;
        text.Foreground = Theme.Title;
    }

    void StyleChip(Border chip, TextBlock text)
    {
        text.FontFamily = _font;
        text.Foreground = Theme.Title;
        chip.Background = Theme.Hover;
        Ui.Hover(chip, Theme.Hover, Theme.ChipHover);
    }

    void PaintDirection(Border chip, bool selected)
    {
        chip.Background = selected ? Theme.ChipHover : Theme.Hover;
    }
}
