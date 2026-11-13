using DynamicIsland.Core.Island;

namespace DynamicIsland.Core.Tests;

public class TrayMenuTests
{
    [Fact]
    public void Place_UsesPhysicalPixelsOnScaledLaptop()
    {
        var screen = Screen(0, 0, 1920, 1080, 0, 1040, 144);
        var anchor = new TrayAnchor(1800, 1048, 24, 24);
        var placed = TrayMenuPlacement.Place(anchor, 196, 160, new[] { screen });

        Assert.Equal(294, placed.Width);
        Assert.Equal(240, placed.Height);
        Assert.Equal(1824 - 294, placed.X);
        Assert.Equal(1048 - 240 - 9, placed.Y);
        AssertInside(placed, screen);
        Assert.NotEqual(1824 - 196, placed.X);
    }

    [Fact]
    public void Place_At100Percent_SitsAboveTheTrayIcon()
    {
        var screen = Screen(0, 0, 1920, 1080, 0, 1040, 96);
        var placed = TrayMenuPlacement.Place(TrayAnchor.Point(1800, 1060), 200, 180, new[] { screen });

        Assert.Equal(200, placed.Width);
        Assert.Equal(180, placed.Height);
        Assert.Equal(1600, placed.X);
        Assert.Equal(1040 - 180, placed.Y);
        AssertInside(placed, screen);
    }

    [Fact]
    public void Place_ClampsUpWhenTheIconIsInTheBottomTaskbar()
    {
        var screen = Screen(0, 0, 1920, 1080, 0, 1000, 96);
        var placed = TrayMenuPlacement.Place(TrayAnchor.Point(1800, 1040), 100, 80, new[] { screen });

        Assert.Equal(920, placed.Y);
        Assert.Equal(1000, placed.Bottom);
        AssertInside(placed, screen);
    }

    [Fact]
    public void Place_FlipsBelowATopTaskbar()
    {
        var screen = Screen(0, 0, 1920, 1080, 48, 1032, 96);
        var placed = TrayMenuPlacement.Place(TrayAnchor.Point(100, 10), 100, 80, new[] { screen });

        Assert.Equal(48, placed.Y);
        Assert.Equal(0, placed.X);
        AssertInside(placed, screen);
    }

    [Fact]
    public void Place_OnLeftMonitor_StaysOnThatMonitor()
    {
        var primary = Screen(0, 0, 1920, 1080, 0, 1040, 96, primary: true);
        var left = new TrayScreen(-1920, 0, 1920, 1080, -1920, 0, 1920, 1040, 96, 96, false);
        var placed = TrayMenuPlacement.Place(new TrayAnchor(-120, 1000, 20, 20), 100, 80, new[] { primary, left });

        Assert.True(placed.X < 0);
        Assert.Equal(-120 + 20 - 100, placed.X);
        AssertInside(placed, left);
    }

    [Fact]
    public void Place_UsesTheSecondaryMonitorDpiNotThePrimary()
    {
        var primary = Screen(0, 0, 1920, 1080, 0, 1040, 96, primary: true);
        var secondary = Screen(1920, 0, 1920, 1080, 0, 1040, 192, primary: false);
        var placed = TrayMenuPlacement.Place(new TrayAnchor(3700, 900, 32, 32), 100, 80, new[] { primary, secondary });

        Assert.Equal(200, placed.Width);
        Assert.Equal(160, placed.Height);
        Assert.Equal(3732 - 200, placed.X);
        AssertInside(placed, secondary);
    }

    [Fact]
    public void Place_ZeroMeasurement_StillLandsInsideTheWorkArea()
    {
        var screen = Screen(0, 0, 1366, 768, 0, 728, 120);
        var placed = TrayMenuPlacement.Place(TrayAnchor.Point(1300, 740), 0, 0, new[] { screen });

        Assert.True(placed.Width >= (int)Math.Round(TrayMenuPlacement.FallbackDipWidth * 120 / 96.0) - 1);
        Assert.True(placed.Height >= (int)Math.Round(TrayMenuPlacement.FallbackDipHeight * 120 / 96.0) - 1);
        AssertInside(placed, screen);
    }

    [Fact]
    public void Place_MenuTallerThanTheWorkArea_PinsToTheTop()
    {
        var screen = Screen(0, 0, 800, 600, 0, 560, 96);
        var placed = TrayMenuPlacement.Place(TrayAnchor.Point(400, 580), 100, 2000, new[] { screen });

        Assert.Equal(0, placed.Y);
        Assert.Equal(2000, placed.Height);
    }

