namespace DynamicIsland.Core.Geometry;

/// <summary>每帧权威玻璃几何（DIP）。只有玻璃用圆弧，近黑 squircle 不变。</summary>
public readonly record struct GlassGeometry(double Width, double Height, double Radius, double Dpi = 1)
{
    public double SafeRadius => Squircle.SafeRadius(Width, Height, Radius);
    public double Band => IslandOutline.RimBand(Width, Height);
    public double PixelWidth => Width * Dpi;
    public double PixelHeight => Height * Dpi;
    public double PixelRadius => SafeRadius * Dpi;
    public GlassGeometry Inset(double amount) => new(Math.Max(1, Width - 2 * amount),
        Math.Max(1, Height - 2 * amount), Math.Max(0, SafeRadius - amount), Dpi);
    public double Distance(double x, double y) => IslandOutline.SignedDistance(x, y, Width, Height, SafeRadius);
    public bool CaptureExcluded(double x, double y, double left, double top, double scaleX, double scaleY,
        double guard = 1) => Distance((x - left) / scaleX, (y - top) / scaleY) <= guard;
}
