using DynamicIsland.Core.Island;

namespace DynamicIsland.Core.Tests;

public class WindowDragTests
{
    [Fact]
    public void KeepVisible_LeavesAWindowThatAlreadyFits()
    {
        var screen = Screen(0, 0, 1920, 1080, 0, 1040);
        var placed = WindowDrag.KeepVisible(200, 100, 440, 520, new[] { screen });
        Assert.Equal(new PixelRect(200, 100, 440, 520), placed);
    }

    [Fact]
    public void KeepVisible_PullsAWindowBackFromTheRightAndBottom()
    {
        var screen = Screen(0, 0, 1920, 1080, 0, 1040);
        var placed = WindowDrag.KeepVisible(1800, 900, 440, 520, new[] { screen });
        Assert.Equal(1920 - 440, placed.X);
        Assert.Equal(1040 - 520, placed.Y);
        AssertInside(placed, screen);
    }

    [Fact]
    public void KeepVisible_OnAScaledLeftMonitor_StaysOnThatMonitor()
    {
        var primary = Screen(0, 0, 1920, 1080, 0, 1040, primary: true);
        var left = new TrayScreen(-2560, 0, 2560, 1440, -2560, 0, 2560, 1400, 144, 144, false);
        var placed = WindowDrag.KeepVisible(-2700, 1200, 440, 400, new[] { primary, left });
        Assert.Equal(-2560, placed.X);
        Assert.Equal(1000, placed.Y);
        AssertInside(placed, left);
    }

    [Fact]
    public void KeepVisible_PinsAnOversizedWindowToTheWorkOrigin()
    {
        var screen = Screen(0, 40, 1280, 800, 40, 720);
        var placed = WindowDrag.KeepVisible(400, 200, 2000, 2000, new[] { screen });
        Assert.Equal(0, placed.X);
        Assert.Equal(40, placed.Y);
    }

    [Fact]
    public void KeepVisible_WithoutScreens_KeepsTheDroppedPosition()
    {
        var placed = WindowDrag.KeepVisible(-20, 30, 100, 80, Array.Empty<TrayScreen>());
        Assert.Equal(new PixelRect(-20, 30, 100, 80), placed);
    }

    static TrayScreen Screen(int x, int y, int width, int height, int workY, int workHeight, bool primary = true) =>
        new(x, y, width, height, x, workY, width, workHeight, 96, 96, primary);

    static void AssertInside(PixelRect window, TrayScreen screen)
    {
        Assert.InRange(window.X, screen.WorkX, screen.WorkX + screen.WorkWidth - window.Width);
        Assert.InRange(window.Y, screen.WorkY, screen.WorkY + screen.WorkHeight - window.Height);
    }
}
