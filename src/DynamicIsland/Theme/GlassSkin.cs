using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DynamicIsland.Core.Island;
using DynamicIsland.Shell;

namespace DynamicIsland.Visuals;

/// <summary>
/// 液态玻璃只画在岛的超椭圆上：半透明的玻璃体、中间的可读底、湿润高光和缓慢流动的光斑。
/// 不改窗口整体透明度，所以文字仍是原来的高对比。收起并停稳后停掉动画，避免空转。
/// </summary>
static class GlassSkin
{
    static readonly DropShadowEffect Shadow = MakeShadow();
    static readonly Brush Body = MakeBody();
    static readonly Brush Scrim = MakeScrim();
    static readonly Brush Specular = MakeSpecular();
    static readonly Brush Lip = MakeLip();
    static readonly Brush Rim = MakeRim();
    static readonly Brush BlobCool = MakeBlob(0x48, 0xD8, 0xF0, 0xF8);
    static readonly Brush BlobWarm = MakeBlob(0x38, 0xFF, 0xE4, 0xC8);
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
            window.PillStroke.StrokeThickness = 1.9;
            window.GlassHost.Visibility = Visibility.Visible;
            window.GlassScrim.Fill = Scrim;
            window.GlassSpecular.Fill = Specular;
            window.GlassLip.Fill = Lip;
            window.GlassBlobA.Fill = BlobCool;
            window.GlassBlobB.Fill = BlobWarm;
            window.GlassStreak.Fill = Streak;
        }
        else
        {
            StopMotion(window, reset: true);
            window.GlassHost.Visibility = Visibility.Collapsed;
            window.Pill.Fill = Theme.IslandFill;
            window.PillStroke.Stroke = Theme.Hairline;
            window.PillStroke.StrokeThickness = 1.6;
        }
    }

    public static void Decorate(IslandWindow window, Geometry geometry, double width, double height, bool animate)
    {
        window.GlassHost.Clip = geometry;
        window.GlassScrim.Data = geometry;
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

        Place(window.GlassBlobA, width * 0.42 * pack, height * 0.46 * pack,
            width * -0.16, height * -0.18, compact ? 0.42 : 0.78);
        Place(window.GlassBlobB, width * 0.36 * pack, height * 0.40 * pack,
            width * 0.18, height * 0.16, compact ? 0.28 : 0.58);
        Place(window.GlassStreak, width * 0.62 * pack, Math.Max(8, height * 0.10),
            width * 0.02, height * -0.06, compact ? 0.22 : 0.42);
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
        window.GlassBlobAShift.X = 0;
        window.GlassBlobAShift.Y = 0;
        window.GlassBlobBShift.X = 0;
        window.GlassBlobBShift.Y = 0;
        window.GlassStreakShift.X = 0;
        window.GlassStreakShift.Y = 0;
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
            Color = Colors.Black,
            BlurRadius = 24,
            ShadowDepth = 8,
            Direction = 270,
            Opacity = 0.34,
            RenderingBias = RenderingBias.Performance
        };
        effect.Freeze();
        return effect;
    }

    static Brush MakeBody()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.18, 0),
            EndPoint = new Point(0.62, 1)
        };
        brush.GradientStops.Add(Stop(0x42, 0xE4, 0xEE, 0xF6, 0));
        brush.GradientStops.Add(Stop(0x30, 0x88, 0x9C, 0xAA, 0.12));
        brush.GradientStops.Add(Stop(0x58, 0x14, 0x18, 0x20, 0.40));
        brush.GradientStops.Add(Stop(0x8A, 0x07, 0x09, 0x0C, 0.74));
        brush.GradientStops.Add(Stop(0xB3, 0x02, 0x03, 0x05, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeScrim()
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.50, 0.44),
            Center = new Point(0.50, 0.48),
            RadiusX = 0.72,
            RadiusY = 0.82
        };
        brush.GradientStops.Add(Stop(0xB0, 0x00, 0x00, 0x00, 0));
        brush.GradientStops.Add(Stop(0x86, 0x00, 0x00, 0x00, 0.48));
        brush.GradientStops.Add(Stop(0x36, 0x00, 0x00, 0x00, 0.82));
        brush.GradientStops.Add(Stop(0x00, 0x00, 0x00, 0x00, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeSpecular()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.04, 0.00),
            EndPoint = new Point(0.70, 0.62)
        };
        brush.GradientStops.Add(Stop(0x5C, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x26, 0xDE, 0xEC, 0xF8, 0.22));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 0.58));
        brush.Freeze();
        return brush;
    }

    static Brush MakeLip()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 0.22)
        };
        brush.GradientStops.Add(Stop(0x6A, 0xFF, 0xFF, 0xFF, 0));
        brush.GradientStops.Add(Stop(0x28, 0xF2, 0xF7, 0xFF, 0.28));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 1));
        brush.Freeze();
        return brush;
    }

    static Brush MakeRim()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(Stop(0x68, 0xB8, 0xD8, 0xEC, 0));
        brush.GradientStops.Add(Stop(0x4A, 0xE8, 0xEA, 0xF0, 0.42));
        brush.GradientStops.Add(Stop(0x58, 0xE0, 0xC4, 0xA4, 1));
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
        brush.GradientStops.Add(Stop(0x3A, 0xF4, 0xFB, 0xFF, 0.45));
        brush.GradientStops.Add(Stop(0x22, 0xFF, 0xE8, 0xC8, 0.62));
        brush.GradientStops.Add(Stop(0x00, 0xFF, 0xFF, 0xFF, 1));
        brush.Freeze();
        return brush;
    }

    static GradientStop Stop(byte a, byte r, byte g, byte b, double offset) =>
        new(Color.FromArgb(a, r, g, b), offset);
}
