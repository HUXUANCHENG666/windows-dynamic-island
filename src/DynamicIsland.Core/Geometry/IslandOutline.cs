namespace DynamicIsland.Core.Geometry;

/// <summary>
/// 岛身真实外轮廓：圆角矩形 / 跑道形的有向距离，而不是内切椭圆。
/// 宽卡片中间那一块如果按 x²/a²+y²/b² 来算，高光会收成一颗独立的椭圆。
/// 内沿用同一套圆角矩形，圆角随内缩减小，但不会被切成直角。
/// </summary>
public static class IslandOutline
{
    /// <summary>边缘月牙约占短边的这么多，沿轮廓走，不随椭圆半径变厚。</summary>
    public const double RimFraction = 0.26;

    /// <summary>内沿圆角至少保留外圆角的这么多，避免内缩后变成尖角矩形。</summary>
    public const double MinInnerCornerFraction = 0.55;

    public const double MinInnerCorner = 12;

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

    /// <summary>与外轮廓同心的内缩圆角矩形距离。内圆角不会小于安全下限。</summary>
    public static double InnerSignedDistance(double x, double y, double width, double height, double radius)
    {
        double inset = RimBand(width, height);
        double innerW = Math.Max(1, width - 2 * inset);
        double innerH = Math.Max(1, height - 2 * inset);
        double innerR = InnerCornerRadius(width, height, radius);
        return SignedDistance(x - inset, y - inset, innerW, innerH, innerR);
    }

    public static double InnerCornerRadius(double width, double height, double radius)
    {
        double r = Squircle.SafeRadius(width, height, radius);
        double inset = RimBand(width, height);
        double innerShort = Math.Max(1, Math.Min(width, height) - 2 * inset);
        double maxR = innerShort * 0.5;
        double minR = Math.Min(maxR, Math.Max(MinInnerCorner, r * MinInnerCornerFraction));
        return Math.Min(Math.Max(minR, r - inset), maxR);
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
    /// 到边缘的归一化进度：0 在内缩后的圆角矩形内侧，1 在外轮廓上。
    /// 用 smootherstep 做更宽的衰减，中间保持通透。
    /// </summary>
    public static double Along(double x, double y, double width, double height, double radius)
    {
        double dOuter = SignedDistance(x, y, width, height, radius);
        if (dOuter >= 0)
            return 1;
        double dInner = InnerSignedDistance(x, y, width, height, radius);
        if (dInner <= 0)
            return 0;
        double span = dInner - dOuter;
        if (span <= 1e-6)
            return 1;
        return Smootherstep(0, 1, dInner / span);
    }

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
    public static int ShadeArgb(double x, double y, double width, double height, double radius, double surface = 0)
    {
        double d = SignedDistance(x, y, width, height, radius);
        if (d >= 0.5)
            return 0;

        double along = Along(x, y, width, height, radius);
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
        int r = 0xF4 * a / 255;
        int g = 0xFB * a / 255;
        int b = 0xFF * a / 255;
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
