using DynamicIsland.Core.Notifications;

namespace DynamicIsland.Core.Tests;

public class PopupAlertTests
{
    static readonly string[] Watched = { "wechat" };

    static ShownWindow Window(
        string process = "wechat",
        string className = "ChatWnd",
        int width = 420,
        int height = 260,
        bool visible = true,
        bool topLevel = true,
        bool shell = false,
        bool own = false) =>
        new(visible, topLevel, shell, own, width, height, className, process);

    [Fact]
    public void WatchedProcess_PopupAboveThreshold_Alerts()
    {
        Assert.True(PopupAlert.ShouldAlert(Window(), Watched));
    }

    [Fact]
    public void UnwatchedProcess_DoesNotAlert()
    {
        Assert.False(PopupAlert.ShouldAlert(Window(process: "chrome"), Watched));
    }

    [Fact]
    public void EmptyWatchList_DoesNotAlert()
    {
        Assert.False(PopupAlert.ShouldAlert(Window(), Array.Empty<string>()));
        Assert.False(PopupAlert.ShouldAlert(Window(), null));
    }

    [Fact]
    public void SmallWindow_IsIgnored()
    {
        Assert.False(PopupAlert.ShouldAlert(Window(width: 60, height: 24), Watched));
    }

    [Fact]
    public void ChildWindow_IsIgnored()
    {
        Assert.False(PopupAlert.ShouldAlert(Window(topLevel: false), Watched));
    }

    [Fact]
    public void ShellOwnAndHidden_AreIgnored()
    {
        Assert.False(PopupAlert.ShouldAlert(Window(shell: true), Watched));
        Assert.False(PopupAlert.ShouldAlert(Window(own: true), Watched));
        Assert.False(PopupAlert.ShouldAlert(Window(visible: false), Watched));
    }

    [Theory]
    [InlineData("tooltips_class32")]
    [InlineData("#32768")]
    [InlineData("IME")]
    [InlineData("MSCTFIME UI")]
    public void NoiseClasses_AreIgnored(string className)
    {
        Assert.True(PopupAlert.IsNoiseClass(className));
        Assert.False(PopupAlert.ShouldAlert(Window(className: className), Watched));
    }

    [Fact]
    public void Matching_IgnoresCasePathAndExtension()
    {
        Assert.True(PopupAlert.ShouldAlert(Window(process: "WeChat.exe"), Watched));
        Assert.True(PopupAlert.ShouldAlert(Window(process: @"C:\Program Files\WeChat\WeChat.exe"), Watched));
    }

    [Fact]
    public void Feed_SuppressesRepeatsWithinTheWindow()
    {
        var feed = new PopupFeed();
        var now = new DateTime(2026, 11, 12, 21, 0, 0, DateTimeKind.Utc);

        Assert.True(feed.Allow("wechat", now));
        Assert.False(feed.Allow("wechat", now.AddSeconds(1)));
        Assert.True(feed.Allow("wechat", now + PopupAlert.RepeatWindow + TimeSpan.FromMilliseconds(1)));
        Assert.True(feed.Allow("qq", now.AddSeconds(1)));
    }
}
