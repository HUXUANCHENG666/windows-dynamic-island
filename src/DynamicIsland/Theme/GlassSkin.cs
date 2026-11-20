using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Motion;
using DynamicIsland.Shell;

namespace DynamicIsland.Visuals;

/// <summary>
/// 液态玻璃只画在岛的超椭圆上。中间几乎透明，桌面从岛身透出来；
/// 边缘是一层按高度场计算的薄流体，高光和焦散跟着表面走。
/// 不改窗口整体透明度，也不铺近黑遮罩。白字靠淡光晕保持可读。
/// </summary>
static class GlassSkin
{
    static readonly DropShadowEffect Shadow = MakeShadow();
    static readonly DropShadowEffect TextHalo = MakeTextHalo();
    static readonly Brush Body = MakeBody();
    static readonly Brush Scrim = MakeScrim();
    static readonly Brush Thickness = MakeThickness();
    static readonly Brush Specular = MakeSpecular();
    static readonly Brush Lip = MakeLip();
    static readonly Brush Bead = MakeBead();
    static readonly Brush Rim = MakeRim();
    static readonly Brush BlobCool = MakeBlob(0x24, 0xD4, 0xF2, 0xFF);
    static readonly Brush BlobCooler = MakeBlob(0x1A, 0xB0, 0xE0, 0xF6);
    static readonly Brush Streak = MakeStreak();

