using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;
using Xunit;

namespace DynamicIsland.Core.Tests;

public class GlassLiveInteriorTests
{
    const int W = 320, H = 140, P = LiquidRefraction.DefaultPad, R = 35;

    [Fact]
    public void LiveInteriorIsNotFilledBlack()
    {
        int stride = W + 2 * P;
        var pad = new int[stride * (H + 2 * P)];
        for (int y = 0; y < H + 2 * P; y++)
        for (int x = 0; x < stride; x++)
        {
            int v = 40 + (x * 3 + y) % 180;
            pad[y * stride + x] = unchecked((int)0xFF000000) | (v << 16) | ((v / 2) << 8) | 90;
        }
        // 贴顶裁切只清掉屏幕外垫边，不应把活体内景涂黑。
        for (int y = 0; y < P; y++)
        for (int x = 0; x < stride; x++)
            pad[y * stride + x] = 0;

        var output = new int[W * H];
        GlassCpuRenderer.Render(output, W, H, W, H, R, 1, null, pad, W, H, R, 0, 0, 1, 1, default, true);
        int c = output[(H / 2) * W + W / 2];
        int a = (int)((uint)c >> 24), r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
        Assert.True(a > 80, $"center alpha {a}");
        Assert.False(r <= 12 && g <= 12 && b <= 12, $"live center filled black rgb=({r},{g},{b})");
        Assert.True(GlassUnderlay.OutputHasOpaqueCenter(output, W, H));
    }

    [Fact]
    public void CaptureFailureBlackHoleMustNotBeDrawnAsLiveBlack()
    {
        int stride = W + 2 * P;
        var hole = new int[stride * (H + 2 * P)];
        for (int y = 0; y < H + 2 * P; y++)
        for (int x = 0; x < stride; x++)
        {
            double d = IslandOutline.SignedDistance(x - P + 0.5, y - P + 0.5, W, H, R);
            hole[y * stride + x] = d < 0.5
                ? unchecked((int)0xFF000000)
                : unchecked((int)0xFF31C46B);
        }
        Assert.Equal(GlassUnderlayKind.BlackHole, GlassUnderlay.Classify(hole, stride, H + 2 * P, W, H, P, R));
        Assert.False(GlassUnderlay.ShouldKeepInterior(GlassUnderlayKind.BlackHole));

        // 正确回退：不把黑块当实况去画，中心应是透明（露出岛身）而不是涂一层实心黑。
        var output = new int[W * H];
        GlassCpuRenderer.Render(output, W, H, W, H, R, 1, null, hole, W, H, R, 0, 0, 1, 1, default, false);
        int c = output[(H / 2) * W + W / 2];
        Assert.Equal(0, (int)((uint)c >> 24));

        // 若误把黑块当成 liveInterior，中心会被涂成不透明黑——这就是用户看到的「中间变黑」。
        var wrong = new int[W * H];
        GlassCpuRenderer.Render(wrong, W, H, W, H, R, 1, null, hole, W, H, R, 0, 0, 1, 1, default, true);
        int w = wrong[(H / 2) * W + W / 2];
        Assert.True(((uint)w >> 24) > 80);
        Assert.True(((w >> 16) & 255) <= 12);
    }

    [Fact]
    public void PresentPolicyDoesNotReplaceLiveInteriorWithEmptyFrame()
    {
        var view = new GlassFrameGeometry(W, H, 180, 40, 1, 1);
        var live = new GlassFrameGeometry(W, H, 160, 40, 1, 1); // 稍旧但有实况
        var empty = view; // 位置更近，但没有内景
        Assert.True(GlassFrameGeometry.ShouldKeepDisplayedBitmap(live, view, 1));
        Assert.False(GlassFrameGeometry.ShouldPresent(empty, view, 1, live, true, readyHasLiveInterior: false, shownHasLiveInterior: true));
        Assert.True(GlassFrameGeometry.ShouldPresent(live, view, 1, empty, true, readyHasLiveInterior: true, shownHasLiveInterior: false));
        Assert.True(GlassFrameGeometry.ShouldPresent(view, view, 1, live, true, readyHasLiveInterior: true, shownHasLiveInterior: true));
    }
}
