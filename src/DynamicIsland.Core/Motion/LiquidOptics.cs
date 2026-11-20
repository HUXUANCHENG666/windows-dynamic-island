using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>
/// 透过玻璃的世界坐标光学滞后：岛的位置仍 1:1 跟手，采样坐标带阻尼并收敛。
/// 不是冻结旧截图，也不是整块平移玻璃层。
/// </summary>
public readonly record struct LiquidOpticsFrame(double LagX, double LagY, double LagScaleX, double LagScaleY, double Strength)
{
    public static LiquidOpticsFrame Identity { get; } = new(0, 0, 1, 1, 0);
    public bool IsActive => Strength > 0.01;
}

public sealed class LiquidOptics
{
    public const double MaxLagDip = 48;
    public const double MaxSizeLagDip = 56;
    public const double InteriorSlopeWarp = 2.5;
    public const double InteriorAlpha = 0.50;
    public const double SettlePosition = 0.35;
    public const double SettleVelocity = 2;
    public const double TeleportPixels = 240;
    public static SpringConfig Spring { get; } = new(81, 17);

    readonly Spring _x = new(Spring, 0);
    readonly Spring _y = new(Spring, 0);
    readonly Spring _w = new(Spring, 1);
    readonly Spring _h = new(Spring, 1);
    bool _primed;

    public double LagX => _x.Target - _x.Value;
    public double LagY => _y.Target - _y.Value;
    public double LaggedWidth => _w.Value;
    public double LaggedHeight => _h.Value;
    public bool IsSettled => !_primed ||
        (_x.IsSettled(SettlePosition, SettleVelocity) && _y.IsSettled(SettlePosition, SettleVelocity)
         && _w.IsSettled(SettlePosition, SettleVelocity) && _h.IsSettled(SettlePosition, SettleVelocity));

    public void Reset()
    {
        _primed = false;
        _x.Jump(0); _y.Jump(0); _w.Jump(1); _h.Jump(1);
    }

    public void Jump(double x, double y, double width, double height)
    {
        _x.Jump(x); _y.Jump(y); _w.Jump(Math.Max(1, width)); _h.Jump(Math.Max(1, height));
        _primed = true;
    }

    public void Step(double dt, double x, double y, double width, double height, double dpi = 1)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        dpi = Math.Max(0.5, dpi);
        if (!_primed)
        {
            Jump(x, y, width, height);
            return;
        }

        double jump = Math.Sqrt((x - _x.Target)*(x - _x.Target) + (y - _y.Target)*(y - _y.Target))
            + Math.Abs(width - _w.Target) + Math.Abs(height - _h.Target);
        if (jump >= TeleportPixels * dpi)
        {
            Jump(x, y, width, height);
            return;
        }

        _x.Target = x; _y.Target = y; _w.Target = width; _h.Target = height;
        _x.Step(dt); _y.Step(dt); _w.Step(dt); _h.Step(dt);
        Clamp(_x, x, MaxLagDip * dpi);
        Clamp(_y, y, MaxLagDip * dpi);
        Clamp(_w, width, MaxSizeLagDip * dpi);
        Clamp(_h, height, MaxSizeLagDip * dpi);
    }

    public LiquidOpticsFrame Frame(double scaleX = 1, double scaleY = 1)
    {
        scaleX = scaleX == 0 ? 1 : scaleX;
        scaleY = scaleY == 0 ? 1 : scaleY;
        double lagX = LagX / scaleX;
        double lagY = LagY / scaleY;
        double lagSx = _w.Value / Math.Max(1, _w.Target);
        double lagSy = _h.Value / Math.Max(1, _h.Target);
        double travel = Math.Sqrt(LagX * LagX + LagY * LagY)
            + 0.5 * (Math.Abs(_w.Target - _w.Value) + Math.Abs(_h.Target - _h.Value));
        double strength = IslandOutline.Smootherstep(0.75, 8, travel);
        return strength <= 0.01 ? LiquidOpticsFrame.Identity : new(lagX, lagY, lagSx, lagSy, strength);
    }

    public static bool TryMapInterior(
        double x, double y, double width, double height, int pad, double radius, double dpi,
        LiquidOpticsFrame optics, double slopeX, double slopeY, double surface,
        out double u, out double v, out double alpha)
    {
        u = v = alpha = 0;
        if (!optics.IsActive || width < 2 || height < 2 || pad < 4)
            return false;
        if (radius < 0)
            radius = Math.Min(width, height) * 0.5;
        radius = Squircle.SafeRadius(width, height, radius);
        double d = IslandOutline.SignedDistance(x, y, width, height, radius);
        double band = IslandOutline.RimBand(width, height, dpi);
        if (d >= -band)
            return false;

        double sx = x * optics.LagScaleX - optics.LagX
            + slopeX / (1 + Math.Abs(slopeX)) * InteriorSlopeWarp * dpi;
        double sy = y * optics.LagScaleY - optics.LagY
            + slopeY / (1 + Math.Abs(slopeY)) * InteriorSlopeWarp * dpi;
        u = pad + sx;
        v = pad + sy;
        double near = IslandOutline.Smootherstep(-band - 4 * dpi, -band + 2 * dpi, d);
        alpha = optics.Strength * (0.42 + 0.28 * Math.Clamp(Math.Abs(surface), 0, 1))
            * (1 - 0.35 * near) * (InteriorAlpha / 0.50);
        alpha = Math.Clamp(alpha, 0, InteriorAlpha);
        return alpha > 0.01;
    }

    static void Clamp(Spring spring, double target, double max)
    {
        double delta = spring.Value - target;
        if (Math.Abs(delta) <= max)
            return;
        spring.Value = target + Math.CopySign(max, delta);
        spring.Velocity = 0;
    }
}
