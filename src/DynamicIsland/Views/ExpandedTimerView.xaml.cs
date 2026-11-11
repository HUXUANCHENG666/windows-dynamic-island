using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Formatting;
using DynamicIsland.Core.Timer;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class ExpandedTimerView : UserControl
{
    public event EventHandler? ToggleRequested;
    public event EventHandler? ResetRequested;
    public event EventHandler? MinusRequested;
    public event EventHandler? PlusRequested;
    public event EventHandler? StopwatchRequested;
    public event EventHandler? CountdownRequested;

    public ExpandedTimerView()
    {
        InitializeComponent();
        var ui = new FontFamily(IslandFonts.Ui);
        Time.FontFamily = new FontFamily(IslandFonts.Display);
        Caption.FontFamily = ui;
        ToggleText.FontFamily = ui;
        ResetText.FontFamily = ui;
        StopwatchModeText.FontFamily = ui;
        CountdownModeText.FontFamily = ui;
        Time.Foreground = Theme.Title;
        Caption.Foreground = Theme.Artist;
        ToggleText.Foreground = Theme.Ink;
        ResetText.Foreground = Theme.Title;
        StopwatchModeText.Foreground = Theme.Title;
        CountdownModeText.Foreground = Theme.Title;
        Toggle.Background = Theme.Title;
        Toggle.Cursor = System.Windows.Input.Cursors.Hand;
        Toggle.Focusable = false;
        Ui.Hover(Reset, Theme.Chip, Theme.ChipHover);
        Ui.Hover(Minus, Theme.Chip, Theme.ChipHover);
        Ui.Hover(Plus, Theme.Chip, Theme.ChipHover);
        Ui.Hover(StopwatchMode, Brushes.Transparent, Theme.Hover);
        Ui.Hover(CountdownMode, Brushes.Transparent, Theme.Hover);
        PaintChip(Minus);
        PaintChip(Plus);
        Toggle.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleRequested?.Invoke(this, EventArgs.Empty); };
        Reset.MouseLeftButtonUp += (_, e) => { e.Handled = true; ResetRequested?.Invoke(this, EventArgs.Empty); };
        Minus.MouseLeftButtonUp += (_, e) => { e.Handled = true; MinusRequested?.Invoke(this, EventArgs.Empty); };
        Plus.MouseLeftButtonUp += (_, e) => { e.Handled = true; PlusRequested?.Invoke(this, EventArgs.Empty); };
        StopwatchMode.MouseLeftButtonUp += (_, e) => { e.Handled = true; StopwatchRequested?.Invoke(this, EventArgs.Empty); };
        CountdownMode.MouseLeftButtonUp += (_, e) => { e.Handled = true; CountdownRequested?.Invoke(this, EventArgs.Empty); };
    }

    public void Render(IslandTimer timer)
    {
        bool countdown = timer.Mode == TimerMode.Countdown;
        string text = countdown ? TimeFormat.Countdown(timer.Remaining) : TimeFormat.Stopwatch(timer.Display);
        Ui.Text(Time, text);
        string caption = timer.IsCompleted ? "时间到" : countdown ? "倒计时" : "秒表";
        Ui.Text(Caption, caption);
        Ui.Text(ToggleText, timer.IsRunning ? "暂停" : timer.IsCompleted ? "再来一次" : "开始");
        Minus.Visibility = countdown ? Visibility.Visible : Visibility.Collapsed;
        Plus.Visibility = countdown ? Visibility.Visible : Visibility.Collapsed;
        StopwatchMode.Background = countdown ? Brushes.Transparent : Theme.Chip;
        CountdownMode.Background = countdown ? Theme.Chip : Brushes.Transparent;
    }

    public void SetVertical(bool vertical)
    {
        Controls.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        Modes.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        var gap = vertical ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 8, 0);
        Minus.Margin = gap;
        Toggle.Margin = gap;
        Reset.Margin = gap;
        Plus.Margin = new Thickness(0);
        StopwatchMode.Margin = vertical ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 6, 0);
        Time.FontSize = vertical ? 36 : 44;
        Controls.HorizontalAlignment = HorizontalAlignment.Center;
        Modes.HorizontalAlignment = HorizontalAlignment.Center;
    }

    static void PaintChip(Border border)
    {
        if (border.Child is TextBlock text)
            text.Foreground = Theme.Title;
    }
}
