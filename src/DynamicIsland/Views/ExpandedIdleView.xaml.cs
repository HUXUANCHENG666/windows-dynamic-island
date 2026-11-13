using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core.Island;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class ExpandedIdleView : UserControl
{
    static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");

    public event EventHandler? StopwatchRequested;
    public event EventHandler? FocusRequested;
    public event EventHandler? CameraRequested;

    public ExpandedIdleView()
    {
        InitializeComponent();
        Clock.FontFamily = new FontFamily(IslandFonts.Display);
        Date.FontFamily = new FontFamily(IslandFonts.Ui);
        StopwatchText.FontFamily = Date.FontFamily;
        CameraText.FontFamily = Date.FontFamily;
        FocusText.FontFamily = Date.FontFamily;
        Clock.Foreground = Theme.Title;
        Date.Foreground = Theme.Artist;
        StopwatchText.Foreground = Theme.Title;
        CameraText.Foreground = Theme.Title;
        FocusText.Foreground = Theme.Title;
        StopwatchIcon.Data = Geometry.Parse(Glyphs.Stopwatch);
        StopwatchIcon.Stroke = Theme.Title;
        CameraIcon.Data = Geometry.Parse(Glyphs.Camera);
        CameraIcon.Stroke = Theme.Title;
        FocusIcon.Data = Geometry.Parse(Glyphs.Focus);
        FocusIcon.Stroke = Theme.Title;
        Ui.Hover(Stopwatch, Theme.Chip, Theme.ChipHover);
        Ui.Hover(CameraChip, Theme.Chip, Theme.ChipHover);
        Ui.Hover(FocusChip, Theme.Chip, Theme.ChipHover);
        Stopwatch.MouseLeftButtonUp += (_, e) => { e.Handled = true; StopwatchRequested?.Invoke(this, EventArgs.Empty); };
        CameraChip.MouseLeftButtonUp += (_, e) => { e.Handled = true; CameraRequested?.Invoke(this, EventArgs.Empty); };
        FocusChip.MouseLeftButtonUp += (_, e) => { e.Handled = true; FocusRequested?.Invoke(this, EventArgs.Empty); };
    }

    public void SetClock(DateTime now)
    {
        Ui.Text(Clock, now.ToString("HH:mm", Chinese));
        Ui.Text(Date, now.ToString("M月d日 dddd", Chinese));
    }

    public void SetVertical(bool vertical)
    {
        Actions.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        var gap = vertical ? new System.Windows.Thickness(0, 0, 0, 8) : new System.Windows.Thickness(0, 0, 8, 0);
        Stopwatch.Margin = gap;
        CameraChip.Margin = gap;
        Stopwatch.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        CameraChip.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        FocusChip.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        Clock.FontSize = vertical ? 36 : 42;
    }
}
