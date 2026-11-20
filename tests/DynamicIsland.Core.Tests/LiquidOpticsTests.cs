using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;

namespace DynamicIsland.Core.Tests;

public class LiquidOpticsTests
{
    const int Pad = LiquidRefraction.DefaultPad;
    const int W = 320, H = 140, R = 35;

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
    public void InteriorMappingShiftsWithLagAndKeepsRgbTogether()
    {
        var optics = new LiquidOpticsFrame(24, 0, 1, 1, 1);
        Assert.True(LiquidOptics.TryMapInterior(160, 70, W, H, Pad, R, 1, optics, 0, 0, 0, out var u, out var v, out var alpha));
        Assert.InRange(u, Pad + 160 - 24 - 0.01, Pad + 160 - 24 + 0.01);
        Assert.InRange(v, Pad + 70 - 0.01, Pad + 70 + 0.01);
        Assert.InRange(alpha, 0.2, LiquidOptics.InteriorAlpha);
        Assert.False(LiquidOptics.TryMapInterior(160, 70, W, H, Pad, R, 1, LiquidOpticsFrame.Identity, 0, 0, 0, out _, out _, out _));
        Assert.False(LiquidOptics.TryMapInterior(2, 70, W, H, Pad, R, 1, optics, 0, 0, 0, out _, out _, out _)); // rim stays on the rim path
    }

    [Fact]
    public void BackdropKeepsPreviouslyVisiblePixelsInsideAGrownIslandAndIgnoresTheCurrentHole()
    {
        const int pad = Pad, smallW = 120, smallH = 40, largeW = 320, largeH = 140;
        int smallStride = smallW + 2 * pad, largeStride = largeW + 2 * pad;
        var previous = new int[smallStride * (smallH + 2 * pad)];
        for (int i = 0; i < previous.Length; i++) previous[i] = unchecked((int)0xFF00AA55);
        // Current small-island capture: only the pad is valid.
        for (int y = 0; y < smallH + 2 * pad; y++)
        for (int x = 0; x < smallStride; x++)
            if (!LiquidRefraction.InPad(x, y, smallW, smallH, pad)) previous[y * smallStride + x] = 0;
        var capture = new int[largeStride * (largeH + 2 * pad)];
        for (int y = 0; y < largeH + 2 * pad; y++)
        for (int x = 0; x < largeStride; x++)
            capture[y * largeStride + x] = LiquidRefraction.InPad(x, y, largeW, largeH, pad) ? unchecked((int)0xFF112233) : 0;
        var dest = new int[largeStride * (largeH + 2 * pad)];
        LiquidBackdrop.Composite(dest, 0, 0, largeW, largeH, previous, (largeW - smallW) / 2, (largeH - smallH) / 2,
            smallW, smallH, capture, 0, 0, largeW, largeH, pad);
        // Old island sits at (100,50). A point 4px above its top, x=160, is old pad and new interior.
        int destX = pad + 160, destY = pad + 46;
        Assert.Equal(unchecked((int)0xFF00AA55), dest[destY * largeStride + destX]);
        Assert.Equal(0, dest[(pad + largeH / 2) * largeStride + pad + largeW / 2]); // old hole stays empty
        Assert.Equal(unchecked((int)0xFF112233), dest[2 * largeStride + 2]); // new outer pad stamped
    }

    [Fact]
    public void CpuInteriorUsesLaggedLocalColorAndSettledFramesStayClear()
    {
        int stride = W + 2 * Pad;
        var source = new int[stride * (H + 2 * Pad)];
        for (int y = 0; y < H + 2 * Pad; y++)
        for (int x = 0; x < stride; x++)
        {
            int px = x - Pad;
            int color = px < W / 2 ? unchecked((int)0xFF202020) : unchecked((int)0xFFE0C040);
            source[y * stride + x] = color; // atlas already holds historical interior pixels
        }
        var settled = new int[W * H];
        GlassCpuRenderer.Render(settled, W, H, W, H, R, 1, null, source, W, H, R);
        Assert.Equal(0, settled[(H / 2) * W + W / 2]);
        var lagged = new int[W * H];
        var optics = new LiquidOpticsFrame(28, 0, 1, 1, 1);
        GlassCpuRenderer.Render(lagged, W, H, W, H, R, 1, null, source, W, H, R, 0, 0, 1, 1, optics);
        int before = lagged[(H / 2) * W + 160]; // samples x-28=132, still the dark half
        int after = lagged[(H / 2) * W + 210]; // samples x-28=182, clearly the yellow half
        Assert.NotEqual(0, before);
        Assert.NotEqual(0, after);
        static int UnpremultiplyRed(int c)
        {
            int a = (int)((uint)c >> 24);
            return a == 0 ? 0 : ((c >> 16) & 255) * 255 / a;
        }
        Assert.InRange(UnpremultiplyRed(before), 0, 80);
        Assert.InRange(UnpremultiplyRed(after), 180, 255);
        // Neutral rim still has no RGB split on a settled punched render of gray.
        var gray = Enumerable.Repeat(unchecked((int)0xFF909090), stride * (H + 2 * Pad)).ToArray();
        for (int y = 0; y < H + 2 * Pad; y++)
        for (int x = 0; x < stride; x++)
            if (!LiquidRefraction.InPad(x, y, W, H, Pad)) gray[y * stride + x] = 0;
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
    public void DpiScaleAndSizeChangeKeepInteriorCoordinatesFiniteAndRgbAligned()
    {
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            int w = (int)(W * dpi), h = (int)(H * dpi);
            var optics = new LiquidOpticsFrame(16 * dpi, 4 * dpi, 0.85, 0.9, 0.8);
            Assert.True(LiquidOptics.TryMapInterior(w * 0.5, h * 0.5, w, h, Pad, 35 * dpi, dpi, optics, 0.2, -0.1, 0.15,
                out var u, out var v, out var alpha));
            Assert.InRange(u, 1, w + 2 * Pad - 1);
            Assert.InRange(v, 1, h + 2 * Pad - 1);
            Assert.InRange(alpha, 0.05, LiquidOptics.InteriorAlpha);
        }
    }
}
