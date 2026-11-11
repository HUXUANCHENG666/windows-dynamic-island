namespace DynamicIsland.Core.Geometry;

/// <summary>
/// 连续圆角。n = 2 是正圆（紧凑态的胶囊），n ≈ 4.5 是更接近 iOS 的超椭圆圆角。
/// 展开时 n 跟宽高一起弹簧插值，所以圆角会从胶囊慢慢“站”成 squircle，而不是中途变成生硬的小圆角矩形。
/// </summary>
public static class Squircle
{
    public static IReadOnlyList<PointD> Build(double width, double height, double radius, double n, int steps = 16)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        n = Math.Clamp(n, 1.2, 8);
        double maxR = Math.Min(width, height) / 2;
        radius = Math.Clamp(radius, 0, maxR);

        if (radius < 0.75 || steps < 1)
        {
            return new[]
            {
                new PointD(0, 0),
                new PointD(width, 0),
                new PointD(width, height),
                new PointD(0, height)
            };
        }

        double exp = 2.0 / n;
        var pts = new List<PointD>(steps * 4 + 4);
        Corner(pts, width - radius, radius, radius, exp, steps, 0);
        Corner(pts, width - radius, height - radius, radius, exp, steps, 1);
        Corner(pts, radius, height - radius, radius, exp, steps, 2);
        Corner(pts, radius, radius, radius, exp, steps, 3);
        return pts;
    }

    public static double SafeRadius(double width, double height, double radius)
    {
        if (width < 1 || height < 1)
            return 0;
        return Math.Clamp(radius, 0, Math.Min(width, height) / 2);
    }

    public static double PolygonArea(IReadOnlyList<PointD> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }

        return Math.Abs(a) * 0.5;
    }

    static void Corner(List<PointD> pts, double cx, double cy, double r, double exp, int steps, int quadrant)
    {
        for (int i = 0; i <= steps; i++)
        {
            double t = Math.PI * 0.5 * (i / (double)steps);
            double u = Math.Pow(Math.Cos(t), exp);
            double v = Math.Pow(Math.Sin(t), exp);
            double x, y;
            switch (quadrant)
            {
                case 0: // top edge → right edge
                    x = cx + r * v;
                    y = cy - r * u;
                    break;
                case 1: // right → bottom
                    x = cx + r * u;
                    y = cy + r * v;
                    break;
                case 2: // bottom → left
                    x = cx - r * v;
                    y = cy + r * u;
                    break;
                default: // left → top
                    x = cx - r * u;
                    y = cy - r * v;
                    break;
            }

            Add(pts, x, y);
        }
    }

    static void Add(List<PointD> pts, double x, double y)
    {
        if (pts.Count > 0)
        {
            var last = pts[^1];
            double dx = last.X - x;
            double dy = last.Y - y;
            if (dx * dx + dy * dy < 0.04)
                return;
        }

        pts.Add(new PointD(x, y));
    }
}
