using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland.Views;

static class Ui
{
    public static void Hover(Border border, Brush normal, Brush hover)
    {
        border.Background = normal;
        border.Focusable = false;
        border.Cursor = Cursors.Hand;
        border.MouseEnter += (_, _) => border.Background = hover;
        border.MouseLeave += (_, _) => border.Background = normal;
    }

    public static void Text(TextBlock block, string value)
    {
        if (!string.Equals(block.Text, value, StringComparison.Ordinal))
            block.Text = value;
    }
}
