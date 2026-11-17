using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DynamicIsland.Core.Island;
using DynamicIsland.Shell;

namespace DynamicIsland.Visuals;

/// <summary>
/// 液态玻璃只画在岛的超椭圆上，按水滴来画：清澈的冷色玻璃体、中间略厚边缘更透、
/// 顶部的水珠高光，以及缓慢流动的折射光斑。不改窗口整体透明度。白字靠淡阴影保持可读，
/// 不再铺一层近黑遮罩。收起并停稳后停掉动画，避免空转。
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
    static readonly Brush BlobCool = MakeBlob(0x3A, 0xD4, 0xF2, 0xFF);
    static readonly Brush BlobCooler = MakeBlob(0x28, 0xB0, 0xE0, 0xF6);
    static readonly Brush Streak = MakeStreak();

    static Storyboard? _motion;
    static bool _running;
    static double _motionWidth;
    static double _motionHeight;

    public static void Apply(IslandWindow window, IslandSkin skin)
    {
        if (skin == IslandSkin.Glass)
        {
            window.Pill.Fill = Body;
            window.Pill.Effect = Shadow;
            window.PillStroke.Stroke = Rim;
            window.PillStroke.StrokeThickness = 1.7;
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
            StopMotion(window, reset: true);
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
        LayoutBlobs(window, width, height);
        if (window.GlassHost.Visibility != Visibility.Visible)
        {
            StopMotion(window, reset: false);
            return;
        }

        if (animate)
            StartMotion(window, width, height);
        else
            StopMotion(window, reset: true);
    }

    static void LayoutBlobs(IslandWindow window, double width, double height)
    {
        width = Math.Max(12, width);
        height = Math.Max(12, height);
        bool compact = height < 64;
        double pack = compact ? 0.55 : 1;

        Place(window.GlassBead, width * 0.22 * pack, height * 0.20 * pack,
            width * -0.22, height * -0.30, compact ? 0.55 : 0.92);
        Place(window.GlassBlobA, width * 0.38 * pack, height * 0.40 * pack,
            width * -0.14, height * -0.16, compact ? 0.28 : 0.48);
        Place(window.GlassBlobB, width * 0.30 * pack, height * 0.34 * pack,
            width * 0.20, height * 0.14, compact ? 0.18 : 0.34);
        Place(window.GlassStreak, width * 0.58 * pack, Math.Max(7, height * 0.08),
            width * 0.04, height * -0.08, compact ? 0.20 : 0.40);
    }

    static void Place(Ellipse blob, double w, double h, double x, double y, double opacity)
    {
        blob.Width = Math.Max(8, w);
        blob.Height = Math.Max(6, h);
        blob.HorizontalAlignment = HorizontalAlignment.Center;
        blob.VerticalAlignment = VerticalAlignment.Center;
        blob.Margin = new Thickness(x, y, -x, -y);
        blob.Opacity = opacity;
    }

    static void StartMotion(IslandWindow window, double width, double height)
    {
        if (_running && Math.Abs(_motionWidth - width) < 12 && Math.Abs(_motionHeight - height) < 12)
            return;

        StopMotion(window, reset: false);
        double ax = Math.Max(6, width * 0.055);
        double ay = Math.Max(4, height * 0.06);
        var board = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        board.Children.Add(Drift(window.GlassBeadShift, TranslateTransform.XProperty, -ax * 0.25, ax * 0.30, 14.0));
        board.Children.Add(Drift(window.GlassBeadShift, TranslateTransform.YProperty, ay * 0.12, -ay * 0.18, 11.5));
        board.Children.Add(Drift(window.GlassBlobAShift, TranslateTransform.XProperty, -ax, ax, 12.5));
        board.Children.Add(Drift(window.GlassBlobAShift, TranslateTransform.YProperty, ay * 0.45, -ay, 15.0));
        board.Children.Add(Drift(window.GlassBlobBShift, TranslateTransform.XProperty, ax * 0.8, -ax * 0.9, 16.5));
        board.Children.Add(Drift(window.GlassBlobBShift, TranslateTransform.YProperty, -ay * 0.7, ay * 0.55, 13.5));
        board.Children.Add(Drift(window.GlassStreakShift, TranslateTransform.XProperty, -ax * 1.1, ax * 1.2, 18.0));
        board.Children.Add(Drift(window.GlassStreakShift, TranslateTransform.YProperty, ay * 0.35, -ay * 0.4, 11.0));
        _motion = board;
        board.Begin();
        _running = true;
        _motionWidth = width;
        _motionHeight = height;
    }

    static void StopMotion(IslandWindow window, bool reset)
    {
        if (_motion != null)
        {
            _motion.Stop();
            _motion.Remove();
            _motion = null;
        }

        _running = false;
        if (!reset)
            return;
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
        double lagX = moving ? Math.Clamp(-vx * 0.035, -28, 28) : 0;
        double lagY = moving ? Math.Clamp(-vy * 0.035, -28, 28) : 0;
        window.GlassBeadLag.X = lagX * 0.45;
        window.GlassBeadLag.Y = lagY * 0.45;
        window.GlassBlobALag.X = lagX;
        window.GlassBlobALag.Y = lagY;
        window.GlassBlobBLag.X = lagX * 0.8;
        window.GlassBlobBLag.Y = lagY * 0.8;
        window.GlassStreakLag.X = lagX * 1.15;
        window.GlassStreakLag.Y = lagY * 1.15;
    }

    static DoubleAnimation Drift(TranslateTransform target, DependencyProperty property, double from, double to, double seconds)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(property));
        return anim;
    }

    static DropShadowEffect MakeShadow()
    {
        var effect = new DropShadowEffect
        {
            Color = Color.FromRgb(0x08, 0x16, 0x22),
            BlurRadius = 20,
            ShadowDepth = 6,
            Direction = 270,
            Opacity = 0.26,
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
            StartPoint = new Point(0.22, 0),
            EndPoint = new Point(0.55, 1)
        };
        brush.GradientStops.Add(Stop(0x3A, 0xF4, 0xFB, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x2C, 0xC6, 0xE4, 0xF2, 0.16));
        brush.GradientStops.Add(Stop(0x3C, 0x7A, 0xA4, 0xB8, 0.48));
        brush.GradientStops.Add(Stop(0x4A, 0x4A, 0x6E, 0x86, 0.78));
        brush.GradientStops.Add(Stop(0x36, 0x36, 0x56, 0x6A, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeScrim()
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.50, 0.40),
            Center = new Point(0.50, 0.46),
            RadiusX = 0.78,
            RadiusY = 0.88
        };
        brush.GradientStops.Add(Stop(0x3A, 0x12, 0x28, 0x38, 0));
        brush.GradientStops.Add(Stop(0x26, 0x14, 0x2A, 0x3C, 0.52));
        brush.GradientStops.Add(Stop(0x10, 0x1A, 0x34, 0x48, 0.80));
        brush.GradientStops.Add(Stop(0x00, 0x1A, 0x34, 0x48, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeThickness()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0.42),
            EndPoint = new Point(0.5, 1)
        };
        brush.GradientStops.Add(Stop(0x00, 0x20, 0x3A, 0x52, 0));
        brush.GradientStops.Add(Stop(0x10, 0x20, 0x3A, 0x52, 0.48));
        brush.GradientStops.Add(Stop(0x28, 0x1A, 0x36, 0x4C, 0.78));
        brush.GradientStops.Add(Stop(0x3A, 0x14, 0x2C, 0x40, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeSpecular()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.06, 0.00),
            EndPoint = new Point(0.62, 0.52)
        };
        brush.GradientStops.Add(Stop(0x8A, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x3A, 0xE0, 0xF2, 0xFF, 0.18));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 0.46));
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
        brush.GradientStops.Add(Stop(0x96, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x3C, 0xF0, 0xF8, 0xFF, 0.34));
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
        brush.GradientStops.Add(Stop(0xC8, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x6A, 0xF4, 0xFB, 0xFF, 0.28));
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
        brush.GradientStops.Add(Stop(0x78, 0xFF, 0xFF, 0xFF, 0.38));
        brush.GradientStops.Add(Stop(0x5A, 0xC8, 0xE4, 0xF4, 1));
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
        brush.GradientStops.Add(Stop(0x4A, 0xF4, 0xFB, 0xFF, 0.46));
        brush.GradientStops.Add(Stop(0x28, 0xD8, 0xEE, 0xFF, 0.62));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 1));
        brush.Freeze();
        return brush;
    }

    static GradientStop Stop(byte a, byte r, byte g, byte b, double offset) =>
        new(Color.FromArgb(a, r, g, b), offset);
}
