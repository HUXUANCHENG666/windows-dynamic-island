using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;

namespace DynamicIsland.Core.Tests;

public class GlassContinuityTests
{
    const int Pad = 72;
    static (double U, double V) Map(double x, double y, double w, double h, double r, double dpi)
    {
        Assert.True(LiquidRefraction.TryMap(x, y, w, h, Pad, r, 0, 0, 0,
            out double u, out double v, out _, out _, out _, out _, out _, dpi));
        Assert.True(IslandOutline.SignedDistance(u - Pad, v - Pad, w, h, r) > 2.9 * dpi);
        return (u, v);
    }
    static double Distance((double U, double V) a, (double U, double V) b) =>
        Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V));

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ThinRimHasFixedDipThicknessThroughoutExpansion(double dpi)
    {
        for (int frame = 0; frame <= 120; frame++)
        {
            double t = frame / 120.0, w = (180 + 354 * t) * dpi, h = (40 + 190 * t) * dpi;
            Assert.Equal(6 * dpi, IslandOutline.RimBand(w, h, dpi), 9);
            Assert.Equal(0, IslandOutline.Along(w / 2, 6.01 * dpi, w, h, 35 * dpi, dpi));
            Assert.True(IslandOutline.Along(w / 2, 2 * dpi, w, h, 35 * dpi, dpi) > 0.5);
        }
    }

    [Theory]
    [InlineData(1.0, 0.85)]
    [InlineData(1.25, 1.0)]
    [InlineData(1.5, 1.4)]
    [InlineData(2.0, 1.4)]
    public void AllEightTangentJoinsAndCornerMidpointsHaveBoundedContinuousMapping(double dpi, double userScale)
    {
        double w = 534 * userScale * dpi, h = 230 * userScale * dpi, r = 35 * userScale * dpi;
        double k = 2 * dpi, e = 0.002 * dpi;
        foreach (bool flipX in new[] { false, true })
        foreach (bool flipY in new[] { false, true })
        {
            (double U, double V) Sample(double x, double y) => Map(flipX ? w - x : x, flipY ? h - y : y, w, h, r, dpi);
            var top = Sample(r, k);
            var topLine = Sample(r + e, k);
            double theta = e / (r - k);
            var topArc = Sample(r - (r - k) * Math.Sin(theta), r - (r - k) * Math.Cos(theta));
            Assert.InRange(Distance(top, topLine) / e, 0.99, 1.01);
            Assert.InRange(Distance(top, topArc) / e, 1, 1.75);
            Assert.True(Distance(topLine, topArc) < 3 * e);
            var left = Sample(k, r);
            var leftLine = Sample(k, r + e);
            var leftArc = Sample(r - (r - k) * Math.Cos(theta), r - (r - k) * Math.Sin(theta));
            Assert.True(Distance(leftLine, leftArc) < 3 * e);
            // 45°处旧矩形射线/选带造成楔形尺度折线；平行圆弧不会突变。
            var a = Sample(r - (r - k) * Math.Cos(Math.PI / 4 - theta), r - (r - k) * Math.Sin(Math.PI / 4 - theta));
            var b = Sample(r - (r - k) * Math.Cos(Math.PI / 4), r - (r - k) * Math.Sin(Math.PI / 4));
            var c = Sample(r - (r - k) * Math.Cos(Math.PI / 4 + theta), r - (r - k) * Math.Sin(Math.PI / 4 + theta));
            Assert.InRange(Distance(a, b) / e, 1, 1.75);
            Assert.True(Math.Abs(Distance(a, b) - Distance(b, c)) < e * 0.001);
        }
    }

    static int Pattern(double x, double y, int kind)
    {
        double z = kind switch
        {
            0 => Math.Sin(x * 0.12) * Math.Cos(y * 0.12), // 网格
            1 => Math.Sin((x + y) * 0.20),               // 斜线
            2 => Math.Sin(x * 0.45) * Math.Sin(y * 0.15),// 文字式横竖条纹
            _ => Math.Tanh((x - 35) * 0.5)             // 双色过渡
        };
        int gray = (int)Math.Round(127.5 + 127.5 * z);
        return unchecked((int)0xFF000000) | (gray << 16) | (gray << 8) | gray;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ActualFilteredPatternsCrossCornerAndStraightWithoutSeams(int kind)
    {
        const int w = 534, h = 230, r = 35, stride = w + Pad * 2;
        var full = new int[stride * (h + 2 * Pad)];
        var captured = new int[full.Length];
        var shape = new GlassGeometry(w, h, r);
        for (int y = 0; y < h + 2 * Pad; y++)
        for (int x = 0; x < stride; x++)
        {
            full[y * stride + x] = Pattern(x - Pad, y - Pad, kind);
            captured[y * stride + x] = shape.Distance(x - Pad + 0.5, y - Pad + 0.5) <= 1 ? 0 : full[y * stride + x];
        }
        int previous = 0; bool first = true;
        // 左边直线 -> 整个左上圆弧 -> 顶边直线，不跳过切线端点。
        var points = new List<(double X, double Y)>();
        for (double y = r + 8; y >= r; y -= 0.02) points.Add((2, y));
        for (double a = 0; a <= Math.PI / 2; a += 0.0005) points.Add((r - (r - 2) * Math.Cos(a), r - (r - 2) * Math.Sin(a)));
        for (double x = r; x <= r + 8; x += 0.02) points.Add((x, 2));
        foreach (var p in points)
        {
            var uv = Map(p.X, p.Y, w, h, r, 1);
            int c = GlassSample.Footprint(captured, stride, h + 2 * Pad, uv.U, uv.V, w, h, w, h, Pad, r);
            int oracle = GlassSample.Footprint(full, stride, h + 2 * Pad, uv.U, uv.V, w, h, w, h, Pad, r);
            Assert.Equal(oracle, c); // 圆角外实际像素没被矩形排除域误擦掉。
            Assert.Equal(255u, (uint)c >> 24);
            Assert.Equal(c & 255, (c >> 16) & 255);
            if (!first) Assert.InRange(Math.Abs((c & 255) - (previous & 255)), 0, 4);
            first = false; previous = c;
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void EveryFrameLayersStrokeInsetAndScaledCaptureShareTheSameCornerCenter(double dpi)
    {
        for (int f = 0; f <= 100; f++)
        {
            double t = f / 100.0;
            var outer = new GlassGeometry(180 + t * 354, 40 + t * 190, 20 + t * 15, dpi);
            var inner = outer.Inset(outer.Band);
            var stroke = outer.Inset(0.75); // 1.5 DIP stroke 中心线内移半宽
            Assert.Equal(outer.SafeRadius, inner.SafeRadius + outer.Band, 9);
            Assert.Equal(outer.SafeRadius, stroke.SafeRadius + 0.75, 9);
            foreach (double a in new[] { 0.0, 0.2, Math.PI / 4, 1.4, Math.PI / 2 })
            {
                double nx = -Math.Cos(a), ny = -Math.Sin(a);
                double x = outer.SafeRadius + outer.SafeRadius * nx;
                double y = outer.SafeRadius + outer.SafeRadius * ny;
                Assert.InRange(Math.Abs(outer.Distance(x, y)), 0, 1e-9);
                Assert.InRange(Math.Abs(inner.Distance(x - nx * outer.Band - outer.Band, y - ny * outer.Band - outer.Band)), 0, 1e-9);
                Assert.InRange(Math.Abs(stroke.Distance(x - nx * 0.75 - 0.75, y - ny * 0.75 - 0.75)), 0, 1e-9);
                // 所有图层继承同一个 PillHost 非均匀弹性变换；捕获排除用其逆变换。
                double sx = 1 + t * 0.12, sy = 1 - t * 0.08;
                Assert.True(outer.CaptureExcluded(30 + x * sx, 50 + y * sy, 30, 50, sx, sy));
                Assert.False(outer.CaptureExcluded(30 + (x + nx * 3) * sx, 50 + (y + ny * 3) * sy, 30, 50, sx, sy));
                Assert.InRange(Math.Abs(IslandOutline.SignedDistance(x * dpi, y * dpi, outer.PixelWidth, outer.PixelHeight, outer.PixelRadius)), 0, 1e-8);
            }
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void FullCpuFallbackPreservesNeutralPremultipliedPixelsAndThinTransparentCenter(double dpi)
    {
        int w = (int)(534 * dpi), h = (int)(230 * dpi);
        var source = Enumerable.Repeat(unchecked((int)0xFF909090), (w + 2 * Pad) * (h + 2 * Pad)).ToArray();
        var pixels = new int[w * h];
        GlassCpuRenderer.Render(pixels, w, h, w, h, 35 * dpi, dpi, null, source, w, h, 35 * dpi);
        foreach (int c in pixels)
        {
            int a = (int)((uint)c >> 24);
            Assert.Equal(c & 255, (c >> 8) & 255);
            Assert.Equal(c & 255, (c >> 16) & 255);
            Assert.InRange(c & 255, 0, a);
        }
        Assert.Equal(0, pixels[(int)(7 * dpi) * w + w / 2]);
        Assert.NotEqual(0, pixels[(int)(2 * dpi) * w + w / 2]);
    }

    [Fact]
    public void WarmCpuFallbackDoesNotAllocatePerPixelOrPerFrame()
    {
        const int w = 534, h = 230;
        var source = Enumerable.Repeat(unchecked((int)0xFF909090), (w + 2 * Pad) * (h + 2 * Pad)).ToArray();
        var pixels = new int[w * h];
        for (int i = 0; i < 3; i++) GlassCpuRenderer.Render(pixels, w, h, w, h, 35, 1, null, source, w, h, 35);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) GlassCpuRenderer.Render(pixels, w, h, w, h, 35, 1, null, source, w, h, 35);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void ParentBounceTransformIsAppliedToRealScreenSamplingNotOnlyTheMask()
    {
        const int w = 534, h = 230, stride = w + 2 * Pad;
        var pixels = new int[stride * (h + 2 * Pad)];
        for (int y = 0; y < h + 2 * Pad; y++)
        for (int x = 0; x < stride; x++)
        {
            int gray = (x * 3 + y * 5) % 256;
            pixels[y * stride + x] = unchecked((int)0xFF000000) | (gray << 16) | (gray << 8) | gray;
        }
        var uv = Map(35, 2, w, h, 35, 1);
        const double sx = 1.08, sy = 0.96, dx = 17.3, dy = -0.4;
        int actual = GlassSample.Footprint(pixels, stride, h + 2 * Pad, uv.U, uv.V, w, h, w, h, Pad, 35, 1, dx, dy, true, sx, sy);
        double screenU = Pad + (uv.U - Pad) * sx + dx, screenV = Pad + (uv.V - Pad) * sy + dy;
        int expected = GlassSample.Footprint(pixels, stride, h + 2 * Pad, screenU, screenV, w, h, w, h, Pad, 35, 1, 0, 0, true);
        int untransformed = GlassSample.Footprint(pixels, stride, h + 2 * Pad, uv.U, uv.V, w, h, w, h, Pad, 35, 1, 0, 0, true);
        Assert.Equal(expected, actual);
        Assert.NotEqual(untransformed, actual);
    }

    [Fact]
    public void UnknownCachedHoleDoesNotInventAnotherEdgesPixelsDuringCollapse()
    {
        const int w = 534, h = 230;
        var pixels = Enumerable.Repeat(unchecked((int)0xFF505050), (w + 2 * Pad) * (h + 2 * Pad)).ToArray();
        GlassSample.NormalizeCapture(pixels, w, h, Pad);
        int unknown = GlassSample.Footprint(pixels, w + 2 * Pad, h + 2 * Pad,
            Pad + 50, Pad + 25, w, h, 180, 40, Pad, 35, 1, 0, 0, true);
        Assert.Equal(0, unknown);
    }

    [Fact]
    public void CachedPadUsesScreenTranslationNotPiecewiseResizeStretch()
    {
        const int w = 534, h = 230;
        var p = Enumerable.Repeat(unchecked((int)0xFF505050), (w + Pad * 2) * (h + Pad * 2)).ToArray();
        var uv = Map(35, 2, w, h, 35, 1);
        int c = GlassSample.Footprint(p, w + 2 * Pad, h + 2 * Pad, uv.U, uv.V, w, h, 620, 280, Pad, 35, 1, 0.3, -0.4, true);
        Assert.Equal(unchecked((int)0xFF505050), c);
        // 显式验证实际采样坐标平移跨旧矩形四带边界时不会突然跳支。
        var a = GlassSample.SafeRoundedPoint(Pad + 34.999, Pad - 4, w, h, 35, Pad);
        var b = GlassSample.SafeRoundedPoint(Pad + 35.001, Pad - 4, w, h, 35, Pad);
        Assert.True(Distance(a, b) < 0.003);
    }
}
