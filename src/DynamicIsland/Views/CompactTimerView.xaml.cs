using System.Windows.Controls;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class CompactTimerView : UserControl
{
    public CompactTimerView()
    {
        InitializeComponent();
        var font = new System.Windows.Media.FontFamily(IslandFonts.Ui);
        Time.Foreground = Theme.Title;
        Time.FontFamily = font;
        Upper.Foreground = Theme.Title;
        Lower.Foreground = Theme.Title;
        Upper.FontFamily = font;
        Lower.FontFamily = font;
    }

    public void SetText(string text)
    {
        Ui.Text(Time, text);
        Split(text, out var upper, out var lower);
        Ui.Text(Upper, upper);
        Ui.Text(Lower, lower);
    }

    public void SetVertical(bool vertical)
    {
        Time.Visibility = vertical ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        Stacked.Visibility = vertical ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    static void Split(string text, out string upper, out string lower)
    {
        int dot = text.LastIndexOf('.');
        if (dot > 0)
        {
            upper = text[..dot];
            lower = text[dot..];
            return;
        }

        int colon = text.LastIndexOf(':');
        if (colon > 0 && colon < text.Length - 1)
        {
            upper = text[..colon];
            lower = text[(colon + 1)..];
            return;
        }

        upper = text;
        lower = "";
    }
}
