using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Motion;

namespace DynamicIsland.Core.Tests;

public class LiquidMotionTests
{
    [Fact]
    public void Stretch_StaysStillWhenSlow()
    {
        var (along, perp) = LiquidDrag.Stretch(40, 0, LiquidDrag.MaxStretch);
        Assert.Equal(1, along);
        Assert.Equal(1, perp);
    }

    [Fact]
    public void Stretch_PreservesArea_AndCapsAlong()
    {
        var (along, perp) = LiquidDrag.Stretch(5000, 0, LiquidDrag.MaxStretch);
        Assert.InRange(along, LiquidDrag.MaxStretch - 0.002, LiquidDrag.MaxStretch);
        Assert.InRange(along * perp, 0.999, 1.001);
    }

    [Fact]
    public void AxisScale_HorizontalDrag_StretchesWidth()
    {
        var (sx, sy) = LiquidDrag.AxisScale(2000, 0, LiquidDrag.MaxStretch, LiquidDrag.MaxStretch);
        Assert.True(sx > 1.08);
        Assert.True(sy < 0.95);
        Assert.InRange(sx * sy, 0.999, 1.001);
    }

    [Fact]
    public void AxisScale_VerticalDrag_StretchesHeight()
    {
        var (sx, sy) = LiquidDrag.AxisScale(0, 2000, LiquidDrag.MaxStretch, LiquidDrag.MaxStretch);
        Assert.True(sy > 1.08);
        Assert.True(sx < 0.95);
    }

    [Fact]
    public void AxisScale_RespectsAxisCap()
    {
        var (sx, sy) = LiquidDrag.AxisScale(3000, 0, 1.06, 1.16);
        Assert.InRange(sx, 1.05, 1.06);
        Assert.InRange(sx * sy, 0.94, 1.001);
    }

    [Fact]
    public void ReleaseKick_PushesTowardRest()
    {
        Assert.True(LiquidDrag.ReleaseKick(1.16) < 0);
        Assert.True(LiquidDrag.ReleaseKick(0.90) > 0);
        Assert.Equal(0, LiquidDrag.ReleaseKick(1));
    }

    [Fact]
    public void BounceSpring_OvershootsVisiblyAfterReleaseKick()
    {
        var spring = new Spring(LiquidDrag.Bounce, 1.16);
        spring.Target = 1;
        spring.Velocity += LiquidDrag.ReleaseKick(1.16);
        double trough = 1.16;
        for (int i = 0; i < 240; i++)
        {
            spring.Step(1.0 / 120);
            if (spring.Value < trough)
                trough = spring.Value;
        }

        Assert.InRange(trough, 0.86, 0.97);
        Assert.True(spring.IsSettled(0.01, 0.05));
    }

    [Fact]
    public void LiquidBounce_DragsThenSnapsBack()
    {
        var bounce = new LiquidBounce();
        for (int i = 0; i < 20; i++)
            bounce.Step(1.0 / 60, moving: true, vx: 2200, vy: 0, maxX: 1.16, maxY: 1.16);

        Assert.True(bounce.ScaleX > 1.08);
        Assert.True(bounce.ScaleY < 0.96);
        Assert.False(bounce.IsSettled);

        for (int i = 0; i < 180; i++)
            bounce.Step(1.0 / 60, moving: false, vx: 0, vy: 0, maxX: 1.16, maxY: 1.16);

        Assert.True(bounce.IsSettled);
        Assert.InRange(bounce.ScaleX, 0.99, 1.01);
        Assert.InRange(bounce.ScaleY, 0.99, 1.01);
    }

    [Fact]
    public void TransformOrigin_PinsTheDockedEdge()
    {
        Assert.Equal((0.5, 0), LiquidDrag.TransformOrigin(top: true, bottom: false, left: false, right: false));
        Assert.Equal((0.5, 1), LiquidDrag.TransformOrigin(top: false, bottom: true, left: false, right: false));
        Assert.Equal((0, 0.5), LiquidDrag.TransformOrigin(top: false, bottom: false, left: true, right: false));
        Assert.Equal((1, 0.5), LiquidDrag.TransformOrigin(top: false, bottom: false, left: false, right: true));
        Assert.Equal((0, 0), LiquidDrag.TransformOrigin(top: true, bottom: false, left: true, right: false));
    }

