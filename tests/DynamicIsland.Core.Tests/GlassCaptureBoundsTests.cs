using DynamicIsland.Core.Motion;
using Xunit;

namespace DynamicIsland.Core.Tests;

public class GlassCaptureBoundsTests
{
    [Fact]
    public void FullyInsideCopiesWholeRect()
    {
        Assert.True(GlassCaptureBounds.TryClipToOutput(
            100, 50, 320, 140, 0, 0, 1920, 1080,
            out int srcX, out int srcY, out int destX, out int destY, out int copyW, out int copyH));
        Assert.Equal(100, srcX);
        Assert.Equal(50, srcY);
        Assert.Equal(0, destX);
        Assert.Equal(0, destY);
        Assert.Equal(320, copyW);
        Assert.Equal(140, copyH);
        Assert.True(GlassCaptureBounds.Intersects(100, 50, 320, 140, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PartialOverflowClipsInsteadOfFailing()
    {
        // 垫边右缘超出 1920，仍应留下岛身与大部分垫边，而不是整张失败去切 GDI。
        Assert.True(GlassCaptureBounds.TryClipToOutput(
            1800, 100, 608, 428, 0, 0, 1920, 1080,
            out int srcX, out int srcY, out int destX, out int destY, out int copyW, out int copyH));
        Assert.Equal(1800, srcX);
        Assert.Equal(100, srcY);
        Assert.Equal(0, destX);
        Assert.Equal(0, destY);
        Assert.Equal(120, copyW);
        Assert.Equal(428, copyH);
        Assert.Equal(120 * 428, GlassCaptureBounds.IntersectionArea(1800, 100, 608, 428, 0, 0, 1920, 1080));
    }

    [Fact]
    public void LeftOverflowShiftsDestination()
    {
        Assert.True(GlassCaptureBounds.TryClipToOutput(
            -80, 10, 608, 200, 0, 0, 1920, 1080,
            out int srcX, out int srcY, out int destX, out int destY, out int copyW, out int copyH));
        Assert.Equal(0, srcX);
        Assert.Equal(10, srcY);
        Assert.Equal(80, destX);
        Assert.Equal(0, destY);
        Assert.Equal(528, copyW);
        Assert.Equal(200, copyH);
    }

    [Fact]
    public void NoOverlapFailsWithoutClaimingACopy()
    {
        Assert.False(GlassCaptureBounds.Intersects(2000, 0, 608, 428, 0, 0, 1920, 1080));
        Assert.False(GlassCaptureBounds.TryClipToOutput(
            2000, 0, 608, 428, 0, 0, 1920, 1080,
            out int srcX, out int srcY, out int destX, out int destY, out int copyW, out int copyH));
        Assert.Equal(0, srcX + srcY + destX + destY + copyW + copyH);
        Assert.Equal(0, GlassCaptureBounds.IntersectionArea(2000, 0, 608, 428, 0, 0, 1920, 1080));
    }
}
