using System.Windows.Controls;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class CompactTimerView : UserControl
{
    public CompactTimerView()
    {
        InitializeComponent();
        Time.Foreground = Theme.Title;
        Time.FontFamily = new System.Windows.Media.FontFamily(IslandFonts.Ui);
    }

    public void SetText(string text) => Ui.Text(Time, text);
}
