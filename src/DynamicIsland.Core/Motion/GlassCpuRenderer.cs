using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>实际 CPU 回退单 pass：共用几何、跳过透明中心、合成中性高光和五点折射。</summary>
public static class GlassCpuRenderer
{
    public static void Render(int[] pixels, int ow, int oh, int w, int h, double radius, double dpi,
        LiquidSurface? field, int[]? padPixels, int sourceW, int sourceH, double sourceRadius,
        double shiftX = 0, double shiftY = 0, double scaleX = 1, double scaleY = 1)
    {
        const int Pad = LiquidRefraction.DefaultPad;
        Array.Clear(pixels, 0, ow * oh);
        double band = IslandOutline.RimBand(w, h, dpi);
        for (int y = 0; y < oh; y++)
        for (int x = 0; x < ow; x++)
        {
            double px = (x + 0.5) * w / ow, py = (y + 0.5) * h / oh;
            // 绝大多数透明中心不必做高度场、SDF 或五 tap。
            if (py > band && py < h - band && px > radius + band && px < w - radius - band) continue;
            double distance = IslandOutline.SignedDistance(px, py, w, h, radius);
            if (distance >= 0.5 || distance <= -band) continue;
            float height = 0, sx = 0, sy = 0;
            field?.SampleSlope(px / w, py / h, out height, out sx, out sy);
            int shade = IslandOutline.ShadeArgb(px, py, w, h, radius, height, dpi);
            int c = 0; double alpha = 0;
            if (padPixels != null && LiquidRefraction.TryMap(px, py, w, h, Pad, radius, sx, sy, height,
                out var u, out var v, out alpha, out _, out _, out _, out _, dpi))
                c = GlassSample.Footprint(padPixels!, sourceW + 2 * Pad, sourceH + 2 * Pad, u, v,
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
