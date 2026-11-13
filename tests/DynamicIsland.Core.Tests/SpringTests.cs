using DynamicIsland.Core.Motion;

namespace DynamicIsland.Core.Tests;

public class SpringTests
{
    [Fact]
    public void IslandSpring_HasASmallOvershoot_AndSettles()
    {
        var spring = new Spring(SpringConfig.Island, 0) { Target = 1 };
        double peak = 0;
        double? settle = null;
        const int frames = 200;
        for (int i = 0; i < frames; i++)
        {
            spring.Step(1.0 / 120);
            if (spring.Value > peak)
                peak = spring.Value;
            if (settle == null && Math.Abs(spring.Value - 1) < 0.02 && Math.Abs(spring.Velocity) < 0.15)
                settle = (i + 1) / 120.0;
        }

        Assert.InRange(peak, 1.025, 1.075);
        Assert.NotNull(settle);
        Assert.InRange(settle.Value, 0.28, 0.85);
        Assert.True(spring.IsSettled(0.02, 0.2));
    }

    [Fact]
    public void IslandSpring_OvershootsOnTheWayBack()
    {
        var spring = new Spring(SpringConfig.Island, 1) { Target = 0 };
        double trough = 1;
        for (int i = 0; i < 200; i++)
        {
            spring.Step(1.0 / 120);
            if (spring.Value < trough)
                trough = spring.Value;
        }

        Assert.InRange(trough, -0.075, -0.025);
        Assert.True(spring.IsSettled(0.02, 0.2));
    }

    [Fact]
    public void PresenceSpring_DoesNotBounce()
    {
        var spring = new Spring(SpringConfig.Presence, 0) { Target = 1 };
        double peak = 0;
        for (int i = 0; i < 180; i++)
        {
            spring.Step(1.0 / 120);
            if (spring.Value > peak)
                peak = spring.Value;
        }

        Assert.InRange(peak, 0.98, 1.012);
        Assert.True(spring.IsSettled(0.01, 0.05));
    }

    [Fact]
    public void RetargetMidFlight_StaysFinite()
    {
        var spring = new Spring(SpringConfig.Island, 130) { Target = 428 };
        for (int i = 0; i < 20; i++)
            spring.Step(1.0 / 60);
        spring.Target = 188;
        for (int i = 0; i < 90; i++)
            spring.Step(1.0 / 60);

        Assert.False(double.IsNaN(spring.Value));
        Assert.False(double.IsInfinity(spring.Value));
        Assert.InRange(spring.Value, 120, 460);
        Assert.True(spring.IsSettled(0.4, 2));
    }

    [Fact]
    public void LongGap_SnapsToTarget()
    {
        var spring = new Spring(SpringConfig.Island, 0) { Target = 40 };
        spring.Step(2);
        Assert.Equal(40, spring.Value);
        Assert.Equal(0, spring.Velocity);
    }

    [Fact]
    public void ZeroDt_DoesNothing()
    {
        var spring = new Spring(SpringConfig.Island, 3) { Target = 9 };
        spring.Step(0);
        spring.Step(-1);
        Assert.Equal(3, spring.Value);
    }

    [Fact]
    public void EntranceScale_StartsSmallAndBarelyOvershoots()
    {
        Assert.InRange(Easing.EntranceScale(0), 0.93, 0.95);
        Assert.InRange(Easing.EntranceScale(1), 0.99, 1.01);
        Assert.InRange(Easing.EntranceScale(1.15), 1.02, 1.05);
    }
}
