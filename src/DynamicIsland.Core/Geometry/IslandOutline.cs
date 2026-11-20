namespace DynamicIsland.Core.Geometry;

/// <summary>
/// 岛身真实外轮廓：圆角矩形 / 跑道形的有向距离，而不是内切椭圆。
/// 宽卡片中间那一块如果按 x²/a²+y²/b² 来算，高光会收成一颗独立的椭圆。
/// </summary>
public static class IslandOutline
{
    /// <summary>边缘月牙约占短边的这么多，沿轮廓等宽，不随椭圆半径变厚。</summary>
    public const double RimFraction = 0.22;

    public static double RimBand(double width, double height) =>
        Math.Max(8, Math.Min(Math.Max(1, width), Math.Max(1, height)) * RimFraction);

    /// <summary>点到圆角矩形的有向距离：内部为负，边上为 0，外部为正。</summary>
    public static double SignedDistance(double x, double y, double width, double height, double radius)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        double hx = width * 0.5;
        double hy = height * 0.5;
        double r = Squircle.SafeRadius(width, height, radius);
        double px = Math.Abs(x - hx);
        double py = Math.Abs(y - hy);
        double bx = Math.Max(0, hx - r);
        double by = Math.Max(0, hy - r);
        double qx = px - bx;
        double qy = py - by;
        double ox = Math.Max(qx, 0);
        double oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    public static double EllipseRadius(double x, double y, double width, double height)
    {
        double hx = Math.Max(1, width) * 0.5;
        double hy = Math.Max(1, height) * 0.5;
        double nx = (x - hx) / hx;
        double ny = (y - hy) / hy;
        return Math.Sqrt(nx * nx + ny * ny);
    }

    public static (double Nx, double Ny) Normal(double x, double y, double width, double height, double radius)
    {
        const double e = 0.75;
        double dx = SignedDistance(x + e, y, width, height, radius) - SignedDistance(x - e, y, width, height, radius);
        double dy = SignedDistance(x, y + e, width, height, radius) - SignedDistance(x, y - e, width, height, radius);
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-8)
            return (0, -1);
        return (dx / len, dy / len);
    }

    /// <summary>
    /// 到边缘的归一化进度：0 在内缩后的跑道形内侧，1 在轮廓上。
    /// 中间保持通透，月牙贴着真实外轮廓，而不是一圈椭圆。
    /// </summary>
    public static double Along(double x, double y, double width, double height, double radius)
    {
        double d = SignedDistance(x, y, width, height, radius);
        if (d >= 0)
            return 1;
        double band = RimBand(width, height);
        // Smoothstep 要求左端小于右端：到边距离 0→1，超过带宽则为 0。
        return Smoothstep(0, band, band + d);
    }

    public static double Smoothstep(double edge0, double edge1, double x)
    {
        if (edge1 <= edge0)
            return x >= edge1 ? 1 : 0;
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// 贴合跑道形边缘的月牙和高光。中心几乎是 0，不会在中间单独画出一块椭圆反光。
    /// </summary>
    public static int ShadeArgb(double x, double y, double width, double height, double radius, double surface = 0)
    {
        double d = SignedDistance(x, y, width, height, radius);
        if (d >= 0.5)
            return 0;

        double along = Along(x, y, width, height, radius);
        if (along < 0.015)
            return 0;

        var (nx, ny) = Normal(x, y, width, height, radius);
        double light = Math.Max(0, nx * -0.28 + ny * -0.96);
        double meniscus = along * 0.50;
        double specular = along * along * light;
        double caustic = along * Math.Clamp(surface, -1, 1);
        double alpha = Math.Clamp(meniscus * 0.42 + specular * 0.90 + Math.Max(0, caustic) * 0.36, 0, 0.70);
        int a = Math.Clamp((int)Math.Round(alpha * 255), 0, 255);
        if (a <= 1)
            return 0;
        int r = 0xF4 * a / 255;
        int g = 0xFB * a / 255;
        int b = 0xFF * a / 255;
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
