using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Motion;

namespace DynamicIsland.Shell;

/// <summary>
/// 宽、高、圆角、超椭圆指数和展开进度共用同一套弹簧，所以它是一块东西在变形。
/// 内容的透明度、模糊和轻微位移跟展开进度走，不自己乱弹。
/// </summary>
sealed class IslandAnimator
{
    readonly Spring _width = new(SpringConfig.Island);
    readonly Spring _height = new(SpringConfig.Island);
    readonly Spring _radius = new(SpringConfig.Island);
    readonly Spring _corner = new(SpringConfig.Island);
    readonly Spring _expand = new(SpringConfig.Island);
    readonly Spring _opacity = new(SpringConfig.Appear);
    readonly Dictionary<IslandMode, Spring> _presence = new();

    public IslandAnimator()
    {
        foreach (IslandMode mode in Enum.GetValues<IslandMode>())
            _presence[mode] = new Spring(SpringConfig.Presence, mode == IslandMode.Idle ? 1 : 0);
    }

    public bool IsSettled =>
        _width.IsSettled(0.2, 1.2)
        && _height.IsSettled(0.2, 1.2)
        && _radius.IsSettled(0.2, 1.2)
        && _corner.IsSettled(0.01, 0.05)
        && _expand.IsSettled(0.004, 0.02)
        && _opacity.IsSettled(0.01, 0.05)
        && _presence.Values.All(spring => spring.IsSettled(0.004, 0.02));

    public void PlayIntro()
    {
        var frame = IslandMetrics.FrameFor(IslandMode.Idle, expanded: false);
        _width.Value = frame.Width * 0.82;
        _width.Target = frame.Width;
        _height.Value = frame.Height * 0.82;
        _height.Target = frame.Height;
        _radius.Value = _height.Value / 2;
        _radius.Target = frame.Radius;
        _corner.Value = 2;
        _corner.Target = frame.CornerN;
        _expand.Value = 0;
        _expand.Target = 0;
        _opacity.Value = 0;
        _opacity.Target = 1;
        foreach (var pair in _presence)
            pair.Value.Jump(pair.Key == IslandMode.Idle ? 1 : 0);
    }

    public void SetTarget(IslandMode mode, bool expanded, bool dot, IslandOrientation orientation)
    {
        var frame = dot ? IslandMetrics.StealthDot : IslandMetrics.FrameFor(mode, expanded, orientation);
        _width.Target = frame.Width;
        _height.Target = frame.Height;
        _radius.Target = frame.Radius;
        _corner.Target = frame.CornerN;
        _expand.Target = expanded ? 1 : 0;
        foreach (var pair in _presence)
            pair.Value.Target = pair.Key == mode ? 1 : 0;
    }

    public void Step(double dt)
    {
        _width.Step(dt);
        _height.Step(dt);
        _radius.Step(dt);
        _corner.Step(dt);
        _expand.Step(dt);
        _opacity.Step(dt);
        foreach (var spring in _presence.Values)
            spring.Step(dt);
    }

    public void Apply(IslandWindow window, double dpi, DockAnchor anchor, bool allowBlur)
    {
        double width = Math.Max(12, _width.Value);
        double height = Math.Max(12, _height.Value);
        double radius = Squircle.SafeRadius(width, height, _radius.Value);
        double corner = Math.Clamp(_corner.Value, 1.2, 8);
        double snapW = WindowPlacement.SnapDip(width, dpi);
        double snapH = WindowPlacement.SnapDip(height, dpi);
        var orientation = Dock.OrientationOf(anchor);
        var (rawLeft, rawTop) = PillLayout.Offset(anchor, window.Width, window.Height, snapW, snapH, IslandMetrics.TopMargin);
        double left = WindowPlacement.SnapDip(rawLeft, dpi);
        double top = WindowPlacement.SnapDip(rawTop, dpi);

        window.PillHost.Width = snapW;
        window.PillHost.Height = snapH;
        System.Windows.Controls.Canvas.SetLeft(window.PillHost, left);
        System.Windows.Controls.Canvas.SetTop(window.PillHost, top);

        double safeRadius = Squircle.SafeRadius(snapW, snapH, radius);
        var geometry = ToGeometry(Squircle.Build(snapW, snapH, safeRadius, corner, 18));
        window.Pill.Data = geometry;
        window.PillStroke.Data = geometry;
        window.PillStroke.Clip = geometry;
        window.ContentHost.Clip = geometry;
        window.HitGeometry = geometry;

        double slop = IslandMetrics.HitSlop;
        window.SlopGeometry = ToGeometry(Squircle.Build(
            snapW + slop * 2,
            snapH + slop * 2,
            Squircle.SafeRadius(snapW + slop * 2, snapH + slop * 2, safeRadius + slop),
            corner,
            12));

        double windowOpacity = Math.Clamp(_opacity.Value, 0, 1);
        if (Math.Abs(window.Opacity - windowOpacity) > 0.002)
            window.Opacity = windowOpacity;

        double expand = _expand.Value;
        double expandFade = Easing.ExpandFade(expand);
        double collapseFade = Easing.CollapseFade(expand);
        var openShift = MotionLayout.ContentShift(anchor, Easing.ExpandedShift(expand));
        var shutShift = MotionLayout.ContentShift(anchor, Easing.CompactShift(expand));
        var idle = IslandMetrics.FrameFor(IslandMode.Idle, true, orientation);
        var media = IslandMetrics.FrameFor(IslandMode.Media, true, orientation);
        var timer = IslandMetrics.FrameFor(IslandMode.Timer, true, orientation);
        var translate = IslandMetrics.FrameFor(IslandMode.Translate, true, orientation);
        var camera = IslandMetrics.FrameFor(IslandMode.Camera, true, orientation);
        PlaceFixed(window.ExpandedIdle, Presence(IslandMode.Idle) * expandFade, openShift, allowBlur,
            idle.Width, idle.Height, snapW, snapH);
        PlaceFixed(window.ExpandedMedia, Presence(IslandMode.Media) * expandFade, openShift, allowBlur,
            media.Width, media.Height, snapW, snapH);
        PlaceFixed(window.ExpandedTimer, Presence(IslandMode.Timer) * expandFade, openShift, allowBlur,
            timer.Width, timer.Height, snapW, snapH);
        PlaceFixed(window.TranslateNotice, Presence(IslandMode.Translate) * expandFade, openShift, allowBlur,
            translate.Width, translate.Height, snapW, snapH);
        PlaceFixed(window.CameraNotice, Presence(IslandMode.Camera) * expandFade, openShift, allowBlur,
            camera.Width, camera.Height, snapW, snapH);
        PlaceStretch(window.CompactMedia, Presence(IslandMode.Media) * collapseFade, shutShift, allowBlur, snapW, snapH);
        PlaceStretch(window.CompactTimer, Presence(IslandMode.Timer) * collapseFade, shutShift, allowBlur, snapW, snapH);
        PlaceFill(window.VolumeNotice, Presence(IslandMode.Volume) * expandFade, openShift, allowBlur, snapW, snapH);
        PlaceFill(window.BatteryNotice, Presence(IslandMode.Battery) * expandFade, openShift, allowBlur, snapW, snapH);
        PlaceFill(window.ToastNotice, Presence(IslandMode.Toast) * expandFade, openShift, allowBlur, snapW, snapH);
    }

