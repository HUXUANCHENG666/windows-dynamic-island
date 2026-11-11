namespace DynamicIsland.Core.Motion;

/// <summary>
/// 一个带轻微过冲的弹簧。用解析解而不是逐帧欧拉积分，
/// 这样同一套参数在 60Hz 和掉帧时走的是同一条曲线。
/// 位移 x 满足 x'' + 2ζω x' + ω² x = 0，x 是离目标的偏差。
/// </summary>
public sealed class Spring
{
    public SpringConfig Config { get; set; }
    public double Value { get; set; }
    public double Velocity { get; set; }
    public double Target { get; set; }

    public Spring(SpringConfig config, double initial = 0)
    {
        Config = config;
        Value = initial;
        Target = initial;
    }

    public void Jump(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    public bool IsSettled(double positionEpsilon, double velocityEpsilon) =>
        Math.Abs(Value - Target) <= positionEpsilon && Math.Abs(Velocity) <= velocityEpsilon;

    public void Step(double dt)
    {
        if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt))
            return;

        // A multi-second stall (machine sleep) should land on the target.
        // A single long frame still follows the curve instead of exploding.
        if (dt >= 0.5)
        {
            Value = Target;
            Velocity = 0;
            return;
        }

        Integrate(dt);
    }

    void Integrate(double dt)
    {
        double k = Config.Stiffness;
        double c = Config.Damping;
        if (k <= 1e-6)
        {
            Value = Target;
            Velocity = 0;
            return;
        }

        double omega = Math.Sqrt(k);
        double zeta = c / (2 * omega);
        double x = Value - Target;
        double v = Velocity;

        if (zeta < 0.999)
        {
            double wd = omega * Math.Sqrt(Math.Max(0, 1 - zeta * zeta));
            if (wd < 1e-8)
            {
                Critical(omega, x, v, dt);
                return;
            }

            double e = Math.Exp(-zeta * omega * dt);
            double a = x;
            double b = (v + zeta * omega * x) / wd;
            double cos = Math.Cos(wd * dt);
            double sin = Math.Sin(wd * dt);
            double x2 = e * (a * cos + b * sin);
            double v2 = (-zeta * omega) * x2 + e * ((-a * wd) * sin + (b * wd) * cos);
            Value = Target + x2;
            Velocity = v2;
        }
        else if (zeta > 1.001)
        {
            double disc = Math.Sqrt(zeta * zeta - 1);
            double r1 = -zeta * omega + omega * disc;
            double r2 = -zeta * omega - omega * disc;
            double denom = r1 - r2;
            if (Math.Abs(denom) < 1e-8)
            {
                Critical(omega, x, v, dt);
                return;
            }

            double a = (v - r2 * x) / denom;
            double b = x - a;
            double e1 = Math.Exp(r1 * dt);
            double e2 = Math.Exp(r2 * dt);
            Value = Target + a * e1 + b * e2;
            Velocity = a * r1 * e1 + b * r2 * e2;
        }
        else
        {
            Critical(omega, x, v, dt);
        }
    }

    void Critical(double omega, double x, double v, double dt)
    {
        double e = Math.Exp(-omega * dt);
        double a = x;
        double b = v + omega * x;
        double x2 = (a + b * dt) * e;
        double v2 = (b - omega * (a + b * dt)) * e;
        Value = Target + x2;
        Velocity = v2;
    }
}
