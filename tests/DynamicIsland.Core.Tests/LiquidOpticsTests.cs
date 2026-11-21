using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;

namespace DynamicIsland.Core.Tests;

public class LiquidOpticsTests
{
    const int Pad = LiquidRefraction.DefaultPad;
    const int W = 320, H = 140, R = 35;
    const int Poison = unchecked((int)0xFFFF00FF);
    const int Dark = unchecked((int)0xFF202020);
    const int Yellow = unchecked((int)0xFFE0C040);
    const int LiveGreen = unchecked((int)0xFF20C060);

    [Fact]
    public void OpticalLagTrailsAMoveThenConvergesWithoutOscillating()
    {
        var optics = new LiquidOptics();
        optics.Jump(100, 40, 200, 80);
        optics.Step(1.0 / 60, 140, 40, 200, 80, 1);
        Assert.False(optics.IsSettled);
        Assert.InRange(optics.LagX, 20, 48);
        Assert.InRange(Math.Abs(optics.LagY), 0, 1);
        var previous = Math.Abs(optics.LagX);
        for (int i = 0; i < 180; i++)
        {
            optics.Step(1.0 / 60, 140, 40, 200, 80, 1);
            Assert.InRange(Math.Abs(optics.LagX), 0, LiquidOptics.MaxLagDip + 0.01);
            // Near-critical damping should not grow after the first steps.
            if (i > 8) Assert.True(Math.Abs(optics.LagX) <= previous + 0.25);
            previous = Math.Min(previous, Math.Abs(optics.LagX) + 0.05);
        }
        Assert.True(optics.IsSettled);
        Assert.InRange(optics.LagX, -LiquidOptics.SettlePosition, LiquidOptics.SettlePosition);
        Assert.Equal(0, optics.Frame().Strength);
    }

    [Fact]
    public void LagIsClampedAndATeleportSnapsInsteadOfLeavingAnEndlessTrail()
    {
        var optics = new LiquidOptics();
        optics.Jump(0, 0, 200, 80);
        optics.Step(1.0 / 60, 400, 0, 200, 80, 1);
        Assert.InRange(Math.Abs(optics.LagX), 0, LiquidOptics.MaxLagDip + 0.01);
        optics.Step(1.0 / 60, 400 + LiquidOptics.TeleportPixels, 0, 200, 80, 1);
        Assert.True(optics.IsSettled);
        Assert.InRange(optics.LagX, -0.01, 0.01);
    }

    [Fact]
    public void SizeLagIsClampedAndSettlesAfterAnExpand()
    {
        var optics = new LiquidOptics();
        optics.Jump(10, 10, 120, 40);
        optics.Step(1.0 / 60, 10, 10, 534, 230, 1);
        Assert.InRange(Math.Abs(534 - optics.LaggedWidth), 0, LiquidOptics.MaxSizeLagDip + 0.01);
        for (int i = 0; i < 180; i++) optics.Step(1.0 / 60, 10, 10, 534, 230, 1);
        Assert.True(optics.IsSettled);
        Assert.InRange(optics.LaggedWidth, 533, 535);
    }

