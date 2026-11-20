using DynamicIsland.Core.LiveTranslate;

namespace DynamicIsland.Core.Tests;

public class CaptionScrollTests
{
    [Fact]
    public void IsAtEnd_WhenEverythingFits()
    {
        Assert.True(CaptionScrollPolicy.IsAtEnd(0, 200, 180));
        Assert.True(CaptionScrollPolicy.IsAtEnd(0, 200, 200));
        Assert.Equal(0, CaptionScrollPolicy.EndOffset(200, 180));
    }

    [Fact]
    public void IsAtEnd_WhenNearTheBottom()
    {
        const double viewport = 160;
        const double extent = 400;
        double end = CaptionScrollPolicy.EndOffset(viewport, extent);
        Assert.Equal(240, end);
        Assert.True(CaptionScrollPolicy.IsAtEnd(end, viewport, extent));
        Assert.True(CaptionScrollPolicy.IsAtEnd(end - CaptionScrollPolicy.StickSlack + 1, viewport, extent));
        Assert.False(CaptionScrollPolicy.IsAtEnd(end - CaptionScrollPolicy.StickSlack - 4, viewport, extent));
    }

    [Fact]
    public void IsAtEnd_FollowsInlineGrowthIfAlreadyStuck()
    {
        // 同一句变长、extent 往下长一点，只要刚才还在底部附近就继续跟上。
        Assert.True(CaptionScrollPolicy.IsAtEnd(200, 160, 380, CaptionScrollPolicy.StickSlack));
        Assert.False(CaptionScrollPolicy.IsAtEnd(40, 160, 380, CaptionScrollPolicy.StickSlack));
    }

    [Fact]
    public void EndOffset_ClampsToZero()
    {
        Assert.Equal(0, CaptionScrollPolicy.EndOffset(200, 10));
        Assert.Equal(90, CaptionScrollPolicy.EndOffset(110, 200));
    }
}
