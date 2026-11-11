using DynamicIsland.Core.Camera;
using DynamicIsland.Core.Ipc;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Translation;

namespace DynamicIsland.Core.Tests;

public class FeatureTests
{
    static DateTime T(int ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    static bool Overlaps(PixelRect a, PixelRect b)
    {
        var hit = PixelRect.Intersect(a, b);
        return hit.Width > 0 && hit.Height > 0;
    }

    [Theory]
    [InlineData(500, 10, DockAnchor.TopCenter)]
    [InlineData(500, 990, DockAnchor.BottomCenter)]
    [InlineData(10, 500, DockAnchor.LeftCenter)]
    [InlineData(990, 500, DockAnchor.RightCenter)]
    [InlineData(10, 10, DockAnchor.TopLeft)]
    [InlineData(990, 10, DockAnchor.TopRight)]
    [InlineData(10, 990, DockAnchor.BottomLeft)]
    [InlineData(990, 990, DockAnchor.BottomRight)]
    [InlineData(500, 500, DockAnchor.TopCenter)]
    public void Snap_PicksTheNearestAnchor(int x, int y, DockAnchor expected)
    {
        var work = new PixelRect(0, 0, 1000, 1000);
        Assert.Equal(expected, SnapMath.Nearest(x, y, work, 0));
    }

    [Fact]
    public void Snap_OnlyLeftAndRightCentersAreVertical()
    {
        Assert.Equal(IslandOrientation.Vertical, Dock.OrientationOf(DockAnchor.LeftCenter));
        Assert.Equal(IslandOrientation.Vertical, Dock.OrientationOf(DockAnchor.RightCenter));
        Assert.Equal(IslandOrientation.Horizontal, Dock.OrientationOf(DockAnchor.TopCenter));
        Assert.Equal(IslandOrientation.Horizontal, Dock.OrientationOf(DockAnchor.BottomCenter));
        Assert.Equal(IslandOrientation.Horizontal, Dock.OrientationOf(DockAnchor.TopLeft));
        Assert.Equal(IslandOrientation.Horizontal, Dock.OrientationOf(DockAnchor.BottomRight));
        Assert.Equal(DockAnchor.TopCenter, Dock.Parse(null));
        Assert.Equal(DockAnchor.TopCenter, Dock.Parse("nope"));
    }

    [Fact]
    public void Dock_IsRememberedPerMonitor_AndDefaultsToTopCenter()
    {
        var docks = new List<MonitorDock>();
        Assert.Equal(DockAnchor.TopCenter, DockStore.Recall(docks, @"\\.\DISPLAY2"));
        DockStore.Remember(docks, @"\\.\DISPLAY2", DockAnchor.LeftCenter);
        DockStore.Remember(docks, @"\\.\DISPLAY1", DockAnchor.BottomRight);
        Assert.Equal(DockAnchor.LeftCenter, DockStore.Recall(docks, @"\\.\display2"));
        Assert.Equal(DockAnchor.BottomRight, DockStore.Recall(docks, @"\\.\DISPLAY1"));
        DockStore.Remember(docks, @"\\.\DISPLAY2", DockAnchor.RightCenter);
        Assert.Equal(DockAnchor.RightCenter, DockStore.Recall(docks, @"\\.\DISPLAY2"));
        Assert.Single(docks, item => item.DeviceName == @"\\.\DISPLAY2");
    }

    [Fact]
    public void Place_StaysOffEveryTaskbarEdge()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var edges = new Dictionary<string, (PixelRect Work, PixelRect Bar)>
        {
            ["bottom"] = (new PixelRect(0, 0, 1920, 1040), new PixelRect(0, 1040, 1920, 40)),
            ["top"] = (new PixelRect(0, 48, 1920, 1032), new PixelRect(0, 0, 1920, 48)),
            ["left"] = (new PixelRect(62, 0, 1858, 1080), new PixelRect(0, 0, 62, 1080)),
            ["right"] = (new PixelRect(0, 0, 1860, 1080), new PixelRect(1860, 0, 60, 1080))
        };

        foreach (var (name, edge) in edges)
        {
            foreach (DockAnchor anchor in Enum.GetValues<DockAnchor>())
            {
                var horizontal = SnapMath.Place(anchor, edge.Work, 760, 360, monitor);
                var vertical = SnapMath.Place(anchor, edge.Work, 420, 560, monitor);
                Assert.False(Overlaps(horizontal, edge.Bar), $"{name} {anchor} horizontal");
                Assert.False(Overlaps(vertical, edge.Bar), $"{name} {anchor} vertical");
                if (horizontal.Width <= edge.Work.Width && horizontal.Height <= edge.Work.Height)
                {
                    Assert.InRange(horizontal.X, edge.Work.X, edge.Work.Right - horizontal.Width);
                    Assert.InRange(horizontal.Y, edge.Work.Y, edge.Work.Bottom - horizontal.Height);
                }
            }
        }
    }