    public static void Apply(IslandWindow window, IslandSkin skin)
    {
        if (skin == IslandSkin.Glass)
        {
            window.Pill.Fill = Body;
            window.Pill.Effect = Shadow;
            window.PillStroke.Stroke = Rim;
            window.PillStroke.StrokeThickness = 1.5;
            window.GlassHost.Visibility = Visibility.Visible;
            window.GlassScrim.Fill = Scrim;
            window.GlassThickness.Fill = Thickness;
            window.GlassSpecular.Fill = Specular;
            window.GlassLip.Fill = Lip;
            window.GlassBead.Fill = Bead;
            window.GlassBlobA.Fill = BlobCool;
            window.GlassBlobB.Fill = BlobCooler;
            window.GlassStreak.Fill = Streak;
            window.ContentHost.Effect = TextHalo;
        }
        else
        {
            ResetShifts(window);
            window.GlassHost.Visibility = Visibility.Collapsed;
            UpdateRefraction(window, null);
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
        LayoutBlobs(window, width, height, null);
        if (window.GlassHost.Visibility != Visibility.Visible || !animate)
            ResetShifts(window);
    }

    public static void ApplySurface(IslandWindow window, LiquidSurface surface, double width, double height, double vx, double vy, bool moving)
    {
        LayoutBlobs(window, width, height, surface);
        SetFlow(window, vx, vy, moving);
    }

    static void LayoutBlobs(IslandWindow window, double width, double height, LiquidSurface? surface)
    {
        width = Math.Max(12, width);
        height = Math.Max(12, height);
        bool compact = height < 64;
        double pack = compact ? 0.55 : 1;
        var crest = surface?.PrimaryCrest() ?? (0.38, 0.36, 0);
        var second = surface?.SecondaryCrest(crest) ?? (0.68, 0.62, 0);
        var spec = surface?.SpecularPoint() ?? (0.28, 0.22, 0);
        float energy = surface == null ? 0.35f : Math.Clamp(surface.PeakAbsHeight / 0.45f, 0, 1);

        Place(window.GlassBead, width * 0.20 * pack, height * 0.18 * pack,
            (spec.U - 0.5) * width, (spec.V - 0.5) * height, (compact ? 0.42 : 0.72) + 0.22 * energy);
        Place(window.GlassBlobA, width * 0.34 * pack, height * 0.36 * pack,
            (crest.U - 0.5) * width, (crest.V - 0.5) * height, (compact ? 0.16 : 0.28) + 0.22 * energy);
        Place(window.GlassBlobB, width * 0.26 * pack, height * 0.30 * pack,
            (second.U - 0.5) * width, (second.V - 0.5) * height, (compact ? 0.10 : 0.20) + 0.16 * energy);
        Place(window.GlassStreak, width * 0.50 * pack, Math.Max(6, height * 0.07),
            ((crest.U + second.U) * 0.5 - 0.5) * width, ((crest.V + second.V) * 0.5 - 0.5) * height,
            (compact ? 0.12 : 0.26) + 0.18 * energy);
    }

    static void Place(Ellipse blob, double w, double h, double x, double y, double opacity)
    {
        blob.Width = Math.Max(8, w);
        blob.Height = Math.Max(6, h);
        blob.HorizontalAlignment = HorizontalAlignment.Center;
        blob.VerticalAlignment = VerticalAlignment.Center;
        blob.Margin = new Thickness(x, y, -x, -y);
        blob.Opacity = Math.Clamp(opacity, 0.06, 1);
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

    public static void UpdateRefraction(IslandWindow window, ImageSource? image)
    {
        window.GlassRefraction.Source = image;
        window.GlassRefraction.Visibility = image == null ? Visibility.Collapsed : Visibility.Visible;
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
    }

    static DropShadowEffect MakeShadow()
    {
        var effect = new DropShadowEffect
        {
            Color = Color.FromRgb(0x08, 0x16, 0x22),
            BlurRadius = 18,
            ShadowDepth = 5,
            Direction = 270,
            Opacity = 0.22,
            RenderingBias = RenderingBias.Performance
        };
        effect.Freeze();
        return effect;
    }

    static DropShadowEffect MakeTextHalo()
    {
        var effect = new DropShadowEffect
        {
            Color = Color.FromRgb(0x04, 0x0A, 0x14),
            BlurRadius = 7,
            ShadowDepth = 0,
            Opacity = 0.82,
            RenderingBias = RenderingBias.Performance
        };
        effect.Freeze();
        return effect;
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

    static Brush MakeScrim()
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.50, 0.46),
            Center = new Point(0.50, 0.50),
            RadiusX = 0.96,
            RadiusY = 0.98
        };
        // 中心完全透出桌面，只在外沿留一圈很淡的冷色月牙。
        brush.GradientStops.Add(Stop(0x00, 0xE8, 0xF4, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x00, 0xD0, 0xE8, 0xF8, 0.58));
        brush.GradientStops.Add(Stop(0x10, 0xC0, 0xDC, 0xF0, 0.84));
        brush.GradientStops.Add(Stop(0x24, 0xB0, 0xD0, 0xE8, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeThickness()
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.50, 0.46),
            Center = new Point(0.50, 0.50),
            RadiusX = 0.98,
            RadiusY = 1.00
        };
        brush.GradientStops.Add(Stop(0x00, 0xF4, 0xFB, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x00, 0xE8, 0xF4, 0xFF, 0.64));
        brush.GradientStops.Add(Stop(0x16, 0xD8, 0xEE, 0xFF, 0.86));
        brush.GradientStops.Add(Stop(0x2C, 0xEC, 0xF8, 0xFF, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeSpecular()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.06, 0.00),
            EndPoint = new Point(0.62, 0.48)
        };
        brush.GradientStops.Add(Stop(0x68, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x28, 0xE0, 0xF2, 0xFF, 0.18));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 0.44));
        brush.Freeze();
        return brush;
    }

    static Brush MakeLip()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 0.16)
        };
        brush.GradientStops.Add(Stop(0x7A, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x2E, 0xF0, 0xF8, 0xFF, 0.34));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeBead()
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.30, 0.24),
            Center = new Point(0.40, 0.36),
            RadiusX = 0.62,
            RadiusY = 0.62
        };
        brush.GradientStops.Add(Stop(0xC0, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x5A, 0xF4, 0xFB, 0xFF, 0.28));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 1));
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

    static Brush MakeBlob(byte a, byte r, byte g, byte b)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.32, 0.28),
            Center = new Point(0.42, 0.40),
            RadiusX = 0.72,
            RadiusY = 0.72
        };
        brush.GradientStops.Add(Stop(a, r, g, b, 0));
        brush.GradientStops.Add(Stop((byte)(a * 0.45), r, g, b, 0.45));
        brush.GradientStops.Add(Stop(0x00, r, g, b, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeStreak()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x3A, 0xF4, 0xFB, 0xFF, 0.46));
        brush.GradientStops.Add(Stop(0x20, 0xD8, 0xEE, 0xFF, 0.62));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 1));
        brush.Freeze();
        return brush;
    }

    static GradientStop Stop(byte a, byte r, byte g, byte b, double offset) =>
        new(Color.FromArgb(a, r, g, b), offset);
}
