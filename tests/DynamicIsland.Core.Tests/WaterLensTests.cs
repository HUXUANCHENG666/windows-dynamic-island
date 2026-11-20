using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;
using Xunit;

namespace DynamicIsland.Core.Tests;
public class WaterLensTests
{
    const int W = 320, H = 140, P = LiquidRefraction.DefaultPad, R = 35;

    static (double U, double V, double A, double Distance) Map(double x, double y, double dpi = 1)
    {
        double w = W * dpi, h = H * dpi, r = R * dpi;
        double d = IslandOutline.SignedDistance(x, y, w, h, r);
        WaterLens.Map(x, y, w, h, r, dpi, d, LiquidOpticsFrame.Identity, 0, 0, 0, out var u, out var v, out var a);
        return (u - P, v - P, a, d);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SettledLensMagnifiesModeratelyAndIsDpiInvariant(double dpi)
    {
        var c = Map(160 * dpi, 70 * dpi, dpi);
        var n = Map(161 * dpi, 70 * dpi, dpi);
        double magnification = dpi / (n.U - c.U);
        Assert.InRange(magnification, 1.16, 1.20);
        Assert.Equal(160 * dpi, c.U, 6);
        Assert.Equal(70 * dpi, c.V, 6);
        Assert.InRange(c.A, 0.95, 0.97);
    }

    [Fact]
    public void InteriorBulgeAndRimBandAreDistinctButContinuous()
    {
        double band = IslandOutline.RimBand(W, H, 1);
        var mid = Map(200, 70);
        // 内部采样点被拉向中心：水滴放大。
        Assert.InRange(200 - mid.U, 4, 20);
        Assert.True(mid.Distance < -WaterLens.BlendStartBands * band);

        double rimX = W - 2.5;
        var rim = Map(rimX, 70);
        Assert.InRange(rim.Distance, -band, 0.4);
        // 边缘带向外折射，与内部收缩方向相反。
        Assert.True(rim.U > rimX);
        Assert.True(mid.U < 200);
        Assert.True(rim.A < mid.A);

        double previous = Map(160, 70).U;
        for (double x = 160.1; x < W - 0.6; x += 0.1)
        {
            double u = Map(x, 70).U;
            Assert.InRange(u - previous, -0.000001, 0.85);
            previous = u;
        }
    }

    [Fact]
    public void CurrentSourceChangesImmediatelyAndNoLiveStaysClear()
    {
        int[] source = new int[(W + 2 * P) * (H + 2 * P)], a = new int[W * H], b = new int[W * H];
        Array.Fill(source, unchecked((int)0xFF101010));
        GlassCpuRenderer.Render(a, W, H, W, H, R, 1, null, source, W, H, R, liveInterior: true);
        Array.Fill(source, unchecked((int)0xFFE0E0E0));
        GlassCpuRenderer.Render(b, W, H, W, H, R, 1, null, source, W, H, R, liveInterior: true);
        Assert.True((b[70 * W + 160] & 255) > (a[70 * W + 160] & 255) + 100);
        GlassCpuRenderer.Render(b, W, H, W, H, R, 1, null, source, W, H, R, liveInterior: false);
        Assert.Equal(0, b[70 * W + 160]);
    }

    [Fact]
    public void FastDragResamplesCurrentGeometryInsteadOfBlanking()
    {
        int[] source = new int[(W + 2 * P) * (H + 2 * P)];
        for (int y = 0; y < H + 2 * P; y++)
        for (int x = 0; x < W + 2 * P; x++)
        {
            int v = ((x - P) / 12 & 1) == 0 ? 0x30 : 0xD0;
            source[y * (W + 2 * P) + x] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
        }

        int[] first = new int[W * H], moved = new int[W * H], resized = new int[400 * H];
        GlassCpuRenderer.Render(first, W, H, W, H, R, 1, null, source, W, H, R, 0, 0, 1, 1, default, true);
        Assert.True(HasGlass(first, W, H));

        // 80px 位移远超旧的 3px Matches 阈值，但仍在 144 pad 内：必须仍有内部透镜和边缘带。
        GlassCpuRenderer.Render(moved, W, H, W, H, R, 1, null, source, W, H, R, 80, 0, 1, 1, default, true);
        Assert.True(HasGlass(moved, W, H));
        Assert.True(InteriorOpaque(moved, W, H));
        Assert.True(RimOpaque(moved, W, H));

        // 用同一张垫边按新尺寸重采样，而不是把 320 宽的旧图拉伸到 400。
        GlassCpuRenderer.Render(resized, 400, H, 400, H, R, 1, null, source, W, H, R, 0, 0, 1, 1, default, true);
        Assert.True(HasGlass(resized, 400, H));
        Assert.True(InteriorOpaque(resized, 400, H));
    }

    [Fact]
    public void DisplayPolicyKeepsGlassOnPositionChangeAndRejectsStretchedSize()
    {
        var shown = new GlassFrameGeometry(320, 140, 100, 100, 1, 1);
        var dragged = shown with { Left = 180, Top = 70 }; // 快速拖动
        Assert.False(shown.Matches(dragged));
        Assert.True(shown.MatchesSize(dragged));
        Assert.True(GlassFrameGeometry.ShouldKeepDisplayedBitmap(shown, dragged, 1));
        Assert.True(GlassFrameGeometry.ShouldPresent(shown, dragged, 1));

        var expanded = shown with { Width = 520 };
        Assert.False(GlassFrameGeometry.ShouldKeepDisplayedBitmap(shown, expanded, 1));
        Assert.False(GlassFrameGeometry.ShouldPresent(shown, expanded, 1));

        Assert.True(GlassFrameGeometry.CanResample(16.67));
        Assert.True(GlassFrameGeometry.CanResample(200)); // 旧 100ms 阈值会把快拖可用垫边扔掉
        Assert.False(GlassFrameGeometry.CanResample(351));
        Assert.False(GlassFrameGeometry.CanResample(-1));

        const int margin = LiquidRefraction.DefaultPad;
        Assert.True(GlassFrameGeometry.Covers(100, 100, 320, 140, margin, 180, 100, 320, 140));
        Assert.False(GlassFrameGeometry.Covers(100, 100, 320, 140, margin, 100 + margin + 80, 100, 320, 140));
    }

    static bool HasGlass(int[] px, int w, int h)
    {
        int lit = 0;
        for (int i = 0; i < px.Length; i++)
            if (((uint)px[i] >> 24) > 8) lit++;
        return lit > w * 4; // 不是整幅真透明
    }

    static bool InteriorOpaque(int[] px, int w, int h)
    {
        int c = px[(h / 2) * w + w / 2];
        return ((uint)c >> 24) > 80;
    }

    static bool RimOpaque(int[] px, int w, int h)
    {
        int c = px[(h / 2) * w + 3];
        return ((uint)c >> 24) > 16;
    }
}
