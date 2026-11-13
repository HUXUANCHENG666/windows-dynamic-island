using DynamicIsland.Core.Island;

namespace DynamicIsland.Core.Tests;

public class ThrowTests
{
    static readonly PixelRect Work = new(0, 0, 1920, 1040);

    static ThrowMath.DragSample Sample(double x, double y, double seconds) => new(x, y, seconds);

    [Fact]
    public void Velocity_OnlyLooksAtTheRecentWindow()
    {
        var samples = new[]
        {
            Sample(0, 0, 0),
            Sample(1000, 0, 0.5),
            Sample(1000, 0, 0.9),
            Sample(1120, 60, 1.0)
        };

        var (vx, vy) = ThrowMath.Velocity(samples);

        Assert.InRange(vx, 900, 1400);
        Assert.InRange(vy, 400, 900);
    }

    [Fact]
    public void Velocity_IsZeroWhenThePointerStopped()
    {
        var samples = new[] { Sample(500, 500, 0), Sample(500, 500, 0.1) };

        var (vx, vy) = ThrowMath.Velocity(samples);

        Assert.Equal(0.0, vx, 3);
        Assert.Equal(0.0, vy, 3);
    }

    [Fact]
    public void Velocity_NeedsTwoSamples()
    {
        Assert.Equal((0.0, 0.0), ThrowMath.Velocity(Array.Empty<ThrowMath.DragSample>()));
        Assert.Equal((0.0, 0.0), ThrowMath.Velocity(new[] { Sample(10, 10, 0) }));
    }

    [Fact]
    public void Launch_NeedsEnoughSpeed()
    {
        Assert.False(ThrowMath.ShouldLaunch(200, 0));
        Assert.False(ThrowMath.ShouldLaunch(0, 0));
        Assert.True(ThrowMath.ShouldLaunch(400, 0));
        Assert.True(ThrowMath.ShouldLaunch(0, -400));
    }

    [Fact]
    public void Step_BouncesOffTheRightEdge()
    {
        var body = new ThrowMath.Body(1900, 500, 2000, 0);

        var next = ThrowMath.Step(body, 0.1, Work, 50, 30);

        Assert.True(next.X <= Work.Right - 50 + 0.001);
        Assert.True(next.Vx < 0);
    }

    [Fact]
    public void Step_BouncesOffTheTopEdge()
    {
        var body = new ThrowMath.Body(900, 5, 0, -1800);

        var next = ThrowMath.Step(body, 0.1, Work, 50, 30);

        Assert.True(next.Y >= Work.Y + 30 - 0.001);
        Assert.True(next.Vy > 0);
    }

    [Fact]
    public void Step_KeepsThePillInsideTheWorkArea()
    {
        var body = new ThrowMath.Body(960, 520, 5000, 5000);

        for (int i = 0; i < 240; i++)
            body = ThrowMath.Step(body, 0.016, Work, 60, 30);

        Assert.InRange(body.X, Work.X + 60 - 0.001, Work.Right - 60 + 0.001);
        Assert.InRange(body.Y, Work.Y + 30 - 0.001, Work.Bottom - 30 + 0.001);
    }

    [Fact]
    public void Step_DecaysUntilSettled()
    {
        var body = new ThrowMath.Body(960, 520, 1500, 0);
        int frames = 0;

        while (!ThrowMath.Settled(body) && frames < 600)
        {
            body = ThrowMath.Step(body, 0.016, Work, 60, 30);
            frames++;
        }

        Assert.True(ThrowMath.Settled(body));
        Assert.True(frames < 600);
    }

    [Fact]
    public void Step_IgnoresNonPositiveTimeStep()
    {
        var body = new ThrowMath.Body(300, 300, 500, -500);

        Assert.Equal(body, ThrowMath.Step(body, 0, Work, 60, 30));
    }
}