    [Fact]
    public void Place_OverflowsAwayFromTheTaskbarWhenTheWindowCannotFit()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var bottom = new PixelRect(0, 0, 1920, 700);
        var tall = SnapMath.Place(DockAnchor.LeftCenter, bottom, 400, 900, monitor);
        Assert.False(Overlaps(tall, new PixelRect(0, 700, 1920, 380)));
        Assert.Equal(700, tall.Bottom);

        var topWork = new PixelRect(0, 80, 1920, 700);
        var fromTop = SnapMath.Place(DockAnchor.TopCenter, topWork, 760, 900, monitor);
        Assert.Equal(80, fromTop.Y);
        Assert.False(Overlaps(fromTop, new PixelRect(0, 0, 1920, 80)));
    }

    [Fact]
    public void WorkArea_SubtractsATaskbarTheSystemForgot()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var forgot = WorkAreaMath.ExcludeBars(monitor, monitor, new[] { new PixelRect(0, 1040, 1920, 40) });
        Assert.Equal(new PixelRect(0, 0, 1920, 1040), forgot);

        var side = WorkAreaMath.ExcludeBars(monitor, monitor, new[] { new PixelRect(0, 0, 48, 1080) });
        Assert.Equal(new PixelRect(48, 0, 1872, 1080), side);

        var already = WorkAreaMath.ExcludeBars(
            monitor,
            new PixelRect(0, 0, 1920, 1040),
            new[] { new PixelRect(0, 1040, 1920, 40) });
        Assert.Equal(new PixelRect(0, 0, 1920, 1040), already);
    }

    [Fact]
    public void Pill_GrowsInwardFromTheDockedEdge()
    {
        foreach (DockAnchor anchor in Enum.GetValues<DockAnchor>())
        {
            bool vertical = Dock.OrientationOf(anchor) == IslandOrientation.Vertical;
            double windowW = vertical ? IslandMetrics.VerticalWindowWidth : IslandMetrics.WindowWidth;
            double windowH = vertical ? IslandMetrics.VerticalWindowHeight : IslandMetrics.WindowHeight;
            var (left, top) = PillLayout.Offset(anchor, windowW, windowH, 120, 40, IslandMetrics.TopMargin);
            Assert.InRange(left, 0, windowW - 120);
            Assert.InRange(top, 0, windowH - 40);
        }

        var topCenter = PillLayout.Offset(DockAnchor.TopCenter, 760, 360, 130, 37, 8);
        Assert.Equal(8, topCenter.Top);
        Assert.Equal((760 - 130) / 2.0, topCenter.Left);

        var right = PillLayout.Offset(DockAnchor.RightCenter, 420, 560, 56, 188, 8);
        Assert.Equal(420 - 56 - 8, right.Left);
        Assert.Equal((560 - 188) / 2.0, right.Top);

        Assert.Equal(new Shift(0, 10), MotionLayout.ContentShift(DockAnchor.TopCenter, 10));
        Assert.Equal(new Shift(0, -10), MotionLayout.ContentShift(DockAnchor.BottomLeft, 10));
        Assert.Equal(new Shift(10, 0), MotionLayout.ContentShift(DockAnchor.LeftCenter, 10));
        Assert.Equal(new Shift(-10, 0), MotionLayout.ContentShift(DockAnchor.RightCenter, 10));
    }

    [Fact]
    public void VerticalMedia_IsAPortraitCard_NotARotatedHorizontalOne()
    {
        var horizontal = IslandMetrics.FrameFor(IslandMode.Media, true, IslandOrientation.Horizontal);
        var vertical = IslandMetrics.FrameFor(IslandMode.Media, true, IslandOrientation.Vertical);
        Assert.True(vertical.Height > vertical.Width);
        Assert.NotEqual(horizontal.Width, vertical.Height);
        Assert.NotEqual(horizontal.Height, vertical.Width);
        Assert.True(vertical.CornerN > 3);

        var compact = IslandMetrics.FrameFor(IslandMode.Idle, false, IslandOrientation.Vertical);
        Assert.Equal(Math.Min(compact.Width, compact.Height) / 2, compact.Radius);
        Assert.Equal(2, compact.CornerN);
        Assert.True(compact.Height > compact.Width);
        Assert.Equal(6, IslandMetrics.StealthDot.Radius);
    }

    [Fact]
    public void Hide_OnlyTheIslandMonitor_AndTheList_AndStealth()
    {
        var empty = Array.Empty<string>();
        Assert.True(HidePolicy.IsOccluded(new HideSignals(true, false, false, false, null), empty));
        Assert.False(HidePolicy.IsOccluded(new HideSignals(false, false, false, false, null), empty));

        var primary = new PixelRect(0, 0, 1920, 1080);
        var second = new PixelRect(1920, 0, 1920, 1080);
        var fullOnSecond = new PixelRect(1920, 0, 1920, 1080);
        Assert.False(HidePolicy.Covers(fullOnSecond, primary, 8));
        Assert.True(HidePolicy.Covers(fullOnSecond, second, 8));
        Assert.False(HidePolicy.CenterOn(fullOnSecond, primary));
        Assert.True(HidePolicy.CenterOn(fullOnSecond, second));
        Assert.False(HidePolicy.IsOccluded(new HideSignals(false, true, false, false, "vlc"), new[] { "vlc" }));
        Assert.True(HidePolicy.IsOccluded(new HideSignals(false, true, true, true, "game"), empty));

        Assert.True(HidePolicy.Covers(new PixelRect(-4, -4, 1928, 1088), primary, 8));
        Assert.False(HidePolicy.Covers(new PixelRect(0, 0, 1600, 900), primary, 8));

        Assert.Equal("powerpnt", HidePolicy.NormalizeProcess(@"C:\Program Files\Microsoft Office\POWERPNT.EXE"));
        Assert.True(HidePolicy.IsOccluded(new HideSignals(false, true, true, false, "POWERPNT.EXE"), new[] { "powerpnt" }));
        Assert.False(HidePolicy.IsOccluded(new HideSignals(false, true, true, false, "notepad"), new[] { "powerpnt" }));

        var list = new List<string>();
        Assert.True(HidePolicy.ToggleListed(list, @"C:\Apps\vlc.exe"));
        Assert.True(HidePolicy.IsListed(list, "VLC"));
        Assert.False(HidePolicy.ToggleListed(list, "vlc"));
        Assert.Empty(list);
    }

    [Fact]
    public void Occlusion_PopsVolumeThenReturnsToTheDot_AndIgnoresHover()
    {
        var director = new IslandDirector();
        director.SetOccluded(true, T(0));
        Assert.True(director.ShowAsDot);
        Assert.Equal(IslandMetrics.StealthDot, director.TargetFrame);

        director.PointerEntered(T(0));
        director.Tick(T(500));
        Assert.False(director.Expanded);
        director.Click(T(500));
        Assert.False(director.Expanded);

        director.PushNotice(IslandMode.Volume, T(500));
        Assert.False(director.ShowAsDot);
        Assert.Equal(IslandMode.Volume, director.Mode);
        director.Tick(T(2200));
        Assert.True(director.ShowAsDot);
        Assert.Equal(IslandMode.Idle, director.Mode);

        director.SetOccluded(false, T(2200));
        Assert.False(director.ShowAsDot);
        Assert.False(director.Expanded);
    }

    [Fact]
    public void Occlusion_TrayPeekRetracts_AndCameraDoesNotSurviveIt()
    {
        var director = new IslandDirector();
        director.SetOccluded(true, T(0));
        director.TrayClick(T(0));
        Assert.True(director.Expanded);
        Assert.False(director.ShowAsDot);
        director.Tick(T(5000));
        Assert.True(director.ShowAsDot);

        director.SetOccluded(false, T(5000));
        director.SetCamera(true);
        Assert.Equal(IslandMode.Camera, director.Mode);
        Assert.True(director.CameraOpen);
        director.SetAmbient(AmbientKind.Media);
        Assert.Equal(IslandMode.Camera, director.Mode);
        director.SetCamera(false);
        Assert.False(director.CameraOpen);
        Assert.Equal(IslandMode.Media, director.Mode);
        Assert.False(director.Expanded);
    }

    [Fact]
    public void Startup_LogonWaitIsInsideTheRequestedWindow()
    {
        Assert.InRange(StartupDelay.LogonWait.TotalSeconds, 10, 15);
        Assert.Equal(12, StartupDelay.LogonWait.TotalSeconds);
        Assert.True(StartupDelay.RequestsDelay(new[] { "--delayed-start" }));
        Assert.True(StartupDelay.RequestsDelay(new[] { "delayed-start" }));
        Assert.False(StartupDelay.RequestsDelay(new[] { "--toast", "你好" }));
        Assert.False(StartupDelay.RequestsDelay(Array.Empty<string>()));
        Assert.Equal("\"C:\\Apps\\DynamicIsland.exe\" --delayed-start", StartupDelay.LogonCommand(@"C:\Apps\DynamicIsland.exe"));

        var toast = ClientCommand.ParseArgs(new[] { "--delayed-start", "--toast", "标题", "正文" });
        Assert.Equal("toast", toast.Cmd);
        Assert.Equal("标题", toast.Title);
        Assert.Equal("正文", toast.Body);
    }

    [Fact]
    public void Effects_StayOffWhileTheCompactIslandIsSettled()
    {
        Assert.False(EffectsPolicy.UseHighFrameRate(expanded: false, transitioning: false, liveMotion: true));
        Assert.False(EffectsPolicy.AllowBlur(expanded: false, transitioning: false));
        Assert.True(EffectsPolicy.UseHighFrameRate(expanded: false, transitioning: true, liveMotion: false));
        Assert.True(EffectsPolicy.AllowBlur(expanded: true, transitioning: false));
        Assert.True(EffectsPolicy.AllowBlur(expanded: false, transitioning: true));
        Assert.True(EffectsPolicy.UseHighFrameRate(expanded: true, transitioning: false, liveMotion: true));
        Assert.False(EffectsPolicy.UseHighFrameRate(expanded: true, transitioning: false, liveMotion: false));
    }

    [Fact]
    public void Translate_ClipboardDoesNotAuthorizeASend_UntilConfirm()
    {
        Assert.False(TranslatePolicy.BackgroundSendEnabled);
        var session = new TranslateSession();
        Assert.True(session.OfferClipboard("今天天气很好"));
        Assert.False(session.SendAuthorized);
        Assert.Null(session.LastRequest);
        Assert.False(session.OfferClipboard("今天天气很好"));

        Assert.False(session.OfferClipboard("   "));
        Assert.False(session.OfferClipboard("12345"));
        Assert.False(session.OfferClipboard(new string('字', TranslatePolicy.MaxInviteChars + 1)));

        var request = session.Confirm(TranslateDirection.Auto);
        Assert.NotNull(request);
        Assert.True(session.SendAuthorized);
        Assert.Equal("zh-CN", request.Value.Source);
        Assert.Equal("en", request.Value.Target);
        Assert.False(request.Value.Clipped);

        session.SetPending("Hello island");
        Assert.False(session.SendAuthorized);
        var english = session.Confirm(TranslateDirection.Auto);
        Assert.Equal("en", english!.Value.Source);
        Assert.Equal("zh-CN", english.Value.Target);

        session.SetPending("你好");
        var forced = session.Confirm(TranslateDirection.EnglishToChinese);
        Assert.Equal("en", forced!.Value.Source);
        Assert.Equal("zh-CN", forced.Value.Target);

        var longText = new string('A', TranslatePolicy.MaxSendChars + 25);
        session.SetPending(longText);
        var clipped = session.Confirm(TranslateDirection.ChineseToEnglish);
        Assert.True(clipped!.Value.Clipped);
        Assert.Equal(TranslatePolicy.MaxSendChars, clipped.Value.Text.Length);
        Assert.Equal("zh-CN", clipped.Value.Source);
        Assert.Equal("en", clipped.Value.Target);
        Assert.Null(TranslatePolicy.Build("   ", TranslateDirection.Auto));
    }

    [Fact]
    public void Camera_DragSnapAndLeave_KeepTheSessionAndTheDevice()
    {
        var director = new IslandDirector();
        director.SetCamera(true);
        director.PointerEntered(T(0));
        director.PointerExited(T(20));
        director.Tick(T(5000));
        Assert.True(director.CameraSession);
        Assert.True(director.Expanded);
        Assert.Equal(IslandMode.Camera, director.Mode);
        Assert.False(director.HasScheduledWork);

        director.TrayClick(T(5000));
        director.Tick(T(6000));
        Assert.True(director.CameraSession);
        Assert.True(director.Expanded);

        Assert.False(CameraPolicy.ShouldRelease(CameraLifetime.Drag));
        Assert.False(CameraPolicy.ShouldRelease(CameraLifetime.Snap));
        Assert.False(CameraPolicy.ShouldRelease(CameraLifetime.Layout));
        Assert.False(CameraPolicy.ShouldRelease(CameraLifetime.Rebind));
        Assert.False(CameraPolicy.ShouldRelease(CameraLifetime.PointerLeave));
        Assert.False(CameraPolicy.ShouldRelease(CameraLifetime.Showing));
        Assert.True(CameraPolicy.ShouldRelease(CameraLifetime.Collapse));
        Assert.True(CameraPolicy.ShouldRelease(CameraLifetime.UserOff));
        Assert.True(CameraPolicy.ShouldRelease(CameraLifetime.Occlusion));
        Assert.True(CameraPolicy.ShouldRelease(CameraLifetime.Exit));

        Assert.Equal(CameraLifetime.Drag, CameraPolicy.Classify(sessionActive: true, expanded: true, occluded: false, dragging: true));
        Assert.Equal(CameraLifetime.Showing, CameraPolicy.Classify(sessionActive: true, expanded: true, occluded: false, dragging: false));
        Assert.Equal(CameraLifetime.Occlusion, CameraPolicy.Classify(sessionActive: true, expanded: true, occluded: true, dragging: true));
        Assert.Equal(CameraLifetime.Collapse, CameraPolicy.Classify(sessionActive: false, expanded: false, occluded: false, dragging: false));
        Assert.False(CameraPolicy.ShouldRelease(CameraPolicy.Classify(true, true, false, dragging: true)));
        Assert.True(CameraPolicy.ShouldRelease(CameraPolicy.Classify(true, true, occluded: true, dragging: false)));
    }

    [Fact]
    public void Camera_Cover_KeepsAspectAndCenterCropsEveryDock()
    {
        var sourceW = 1920.0;
        var sourceH = 1080.0;
        var horizontal = CameraFraming.Cover(sourceW, sourceH, 392, 248);
        var vertical = CameraFraming.Cover(sourceW, sourceH, 248, 420);
        var square = CameraFraming.Cover(100, 100, 80, 80);

        Assert.Equal(0, square.X, 3);
        Assert.Equal(0, square.Y, 3);
        Assert.Equal(80, square.Width, 3);
        Assert.Equal(80, square.Height, 3);

        AssertCover(horizontal, sourceW, sourceH, 392, 248);
        AssertCover(vertical, sourceW, sourceH, 248, 420);
        Assert.True(horizontal.Width >= 392 - 0.01);
        Assert.True(vertical.Height >= 420 - 0.01);
        Assert.Equal(0, CameraFraming.Cover(0, 1080, 100, 100).Scale);
    }

    static void AssertCover(CameraFraming.CoverFit fit, double sourceW, double sourceH, double destW, double destH)
    {
        Assert.True(fit.Scale > 0);
        Assert.Equal(sourceW / sourceH, fit.Width / fit.Height, 6);
        Assert.Equal((destW - fit.Width) / 2.0, fit.X, 6);
        Assert.Equal((destH - fit.Height) / 2.0, fit.Y, 6);
        Assert.True(fit.Width + 0.01 >= destW);
        Assert.True(fit.Height + 0.01 >= destH);
        double cropW = destW / fit.Scale;
        double cropH = destH / fit.Scale;
        double cropX = -fit.X / fit.Scale;
        double cropY = -fit.Y / fit.Scale;
        Assert.Equal(destW / destH, cropW / cropH, 5);
        Assert.InRange(cropX, -0.01, sourceW);
        Assert.InRange(cropY, -0.01, sourceH);
        Assert.Equal(sourceW / 2.0, cropX + cropW / 2.0, 4);
        Assert.Equal(sourceH / 2.0, cropY + cropH / 2.0, 4);
    }

    [Fact]
    public void Camera_IsOffByDefault_AndMustReleaseWhenCollapsed()
    {
        Assert.False(CameraPolicy.EnabledByDefault);
        Assert.False(CameraPolicy.PersistsAcrossLaunches);
        Assert.False(CameraPolicy.MayStart(userRequested: false, occluded: false));
        Assert.False(CameraPolicy.MayStart(userRequested: true, occluded: true));
        Assert.True(CameraPolicy.MayStart(userRequested: true, occluded: false));
        Assert.True(CameraPolicy.ShouldKeepDevice(userEnabled: true, expanded: true, showingCamera: true));
        Assert.False(CameraPolicy.ShouldKeepDevice(userEnabled: true, expanded: false, showingCamera: true));
        Assert.False(CameraPolicy.ShouldKeepDevice(userEnabled: false, expanded: true, showingCamera: true));
        Assert.False(CameraPolicy.ShouldKeepDevice(userEnabled: true, expanded: true, showingCamera: false));
    }

    [Fact]
    public void Settings_RoundTripTheNewFields()
    {
        var settings = new AppSettings
        {
            Stealth = true,
            TranslateDirection = "zh-en",
            HideProcesses = new List<string> { "powerpnt", "vlc" },
            Docks = new List<MonitorDock>
            {
                new() { DeviceName = @"\\.\DISPLAY2", Anchor = "LeftCenter" }
            }
        };
        var loaded = SettingsJson.Deserialize(SettingsJson.Serialize(settings));
        Assert.True(loaded.Stealth);
        Assert.Equal("zh-en", loaded.TranslateDirection);
        Assert.Equal(new[] { "powerpnt", "vlc" }, loaded.HideProcesses);
        Assert.Equal("LeftCenter", Assert.Single(loaded.Docks).Anchor);
        Assert.False(SettingsJson.Deserialize("{}").Stealth);
        Assert.NotNull(SettingsJson.Deserialize("{}").HideProcesses);
    }
}
