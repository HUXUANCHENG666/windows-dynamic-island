using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Motion;
using DynamicIsland.Shell;

namespace DynamicIsland.Visuals;

/// <summary>
/// 液态玻璃只画在岛的超椭圆上。中间几乎透明，桌面从岛身透出来；
/// 边缘月牙、高光和折射都按跑道形 / 圆角矩形的距离场来算，不再在中间单独画一块椭圆反光。
/// 不改窗口整体透明度，也不铺近黑遮罩。不用 DropShadow，以免透明窗口掉进软件渲染。
/// </summary>
static class GlassSkin
{
    static readonly Brush Body = MakeBody();
    static readonly Brush Rim = MakeRim();

    public static void Apply(IslandWindow window, IslandSkin skin)
    {
        if (skin == IslandSkin.Glass)
        {
            window.Pill.Fill = Body;
            window.Pill.Effect = null;
            window.Pill.CacheMode = null;
            window.PillStroke.Stroke = Rim;
            window.PillStroke.StrokeThickness = 1.5;
            window.GlassHost.Visibility = Visibility.Visible;
            window.GlassScrim.Fill = Brushes.Transparent;
            window.GlassThickness.Fill = Brushes.Transparent;
            window.GlassSpecular.Fill = Brushes.Transparent;
            window.GlassLip.Fill = Brushes.Transparent;
            HideBlobs(window);
            // DropShadow 会把透明窗口整棵子树打回软件渲染，改由高光和淡描边保可读。
            window.ContentHost.Effect = null;
            RenderOptions.SetBitmapScalingMode(window.GlassShade, BitmapScalingMode.Fant);
            RenderOptions.SetBitmapScalingMode(window.GlassRefraction, BitmapScalingMode.Fant);
            RenderOptions.SetBitmapScalingMode(window.GlassGpu, BitmapScalingMode.Fant);
        }
        else
        {
            ResetShifts(window);
            window.GlassHost.Visibility = Visibility.Collapsed;
            HideBlobs(window);
            UpdateRefraction(window, null, null);
            SetFlow(window, 0, 0, false);
            window.Pill.Fill = Theme.IslandFill;
            window.PillStroke.Stroke = Theme.Hairline;
            window.PillStroke.StrokeThickness = 1.6;
            window.ContentHost.Effect = null;
        }
    }

    public static void Decorate(IslandWindow window, Geometry geometry, double width, double height, bool animate)
    {
        window.GlassHost.Clip = geometry;
        window.GlassScrim.Data = geometry;
        window.GlassThickness.Data = geometry;
        window.GlassSpecular.Data = geometry;
        window.GlassLip.Data = geometry;
        HideBlobs(window);
        if (window.GlassHost.Visibility != Visibility.Visible || !animate)
            ResetShifts(window);
    }

    public static void ApplySurface(IslandWindow window, LiquidSurface surface, double width, double height, double vx, double vy, bool moving)
    {
        HideBlobs(window);
        SetFlow(window, vx, vy, moving);
    }

    static void HideBlobs(IslandWindow window)
    {
        window.GlassBead.Visibility = Visibility.Collapsed;
        window.GlassBlobA.Visibility = Visibility.Collapsed;
        window.GlassBlobB.Visibility = Visibility.Collapsed;
        window.GlassStreak.Visibility = Visibility.Collapsed;
    }

    static void ResetShifts(IslandWindow window)
    {
        window.GlassBeadShift.X = 0;
        window.GlassBeadShift.Y = 0;
        window.GlassBlobAShift.X = 0;
        window.GlassBlobAShift.Y = 0;
        window.GlassBlobBShift.X = 0;
        window.GlassBlobBShift.Y = 0;
        window.GlassStreakShift.X = 0;
        window.GlassStreakShift.Y = 0;
        SetFlow(window, 0, 0, false);
    }

    public static void UpdateRefraction(IslandWindow window, ImageSource? image, ImageSource? shade, ImageSource? gpu = null)
    {
        bool useGpu = gpu != null;
        window.GlassGpu.Source = gpu;
        window.GlassGpu.Visibility = useGpu ? Visibility.Visible : Visibility.Collapsed;
        window.GlassRefraction.Source = useGpu ? null : image;
        window.GlassRefraction.Visibility = !useGpu && image != null ? Visibility.Visible : Visibility.Collapsed;
        window.GlassShade.Source = useGpu ? null : shade;
        window.GlassShade.Visibility = !useGpu && shade != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public static void SetFlow(IslandWindow window, double vx, double vy, bool moving)
    {
        double lagX = moving ? Math.Clamp(-vx * 0.028, -22, 22) : 0;
        double lagY = moving ? Math.Clamp(-vy * 0.028, -22, 22) : 0;
        window.GlassBeadLag.X = lagX * 0.45;
        window.GlassBeadLag.Y = lagY * 0.45;
        window.GlassBlobALag.X = lagX;
        window.GlassBlobALag.Y = lagY;
        window.GlassBlobBLag.X = lagX * 0.8;
        window.GlassBlobBLag.Y = lagY * 0.8;
        window.GlassStreakLag.X = lagX * 1.15;
        window.GlassStreakLag.Y = lagY * 1.15;
        window.GlassShadeLag.X = 0;
        window.GlassShadeLag.Y = 0;
        window.GlassRefractionLag.X = 0;
        window.GlassRefractionLag.Y = 0;
        window.GlassGpuLag.X = 0;
        window.GlassGpuLag.Y = 0;
    }

    static Brush MakeBody()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.28, 0),
            EndPoint = new Point(0.55, 1)
        };
        // 中间几乎透明，只留一层很淡的冷色，避免再铺成半透色块。
        brush.GradientStops.Add(Stop(0x18, 0xF4, 0xFB, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x0C, 0xD0, 0xE8, 0xF4, 0.20));
        brush.GradientStops.Add(Stop(0x06, 0xA8, 0xC8, 0xDC, 0.50));
        brush.GradientStops.Add(Stop(0x0E, 0x78, 0x9C, 0xB4, 0.82));
        brush.GradientStops.Add(Stop(0x16, 0x5A, 0x80, 0x9A, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeRim()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.05, 0),
            EndPoint = new Point(0.95, 1)
        };
        brush.GradientStops.Add(Stop(0xA8, 0xF4, 0xFB, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x6A, 0xFF, 0xFF, 0xFF, 0.38));
        brush.GradientStops.Add(Stop(0x52, 0xC8, 0xE4, 0xF4, 1));
        brush.Freeze();
        return brush;
    }

    static GradientStop Stop(byte a, byte r, byte g, byte b, double offset) =>
        new(Color.FromArgb(a, r, g, b), offset);
}