    [Fact]
    public void InteriorMappingWarpsCurrentPadAndKeepsRgbTogether()
    {
        var optics = new LiquidOpticsFrame(28, 0, 1, 1, 1);
        // A fringe point whose displacement lands in the current live pad.
        Assert.True(LiquidOptics.TryMapInterior(22, 70, W, H, Pad, R, 1, optics, 0, 0, 0, out var u, out var v, out var alpha));
        LiquidOptics.DisplacedUv(22, 70, W, H, 1, optics, 0, 0, out var sx, out var sy);
        Assert.InRange(u, Pad + sx - 0.01, Pad + sx + 0.01);
        Assert.InRange(v, Pad + sy - 0.01, Pad + sy + 0.01);
        Assert.True(sx < 0); // pulled into the left pad of this frame
        Assert.InRange(alpha, 0.2, LiquidOptics.InteriorAlpha);
        Assert.False(LiquidOptics.TryMapInterior(160, 70, W, H, Pad, R, 1, optics, 0, 0, 0, out _, out _, out _));
        Assert.True(LiquidOptics.TryMapInterior(160, 70, W, H, Pad, R, 1, optics, 0, 0, 0, out var cu, out _, out var ca, liveInterior: true));
        Assert.InRange(cu, Pad + 160 - 28 - 0.01, Pad + 160 - 28 + 0.01);
        Assert.InRange(ca, 0.2, LiquidOptics.InteriorLiveAlpha);
        Assert.False(LiquidOptics.TryMapInterior(22, 70, W, H, Pad, R, 1, LiquidOpticsFrame.Identity, 0, 0, 0, out _, out _, out _));
        Assert.False(LiquidOptics.TryMapInterior(2, 70, W, H, Pad, R, 1, optics, 0, 0, 0, out _, out _, out _)); // rim stays on the rim path
        // Size lag is a radial strain of the current frame, still RGB-shared.
        var strain = new LiquidOpticsFrame(0, 0, 0.8, 1, 1);
        Assert.True(LiquidOptics.TryMapInterior(22, 70, W, H, Pad, R, 1, strain, 0, 0, 0, out var su, out var sv, out _));
        Assert.True(su < Pad);
        Assert.InRange(sv, Pad + 70 - 0.01, Pad + 70 + 0.01);
    }

    [Fact]
    public void CpuWarpSamplesOnlyTheLivePadAndIgnoresPoisonInTheHole()
    {
        int[] source = Punched(Dark, Yellow, Poison);
        var settled = new int[W * H];
        GlassCpuRenderer.Render(settled, W, H, W, H, R, 1, null, source, W, H, R);
        Assert.Equal(0, settled[(H / 2) * W + W / 2]);

        var lagged = new int[W * H];
        var optics = new LiquidOpticsFrame(28, 0, 1, 1, 1);
        GlassCpuRenderer.Render(lagged, W, H, W, H, R, 1, null, source, W, H, R, 0, 0, 1, 1, optics);
        int fringe = lagged[(H / 2) * W + 22]; // displacement lands in the current left pad
        int center = lagged[(H / 2) * W + 160];
        int wouldHaveBeenHistorical = lagged[(H / 2) * W + 210]; // old path sampled x-28 inside the hole
        Assert.NotEqual(0, fringe);
        Assert.Equal(0, center);
        Assert.Equal(0, wouldHaveBeenHistorical);
        static int UnpremultiplyRed(int c)
        {
            int a = (int)((uint)c >> 24);
            return a == 0 ? 0 : ((c >> 16) & 255) * 255 / a;
        }
        static int UnpremultiplyBlue(int c)
        {
            int a = (int)((uint)c >> 24);
            return a == 0 ? 0 : (c & 255) * 255 / a;
        }
        Assert.InRange(UnpremultiplyRed(fringe), 0, 80);
        Assert.InRange(UnpremultiplyBlue(fringe), 0, 80); // not the magenta poison

        // The same warp immediately follows a new live pad color: no stale atlas.
        int[] updated = Punched(LiveGreen, LiveGreen, Poison);
        var again = new int[W * H];
        GlassCpuRenderer.Render(again, W, H, W, H, R, 1, null, updated, W, H, R, 0, 0, 1, 1, optics);
        int live = again[(H / 2) * W + 22];
        Assert.NotEqual(0, live);
        Assert.InRange(UnpremultiplyRed(live), 0, 80);
        static int UnpremultiplyGreen(int c)
        {
            int a = (int)((uint)c >> 24);
            return a == 0 ? 0 : ((c >> 8) & 255) * 255 / a;
        }
        Assert.InRange(UnpremultiplyGreen(live), 140, 255);
        Assert.Equal(0, again[(H / 2) * W + W / 2]);

        var gray = Punched(unchecked((int)0xFF909090), unchecked((int)0xFF909090), 0);
        var rim = new int[W * H];
        GlassCpuRenderer.Render(rim, W, H, W, H, R, 1, null, gray, W, H, R);
        foreach (int c in rim)
        {
            Assert.Equal(c & 255, (c >> 8) & 255);
            Assert.Equal(c & 255, (c >> 16) & 255);
        }
        Assert.Equal(0, rim[(H / 2) * W + W / 2]);
        Assert.NotEqual(0, rim[2 * W + W / 2]);
    }

