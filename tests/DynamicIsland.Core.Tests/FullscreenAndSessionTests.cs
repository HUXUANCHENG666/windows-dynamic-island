using DynamicIsland.Core.Island;
using DynamicIsland.Core.Media;

namespace DynamicIsland.Core.Tests;

public class FullscreenAndSessionTests
{
    static readonly PixelRect Primary = new(0, 0, 1920, 1080);
    static readonly PixelRect Second = new(1920, 0, 1920, 1080);

    static TopWindow Window(PixelRect rect, bool borderless = false, bool ignore = false, bool maximized = false) =>
        new(rect, ignore, borderless, maximized);

    [Fact]
    public void Fullscreen_BorderlessCover_HidesOnlyOnThatMonitor()
    {
        var kazumi = Window(new PixelRect(0, 0, 1920, 1080), borderless: true);
        Assert.True(FullscreenPolicy.ShouldHide(Primary, new[] { kazumi }, ShellAttention.None));
        Assert.False(FullscreenPolicy.ShouldHide(Second, new[] { kazumi }, ShellAttention.None));

        var overflow = Window(new PixelRect(-4, -4, 1928, 1088), borderless: true);
        Assert.True(FullscreenPolicy.ShouldHide(Primary, new[] { overflow }, ShellAttention.Busy));

        var browser = Window(new PixelRect(0, 0, 1920, 1080), borderless: true);
        Assert.True(FullscreenPolicy.ShouldHide(Primary, new[] { browser }, ShellAttention.None));

        var otherScreen = Window(Second, borderless: true);
        var local = Window(new PixelRect(200, 160, 1280, 800));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { otherScreen, local }, ShellAttention.Presentation));
        Assert.True(FullscreenPolicy.ShouldHide(Second, new[] { otherScreen, local }, ShellAttention.Presentation));
    }

    [Fact]
    public void Fullscreen_BorderlessNearlyCovering_Hides_CaptionedMaximizeDoesNot()
    {
        var kazumiOverTaskbarGap = Window(new PixelRect(0, 0, 1920, 1032), borderless: true);
        Assert.True(FullscreenPolicy.NearlyCovers(kazumiOverTaskbarGap.Rect, Primary));
        Assert.True(FullscreenPolicy.ShouldHide(Primary, new[] { kazumiOverTaskbarGap }, ShellAttention.Busy));

        // 同样的尺寸，只要带 WS_MAXIMIZE 就是普通最大化窗口，不能让它把岛藏起来。
        var maximizedBorderless = Window(new PixelRect(0, 0, 1920, 1032), borderless: true, maximized: true);
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { maximizedBorderless }, ShellAttention.Busy));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { maximizedBorderless }, ShellAttention.None));

        var maximizedWord = Window(new PixelRect(0, 0, 1920, 1032), borderless: false);
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { maximizedWord }, ShellAttention.Busy));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { maximizedWord }, ShellAttention.None));

        var small = Window(new PixelRect(400, 200, 640, 480), borderless: true);
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { small }, ShellAttention.Busy));

        // 自动隐藏任务栏时，普通最大化窗口的矩形会盖住整块屏，仍然不能隐身。
        var maximizedCaption = Window(Primary, borderless: false, maximized: true);
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { maximizedCaption }, ShellAttention.None));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { maximizedCaption }, ShellAttention.Direct3D));

        // 无边框又盖住整块屏，就算还留着 WS_MAXIMIZE，也是直接全屏。
        var kazumiStaleMaximize = Window(Primary, borderless: true, maximized: true);
        Assert.True(FullscreenPolicy.ShouldHide(Primary, new[] { kazumiStaleMaximize }, ShellAttention.None));
    }

    [Fact]
    public void Fullscreen_ShellTransitionWindowsAreNotFullscreen()
    {
        Assert.True(FullscreenPolicy.IsShellOrDesktop("Shell_LightDismissOverlay"));
        Assert.True(FullscreenPolicy.IsShellOrDesktop("Windows.Internal.Shell.TabProxyWindow"));
        Assert.True(FullscreenPolicy.IsShellOrDesktop("MultitaskingViewFrame"));
        Assert.True(FullscreenPolicy.IsShellOrDesktop("TopLevelWindowForOverflowXamlIsland"));
        Assert.True(FullscreenPolicy.IsShellOrDesktop("NotifyIconOverflowWindow"));
        Assert.False(FullscreenPolicy.IsShellOrDesktop("FLUTTER_RUNNER_WIN32_WINDOW"));
        Assert.False(FullscreenPolicy.IsShellOrDesktop("Windows.UI.Core.CoreWindow"));
        Assert.True(FullscreenPolicy.IsShellHostProcess("StartMenuExperienceHost.exe"));
        Assert.True(FullscreenPolicy.IsShellHostProcess("SearchHost.exe"));
        Assert.True(FullscreenPolicy.IsShellHostProcess("SearchApp.exe"));
        Assert.True(FullscreenPolicy.IsShellHostProcess("ShellExperienceHost.exe"));
        Assert.False(FullscreenPolicy.IsShellHostProcess("kazumi.exe"));
        Assert.False(FullscreenPolicy.IsShellHostProcess("explorer.exe"));
        Assert.True(FullscreenPolicy.IsShellOverlay("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost.exe"));
        Assert.True(FullscreenPolicy.IsShellOverlay("Windows.UI.Composition.DesktopWindowContentBridge", "SearchHost.exe"));
        Assert.False(FullscreenPolicy.IsShellOverlay("Windows.UI.Core.CoreWindow", "kazumi"));
        Assert.False(FullscreenPolicy.IsShellOverlay("FLUTTER_RUNNER_WIN32_WINDOW", "kazumi"));

        var start = Window(Primary, borderless: true, ignore: true);
        var kazumiBehindStart = Window(Primary, borderless: true);
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { start }, ShellAttention.None));
        Assert.True(FullscreenPolicy.ShouldHide(Primary, new[] { start, kazumiBehindStart }, ShellAttention.None));
        Assert.True(FullscreenPolicy.IsClickThrough(0x20));
        Assert.False(FullscreenPolicy.IsClickThrough(0));
        Assert.True(FullscreenPolicy.IsVisuallyBorderless(captionStyle: true, nonClientWidth: 0, nonClientHeight: 0));
        Assert.False(FullscreenPolicy.IsVisuallyBorderless(captionStyle: true, nonClientWidth: 16, nonClientHeight: 39));

        var overlay = Window(Primary, borderless: true, ignore: true);
        var notepad = Window(new PixelRect(200, 160, 900, 640));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { overlay, notepad }, ShellAttention.None));
    }

    [Fact]
    public void Occlusion_IgnoresABurstAndHidesOnlyAfterItStays()
    {
        var latch = new OcclusionLatch();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(latch.Observe(true, t0));
        Assert.False(latch.Observe(true, t0.AddMilliseconds(40)));
        Assert.False(latch.Engaged);
        Assert.False(latch.Observe(false, t0.AddMilliseconds(220)));

        Assert.False(latch.Observe(true, t0.AddMilliseconds(1000)));
        Assert.True(latch.Observe(true, t0.AddMilliseconds(1000 + (int)OcclusionLatch.EngageAfter.TotalMilliseconds)));
        Assert.True(latch.Engaged);
        Assert.False(latch.Observe(false, t0.AddMilliseconds(1600)));
        Assert.False(latch.Engaged);
    }

    [Fact]
    public void Fullscreen_Direct3D_UsesTheMonitorOfTheLargeForegroundWindow()
    {
        var game = Window(new PixelRect(40, 40, 1840, 1000));
        Assert.True(FullscreenPolicy.OverlapRatio(game.Rect, Primary) >= 0.85);
        Assert.True(FullscreenPolicy.ShouldHide(Primary, new[] { game }, ShellAttention.Direct3D));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { game }, ShellAttention.None));
        Assert.False(FullscreenPolicy.ShouldHide(Second, new[] { game }, ShellAttention.Direct3D));

        var elsewhere = Window(new PixelRect(1920, 0, 1920, 1080));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { elsewhere }, ShellAttention.Direct3D));
        Assert.True(FullscreenPolicy.ShouldHide(Second, new[] { elsewhere }, ShellAttention.Direct3D));
    }

    [Fact]
    public void Fullscreen_SkipsShellDesktopAndThisProcess_ThenLooksBehindThem()
    {
        Assert.True(FullscreenPolicy.IsShellOrDesktop("Progman"));
        Assert.True(FullscreenPolicy.IsShellOrDesktop("WorkerW"));
        Assert.True(FullscreenPolicy.IsShellOrDesktop("Shell_TrayWnd"));
        Assert.True(FullscreenPolicy.IsShellOrDesktop("Shell_SecondaryTrayWnd"));
        Assert.False(FullscreenPolicy.IsShellOrDesktop("FLUTTER_RUNNER_WIN32_WINDOW"));
        Assert.False(FullscreenPolicy.IsShellOrDesktop("Chrome_WidgetWin_1"));

        var desktop = Window(Primary, ignore: true);
        var ours = Window(new PixelRect(800, 0, 200, 40), ignore: true);
        var player = Window(Primary, borderless: true);
        Assert.True(FullscreenPolicy.ShouldHide(Primary, new[] { desktop, ours, player }, ShellAttention.None));

        var shell = Window(Primary, ignore: true);
        var notepad = Window(new PixelRect(300, 200, 900, 600));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { shell, notepad }, ShellAttention.Presentation));
    }

    [Fact]
    public void Sessions_PreferTheLatestPlaying_ThenTheSystemSession()
    {
        var board = new DisplayedSession();
        board.Update(new[]
        {
            new SessionObservation("spotify", true, true),
            new SessionObservation("edge", false, false)
        });
        Assert.Equal("spotify", board.CommandTarget);
        Assert.Equal(1, board.Index);
        Assert.Equal(2, board.Count);

        board.Update(new[]
        {
            new SessionObservation("spotify", true, true),
            new SessionObservation("edge", true, false),
            new SessionObservation("kazumi", false, false)
        });
        Assert.Equal("edge", board.CommandTarget);
        Assert.Equal("edge", board.SelectedId);

        board.Update(new[]
        {
            new SessionObservation("spotify", false, true),
            new SessionObservation("edge", false, false),
            new SessionObservation("kazumi", false, false)
        });
        Assert.Equal("spotify", board.CommandTarget);

        board.Update(new[]
        {
            new SessionObservation("edge", false, false),
            new SessionObservation("kazumi", false, false)
        });
        Assert.Equal("edge", board.CommandTarget);
    }

    [Fact]
    public void Sessions_SwitchAndRemoval_OnlyChangeTheDisplayedTarget()
    {
        var board = new DisplayedSession();
        board.Update(new[]
        {
            new SessionObservation("a", true, true),
            new SessionObservation("b", false, false),
            new SessionObservation("c", false, false)
        });
        board.Update(new[]
        {
            new SessionObservation("a", true, true),
            new SessionObservation("b", false, false),
            new SessionObservation("c", true, false)
        });
        Assert.Equal("c", board.CommandTarget);

        Assert.True(board.TrySwitch(1));
        Assert.Equal("a", board.CommandTarget);
        Assert.Equal(1, board.Index);
        Assert.Equal("a", board.PinnedId);

        board.Update(new[]
        {
            new SessionObservation("a", false, true),
            new SessionObservation("b", true, false),
            new SessionObservation("c", true, false)
        });
        Assert.Equal("a", board.CommandTarget);

        Assert.True(board.TrySwitch(-1));
        Assert.Equal("c", board.CommandTarget);
        Assert.Equal(3, board.Index);

        board.Update(new[]
        {
            new SessionObservation("a", true, true),
            new SessionObservation("b", false, false)
        });
        Assert.Equal("a", board.CommandTarget);
        Assert.Null(board.PinnedId);

        board.TrySwitch(1);
        Assert.Equal("b", board.CommandTarget);
        board.ClearPin();
        Assert.Equal("a", board.CommandTarget);
        Assert.Null(board.PinnedId);
    }

    [Fact]
    public void Sessions_AddedAndRemoved_KeepStableIds()
    {
        int counter = 0;
        var first = SessionIdentity.Assign(Array.Empty<string>(), Array.Empty<string>(), new[] { "Spotify", "MSEdge" }, ref counter);
        Assert.Equal(new[] { "s1", "s2" }, first);

        var again = SessionIdentity.Assign(first, new[] { "Spotify", "MSEdge" }, new[] { "MSEdge", "Spotify", "Kazumi" }, ref counter);
        Assert.Equal("s2", again[0]);
        Assert.Equal("s1", again[1]);
        Assert.Equal("s3", again[2]);

        var twins = SessionIdentity.Assign(
            new[] { "s1", "s2" },
            new[] { "Chrome", "Chrome" },
            new[] { "Chrome", "Chrome" },
            ref counter);
        Assert.Equal(new[] { "s1", "s2" }, twins);
    }
}