    [Fact]
    public void ChooseAnchor_PrefersTheIconWhenTheClickLandsOnIt()
    {
        var icon = new TrayAnchor(1800, 1040, 24, 24);
        var cursor = TrayAnchor.Point(1810, 1050);
        var chosen = TrayMenuPlacement.ChooseAnchor(true, icon, true, cursor);

        Assert.Equal(icon, chosen);
    }

    [Fact]
    public void ChooseAnchor_TrustsTheCursorWhenTheIconRectIsElsewhere()
    {
        var chevron = new TrayAnchor(1800, 1040, 24, 24);
        var flyout = TrayAnchor.Point(1400, 600);
        var chosen = TrayMenuPlacement.ChooseAnchor(true, chevron, true, flyout);

        Assert.Equal(flyout, chosen);
        var screen = Screen(0, 0, 1920, 1080, 0, 1040, 96);
        var placed = TrayMenuPlacement.Place(chosen, 120, 90, new[] { screen });
        Assert.Equal(1400 - 120, placed.X);
        Assert.InRange(placed.Y, 400, 600);
    }

    [Fact]
    public void ChooseAnchor_FallsBackWhenOneSideIsMissing()
    {
        var icon = new TrayAnchor(10, 10, 16, 16);
        Assert.Equal(icon, TrayMenuPlacement.ChooseAnchor(true, icon, false, default));
        Assert.Equal(TrayAnchor.Point(3, 4), TrayMenuPlacement.ChooseAnchor(false, default, true, TrayAnchor.Point(3, 4)));
        Assert.Equal(TrayAnchor.Point(0, 0), TrayMenuPlacement.ChooseAnchor(false, default, false, default));
    }

    [Theory]
    [InlineData(0x0205, TrayPointerAction.OpenMenu)]
    [InlineData(0x007B, TrayPointerAction.OpenMenu)]
    [InlineData(0x00010205, TrayPointerAction.OpenMenu)]
    [InlineData(0x0001007B, TrayPointerAction.OpenMenu)]
    [InlineData(0x0202, TrayPointerAction.Primary)]
    [InlineData(0x00010202, TrayPointerAction.Primary)]
    [InlineData(0x0400, TrayPointerAction.Primary)]
    [InlineData(0x0401, TrayPointerAction.Primary)]
    [InlineData(0x0204, TrayPointerAction.None)]
    [InlineData(0x0200, TrayPointerAction.None)]
    public void Decode_NotifiesOpenAndPrimaryWithoutConfusingMoves(int lParam, TrayPointerAction expected)
    {
        Assert.Equal(expected, TrayMenuEvents.Decode(lParam));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(399, false)]
    [InlineData(400, true)]
    public void ShouldOpen_CollapsesTheSecondMessageOfOneRightClick(long elapsed, bool open)
    {
        Assert.Equal(open, TrayMenuEvents.ShouldOpen(elapsed));
    }

    [Fact]
    public void Dismiss_IgnoresTheOpeningDeactivateAndKeepsRealChoices()
    {
        Assert.False(TrayMenuDismiss.ShouldDismiss(false, TrayDismiss.Deactivated, 10));
        Assert.False(TrayMenuDismiss.ShouldDismiss(false, TrayDismiss.Deactivated, 5_000));
        Assert.True(TrayMenuDismiss.ShouldDismiss(true, TrayDismiss.Deactivated, 5_000));
        Assert.False(TrayMenuDismiss.ShouldDismiss(false, TrayDismiss.Outside, 0));
        Assert.False(TrayMenuDismiss.ShouldDismiss(false, TrayDismiss.Outside, TrayMenuDismiss.OutsideGraceMs - 1));
        Assert.True(TrayMenuDismiss.ShouldDismiss(false, TrayDismiss.Outside, TrayMenuDismiss.OutsideGraceMs));
        Assert.True(TrayMenuDismiss.ShouldDismiss(false, TrayDismiss.Escape, 0));
        Assert.True(TrayMenuDismiss.ShouldDismiss(false, TrayDismiss.ItemChosen, 0));
        Assert.Equal(800, TrayMenuDismiss.DeactivateGraceMs);
    }

    static TrayScreen Screen(int x, int y, int width, int height, int workY, int workHeight, uint dpi, bool primary = true) =>
        new(x, y, width, height, x, workY, width, workHeight, dpi, dpi, primary);

    static void AssertInside(PixelRect menu, TrayScreen screen)
    {
        Assert.True(menu.Width <= screen.WorkWidth && menu.Height <= screen.WorkHeight, "menu is larger than the work area");
        Assert.InRange(menu.X, screen.WorkX, screen.WorkX + screen.WorkWidth - menu.Width);
        Assert.InRange(menu.Y, screen.WorkY, screen.WorkY + screen.WorkHeight - menu.Height);
    }
}