    [Fact]
    public void CpuLiveInteriorSamplesCurrentUnderlayRatherThanAHole()
    {
        int[] live = Filled(Dark, Yellow); // no punch: interior is the current underlay
        var lagged = new int[W * H];
        var optics = new LiquidOpticsFrame(28, 0, 1, 1, 1);
        GlassCpuRenderer.Render(lagged, W, H, W, H, R, 1, null, live, W, H, R, 0, 0, 1, 1, optics, liveInterior: true);
        int center = lagged[(H / 2) * W + 160]; // samples x-28=132, still the dark half of the live underlay
        int right = lagged[(H / 2) * W + 210]; // samples x-28=182, yellow underlay
        Assert.NotEqual(0, center);
        Assert.NotEqual(0, right);
        static int UnpremultiplyRed(int c)
        {
            int a = (int)((uint)c >> 24);
            return a == 0 ? 0 : ((c >> 16) & 255) * 255 / a;
        }
        Assert.InRange(UnpremultiplyRed(center), 0, 80);
        Assert.InRange(UnpremultiplyRed(right), 180, 255);
        var settled = new int[W * H];
        GlassCpuRenderer.Render(settled, W, H, W, H, R, 1, null, live, W, H, R);
        Assert.Equal(0, settled[(H / 2) * W + W / 2]); // settled stays truly clear
    }

    [Fact]
    public void DpiScaleAndSizeChangeKeepInteriorCoordinatesFiniteAndRgbAligned()
    {
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            int w = (int)(W * dpi), h = (int)(H * dpi);
            var optics = new LiquidOpticsFrame(28 * dpi, 4 * dpi, 0.85, 0.9, 0.8);
            double x = 20 * dpi, y = h * 0.5;
            Assert.True(LiquidOptics.TryMapInterior(x, y, w, h, Pad, 35 * dpi, dpi, optics, 0.2, -0.1, 0.15,
                out var u, out var v, out var alpha));
            Assert.InRange(u, 1, w + 2 * Pad - 1);
            Assert.InRange(v, 1, h + 2 * Pad - 1);
            Assert.InRange(alpha, 0.05, LiquidOptics.InteriorLiveAlpha);
            Assert.False(LiquidOptics.TryMapInterior(w * 0.5, h * 0.5, w, h, Pad, 35 * dpi, dpi, optics, 0, 0, 0,
                out _, out _, out _));
            Assert.True(LiquidOptics.TryMapInterior(w * 0.5, h * 0.5, w, h, Pad, 35 * dpi, dpi, optics, 0, 0, 0,
                out var cu, out _, out var ca, liveInterior: true));
            Assert.InRange(cu, 1, w + 2 * Pad - 1);
            Assert.InRange(ca, 0.05, LiquidOptics.InteriorLiveAlpha);
        }
    }

    static int[] Punched(int leftPad, int rightPad, int hole)
    {
        int stride = W + 2 * Pad;
        var source = new int[stride * (H + 2 * Pad)];
        for (int y = 0; y < H + 2 * Pad; y++)
        for (int x = 0; x < stride; x++)
        {
            double px = x - Pad, py = y - Pad;
            bool inside = IslandOutline.SignedDistance(px + 0.5, py + 0.5, W, H, R) < 0.5;
            source[y * stride + x] = inside ? hole : (px < W / 2.0 ? leftPad : rightPad);
        }
        return source;
    }

    static int[] Filled(int left, int right)
    {
        int stride = W + 2 * Pad;
        var source = new int[stride * (H + 2 * Pad)];
        for (int y = 0; y < H + 2 * Pad; y++)
        for (int x = 0; x < stride; x++)
            source[y * stride + x] = (x - Pad) < W / 2.0 ? left : right;
        return source;
    }
}
