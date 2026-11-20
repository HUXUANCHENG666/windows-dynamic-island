namespace DynamicIsland.Core.Geometry;

/// <summary>
/// 岛身真实外轮廓：圆角矩形 / 跑道形的有向距离，而不是内切椭圆。
/// 宽卡片中间那一块如果按 x²/a²+y²/b² 来算，高光会收成一颗独立的椭圆。
/// 内沿从同一外 SDF 等距内缩，不再独立 clamp 圆角。
/// </summary>
public static class IslandOutline
{
    public const double RimWidthDip = 6;
    public static double RimBand(double width, double height, double dpi = 1) =>
        Math.Min(RimWidthDip * dpi, Math.Min(width, height) * 0.20);

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

    /// <summary>与外轮廓同心、等距内缩的圆角矩形距离。</summary>
    public static double InnerSignedDistance(double x, double y, double width, double height, double radius)
    {
        double inset = RimBand(width, height);
        double innerW = Math.Max(1, width - 2 * inset);
        double innerH = Math.Max(1, height - 2 * inset);
        double innerR = InnerCornerRadius(width, height, radius);
        return SignedDistance(x - inset, y - inset, innerW, innerH, innerR);
    }

    public static double InnerCornerRadius(double width, double height, double radius) =>
        Math.Max(0, Squircle.SafeRadius(width, height, radius) - RimBand(width, height));

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
        double hx = width * 0.5, hy = height * 0.5;
        double r = Squircle.SafeRadius(width, height, radius);
        double qx = Math.Abs(x - hx) - (hx - r), qy = Math.Abs(y - hy) - (hy - r);
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        double len = Math.Sqrt(ox * ox + oy * oy);
        if (len > 1e-9)
            return (ox / len * (x < hx ? -1 : 1), oy / len * (y < hy ? -1 : 1));
        return qx > qy ? (x < hx ? -1 : 1, 0) : (0, y < hy ? -1 : 1);
    }

    // 真正的等距内沿：仅从外 SDF 推进，没有第二套独立 clamp 的内圆角。
    public static double Along(double x, double y, double width, double height, double radius, double dpi = 1) =>
        Smootherstep(0, 1, 1 + SignedDistance(x, y, width, height, radius) / RimBand(width, height, dpi));

    public static double Smoothstep(double edge0, double edge1, double x)
    {
        if (edge1 <= edge0)
            return x >= edge1 ? 1 : 0;
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static double Smootherstep(double edge0, double edge1, double x)
    {
        if (edge1 <= edge0)
            return x >= edge1 ? 1 : 0;
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    /// <summary>
    /// 贴合跑道形边缘的月牙和高光。中心几乎是 0，内沿带圆角，不会在中间单独画出一块椭圆反光。
    /// </summary>
    public static int ShadeArgb(double x, double y, double width, double height, double radius, double surface = 0, double dpi = 1)
    {
        double d = SignedDistance(x, y, width, height, radius);
        if (d >= 0.5)
            return 0;

        double along = Along(x, y, width, height, radius, dpi);
        if (along <= 0)
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
        int r = a;
        int g = a;
        int b = a;
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
