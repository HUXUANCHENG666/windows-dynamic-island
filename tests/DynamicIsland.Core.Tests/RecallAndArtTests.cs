using DynamicIsland.Core.Camera;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Translation;

namespace DynamicIsland.Core.Tests;

public class RecallAndArtTests
{
    static DateTime T(int ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    [Fact]
    public void TranslationResult_PointerLeaveDoesNotDismiss_AndCollapseKeepsTheText()
    {
        var director = new IslandDirector();
        director.ShowTranslationResult("  你好，岛  ", T(0));
        Assert.Equal("你好，岛", director.LastTranslation);
        Assert.True(director.IsShowingTranslation);
        Assert.False(director.TranslationCopied);

        director.PointerEntered(T(10));
        director.PointerExited(T(20));
        director.Tick(T(20_000));
        Assert.True(director.IsShowingTranslation);
        Assert.True(director.Expanded);
        Assert.Equal(IslandMode.Translate, director.Mode);
        Assert.Equal("你好，岛", director.LastTranslation);

        director.TrayClick(T(20_000));
        Assert.False(director.Expanded);
        Assert.False(director.IsShowingTranslation);
        Assert.NotEqual(IslandMode.Translate, director.Mode);
        Assert.Equal("你好，岛", director.LastTranslation);

        director.PointerEntered(T(21_000));
        director.Tick(T(21_079));
        Assert.False(director.IsShowingTranslation);
        director.Tick(T(21_080));
        Assert.True(director.IsShowingTranslation);
        Assert.Equal("你好，岛", director.LastTranslation);

        director.CollapseKeepingTranslation(T(22_000));
        director.Click(T(22_100));
        Assert.True(director.IsShowingTranslation);
        Assert.Equal("你好，岛", director.LastTranslation);

        Assert.True(director.RevealTranslation(T(23_000)));
        director.DismissTranslation(T(24_000));
        Assert.False(director.HasTranslation);
        Assert.False(director.RevealTranslation(T(24_100)));
        director.PointerEntered(T(24_200));
        director.Tick(T(25_000));
        Assert.False(director.IsShowingTranslation);
        Assert.Equal(IslandMode.Idle, director.Mode);
    }

    [Fact]
    public void TranslationResult_CopyThenLeaveDropsIt_SelectionDoesNot()
    {
        var director = new IslandDirector();
        director.ShowTranslationResult("Hello island", T(0));
        director.MarkTranslationCopied();
        Assert.True(director.IsShowingTranslation);
        Assert.True(director.Expanded);

        director.SetTranslationSelection(true);
        director.PointerExited(T(10));
        director.Tick(T(5_000));
        Assert.True(director.IsShowingTranslation);
        Assert.Equal("Hello island", director.LastTranslation);

        director.SetTranslationSelection(false);
        director.PointerExited(T(5_000));
        director.Tick(T(5_479));
        Assert.Equal("Hello island", director.LastTranslation);
        director.Tick(T(5_480));
        Assert.False(director.HasTranslation);
        Assert.False(director.Expanded);

        director = new IslandDirector();
        director.ShowTranslationResult("Hello island", T(0));
        director.MarkTranslationCopied();
        director.PointerExited(T(0));
        director.PointerEntered(T(200));
        director.Tick(T(2_000));
        Assert.Equal("Hello island", director.LastTranslation);
        Assert.True(director.IsShowingTranslation);
    }

    [Fact]
    public void TranslationResult_SurvivesAnInviteAndOcclusion_UntilDismissed()
    {
        var director = new IslandDirector();
        director.SetAmbient(AmbientKind.Media);
        director.ShowTranslationResult("old result", T(0));
        director.SetAmbient(AmbientKind.Timer);
        Assert.Equal(IslandMode.Translate, director.Mode);

        director.PushNotice(IslandMode.Translate, T(10));
        Assert.False(director.IsShowingTranslation);
        Assert.Equal("old result", director.LastTranslation);
        director.PointerExited(T(20));
        director.Tick(T(500));
        Assert.False(director.Expanded);
        Assert.Equal("old result", director.LastTranslation);

        director.RevealTranslation(T(600));
        director.PointerExited(T(700));
        director.PushNotice(IslandMode.Volume, T(800));
        Assert.Equal(IslandMode.Volume, director.Mode);
        director.Tick(T(800 + 1700));
        Assert.Equal("old result", director.LastTranslation);
        Assert.False(director.IsShowingTranslation);

        director.RevealTranslation(T(3000));
        director.SetOccluded(true, T(3100));
        Assert.True(director.ShowAsDot);
        Assert.Equal("old result", director.LastTranslation);
        Assert.NotEqual(IslandMode.Translate, director.Mode);
        director.SetOccluded(false, T(3200));
        director.PointerEntered(T(3300));
        director.Tick(T(3380));
        Assert.True(director.IsShowingTranslation);
        Assert.Equal("old result", director.LastTranslation);
    }

    [Fact]
    public void TranslationGesture_SelectionAndCopyDoNotArmADrag()
    {
        Assert.False(TranslationGesture.ShouldArmDrag(TranslationHit.ResultText, selecting: false));
        Assert.False(TranslationGesture.ShouldArmDrag(TranslationHit.CopyControl, selecting: false));
        Assert.False(TranslationGesture.ShouldArmDrag(TranslationHit.DismissControl, selecting: false));
        Assert.False(TranslationGesture.ShouldArmDrag(TranslationHit.None, selecting: true));
        Assert.True(TranslationGesture.ShouldArmDrag(TranslationHit.None, selecting: false));
        Assert.True(TranslationGesture.AllowsFocusForSelection(TranslationHit.ResultText));
        Assert.False(TranslationGesture.AllowsFocusForSelection(TranslationHit.CopyControl));
        Assert.False(TranslationGesture.AllowsFocusForSelection(TranslationHit.None));
        Assert.True(TranslationGesture.ShouldHoldAgainstPointerLeave(resultOpen: true, copied: false, selecting: false));
        Assert.False(TranslationGesture.ShouldHoldAgainstPointerLeave(resultOpen: true, copied: true, selecting: false));
        Assert.True(TranslationGesture.ShouldHoldAgainstPointerLeave(resultOpen: true, copied: true, selecting: true));
        Assert.False(TranslationGesture.ShouldHoldAgainstPointerLeave(resultOpen: false, copied: false, selecting: false));
    }

    [Fact]
    public void TranslationClipboard_IgnoresTheResultWeJustCopied()
    {
        var session = new TranslateSession();
        session.SetPending("今天天气很好");
        session.RememberResult("The weather is nice");
        Assert.False(session.OfferClipboard("The weather is nice"));
        Assert.Equal("今天天气很好", session.PendingText);

        session.SuppressClipboardEcho();
        Assert.False(session.OfferClipboard("only the first line"));
        Assert.True(session.OfferClipboard("另一段要翻译的话"));
        session.SuppressClipboardEcho();
        session.CancelClipboardEcho();
        Assert.True(session.OfferClipboard("再来一段新的"));
    }

    [Fact]
    public void MediaArt_VerticalDocksCenterTheCover_HorizontalDocksDoNotShiftIt()
    {
        foreach (var width in new[] { 180.0, 216, 248, 320 })
        {
            Assert.Equal(width / 2.0, MediaArtLayout.ArtCenterX(IslandOrientation.Vertical, width), 5);
            Assert.True(MediaArtLayout.ArtLeft(IslandOrientation.Vertical, width) > 0);
            Assert.Equal(0, MediaArtLayout.ArtLeft(IslandOrientation.Horizontal, width));
            Assert.Equal(MediaArtLayout.HorizontalSize / 2.0, MediaArtLayout.ArtCenterX(IslandOrientation.Horizontal, width), 5);
        }

        foreach (var anchor in Enum.GetValues<DockAnchor>())
        {
            var spec = MediaArtLayout.For(anchor);
            var orientation = Dock.OrientationOf(anchor);
            if (anchor is DockAnchor.LeftCenter or DockAnchor.RightCenter)
            {
                Assert.Equal(IslandOrientation.Vertical, orientation);
                Assert.True(spec.TrailingColumnCollapsed);
                Assert.True(spec.CenterArt);
                Assert.Equal(3, spec.ArtSpan);
                Assert.Equal(3, spec.MetaSpan);
                Assert.Equal(0, spec.ArtColumn);
                Assert.Equal(0, spec.MetaColumn);
                Assert.Equal(MediaArtLayout.VerticalSize, spec.ArtSize);
            }
            else
            {
                Assert.Equal(IslandOrientation.Horizontal, orientation);
                Assert.False(spec.TrailingColumnCollapsed);
                Assert.False(spec.CenterArt);
                Assert.Equal(1, spec.ArtSpan);
                Assert.Equal(1, spec.MetaSpan);
                Assert.Equal(2, spec.MetaColumn);
                Assert.Equal(0, spec.ArtRow);
                Assert.Equal(0, spec.MetaRow);
                Assert.Equal(MediaArtLayout.HorizontalSize, spec.ArtSize);
                Assert.Equal(0, MediaArtLayout.ArtLeft(orientation, 216));
            }
        }

        var vertical = MediaArtLayout.For(DockAnchor.LeftCenter);
        var backToTop = MediaArtLayout.For(DockAnchor.TopCenter);
        var backToBottom = MediaArtLayout.For(DockAnchor.BottomCenter);
        Assert.True(vertical.TrailingColumnCollapsed);
        Assert.False(backToTop.TrailingColumnCollapsed);
        Assert.False(backToBottom.TrailingColumnCollapsed);
        Assert.Equal(0, MediaArtLayout.ArtLeft(IslandOrientation.Horizontal, 216));
    }

    [Fact]
    public void MediaArt_CropsWideAndTallCoversFromTheCenter()
    {
        AssertSameCrop(1920, 1080);
        AssertSameCrop(1080, 1920);
        AssertSameCrop(400, 400);

        var wide = CameraFraming.CenteredViewbox(1920, 1080, MediaArtLayout.VerticalSize, MediaArtLayout.VerticalSize);
        Assert.True(wide.X > 0.05);
        Assert.Equal(0, wide.Y, 5);
        Assert.Equal(1, wide.Height, 5);
        AssertCentered(1920, 1080, MediaArtLayout.VerticalSize, MediaArtLayout.VerticalSize);
        AssertCentered(1920, 1080, MediaArtLayout.HorizontalSize, MediaArtLayout.HorizontalSize);

        var tall = CameraFraming.CenteredViewbox(1080, 1920, 104, 104);
        Assert.Equal(0, tall.X, 5);
        Assert.True(tall.Y > 0.05);
        Assert.Equal(1, tall.Width, 5);
        AssertCentered(1080, 1920, 132, 132);

        var square = CameraFraming.CenteredViewbox(400, 400, 104, 104);
        Assert.Equal(0, square.X, 5);
        Assert.Equal(0, square.Y, 5);
        Assert.Equal(1, square.Width, 5);
        Assert.Equal(1, square.Height, 5);
        Assert.Equal(0, CameraFraming.CenteredViewbox(0, 1080, 104, 104).Width);
        Assert.Equal(0, CameraFraming.CenteredViewbox(1920, 1080, 0, 104).Width);
    }

    static void AssertSameCrop(double sourceW, double sourceH)
    {
        var horizontal = CameraFraming.CenteredViewbox(sourceW, sourceH, 104, 104);
        var vertical = CameraFraming.CenteredViewbox(sourceW, sourceH, 132, 132);
        Assert.Equal(horizontal, vertical);
    }

    static void AssertCentered(double sourceW, double sourceH, double destW, double destH)
    {
        var fit = CameraFraming.Cover(sourceW, sourceH, destW, destH);
        var box = CameraFraming.CenteredViewbox(sourceW, sourceH, destW, destH);
        Assert.True(fit.Scale > 0);
        Assert.Equal(-fit.X, fit.Width + fit.X - destW, 4);
        Assert.Equal(-fit.Y, fit.Height + fit.Y - destH, 4);
        Assert.Equal(0.5, box.X + box.Width / 2.0, 5);
        Assert.Equal(0.5, box.Y + box.Height / 2.0, 5);
        double cropW = box.Width * sourceW;
        double cropH = box.Height * sourceH;
        Assert.Equal(destW / destH, cropW / cropH, 4);
        Assert.InRange(box.X, -0.001, 1);
        Assert.InRange(box.Y, -0.001, 1);
        Assert.InRange(box.X + box.Width, 0, 1.001);
        Assert.InRange(box.Y + box.Height, 0, 1.001);
        Assert.Equal(sourceW / 2.0, (box.X + box.Width / 2.0) * sourceW, 3);
        Assert.Equal(sourceH / 2.0, (box.Y + box.Height / 2.0) * sourceH, 3);
    }
}
