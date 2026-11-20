using DynamicIsland.Core.Motion;

namespace DynamicIsland.Core.Tests;

public class GlassUnderlayTests
{
    const int Pad = LiquidRefraction.DefaultPad, W = 320, H = 140, R = 35;

    [Theory]
    [InlineData(10, 0, 19041, true)]
    [InlineData(10, 0, 19041, true, 1)]
    [InlineData(10, 0, 26100, true)]
    [InlineData(10, 0, 18362, false)]
    [InlineData(6, 1, 7601, false)]
    [InlineData(10, 0, 0, false)]
    public void ExcludeFromCaptureIsGatedAtWindows10Build19041(int major, int minor, int build, bool expected, int revision = 0)
    {
        var os = revision == 1 ? new Version(major, minor, build, 0) : new Version(major, minor, build);
        Assert.Equal(expected, GlassUnderlay.OsSupportsExcludeFromCapture(os));
        Assert.False(GlassUnderlay.OsSupportsExcludeFromCapture(null));
    }

    [Fact]
    public void ClassifyDetectsLiveInteriorBlackHoleAndMissing()
    {
        int stride = W + 2 * Pad, rows = H + 2 * Pad;
        int[] missing = new int[stride * rows];
        Fill(missing, stride, left: unchecked((int)0xFF31C46B), right: unchecked((int)0xFFE0C040), interior: 0);
        Assert.Equal(GlassUnderlayKind.Missing, GlassUnderlay.Classify(missing, stride, rows, W, H, Pad, R));
        Assert.False(GlassUnderlay.ShouldKeepInterior(GlassUnderlayKind.Missing));

        int[] hole = new int[stride * rows];
        Fill(hole, stride, left: unchecked((int)0xFF31C46B), right: unchecked((int)0xFFE0C040), interior: unchecked((int)0xFF000000));
        Assert.Equal(GlassUnderlayKind.BlackHole, GlassUnderlay.Classify(hole, stride, rows, W, H, Pad, R));
        Assert.False(GlassUnderlay.ShouldKeepInterior(GlassUnderlayKind.BlackHole));

        int[] live = new int[stride * rows];
        Fill(live, stride, left: unchecked((int)0xFF31C46B), right: unchecked((int)0xFFE0C040), interior: unchecked((int)0xFF2244AA));
        Assert.Equal(GlassUnderlayKind.Live, GlassUnderlay.Classify(live, stride, rows, W, H, Pad, R));
        Assert.True(GlassUnderlay.ShouldKeepInterior(GlassUnderlayKind.Live));

        // 纯黑壁纸：垫边和内部都是黑，不能当成黑块丢掉。
        int[] blackDesk = new int[stride * rows];
        Fill(blackDesk, stride, left: unchecked((int)0xFF000000), right: unchecked((int)0xFF050505), interior: unchecked((int)0xFF000000));
        Assert.Equal(GlassUnderlayKind.Live, GlassUnderlay.Classify(blackDesk, stride, rows, W, H, Pad, R));
    }

    static void Fill(int[] dest, int stride, int left, int right, int interior)
    {
        int rows = dest.Length / stride;
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < stride; x++)
        {
            double px = x - Pad + 0.5, py = y - Pad + 0.5;
            bool inside = DynamicIsland.Core.Geometry.IslandOutline.SignedDistance(px, py, W, H, R) < 0.5;
            dest[y * stride + x] = inside ? interior : (px < W / 2.0 ? left : right);
        }
    }
}
