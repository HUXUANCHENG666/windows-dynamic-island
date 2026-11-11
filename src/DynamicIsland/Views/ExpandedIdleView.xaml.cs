using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class ExpandedIdleView : UserControl
{
    static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");

    public event EventHandler? StopwatchRequested;
    public event EventHandler? FocusRequested;

    public ExpandedIdleView()
    {
        InitializeComponent();
        Clock.FontFamily = new FontFamily(IslandFonts.Display);
        Date.FontFamily = new FontFamily(IslandFonts.Ui);
        StopwatchText.FontFamily = Date.FontFamily;
        FocusText.FontFamily = Date.FontFamily;
        Clock.Foreground = Theme.Title;
        Date.Foreground = Theme.Artist;
        StopwatchText.Foreground = Theme.Title;
        FocusText.Foreground = Theme.Title;
        Ui.Hover(Stopwatch, Theme.Chip, Theme.ChipHover);
        Ui.Hover(FocusChip, Theme.Chip, Theme.ChipHover);
        Stopwatch.MouseLeftButtonUp += (_, e) => { e.Handled = true; StopwatchRequested?.Invoke(this, EventArgs.Empty); };
        FocusChip.MouseLeftButtonUp += (_, e) => { e.Handled = true; FocusRequested?.Invoke(this, EventArgs.Empty); };
    }

    public void SetClock(DateTime now)
    {
        Ui.Text(Clock, now.ToString("HH:mm", Chinese));
        Ui.Text(Date, now.ToString("M月d日 dddd", Chinese));
    }
}
