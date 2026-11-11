using DynamicIsland.Core.Island;

namespace DynamicIsland.Core.Tests;

public class DirectorTests
{
    static DateTime T(int ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    [Fact]
    public void Hover_ExpandsAfterDelay_AndCollapsesAfterLeave()
    {
        var d = new IslandDirector();
        d.PointerEntered(T(0));
        d.Tick(T(79));
        Assert.False(d.Expanded);

        d.Tick(T(80));
        Assert.True(d.Expanded);
        Assert.Equal(IslandMode.Idle, d.Mode);

        d.PointerExited(T(80));
        d.Tick(T(559));
        Assert.True(d.Expanded);
        d.Tick(T(560));
        Assert.False(d.Expanded);
    }

    [Fact]
    public void Click_ExpandsImmediately()
    {
        var d = new IslandDirector();
        d.Click(T(0));
        Assert.True(d.Expanded);
    }

    [Fact]
    public void TrayClick_OpensForAWhile_AndTheSecondClickClosesIt()
    {
        var d = new IslandDirector();
        d.TrayClick(T(0));
        Assert.True(d.Expanded);
        Assert.False(d.PointerInside);
        d.Tick(T(1000));
        Assert.True(d.Expanded);
        d.TrayClick(T(1000));
        d.Tick(T(1000));
        Assert.False(d.Expanded);
    }

    [Fact]
    public void LeaveBeforeHoverDelay_DoesNotExpand()
    {
        var d = new IslandDirector();
        d.PointerEntered(T(0));
        d.PointerExited(T(40));
        d.Tick(T(500));
        Assert.False(d.Expanded);
    }

    [Fact]
    public void VolumeWhileCompact_OpensImmediately_ThenExpires()
    {
        var d = new IslandDirector();
        d.PushNotice(IslandMode.Volume, T(0));
        Assert.True(d.Expanded);
        Assert.Equal(IslandMode.Volume, d.Mode);

        d.Tick(T(1699));
        Assert.Equal(IslandMode.Volume, d.Mode);
        d.Tick(T(1700));
        Assert.Equal(IslandMode.Idle, d.Mode);
        Assert.False(d.Expanded);
    }

    [Fact]
    public void VolumeWhileReadingMusic_WaitsUntilThePointerLeaves()
    {
        var d = new IslandDirector();
        d.SetAmbient(AmbientKind.Media);
        d.Click(T(0));
        d.PushNotice(IslandMode.Volume, T(10));
        Assert.Equal(IslandMode.Media, d.Mode);

        d.PointerExited(T(10));
        d.Tick(T(489));
        Assert.Equal(IslandMode.Media, d.Mode);
        d.Tick(T(490));
        Assert.Equal(IslandMode.Volume, d.Mode);
        Assert.True(d.Expanded);
    }

    [Fact]
    public void StaleQueuedVolume_IsDropped()
    {
        var d = new IslandDirector();
        d.SetAmbient(AmbientKind.Media);
        d.Click(T(0));
        d.PushNotice(IslandMode.Volume, T(0));
        d.PointerExited(T(0));
        d.Tick(T(4000));
        Assert.Equal(IslandMode.Media, d.Mode);
        Assert.False(d.Expanded);
    }

    [Fact]
    public void NoticeExpiryWhileHovered_MorphsToAmbientExpanded()
    {
        var d = new IslandDirector();
        d.SetAmbient(AmbientKind.Media);
        d.PushNotice(IslandMode.Volume, T(0));
        d.PointerEntered(T(100));
        d.Tick(T(1700));
        Assert.Equal(IslandMode.Media, d.Mode);
        Assert.True(d.Expanded);
    }

    [Fact]
    public void RepeatingVolume_ExtendsTheHold()
    {
        var d = new IslandDirector();
        d.PushNotice(IslandMode.Volume, T(0));
        d.PushNotice(IslandMode.Volume, T(1000));
        d.Tick(T(1700));
        Assert.Equal(IslandMode.Volume, d.Mode);
        d.Tick(T(2700));
        Assert.Equal(IslandMode.Idle, d.Mode);
    }

    [Fact]
    public void AmbientChangeDuringNotice_AppliesAfterTheNotice()
    {
        var d = new IslandDirector();
        d.PushNotice(IslandMode.Battery, T(0));
        d.SetAmbient(AmbientKind.Timer);
        Assert.Equal(IslandMode.Battery, d.Mode);
        d.Tick(T(2500));
        Assert.Equal(IslandMode.Timer, d.Mode);
    }

    [Fact]
    public void BatteryReplacesVolume()
    {
        var d = new IslandDirector();
        d.PushNotice(IslandMode.Volume, T(0));
        d.PushNotice(IslandMode.Battery, T(200));
        Assert.Equal(IslandMode.Battery, d.Mode);
        Assert.True(d.Expanded);
    }

    [Fact]
    public void Frames_CompactIsAPill_ExpandedCardIsLarger()
    {
        var idle = IslandMetrics.FrameFor(IslandMode.Idle, false);
        var media = IslandMetrics.FrameFor(IslandMode.Media, true);
        Assert.Equal(idle.Height / 2, idle.Radius);
        Assert.Equal(2, idle.CornerN);
        Assert.True(media.Width > idle.Width);
        Assert.True(media.Height > idle.Height);
        Assert.True(media.CornerN > 3);
        Assert.True(media.Radius <= media.Height / 2);
    }
}
