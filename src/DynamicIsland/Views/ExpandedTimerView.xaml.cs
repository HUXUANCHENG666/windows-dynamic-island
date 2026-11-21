using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Formatting;
using DynamicIsland.Core.Island;
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

    readonly HoverFill _minusLook = new();
    readonly HoverFill _toggleLook = new();
    readonly HoverFill _resetLook = new();
    readonly HoverFill _plusLook = new();
    IslandSkin _skin = IslandSkin.Default;

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
        ResetText.Foreground = Theme.Title;
        StopwatchModeText.Foreground = Theme.Title;
        CountdownModeText.Foreground = Theme.Title;
        Ui.Hover(Minus, _minusLook);
        Ui.Hover(Toggle, _toggleLook);
        Ui.Hover(Reset, _resetLook);
        Ui.Hover(Plus, _plusLook);
        Ui.Hover(StopwatchMode, Brushes.Transparent, Theme.Hover);
        Ui.Hover(CountdownMode, Brushes.Transparent, Theme.Hover);
        ApplySkin(IslandSkin.Default);
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
        var selected = _skin == IslandSkin.Glass ? Theme.GlassKeyHover : Theme.Chip;
        StopwatchMode.Background = countdown ? Brushes.Transparent : selected;
        CountdownMode.Background = countdown ? selected : Brushes.Transparent;
    }

    public void ApplySkin(IslandSkin skin)
    {
        _skin = skin;
        bool glass = skin == IslandSkin.Glass;
        StyleKey(Minus, _minusLook, glass, primary: false);
        StyleKey(Toggle, _toggleLook, glass, primary: true);
        StyleKey(Reset, _resetLook, glass, primary: false);
        StyleKey(Plus, _plusLook, glass, primary: false);
        ToggleText.Foreground = glass ? Theme.Title : Theme.Ink;
    }

    static void StyleKey(Border border, HoverFill fill, bool glass, bool primary)
    {
        if (glass)
        {
            fill.Normal = Theme.GlassKey;
            fill.Hover = Theme.GlassKeyHover;
            fill.Pressed = Theme.GlassKeyPress;
            border.BorderBrush = Theme.GlassKeyStroke;
            border.BorderThickness = new Thickness(1);
        }
        else if (primary)
        {
            fill.Normal = Theme.Title;
            fill.Hover = Theme.Title;
            fill.Pressed = Theme.ChipHover;
            border.BorderBrush = Brushes.Transparent;
            border.BorderThickness = new Thickness(0);
        }
        else
        {
            fill.Normal = Theme.Chip;
            fill.Hover = Theme.ChipHover;
            fill.Pressed = Theme.Hover;
            border.BorderBrush = Brushes.Transparent;
            border.BorderThickness = new Thickness(0);
        }

        Ui.Paint(border, fill);
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
