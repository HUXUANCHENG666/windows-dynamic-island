using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;

namespace DynamicIsland.Core.Tests;

public class GlassSpatialTests
{
    const int Pad = 72;
    const int Yellow = unchecked((int)0xFFE0B030);
    const int Black = unchecked((int)0xFF000000);
    const int Red = unchecked((int)0xFFFF0000);
    const int Blue = unchecked((int)0xFF0000FF);
    const int White = unchecked((int)0xFFFFFFFF);

    static int[] Background(int w, int h, double edge, bool vertical, int low, int high)
    {
        int stride = w + 2 * Pad;
        var pixels = new int[stride * (h + 2 * Pad)];
        for (int y = 0; y < h + 2 * Pad; y++)
            for (int x = 0; x < stride; x++)
                pixels[y * stride + x] = (vertical ? x - Pad : y - Pad) < edge ? low : high;
        GlassSample.NormalizeCapture(pixels, w, h, Pad);
        return pixels;
    }

    static int At(int[] pixels, int sw, int sh, int w, int h, double x, double y,
        double sx = 0, double sy = 0, double surface = 0)
    {
        Assert.True(LiquidRefraction.TryMap(x, y, w, h, Pad, 30, sx, sy, surface,
            out double u, out double v, out _, out double ru, out double rv, out double bu, out double bv));
        Assert.Equal(u, ru); Assert.Equal(u, bu);
        Assert.Equal(v, rv); Assert.Equal(v, bv);
        Assert.True(LiquidRefraction.InPad(u, v, w, h, Pad));
        return GlassSample.Footprint(pixels, sw + 2 * Pad, sh + 2 * Pad, u, v, sw, sh, w, h, Pad);
    }

    [Theory]
    [InlineData(Black, Yellow)]
    [InlineData(Red, Blue)]
    [InlineData(Black, White)]
    public void EveryStraightRimRetainsItsOwnSpatialBackground(int low, int high)
    {
        const int w = 400, h = 240;
        var vertical = Background(w, h, 200, true, low, high);
        foreach (double y in new[] { 3.0, h - 3.0 })
            foreach (double x in new[] { 55.0, 120, 180, 220, 280, 345 })
                Assert.Equal(x < 200 ? low : high, At(vertical, w, h, w, h, x, y));
        var horizontal = Background(w, h, 120, false, low, high);
        foreach (double x in new[] { 3.0, w - 3.0 })
            foreach (double y in new[] { 45.0, 85, 105, 135, 160, 195 })
                Assert.Equal(y < 120 ? low : high, At(horizontal, w, h, w, h, x, y));
    }

    [Theory]
    [InlineData(Black, Yellow)]
    [InlineData(Red, Blue)]
    [InlineData(Black, White)]
    public void MovingBoundaryChangesOnlyNearbyPointsContinuously(int low, int high)
    {
        const int w = 400, h = 100;
        // Translate the island sampling position by subpixel increments against a fixed edge.
        var pixels = Background(w, h, 200, true, low, high);
        int previous = At(pixels, w, h, w, h, 197, 3);
        bool mixed = false;
        for (int i = 1; i <= 60; i++)
        {
            double x = 197 + i * 0.1;
            int c = At(pixels, w, h, w, h, x, 3);
            foreach (int shift in new[] { 0, 8, 16 })
                Assert.InRange(Math.Abs(((c >> shift) & 255) - ((previous >> shift) & 255)), 0, 27);
            Assert.Equal(low, At(pixels, w, h, w, h, x - 80, 3));
            Assert.Equal(high, At(pixels, w, h, w, h, x + 80, 3));
            if (c != low && c != high) mixed = true;
            previous = c;
        }
        Assert.True(mixed);
        // A moving color boundary must not flip either whole straight rim together.
        for (int edge = 80; edge <= 320; edge += 20)
        {
            var moved = Background(w, h, edge, true, low, high);
            foreach (double y in new[] { 3.0, 97.0 })
            {
                Assert.Equal(low, At(moved, w, h, w, h, 50, y));
                Assert.Equal(high, At(moved, w, h, w, h, 350, y));
            }
        }
    }