    [Fact]
    public void Refraction_IgnoresTheClearCenter()
    {
        Assert.False(LiquidRefraction.TryMap(100, 50, 200, 100, 72, out _, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void Refraction_RimSamplesThePad_WithChromaticSplit()
    {
        Assert.True(LiquidRefraction.TryMap(198, 50, 200, 100, 72,
            out var u, out var v, out var alpha, out var ru, out var rv, out var bu, out var bv));
        Assert.True(alpha > 0.5);
        Assert.True(LiquidRefraction.InPad(u, v, 200, 100, 72));
        Assert.True(LiquidRefraction.InPad(ru, rv, 200, 100, 72));
        Assert.True(LiquidRefraction.InPad(bu, bv, 200, 100, 72));
        // 右边缘：红通道采得更靠外。
        Assert.True(ru > u);
        Assert.True(bu < u);
        Assert.InRange(v, 72, 72 + 100);
    }

    [Fact]
    public void Refraction_AlphaRisesTowardTheEdge()
    {
        Assert.True(LiquidRefraction.TryMap(182, 50, 200, 100, 72, out _, out _, out var inner, out _, out _, out _, out _));
        Assert.True(LiquidRefraction.TryMap(194, 50, 200, 100, 72, out _, out _, out var outer, out _, out _, out _, out _));
        Assert.True(outer > inner);
    }

    [Fact]
    public void DockOrigins_MatchAnchors()
    {
        Assert.Equal((0.5, 0), Origin(DockAnchor.TopCenter));
        Assert.Equal((0, 0.5), Origin(DockAnchor.LeftCenter));
        Assert.Equal((1, 1), Origin(DockAnchor.BottomRight));
    }

    static (double, double) Origin(DockAnchor anchor)
    {
        bool top = anchor is DockAnchor.TopLeft or DockAnchor.TopCenter or DockAnchor.TopRight;
        bool bottom = anchor is DockAnchor.BottomLeft or DockAnchor.BottomCenter or DockAnchor.BottomRight;
        bool left = anchor is DockAnchor.TopLeft or DockAnchor.LeftCenter or DockAnchor.BottomLeft;
        bool right = anchor is DockAnchor.TopRight or DockAnchor.RightCenter or DockAnchor.BottomRight;
        return LiquidDrag.TransformOrigin(top, bottom, left, right);
    }

    [Fact]
    public void Surface_LongSideStaysInBudget()
    {
        var surface = new LiquidSurface();
        surface.EnsureSize(424, 236);
        Assert.Equal(LiquidSurface.DefaultLongSide, Math.Max(surface.Columns, surface.Rows));
        Assert.InRange(Math.Max(surface.Columns, surface.Rows), LiquidSurface.MinLongSide, LiquidSurface.MaxLongSide);
        Assert.True(Math.Min(surface.Columns, surface.Rows) >= 16);
        surface.EnsureSize(228, 400);
        Assert.Equal(LiquidSurface.DefaultLongSide, Math.Max(surface.Columns, surface.Rows));
        Assert.True(surface.Columns < surface.Rows);
    }

    [Fact]
    public void Surface_InjectedForceRaisesTheField()
    {
        var surface = new LiquidSurface();
        surface.EnsureSize(200, 100);
        Assert.True(surface.IsSettled);
        Assert.InRange(surface.PrimaryCrest().U, 0.3, 0.5);
        surface.InjectForce(0.5, 0.5, 0.8f, 0.25f);
        for (int i = 0; i < 8; i++)
            surface.Step(1.0 / 60, 0, 0, moving: false, released: false);
        Assert.False(surface.IsSettled);
        Assert.True(surface.PeakAbsHeight > 0.02);
        Assert.InRange(surface.Sample(0.5, 0.5), -LiquidSurface.MaxAbsHeight, LiquidSurface.MaxAbsHeight);
    }

    [Fact]
    public void Surface_HeightsDoNotExplode()
    {
        var surface = new LiquidSurface();
        surface.EnsureSize(200, 80);
        surface.InjectForce(0.3, 0.4, 4f, 0.2f);
        surface.InjectForce(0.7, 0.6, -4f, 0.2f);
        for (int i = 0; i < 240; i++)
            surface.Step(1.0 / 60, vx: 2400, vy: -800, moving: i < 40, released: i == 40, stretch: 0.2);

        Assert.InRange(surface.PeakAbsHeight, 0, LiquidSurface.MaxAbsHeight);
        for (double u = 0; u <= 1; u += 0.25)
        for (double v = 0; v <= 1; v += 0.25)
            Assert.InRange(surface.Sample(u, v), -LiquidSurface.MaxAbsHeight, LiquidSurface.MaxAbsHeight);
    }

    [Fact]
    public void Surface_DragThenReleaseMakesAPulse()
    {
        var surface = new LiquidSurface();
        surface.EnsureSize(200, 100);
        for (int i = 0; i < 12; i++)
            surface.Step(1.0 / 60, vx: 2000, vy: 0, moving: true, released: false);

        float during = surface.PeakAbsHeight;
        Assert.True(during > 0.01);

        surface.Step(1.0 / 60, vx: 2000, vy: 0, moving: false, released: true, stretch: 0.16);
        float after = surface.PeakAbsHeight;
        Assert.True(after >= during * 0.8);
        Assert.False(surface.IsSettled);

        for (int i = 0; i < 240; i++)
            surface.Step(1.0 / 60, 0, 0, moving: false, released: false);
        Assert.True(surface.IsSettled);
        Assert.InRange(surface.PeakAbsHeight, 0, LiquidSurface.SettledHeight);
    }

    [Fact]
    public void Refraction_SurfaceSlopeBendsTheSample()
    {
        Assert.True(LiquidRefraction.TryMap(198, 50, 200, 100, 72,
            out var u0, out _, out _, out _, out _, out _, out _));
        Assert.True(LiquidRefraction.TryMap(198, 50, 200, 100, 72, 0.4, 0, 0.3,
            out var u1, out _, out _, out _, out _, out _, out _));
        Assert.True(u1 > u0);
        Assert.True(LiquidRefraction.InPad(u1, 72 + 50, 200, 100, 72));
    }

    [Fact]
    public void Outline_UsesRoundedRect_NotAnInscribedEllipse()
    {
        const double w = 400;
        const double h = 120;
        const double radius = 24;
        // 宽卡片顶边中点内侧：离真实边缘很近，但按椭圆半径还在 0.78 以内。
        double x = 200;
        double y = 14;
        double ellipse = IslandOutline.EllipseRadius(x, y, w, h);
        Assert.True(ellipse < LiquidRefraction.InnerRadius);
        Assert.True(IslandOutline.SignedDistance(x, y, w, h, radius) < 0);
        Assert.True(IslandOutline.Along(x, y, w, h, radius) > 0.15);
        Assert.True(LiquidRefraction.TryMap(x, y, w, h, 72, radius, 0, 0, 0,
            out var u, out var v, out var alpha, out _, out _, out _, out _));
        Assert.True(alpha > 0.1);
        Assert.True(LiquidRefraction.InPad(u, v, w, h, 72));
        Assert.True(IslandOutline.EllipseRadius(w / 2, h / 2, w, h) < 0.01);
    }

    [Fact]
    public void Shade_FollowsTheStadiumRim_NotACenterEllipse()
    {
        const double w = 428;
        const double h = 184;
        const double radius = 52;
        int center = IslandOutline.ShadeArgb(w * 0.5, h * 0.5, w, h, radius);
        int midTop = IslandOutline.ShadeArgb(w * 0.5, 10, w, h, radius);
        int blob = IslandOutline.ShadeArgb(w * 0.38, h * 0.36, w, h, radius);
        int topCorner = IslandOutline.ShadeArgb(w - radius, 12, w, h, radius);
        static int Alpha(int argb) => (argb >> 24) & 0xFF;
        Assert.Equal(0, center);
        Assert.True(Alpha(midTop) > 20);
        Assert.True(Alpha(midTop) > Alpha(blob) + 10);
        Assert.True(Alpha(topCorner) > 20);
        // 底部月牙弱于顶部高光，仍然贴边而不是椭圆心。
        int midBottom = IslandOutline.ShadeArgb(w * 0.5, h - 10, w, h, radius);
        Assert.True(Alpha(midTop) > Alpha(midBottom));
    }

    [Fact]
    public void GlassWork_DoesNotCaptureWhileMoving()
    {
        Assert.True(GlassWork.ShouldCapture(moving: false));
        Assert.False(GlassWork.ShouldCapture(moving: true));
    }
}
