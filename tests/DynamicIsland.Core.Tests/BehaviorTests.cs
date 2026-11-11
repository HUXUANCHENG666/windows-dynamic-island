using DynamicIsland.Core.Formatting;
using DynamicIsland.Core.Motion;
using DynamicIsland.Core.Ipc;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.SystemState;
using DynamicIsland.Core.Timer;

namespace DynamicIsland.Core.Tests;

public class BehaviorTests
{
    [Theory]
    [InlineData(true, true, true, AmbientKind.Media)]
    [InlineData(true, true, false, AmbientKind.Media)]
    [InlineData(false, true, true, AmbientKind.Timer)]
    [InlineData(false, true, false, AmbientKind.Media)]
    [InlineData(false, false, true, AmbientKind.Timer)]
    [InlineData(false, false, false, AmbientKind.Idle)]
    public void Ambient_Priority(bool playing, bool open, bool timer, AmbientKind expected)
    {
        Assert.Equal(expected, AmbientPolicy.Choose(playing, open, timer));
    }

    [Fact]
    public void BatteryPercent_OnlyOnUsefulSteps()
    {
        Assert.False(NoticePolicy.ShouldAnnounceBatteryPercent(-1, 80));
        Assert.False(NoticePolicy.ShouldAnnounceBatteryPercent(78, 77));
        Assert.True(NoticePolicy.ShouldAnnounceBatteryPercent(80, 79));
        Assert.False(NoticePolicy.ShouldAnnounceBatteryPercent(40, 55));
        Assert.True(NoticePolicy.ShouldAnnounceBatteryPercent(21, 20));
        Assert.True(NoticePolicy.ShouldAnnounceBatteryPercent(11, 10));
        Assert.True(NoticePolicy.ShouldAnnounceBatteryPercent(6, 5));
        Assert.True(NoticePolicy.ShouldAnnounceBatteryPercent(50, 49));
        Assert.False(NoticePolicy.ShouldAnnounceBatteryPercent(49, 41));
    }

    [Fact]
    public void Power_PlugAndUnplug_DesktopWithoutBatteryIsSilent()
    {
        var desktop = PowerLogic.Parse(1, 128, 255);
        Assert.False(desktop.HasBattery);
        Assert.Null(PowerLogic.Diff(null, desktop));

        var unplugged = PowerLogic.Parse(0, 1, 80);
        var plugged = PowerLogic.Parse(1, 8, 80);
        Assert.Null(PowerLogic.Diff(null, unplugged));
        var e = PowerLogic.Diff(unplugged, plugged);
        Assert.NotNull(e);
        Assert.True(e.Value.Charging);
        Assert.Equal("正在充电", e.Value.Title);

        var back = PowerLogic.Diff(plugged, unplugged);
        Assert.Equal("已断开电源", back!.Value.Title);

        var low = PowerLogic.Parse(0, 4, 20);
        var warn = PowerLogic.Diff(PowerLogic.Parse(0, 1, 21), low);
        Assert.Equal("电池", warn!.Value.Title);

        var critical = PowerLogic.Diff(PowerLogic.Parse(0, 1, 11), PowerLogic.Parse(0, 1, 9));
        Assert.Equal("电量不足", critical!.Value.Title);
    }

    [Fact]
    public void Volume_IgnoresTheFirstSample_AndTinyNoise()
    {
        var half = new VolumeReading(0.5f, false);
        Assert.False(VolumeLogic.ShouldAnnounce(null, half));
        Assert.False(VolumeLogic.ShouldAnnounce(half, new VolumeReading(0.504f, false)));
        Assert.True(VolumeLogic.ShouldAnnounce(half, new VolumeReading(0.56f, false)));
        Assert.True(VolumeLogic.ShouldAnnounce(half, new VolumeReading(0.5f, true)));
        Assert.Equal(56, new VolumeReading(0.555f, false).Percent);
    }

    [Fact]
    public void Timeline_AdvancesOnlyWhilePlaying_AndClamps()
    {
        var clock = new TimelineEstimator();
        var t0 = DateTime.UnixEpoch;
        clock.Update(TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromSeconds(15), true, t0);
        Assert.Equal(TimeSpan.FromSeconds(12), clock.Estimate(t0.AddSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(15), clock.Estimate(t0.AddSeconds(20)));

        clock.Update(TimeSpan.FromSeconds(12), TimeSpan.Zero, TimeSpan.FromSeconds(30), false, t0.AddSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(12), clock.Estimate(t0.AddSeconds(40)));
    }

    [Theory]
    [InlineData("Playing", PlaybackPhase.Playing)]
    [InlineData("Paused", PlaybackPhase.Open)]
    [InlineData("Changing", PlaybackPhase.Open)]
    [InlineData("Stopped", PlaybackPhase.None)]
    [InlineData(null, PlaybackPhase.None)]
    public void Playback_MapsStatus(string? status, PlaybackPhase phase)
    {
        Assert.Equal(phase, PlaybackMap.Map(status));
    }

    [Fact]
    public void TimeFormat_AndAppId()
    {
        Assert.Equal("1:02", TimeFormat.Media(TimeSpan.FromSeconds(62)));
        Assert.Equal("1:02:03", TimeFormat.Media(TimeSpan.FromSeconds(3723)));
        Assert.Equal("00:01.50", TimeFormat.Stopwatch(TimeSpan.FromMilliseconds(1500)));
        Assert.Equal("01:00", TimeFormat.Countdown(TimeSpan.FromSeconds(59.2)));
        Assert.Equal("00:00", TimeFormat.Countdown(TimeSpan.Zero));
        Assert.Equal(0.5, TimeFormat.Progress(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)), 3);
        Assert.Equal("Spotify", TimeFormat.PrettyAppId(@"C:\Apps\Spotify.exe"));
        Assert.Equal("Microsoft.ZuneMusic", TimeFormat.PrettyAppId("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"));
    }

