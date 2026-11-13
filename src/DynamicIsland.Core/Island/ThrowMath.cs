namespace DynamicIsland.Core.Island;

/// <summary>
/// 甩出去以后的惯性：松手时的速度让岛在工作区里滑行、撞边反弹，速度掉到阈值以下就交给吸附。
/// 这里只做数值模拟，不碰窗口，方便单独验证。
/// </summary>
public static class ThrowMath
{
    /// <summary>松手速度低于这个值时不做抛掷，直接吸附，免得轻轻一放就滑走。</summary>
    public const double LaunchSpeed = 280;

    /// <summary>撞边后保留的速度比例。再高一点，弹跳能多看清一两下。</summary>
    public const double Restitution = 0.70;

    /// <summary>每秒的空气阻尼系数。从 1.3 降到 0.82，滑行大约长出一半，仍会自己停住。</summary>
    public const double Damping = 0.82;

    /// <summary>速度低于这个值认为已经停下。</summary>
    public const double RestSpeed = 64;

    /// <summary>速度上限，避免极快的甩动把岛扔出很远。</summary>
    public const double MaxSpeed = 5200;

    /// <summary>估算速度时回看的时间窗口。</summary>
    public const double SampleWindow = 0.12;

    public readonly record struct DragSample(double X, double Y, double Seconds);

    public readonly record struct Body(double X, double Y, double Vx, double Vy);

    public static double Speed(double vx, double vy) => Math.Sqrt(vx * vx + vy * vy);

    public static bool ShouldLaunch(double vx, double vy) => Speed(vx, vy) >= LaunchSpeed;

    public static bool Settled(Body body) => Speed(body.Vx, body.Vy) < RestSpeed;

    /// <summary>
    /// 用最近一个采样窗口里的位移估速度。分两段取，前半段和后半段各求一次，
    /// 再取较新的那一段，这样松手前突然停住也能被反映出来。
    /// </summary>
    public static (double Vx, double Vy) Velocity(IReadOnlyList<DragSample> samples)
    {
        if (samples.Count < 2)
            return (0, 0);

        var last = samples[^1];
        int first = samples.Count - 1;
        while (first > 0 && last.Seconds - samples[first - 1].Seconds <= SampleWindow)
            first--;

        double vx = 0;
        double vy = 0;
        if (first < samples.Count - 1)
        {
            var start = samples[first];
            double dt = last.Seconds - start.Seconds;
            if (dt > 0.001)
            {
                vx = (last.X - start.X) / dt;
                vy = (last.Y - start.Y) / dt;
            }
        }

        double speed = Speed(vx, vy);
        if (speed > MaxSpeed)
        {
            double k = MaxSpeed / speed;
            vx *= k;
            vy *= k;
        }

        return (vx, vy);
    }

    /// <summary>
    /// 推进一步。岛的中心点被限制在 work 里，撞到边界时位置贴回去、速度反向并按 Restitution 衰减。
    /// halfW/halfH 是胶囊的半宽半高，让边缘碰到的是胶囊而不是窗口。
    /// </summary>
    public static Body Step(Body body, double dt, PixelRect work, double halfW, double halfH)
    {
        if (dt <= 0 || double.IsNaN(dt))
            return body;

        double minX = work.X + halfW;
        double maxX = work.Right - halfW;
        double minY = work.Y + halfH;
        double maxY = work.Bottom - halfH;
        if (maxX < minX)
            minX = maxX = (minX + maxX) / 2.0;
        if (maxY < minY)
            minY = maxY = (minY + maxY) / 2.0;

        double x = body.X + body.Vx * dt;
        double y = body.Y + body.Vy * dt;
        double vx = body.Vx;
        double vy = body.Vy;

        if (x < minX)
        {
            x = minX;
            vx = -vx * Restitution;
        }
        else if (x > maxX)
        {
            x = maxX;
            vx = -vx * Restitution;
        }

        if (y < minY)
        {
            y = minY;
            vy = -vy * Restitution;
        }
        else if (y > maxY)
        {
            y = maxY;
            vy = -vy * Restitution;
        }

        double damp = Math.Exp(-Damping * dt);
        vx *= damp;
        vy *= damp;

        return new Body(x, y, vx, vy);
    }
}
