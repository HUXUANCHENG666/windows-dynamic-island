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

    public void SetTarget(IslandMode mode, bool expanded)
    {
        var frame = IslandMetrics.FrameFor(mode, expanded);
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

    public void Apply(IslandWindow window, double dpi)
    {
        double width = Math.Max(16, _width.Value);
        double height = Math.Max(16, _height.Value);
        double radius = Squircle.SafeRadius(width, height, _radius.Value);
        double corner = Math.Clamp(_corner.Value, 1.2, 8);
        double snapW = WindowPlacement.SnapDip(width, dpi);
        double snapH = WindowPlacement.SnapDip(height, dpi);
        double left = WindowPlacement.SnapDip((window.Width - snapW) / 2.0, dpi);
        double top = WindowPlacement.SnapDip(IslandMetrics.TopMargin, dpi);

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
        PlaceFixed(window.ExpandedIdle, Presence(IslandMode.Idle) * expandFade, Easing.ExpandedShift(expand),
            IslandMetrics.IdleExpandedW, IslandMetrics.IdleExpandedH, snapW, snapH);
        PlaceFixed(window.ExpandedMedia, Presence(IslandMode.Media) * expandFade, Easing.ExpandedShift(expand),
            IslandMetrics.MediaExpandedW, IslandMetrics.MediaExpandedH, snapW, snapH);
        PlaceFixed(window.ExpandedTimer, Presence(IslandMode.Timer) * expandFade, Easing.ExpandedShift(expand),
            IslandMetrics.TimerExpandedW, IslandMetrics.TimerExpandedH, snapW, snapH);
        PlaceStretch(window.CompactMedia, Presence(IslandMode.Media) * collapseFade, Easing.CompactShift(expand),
            snapW, IslandMetrics.MediaCompactH);
        PlaceStretch(window.CompactTimer, Presence(IslandMode.Timer) * collapseFade, Easing.CompactShift(expand),
            snapW, IslandMetrics.TimerCompactH);
        PlaceFill(window.VolumeNotice, Presence(IslandMode.Volume) * expandFade, Easing.ExpandedShift(expand), snapW, snapH);
        PlaceFill(window.BatteryNotice, Presence(IslandMode.Battery) * expandFade, Easing.ExpandedShift(expand), snapW, snapH);
        PlaceFill(window.ToastNotice, Presence(IslandMode.Toast) * expandFade, Easing.ExpandedShift(expand), snapW, snapH);
    }

    double Presence(IslandMode mode) => Math.Clamp(_presence[mode].Value, 0, 1.15);

    static void PlaceFixed(FrameworkElement view, double opacity, double shift, double designW, double designH, double pillW, double pillH)
    {
        double shown = Math.Clamp(opacity, 0, 1);
        if (shown < 0.012)
        {
            Hide(view);
            return;
        }

        double fit = Math.Min(pillW / designW, pillH / designH);
        Show(view, shown, Math.Clamp(fit, 0.45, 1.06), shift);
        view.Width = designW;
        view.Height = designH;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
    }

    static void PlaceStretch(FrameworkElement view, double opacity, double shift, double width, double height)
    {
        double shown = Math.Clamp(opacity, 0, 1);
        if (shown < 0.012)
        {
            Hide(view);
            return;
        }

        Show(view, shown, 1, shift);
        view.Width = width;
        view.Height = height;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
    }

    static void PlaceFill(FrameworkElement view, double opacity, double shift, double width, double height)
    {
        double shown = Math.Clamp(opacity, 0, 1);
        if (shown < 0.012)
        {
            Hide(view);
            return;
        }

        Show(view, shown, 1, shift);
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

    static void Show(FrameworkElement view, double opacity, double scale, double shift)
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

        var scaleTransform = (ScaleTransform)group.Children[0];
        scaleTransform.ScaleX = scale;
        scaleTransform.ScaleY = scale;
        ((TranslateTransform)group.Children[1]).Y = shift;

        double blur = Easing.BlurFor(opacity);
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
