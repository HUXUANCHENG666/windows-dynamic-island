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
        DirectionLabel.Foreground = Theme.Quiet;
        HideLabel.Foreground = Theme.Quiet;
        ToolLabel.Foreground = Theme.Quiet;
        NoticeRow.Background = Theme.Chip;
        NoticeTitle.Foreground = Theme.Title;
        NoticeTitle.FontFamily = _font;
        NoticeDetail.Foreground = Theme.Artist;
        NoticeDetail.FontFamily = _font;
        NoticeKnob.Background = Theme.Title;
        HideHint.Foreground = Theme.Artist;
        HideHint.FontFamily = _font;
        HideNameBox.Background = Theme.Chip;
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
        HideCurrent.Background = Theme.Chip;
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
        StyleChip(ActTranslate, ActTranslateText);
        StyleChip(ActRecall, ActRecallText);
        StyleChip(ActCamera, ActCameraText);
        StyleChip(ActStopwatch, ActStopwatchText);
        StyleChip(ActFocus, ActFocusText);
        StyleChip(ActLog, ActLogText);

        Ui.Hover(CloseButton, Theme.Chip, Theme.ChipHover);
        Ui.Hover(HideCurrent, Theme.Chip, Theme.ChipHover);
        CloseButton.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        NoticeToggle.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.ToggleNotificationCenter();
            RefreshFromIsland();
        };
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

        var direction = TranslatePolicy.Parse(_island.Settings.TranslateDirection);
        PaintDirection(DirAuto, direction == TranslateDirection.Auto);
        PaintDirection(DirZhEn, direction == TranslateDirection.ChineseToEnglish);
        PaintDirection(DirEnZh, direction == TranslateDirection.EnglishToChinese);

        var names = _island.Settings.HideProcesses ?? new List<string>();
        bool suggest = _suggested.Length > 0 && !HidePolicy.IsListed(names, _suggested);
        HideCurrent.Visibility = suggest ? Visibility.Visible : Visibility.Collapsed;
        HideCurrentText.Text = suggest ? $"把刚才的 {_suggested} 加入名单" : "";

        ActCameraText.Text = _island.CameraIsOpen ? "关闭摄像头" : "摄像头";
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
                Background = Theme.Chip,
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
                Background = Theme.Chip,
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
            Ui.Hover(chip, Theme.Chip, Theme.ChipHover);
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
        chip.Background = Theme.Chip;
        Ui.Hover(chip, Theme.Chip, Theme.ChipHover);
    }

    void PaintDirection(Border chip, bool selected)
    {
        chip.Background = selected ? Theme.ChipHover : Theme.Chip;
    }
}
