using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Tests;

public class SquircleTests
{
    [Fact]
    public void Pill_TouchesTheFourExtremes_AndStaysInside()
    {
        const double w = 200;
        const double h = 40;
        var pts = Squircle.Build(w, h, h / 2, 2, 20);

        Assert.InRange(pts.Min(p => p.X), -0.05, 0.05);
        Assert.InRange(pts.Max(p => p.X), w - 0.05, w + 0.05);
        Assert.InRange(pts.Min(p => p.Y), -0.05, 0.05);
        Assert.InRange(pts.Max(p => p.Y), h - 0.05, h + 0.05);
        Assert.All(pts, p =>
        {
            Assert.InRange(p.X, -0.05, w + 0.05);
            Assert.InRange(p.Y, -0.05, h + 0.05);
        });
    }

    [Fact]
    public void Pill_AreaMatchesAStadium()
    {
        const double w = 200;
        const double h = 40;
        var pts = Squircle.Build(w, h, h / 2, 2, 24);
        double area = Squircle.PolygonArea(pts);
        double expected = (w - h) * h + Math.PI * (h / 2) * (h / 2);
        Assert.InRange(area, expected * 0.97, expected * 1.03);
    }

    [Fact]
    public void CircleCorner_PassesNearThe45DegreePoint()
    {
        const double w = 120;
        const double h = 80;
        const double r = 30;
        var pts = Squircle.Build(w, h, r, 2, 32);
        double cx = w - r;
        double cy = r;
        double targetX = cx + r * Math.Cos(Math.PI / 4);
        double targetY = cy - r * Math.Sin(Math.PI / 4);
        double best = pts.Min(p =>
        {
            double dx = p.X - targetX;
            double dy = p.Y - targetY;
            return dx * dx + dy * dy;
        });
        Assert.True(best < 1.5 * 1.5, $"closest distance squared was {best}");
    }

    [Fact]
    public void Squircle_IsSymmetric()
    {
        var pts = Squircle.Build(180, 90, 28, 4.5, 12);
        foreach (var p in pts)
        {
            Assert.Contains(pts, q => Math.Abs(q.X - (180 - p.X)) < 0.6 && Math.Abs(q.Y - p.Y) < 0.6);
            Assert.Contains(pts, q => Math.Abs(q.X - p.X) < 0.6 && Math.Abs(q.Y - (90 - p.Y)) < 0.6);
        }
    }

    [Fact]
    public void Card_FillsMostOfItsBounds_ButIsNotARectangle()
    {
        const double w = 428;
        const double h = 184;
        var pts = Squircle.Build(w, h, 52, 4.5, 16);
        double area = Squircle.PolygonArea(pts);
        Assert.InRange(area, w * h * 0.86, w * h * 0.995);
    }

    [Fact]
    public void SafeRadius_NeverExceedsHalfTheShortSide()
    {
        Assert.Equal(18.5, Squircle.SafeRadius(130, 37, 40));
        Assert.Equal(0, Squircle.SafeRadius(10, 0, 4));
    }
}
