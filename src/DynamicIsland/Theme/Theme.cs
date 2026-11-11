using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core.Island;

namespace DynamicIsland.Visuals;

static class IslandFonts
{
    public const string Ui = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei";
    public const string Display = "Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei";
}

static class Theme
{
    public static SolidColorBrush Title { get; } = Solid(Palette.Title);
    public static SolidColorBrush Artist { get; } = Solid(Palette.Artist);
    public static SolidColorBrush Quiet { get; } = Solid(Palette.Quiet);
    public static SolidColorBrush Faint { get; } = Solid(Palette.Faint);
    public static SolidColorBrush Track { get; } = Solid(Palette.Track);
    public static SolidColorBrush Hover { get; } = Solid(Palette.Hover);
    public static SolidColorBrush Chip { get; } = Solid(Palette.Chip);
    public static SolidColorBrush ChipHover { get; } = Solid(Palette.ChipHover);
    public static SolidColorBrush Hairline { get; } = Solid(Palette.Hairline);
    public static SolidColorBrush Placeholder { get; } = Solid(Palette.Placeholder);
    public static SolidColorBrush Danger { get; } = Solid(Palette.Danger);
    public static SolidColorBrush Ink { get; } = Solid(Palette.Ink);
    public static LinearGradientBrush IslandFill { get; } = MakeFill();

    public static SolidColorBrush Solid(Rgba color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    static LinearGradientBrush MakeFill()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        brush.GradientStops.Add(Stop(Palette.FillTop, 0));
        brush.GradientStops.Add(Stop(Palette.FillMid, 0.42));
        brush.GradientStops.Add(Stop(Palette.FillBottom, 1));
        brush.Freeze();
        return brush;
    }

    static GradientStop Stop(Rgba color, double offset) =>
        new(Color.FromRgb(color.R, color.G, color.B), offset);
}