    [Fact]
    public void Stopwatch_AccumulatesAcrossPauses()
    {
        var now = DateTime.UnixEpoch;
        var timer = new IslandTimer(() => now);
        timer.SetMode(TimerMode.Stopwatch);
        timer.Start();
        now = now.AddSeconds(2.5);
        timer.Pause();
        now = now.AddSeconds(10);
        timer.Start();
        now = now.AddSeconds(1);
        Assert.Equal(TimeSpan.FromSeconds(3.5), timer.Elapsed);
        Assert.False(timer.ConsumeCompletion());
    }

    [Fact]
    public void Countdown_CompletesOnce_AndAddMinutesClamps()
    {
        var now = DateTime.UnixEpoch;
        var timer = new IslandTimer(() => now);
        timer.SetMode(TimerMode.Countdown);
        timer.SetCountdownMinutes(1);
        timer.AddMinutes(-10);
        Assert.Equal(0.25, timer.CountdownLength.TotalMinutes);
        timer.SetCountdownMinutes(1);
        timer.Start();
        now = now.AddSeconds(30);
        Assert.False(timer.ConsumeCompletion());
        Assert.Equal("00:30", TimeFormat.Countdown(timer.Remaining));
        now = now.AddSeconds(30);
        Assert.True(timer.ConsumeCompletion());
        Assert.False(timer.ConsumeCompletion());
        Assert.True(timer.IsCompleted);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
    }

    [Fact]
    public void Import_ResumesAndAnnouncesARecentFinish()
    {
        var now = DateTime.UnixEpoch.AddMinutes(10);
        var timer = new IslandTimer(() => now);
        bool announce = timer.Import(new TimerState
        {
            Mode = "countdown",
            CountdownMinutes = 25,
            AccumulatedSeconds = 0,
            RunningSinceUnixMs = new DateTimeOffset(now.AddMinutes(-27)).ToUnixTimeMilliseconds()
        }, now);
        Assert.True(timer.IsCompleted);
        Assert.False(timer.IsRunning);
        Assert.False(announce);

        announce = timer.Import(new TimerState
        {
            Mode = "countdown",
            CountdownMinutes = 5,
            AccumulatedSeconds = 0,
            RunningSinceUnixMs = new DateTimeOffset(now.AddMinutes(-5).AddSeconds(-2)).ToUnixTimeMilliseconds()
        }, now);
        Assert.True(announce);
    }

    [Fact]
    public void Commands_AndSettings_RoundTrip()
    {
        var toast = ClientCommand.ParseArgs(new[] { "--toast", "标题", "第一行", "第二行" });
        Assert.Equal("toast", toast.Cmd);
        Assert.Equal("标题", toast.Title);
        Assert.Equal("第一行 第二行", toast.Body);
        var again = ClientCommand.FromJson(toast.ToJson());
        Assert.Equal(toast.Body, again.Body);

        var timer = ClientCommand.ParseArgs(new[] { "--timer", "25" });
        Assert.Equal("countdown", timer.Mode);
        Assert.Equal(25, timer.Minutes);

        var noisy = new ClientCommand { Cmd = "toast", Title = "  a\nb  ", Body = new string('字', 200), Minutes = 9999 }.Sanitized();
        Assert.DoesNotContain('\n', noisy.Title);
        Assert.Equal(96, noisy.Body.Length);
        Assert.Equal(600, noisy.Minutes);

        var settings = new AppSettings
        {
            MonitorDeviceName = @"\\.\DISPLAY2",
            Timer = new TimerState { Mode = "countdown", CountdownMinutes = 15, Pinned = true }
        };
        var loaded = SettingsJson.Deserialize(SettingsJson.Serialize(settings));
        Assert.Equal(settings.MonitorDeviceName, loaded.MonitorDeviceName);
        Assert.Equal(15, loaded.Timer.CountdownMinutes);
        Assert.True(loaded.Timer.Pinned);
        Assert.NotNull(SettingsJson.Deserialize("{"));
    }

    [Fact]
    public void Placement_CentersOnTheChosenMonitor()
    {
        var rect = WindowPlacement.CenterTop(1920, -240, 2560, 1440, 144, 144, 760, 360);
        Assert.Equal(1140, rect.Width);
        Assert.Equal(540, rect.Height);
        Assert.Equal(1920 + (2560 - 1140) / 2, rect.X);
        Assert.Equal(-240, rect.Y);
        Assert.Equal(10 + 2.0 / 3.0, WindowPlacement.SnapDip(10.4, 1.5), 5);
    }

    [Fact]
    public void Easing_CrossfadesInTheMiddle()
    {
        Assert.Equal(0, Easing.ExpandFade(0), 3);
        Assert.Equal(1, Easing.ExpandFade(1), 3);
        Assert.Equal(1, Easing.CollapseFade(0), 3);
        Assert.Equal(0, Easing.CollapseFade(1), 3);
        Assert.InRange(Easing.ExpandFade(0.5), 0.4, 0.7);
        Assert.InRange(Easing.CollapseFade(0.5), 0.4, 0.7);
        Assert.Equal(0, Easing.BlurFor(1), 3);
        Assert.True(Easing.BlurFor(0) > 5);
    }
}
