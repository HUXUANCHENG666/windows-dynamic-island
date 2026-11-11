using DynamicIsland.Core.Island;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Tests;

public class NotificationTests
{
    const string Secret = "正文密钥-请勿落盘-9f3a";

    static DateTime T(int ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    [Fact]
    public void Access_StaysOffUntilTheUserIsAllowed()
    {
        Assert.False(NotificationAccessPolicy.EnabledByDefault);
        Assert.False(new AppSettings().NotificationCenter);
        Assert.False(NotificationAccessPolicy.MayListen(false, NotificationAccess.Allowed));
        Assert.False(NotificationAccessPolicy.MayListen(true, NotificationAccess.Denied));
        Assert.False(NotificationAccessPolicy.MayListen(true, NotificationAccess.Unspecified));
        Assert.False(NotificationAccessPolicy.MayListen(true, NotificationAccess.Unknown));
        Assert.False(NotificationAccessPolicy.MayListen(true, NotificationAccess.Unavailable));
        Assert.True(NotificationAccessPolicy.MayListen(true, NotificationAccess.Allowed));
        // 轮询只是事件订阅不可用时的兜底，通知堆得多时每次都要跨进程重读，
        // 所以不再要求亚秒级；上限放宽到 8 秒，避免有人把它改回 1 秒。
        Assert.InRange(NotificationPolicy.PollInterval.TotalMilliseconds, 1000, 8000);
    }

    [Fact]
    public void DeniedOrDisabled_DoesNotConsumeTheFeed()
    {
        var feed = new NotificationFeed();
        var now = T(0);
        var existing = new[] { Sample(7, "微信", "标题", Secret) };
        Assert.Empty(NotificationSession.Ingest(feed, true, NotificationAccess.Denied, existing, now));
        Assert.False(feed.IsSeeded);
        Assert.Empty(NotificationSession.Ingest(feed, false, NotificationAccess.Allowed, existing, now));
        Assert.False(feed.IsSeeded);

        Assert.Empty(NotificationSession.Ingest(feed, true, NotificationAccess.Allowed, existing, now));
        Assert.True(feed.IsSeeded);
        Assert.Empty(NotificationSession.Ingest(feed, true, NotificationAccess.Allowed, existing, now.AddSeconds(2)));

        var fresh = Sample(8, "QQ", "新消息", "在吗");
        var shown = NotificationSession.Ingest(
            feed,
            true,
            NotificationAccess.Allowed,
            new[] { existing[0], fresh },
            now.AddSeconds(3));
        var card = Assert.Single(shown);
        Assert.Equal("QQ", card.AppName);
        Assert.Equal("新消息", card.Title);
        Assert.Equal("在吗", card.Summary);
        Assert.Equal(NotificationLaunchKind.App, card.Launch.Kind);
        Assert.Equal("QQ", card.Launch.Target);
    }

    [Fact]
    public void Payload_MapsAppTitleAndShortSummary()
    {
        var card = NotificationText.Map(new NotificationSource(
            3,
            " 微 信 ",
            " 你好\r\n世界 ",
            new[] { "在\t吗", "  ", "第二句" },
            null,
            "WeChat"));
        Assert.NotNull(card);
        Assert.Equal("微 信", card!.AppName);
        Assert.Equal("你好 世界", card.Title);
        Assert.Equal("在 吗 第二句", card.Summary);

        var titleOnly = NotificationText.Map(new NotificationSource(4, "QQ", null, new[] { "只\n有摘要" }, null, "QQ"));
        Assert.Equal("QQ", titleOnly!.AppName);
        Assert.Equal("只 有摘要", titleOnly.Title);
        Assert.Equal("", titleOnly.Summary);

        var appOnly = NotificationText.Map(new NotificationSource(5, "邮箱", " ", null, null, "Mail"));
        Assert.Equal("邮箱", appOnly!.AppName);
        Assert.Equal("新通知", appOnly.Title);
        Assert.Equal("", appOnly.Summary);

        Assert.Null(NotificationText.Map(new NotificationSource(6, " \n ", null, new[] { "\t" }, null, null)));

        var echoed = NotificationText.Map(new NotificationSource(9, "微信", "你好", new[] { "你好" }, null, "WeChat"));
        Assert.Equal("", echoed!.Summary);

        var longBody = new string('字', 120);
        var clipped = NotificationText.Map(new NotificationSource(10, "微信", "标题", new[] { longBody }, null, "WeChat"));
        Assert.Equal(NotificationText.MaxSummary, clipped!.Summary.Length);
        Assert.EndsWith("…", clipped.Summary);
        Assert.DoesNotContain("\n", clipped.Summary);
        Assert.DoesNotContain(longBody, clipped.Summary);
    }

    [Fact]
    public void Launch_UsesAProtocolOrTheApp_AndRejectsFileCommands()
    {
        var web = NotificationLaunchPlanner.Plan("https://example.com/a?b=1", "WeChat");
        Assert.Equal(NotificationLaunchKind.Protocol, web.Kind);
        Assert.StartsWith("https://example.com/", web.Target);

        var appScheme = NotificationLaunchPlanner.Plan("weixin://dl/chat", null);
        Assert.Equal(NotificationLaunchKind.Protocol, appScheme.Kind);

        var settings = NotificationLaunchPlanner.Plan("ms-settings:privacy-notifications", null);
        Assert.Equal(NotificationLaunchKind.Protocol, settings.Kind);

        var file = NotificationLaunchPlanner.Plan("file:///C:/Windows/system32/cmd.exe", "WeChat");
        Assert.Equal(NotificationLaunchKind.App, file.Kind);
        Assert.Equal("WeChat", file.Target);
        Assert.False(NotificationLaunchPlanner.IsAllowedProtocol(file.Arguments));

        var script = NotificationLaunchPlanner.Plan("javascript:alert(1)", "WeChat");
        Assert.Equal(NotificationLaunchKind.App, script.Kind);

        var command = NotificationLaunchPlanner.Plan("cmd /c calc.exe", "Contoso.App");
        Assert.Equal(NotificationLaunchKind.App, command.Kind);
        Assert.Equal("Contoso.App", command.Target);

        Assert.Equal(NotificationLaunchKind.None, NotificationLaunchPlanner.Plan(null, @"C:\Windows\system32\cmd.exe").Kind);
        Assert.Equal(NotificationLaunchKind.None, NotificationLaunchPlanner.Plan(null, "bad&id").Kind);
        Assert.Equal(NotificationLaunchKind.App, NotificationLaunchPlanner.Plan(null, "Microsoft.Windows.Photos_8wekyb3d8bbwe!App").Kind);
    }

    [Fact]
    public void Feed_SkipsDuplicates_AndAnnouncesAChangedSummary()
    {
        var feed = new NotificationFeed();
        feed.Seed(new[] { Sample(1, "微信", "旧的", "还在通知中心") });
        Assert.Null(feed.Observe(Sample(1, "微信", "旧的", "还在通知中心"), T(0)));

        var first = feed.Observe(Sample(2, "微信", "你好", "在吗"), T(0));
        Assert.Equal("在吗", first!.Summary);
        Assert.Null(feed.Observe(Sample(3, "微信", "你好", "在吗"), T(100)));
        Assert.Equal("在吗", feed.Observe(Sample(4, "微信", "你好", "在吗"), T(1000))!.Summary);

        var changed = feed.Observe(Sample(2, "微信", "你好", "第二句"), T(2000));
        Assert.Equal("第二句", changed!.Summary);

        feed.Forget(2);
        var ids = new HashSet<uint> { 1u };
        feed.Retain(ids);
        Assert.Null(feed.Observe(Sample(1, "微信", "旧的", "还在通知中心"), T(3000)));
        Assert.NotNull(feed.Observe(Sample(2, "微信", "你好", "又来了"), T(4000)));
    }

    [Fact]
    public void Logs_NeverContainTheBodyTitleOrSummary()
    {
        var source = new NotificationSource(
            11,
            "微信",
            "标题" + Secret,
            new[] { Secret + "摘要" },
            "launch-" + Secret,
            "WeChat");
        var card = NotificationText.Map(source);
        Assert.NotNull(card);
        Assert.Contains(Secret, card!.Title);
        Assert.Contains(Secret, card.Summary);
        Assert.Contains(Secret, card.Launch.Arguments);

        var lines = new List<string>
        {
            NotificationLog.Access(NotificationAccess.Denied),
            NotificationLog.Access(NotificationAccess.Allowed),
            NotificationLog.Access(NotificationAccess.Unspecified),
            NotificationLog.Arrival(card),
            NotificationLog.Failed(unchecked((int)0x80070490))
        };
        foreach (NotificationLogEvent kind in Enum.GetValues<NotificationLogEvent>())
            lines.Add(NotificationLog.Line(kind));

        foreach (var line in lines)
        {
            Assert.False(NotificationLog.Exposes(line, Secret, card.Title, card.Summary, card.Launch.Arguments, card.AppName));
            Assert.DoesNotContain("微信", line);
        }

        Assert.False(NotificationPrivacy.UploadsContent);
        Assert.False(NotificationPrivacy.MayTransmit(card.Title, card.Summary, Secret));
        Assert.Contains("微信", NotificationCopy.FirstUse);
        Assert.Contains("系统通知", NotificationCopy.FirstUse);
        Assert.Contains("隐私和安全性", NotificationCopy.Denied);
        Assert.Contains("微信", NotificationCopy.Allowed);
        Assert.Contains("系统通知", NotificationCopy.Allowed);
        Assert.Contains("通知中心", NotificationCopy.Allowed);
    }

    [Fact]
    public void StealthAndFullscreen_PopTheNotificationThenCollapse()
    {
        Assert.True(NoticePolicy.GuidanceHold > NoticePolicy.ToastHold);
        Assert.True(HidePolicy.IsOccluded(new HideSignals(true, false, false, false, null), Array.Empty<string>()));
        var stealth = new IslandDirector();
        stealth.SetOccluded(true, T(0));
        Assert.True(stealth.ShowAsDot);
        stealth.PushNotice(IslandMode.Toast, T(0));
        Assert.False(stealth.ShowAsDot);
        Assert.Equal(IslandMode.Toast, stealth.Mode);
        Assert.True(stealth.Expanded);
        stealth.Tick(T((int)NoticePolicy.ToastHold.TotalMilliseconds - 1));
        Assert.Equal(IslandMode.Toast, stealth.Mode);
        stealth.Tick(T((int)NoticePolicy.ToastHold.TotalMilliseconds));
        Assert.True(stealth.ShowAsDot);
        Assert.False(stealth.Expanded);
        Assert.Equal(IslandMode.Idle, stealth.Mode);

        Assert.True(HidePolicy.IsOccluded(new HideSignals(false, true, true, true, "game"), Array.Empty<string>()));
        Assert.False(HidePolicy.IsOccluded(new HideSignals(false, true, false, false, "game"), Array.Empty<string>()));
        var fullscreen = new IslandDirector();
        fullscreen.SetOccluded(true, T(0));
        fullscreen.PushNotice(IslandMode.Toast, T(10));
        Assert.Equal(IslandMode.Toast, fullscreen.Mode);
        Assert.False(fullscreen.ShowAsDot);
        fullscreen.Tick(T(10 + (int)NoticePolicy.ToastHold.TotalMilliseconds));
        Assert.True(fullscreen.ShowAsDot);
        Assert.Equal(IslandMode.Idle, fullscreen.Mode);

        var guidance = new IslandDirector();
        guidance.PushNotice(IslandMode.Toast, T(0), NoticePolicy.GuidanceHold);
        guidance.Tick(T((int)NoticePolicy.ToastHold.TotalMilliseconds));
        Assert.Equal(IslandMode.Toast, guidance.Mode);
        guidance.Tick(T((int)NoticePolicy.GuidanceHold.TotalMilliseconds));
        Assert.Equal(IslandMode.Idle, guidance.Mode);
    }

    [Fact]
    public void NotificationDuringCamera_ReturnsToThePreviewAndKeepsTheSession()
    {
        var director = new IslandDirector();
        director.SetCamera(true);
        director.PointerEntered(T(0));
        director.PushNotice(IslandMode.Toast, T(50));
        Assert.Equal(IslandMode.Toast, director.Mode);
        Assert.True(director.CameraOpen);
        Assert.True(director.CameraSession);
        director.Tick(T(50 + (int)NoticePolicy.ToastHold.TotalMilliseconds));
        Assert.Equal(IslandMode.Camera, director.Mode);
        Assert.True(director.Expanded);
        Assert.True(director.CameraOpen);
        Assert.True(director.CameraSession);

        director.PushNotice(IslandMode.Volume, T(4000));
        Assert.Equal(IslandMode.Volume, director.Mode);
        Assert.True(director.CameraSession);
        director.Tick(T(4000 + (int)NoticePolicy.VolumeHold.TotalMilliseconds));
        Assert.Equal(IslandMode.Camera, director.Mode);
        Assert.True(director.CameraSession);
    }

    [Fact]
    public void NotificationDuringTranslation_ReturnsToTheResult_AndCameraStillWins()
    {
        var director = new IslandDirector();
        director.ShowTranslationResult("岛上的译文", T(0));
        director.PointerEntered(T(0));
        director.PushNotice(IslandMode.Toast, T(20));
        Assert.Equal(IslandMode.Toast, director.Mode);
        Assert.Equal("岛上的译文", director.LastTranslation);
        Assert.False(director.IsShowingTranslation);
        director.Tick(T(20 + (int)NoticePolicy.ToastHold.TotalMilliseconds));
        Assert.True(director.IsShowingTranslation);
        Assert.Equal("岛上的译文", director.LastTranslation);
        Assert.Equal(IslandMode.Translate, director.Mode);

        director.SetCamera(true);
        director.ShowTranslationResult("另一条", T(4000));
        Assert.False(director.CameraOpen);
        Assert.Equal(IslandMode.Translate, director.Mode);
    }

    [Fact]
    public void NotificationService_DoesNotLogOrUploadNotificationText()
    {
        var service = ReadRepo("src/DynamicIsland/Services/NotificationCenterService.cs");
        var activator = ReadRepo("src/DynamicIsland/Services/NotificationActivator.cs");
        Assert.Contains("GetNotificationsAsync", service);
        Assert.Contains("RequestAccessAsync", service);
        Assert.Contains("MayListen", service);
        Assert.DoesNotContain("Log.Error", service);
        foreach (var source in new[] { service, activator })
        {
            Assert.DoesNotContain("HttpClient", source);
            Assert.DoesNotContain("WebRequest", source);
            Assert.DoesNotContain("mymemory", source, StringComparison.OrdinalIgnoreCase);
            foreach (var line in source.Split('\n'))
            {
                if (!line.Contains("Log.", StringComparison.Ordinal))
                    continue;
                Assert.DoesNotContain("Title", line);
                Assert.DoesNotContain("Summary", line);
                Assert.DoesNotContain("Body", line);
                Assert.DoesNotContain(".Text", line);
                Assert.DoesNotContain("AppName", line);
                Assert.DoesNotContain("Details", line);
                Assert.DoesNotContain("Launch", line);
            }
        }
    }

    static NotificationSource Sample(uint id, string app, string title, string summary) =>
        new(id, app, title, new[] { summary }, null, app);

    static string ReadRepo(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