    [Theory]
    [InlineData(Black, White)]
    [InlineData(Red, Blue)]
    [InlineData(Black, Yellow)]
    public void CornersSlopesAndResizedPadsStayInValidColorHull(int low, int high)
    {
        const int sw = 400, sh = 120;
        var pixels = Background(sw, sh, 200, true, low, high);
        foreach (int w in new[] { 200, 320, 400, 480, 600 })
        foreach (int h in new[] { 80, 120, 180 })
        foreach (double slope in new[] { -0.8, 0.0, 0.8 })
        {
            int count = 0;
            for (int y = 1; y < h; y += 3)
            for (int x = 1; x < w; x += 3)
            {
                if (!LiquidRefraction.TryMap(x, y, w, h, Pad, 30, slope, -slope, slope,
                    out var u, out var v, out _, out _, out _, out _, out _)) continue;
                int c = GlassSample.Footprint(pixels, sw + 2 * Pad, sh + 2 * Pad, u, v, sw, sh, w, h, Pad);
                Assert.Equal(255u, (uint)c >> 24);
                // RGB must lie on the line segment between the two input colors,
                // not just anywhere in their per-channel min/max box (RGB stripes fail).
                if (low == Black && high == White)
                {
                    Assert.Equal(c & 255, (c >> 8) & 255);
                    Assert.Equal(c & 255, (c >> 16) & 255);
                }
                else if (low == Red)
                {
                    Assert.Equal(0, (c >> 8) & 255);
                    Assert.InRange(((c >> 16) & 255) + (c & 255), 254, 256);
                }
                else
                {
                    double t = ((c >> 16) & 255) / 224.0;
                    Assert.InRange(Math.Abs(((c >> 8) & 255) - t * 176), 0, 1);
                    Assert.InRange(Math.Abs((c & 255) - t * 48), 0, 1);
                }
                count++;
            }
            Assert.True(count > 100);
        }
    }

    [Fact]
    public void CaptureHoleIsClearedAndBlackIsExplicitlyValid()
    {
        const int w = 200, h = 100;
        var pixels = Enumerable.Repeat(unchecked((int)0x00FF00FF), (w + Pad * 2) * (h + Pad * 2)).ToArray();
        pixels[0] = 0; // A valid GDI black has no alpha, just like uninitialized hole data.
        GlassSample.NormalizeCapture(pixels, w, h, Pad);
        Assert.Equal(Black, pixels[0]);
        for (int y = Pad; y < Pad + h; y++)
            for (int x = Pad; x < Pad + w; x++)
                Assert.Equal(0, pixels[y * (w + Pad * 2) + x]);
    }

    [Fact]
    public void InvalidTexelsCannotTintOrDarkenValidBlackOrWhite()
    {
        const int w = 200, h = 100;
        foreach (int color in new[] { Black, White })
        {
            var pixels = Enumerable.Repeat(color, (w + Pad * 2) * (h + Pad * 2)).ToArray();
            for (int y = 0; y < h + Pad * 2; y++)
                for (int x = 0; x < w + Pad * 2; x++)
                    if ((x + y) % 3 == 0) pixels[y * (w + Pad * 2) + x] = 0x00FF007F;
            int c = GlassSample.Footprint(pixels, w + 2 * Pad, h + 2 * Pad,
                Pad + 90.2, Pad - 12.6, w, h, w, h, Pad);
            Assert.Equal(color, c);
        }
    }

    [Fact]
    public void GlassHighlightIsNeutralAndSnapshotDoesNotFollowUiMutations()
    {
        int c = IslandOutline.ShadeArgb(100, 3, 400, 100, 30);
        Assert.NotEqual(0, c);
        Assert.Equal(c & 255, (c >> 8) & 255);
        Assert.Equal(c & 255, (c >> 16) & 255);
        var field = new LiquidSurface();
        field.EnsureSize(400, 100);
        field.InjectForce(0.5, 0.5, 0.4f);
        field.Step(1.0 / 60, 0, 0, false, false);
        var snapshot = field.Snapshot();
        float before = snapshot.Sample(0.5, 0.5);
        field.Reset();
        Assert.Equal(before, snapshot.Sample(0.5, 0.5));
        Assert.NotEqual(0, before);
        Assert.Equal(0, field.Sample(0.5, 0.5));
    }
}
