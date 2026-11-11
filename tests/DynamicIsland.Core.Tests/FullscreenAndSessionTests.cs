using DynamicIsland.Core.Island;
using DynamicIsland.Core.Media;

namespace DynamicIsland.Core.Tests;

public class FullscreenAndSessionTests
{
    static readonly PixelRect Primary = new(0, 0, 1920, 1080);
    static readonly PixelRect Second = new(1920, 0, 1920, 1080);

    static TopWindow Window(PixelRect rect, bool borderless = false, bool ignore = false) =>
        new(rect, ignore, borderless);

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

        var maximizedWord = Window(new PixelRect(0, 0, 1920, 1032), borderless: false);
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { maximizedWord }, ShellAttention.Busy));
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { maximizedWord }, ShellAttention.None));

        var small = Window(new PixelRect(400, 200, 640, 480), borderless: true);
        Assert.False(FullscreenPolicy.ShouldHide(Primary, new[] { small }, ShellAttention.Busy));
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
