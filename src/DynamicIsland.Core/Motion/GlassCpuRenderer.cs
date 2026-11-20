using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>实际 CPU 回退单 pass：共用几何、静止时跳过透明中心、合成中性高光、边缘与液态滞后内部折射。</summary>
public static class GlassCpuRenderer
{
    public static void Render(int[] pixels, int ow, int oh, int w, int h, double radius, double dpi,
        LiquidSurface? field, int[]? padPixels, int sourceW, int sourceH, double sourceRadius,
        double shiftX = 0, double shiftY = 0, double scaleX = 1, double scaleY = 1,
        LiquidOpticsFrame optics = default)
    {
        const int Pad = LiquidRefraction.DefaultPad;
        if (!optics.IsActive) optics = LiquidOpticsFrame.Identity;
        Array.Clear(pixels, 0, ow * oh);
        double band = IslandOutline.RimBand(w, h, dpi);
        bool fillInterior = optics.IsActive && padPixels != null;
        for (int y = 0; y < oh; y++)
        for (int x = 0; x < ow; x++)
        {
            double px = (x + 0.5) * w / ow, py = (y + 0.5) * h / oh;
            // 静止且无滞后时，绝大多数透明中心不必做高度场、SDF 或五 tap。
            if (!fillInterior && py > band && py < h - band && px > radius + band && px < w - radius - band) continue;
            double distance = IslandOutline.SignedDistance(px, py, w, h, radius);
            if (distance >= 0.5) continue;
            if (distance <= -band && !fillInterior) continue;
            float height = 0, sx = 0, sy = 0;
            field?.SampleSlope(px / w, py / h, out height, out sx, out sy);
            int shade = distance <= -band ? 0 : IslandOutline.ShadeArgb(px, py, w, h, radius, height, dpi);
            int c = 0; double u = 0, v = 0, alpha = 0;
            if (padPixels != null && distance > -band && LiquidRefraction.TryMap(px, py, w, h, Pad, radius, sx, sy, height,
                out u, out v, out alpha, out _, out _, out _, out _, dpi))
                c = GlassSample.Footprint(padPixels, sourceW + 2 * Pad, sourceH + 2 * Pad, u, v,
                    sourceW, sourceH, w, h, Pad, sourceRadius, dpi, shiftX, shiftY, true, scaleX, scaleY);
            else if (padPixels != null && LiquidOptics.TryMapInterior(px, py, w, h, Pad, radius, dpi, optics, sx, sy, height,
                out u, out v, out alpha))
                c = GlassSample.Footprint(padPixels, sourceW + 2 * Pad, sourceH + 2 * Pad, u, v,
                    sourceW, sourceH, w, h, Pad, sourceRadius, dpi, shiftX, shiftY, true, scaleX, scaleY);
            int a = c == 0 ? 0 : (int)Math.Round(alpha * ((uint)c >> 24));
            int sa = ((shade >> 24) & 255) * (255 - a) / 255;
            int r = ((c >> 16) & 255) * a / 255 + sa;
            int g = ((c >> 8) & 255) * a / 255 + sa;
            int b = (c & 255) * a / 255 + sa;
            pixels[y * ow + x] = ((a + sa) << 24) | (r << 16) | (g << 8) | b;
        }
    }
}
