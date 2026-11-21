using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland.Views;

sealed class HoverFill
{
    public Brush Normal { get; set; } = Brushes.Transparent;
    public Brush Hover { get; set; } = Brushes.Transparent;
    public Brush? Pressed { get; set; }
}

static class Ui
{
    public static void Hover(Border border, Brush normal, Brush hover)
    {
        var fill = new HoverFill { Normal = normal, Hover = hover };
        Hover(border, fill);
    }

    public static void Hover(Border border, HoverFill fill)
    {
        Paint(border, fill);
        border.Focusable = false;
        border.Cursor = Cursors.Hand;
        border.MouseEnter += (_, _) => Paint(border, fill);
        border.MouseLeave += (_, _) => Paint(border, fill);
        border.PreviewMouseLeftButtonDown += (_, _) => Paint(border, fill);
        border.PreviewMouseLeftButtonUp += (_, _) => Paint(border, fill);
    }

    public static void Paint(Border border, HoverFill fill)
    {
        bool over = border.IsMouseOver;
        bool pressed = over && Mouse.LeftButton == MouseButtonState.Pressed;
        border.Background = pressed && fill.Pressed != null ? fill.Pressed
            : over ? fill.Hover
            : fill.Normal;
    }

    public static void Text(TextBlock block, string value)
    {
        if (!string.Equals(block.Text, value, StringComparison.Ordinal))
            block.Text = value;
    }
}
