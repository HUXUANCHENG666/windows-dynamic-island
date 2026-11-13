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

    public SettingsWindow(IslandWindow island)
    {
        _island = island;
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
        HideCurrent.Background = Theme.Chip;
        HideCurrentText.Foreground = Theme.Title;
        HideCurrentText.FontFamily = _font;

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
        HideCurrent.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _island.ToggleListedProcess(_island.ForeignProcess);
            RefreshFromIsland();
        };
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

        string? foreign = _island.ForeignProcess;
        bool listed = !string.IsNullOrWhiteSpace(foreign)
            && HidePolicy.IsListed(_island.Settings.HideProcesses ?? new List<string>(), foreign);
        HideCurrentText.Text = string.IsNullOrWhiteSpace(foreign)
            ? "先切到要隐藏的程序"
            : listed ? $"把 {foreign} 移出名单" : $"把 {foreign} 加入名单";
        HideCurrent.IsHitTestVisible = !string.IsNullOrWhiteSpace(foreign);
        HideCurrent.Opacity = string.IsNullOrWhiteSpace(foreign) ? 0.45 : 1;

        ActCameraText.Text = _island.CameraIsOpen ? "关闭摄像头" : "摄像头";
        ActRecall.Opacity = _island.HasStoredTranslation ? 1 : 0.4;
        ActRecall.IsHitTestVisible = _island.HasStoredTranslation;

        HideList.Children.Clear();
        var names = _island.Settings.HideProcesses;
        if (names == null || names.Count == 0)
        {
            HideList.Children.Add(new TextBlock
            {
                Text = "还没有程序。",
                Foreground = Theme.Quiet,
                FontFamily = _font,
                FontSize = 13,
                Margin = new Thickness(2, 0, 0, 0)
            });
            return;
        }

        foreach (var name in names.Take(12).ToArray())
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
                _island.ToggleListedProcess(captured);
                RefreshFromIsland();
            };
            Grid.SetColumn(remove, 1);
            grid.Children.Add(label);
            grid.Children.Add(remove);
            row.Child = grid;
            HideList.Children.Add(row);
        }
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