    double Presence(IslandMode mode) => Math.Clamp(_presence[mode].Value, 0, 1.15);

    static void PlaceFixed(FrameworkElement view, double opacity, Shift shift, bool allowBlur, double designW, double designH, double pillW, double pillH)
    {
        double shown = Math.Clamp(opacity, 0, 1);
        if (shown < 0.012)
        {
            Hide(view);
            return;
        }

        double fit = Math.Min(pillW / designW, pillH / designH);
        Show(view, shown, Math.Clamp(fit, 0.45, 1.06), shift, allowBlur, opacity);
        view.Width = designW;
        view.Height = designH;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
    }

    static void PlaceStretch(FrameworkElement view, double opacity, Shift shift, bool allowBlur, double width, double height)
    {
        double shown = Math.Clamp(opacity, 0, 1);
        if (shown < 0.012)
        {
            Hide(view);
            return;
        }

        Show(view, shown, 1, shift, allowBlur, opacity);
        view.Width = width;
        view.Height = height;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
    }

    static void PlaceFill(FrameworkElement view, double opacity, Shift shift, bool allowBlur, double width, double height)
    {
        double shown = Math.Clamp(opacity, 0, 1);
        if (shown < 0.012)
        {
            Hide(view);
            return;
        }

        Show(view, shown, 1, shift, allowBlur, opacity);
        view.Width = width;
        view.Height = height;
        view.HorizontalAlignment = HorizontalAlignment.Stretch;
        view.VerticalAlignment = VerticalAlignment.Stretch;
    }

    static void Hide(FrameworkElement view)
    {
        if (view.Visibility != Visibility.Collapsed)
            view.Visibility = Visibility.Collapsed;
        view.Effect = null;
        view.IsHitTestVisible = false;
    }

    static void Show(FrameworkElement view, double opacity, double scale, Shift shift, bool allowBlur, double presence)
    {
        view.Visibility = Visibility.Visible;
        view.Opacity = opacity;
        view.IsHitTestVisible = opacity > 0.62;
        if (view.RenderTransform is not TransformGroup group)
        {
            group = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(1, 1),
                    new TranslateTransform(0, 0)
                }
            };
            view.RenderTransform = group;
            view.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        double entrance = Easing.EntranceScale(presence);
        var scaleTransform = (ScaleTransform)group.Children[0];
        scaleTransform.ScaleX = scale * entrance;
        scaleTransform.ScaleY = scale * entrance;
        var translate = (TranslateTransform)group.Children[1];
        translate.X = shift.X;
        translate.Y = shift.Y;

        double blur = allowBlur ? Easing.BlurFor(opacity) : 0;
        if (blur < 0.4)
        {
            view.Effect = null;
        }
        else if (view.Effect is BlurEffect effect)
        {
            effect.Radius = blur;
        }
        else
        {
            view.Effect = new BlurEffect
            {
                Radius = blur,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            };
        }
    }

    static StreamGeometry ToGeometry(IReadOnlyList<PointD> points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (points.Count == 0)
                return geometry;
            context.BeginFigure(new Point(points[0].X, points[0].Y), isFilled: true, isClosed: true);
            for (int i = 1; i < points.Count; i++)
                context.LineTo(new Point(points[i].X, points[i].Y), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }
}
