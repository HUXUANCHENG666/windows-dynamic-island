using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;
using Xunit;

namespace DynamicIsland.Core.Tests;

/// <summary>
/// 拖动时中间抽动：静态桌面上，世界坐标重采样与新捕获应连续；
/// 迟到的旧位置光栅若被画上去再纠正，中心亮度会大幅跳变。
/// </summary>
public class GlassDragStabilityTests
{
    const int W = 320, H = 140, P = LiquidRefraction.DefaultPad, R = 35;

    static int[] World(int extraX)
    {
        int ww = W + extraX + 2 * P, hh = H + 2 * P;
        var world = new int[ww * hh];
        for (int y = 0; y < hh; y++)
        for (int x = 0; x < ww; x++)
        {
            int v = (int)(80 + 80 * Math.Sin((x - P) * 0.12) + 40 * Math.Cos((y - P) * 0.09));
            world[y * ww + x] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
        }
        return world;
    }

    static int[] Crop(int[] world, int extraX, int originX)
    {
        int srcW = W + extraX + 2 * P, stride = W + 2 * P;
        var pad = new int[stride * (H + 2 * P)];
        for (int y = 0; y < H + 2 * P; y++)
            Array.Copy(world, y * srcW + originX, pad, y * stride, stride);
        return pad;
    }

    static int Center(int[] px) => px[(H / 2) * W + W / 2] & 255;

    static bool HasGlass(int[] px)
    {
        int lit = 0;
        for (int i = 0; i < px.Length; i++)
            if (((uint)px[i] >> 24) > 8) lit++;
        return lit > W * 4;
    }

    [Fact]
    public void ResampledPadMatchesFreshCaptureAtNewPosition()
    {
        var world = World(160);
        var pad0 = Crop(world, 160, 0);
        var a = new int[W * H];
        var b = new int[W * H];
        int maxCenter = 0, maxInterior = 0;
        for (int s = 0; s <= 160; s += 8)
        {
            GlassCpuRenderer.Render(a, W, H, W, H, R, 1, null, pad0, W, H, R, s, 0, 1, 1, default, true);
            var fresh = Crop(world, 160, s);
            GlassCpuRenderer.Render(b, W, H, W, H, R, 1, null, fresh, W, H, R, 0, 0, 1, 1, default, true);
            maxCenter = Math.Max(maxCenter, Math.Abs(Center(a) - Center(b)));
            // 只比透镜内部（避开 9 DIP 边带和垫边即将露出的右缘）。
            for (int y = 40; y < 100; y++)
            for (int x = 80; x < 240; x++)
            {
                int i = y * W + x;
                maxInterior = Math.Max(maxInterior, Math.Abs((a[i] & 255) - (b[i] & 255)));
            }
            Assert.True(HasGlass(a));
            Assert.True(HasGlass(b));
        }
        // 静态桌面上，旧垫边按当前位移重采样与新捕获在内部应对齐，切换本身不应抽动。
        Assert.Equal(0, maxCenter);
        Assert.Equal(0, maxInterior);
    }

    [Fact]
    public void LateAsyncFramesAreRejectedSoInteriorDoesNotRewind()
    {
        var world = World(160);
        var pad0 = Crop(world, 160, 0);
        var natural = new int[W * H];
        var late = new int[W * H];
        var shownPx = new int[W * H];
        int maxNatural = 0, maxNaive = 0, maxPolicy = 0, prevNatural = -1, prevNaive = -1, prevPolicy = -1;
        bool hasShown = false;
        var shown = default(GlassFrameGeometry);
        bool alwaysVisible = true;

        for (int s = 0; s <= 160; s += 8)
        {
            var view = new GlassFrameGeometry(W, H, s, 0, 1, 1);
            GlassCpuRenderer.Render(natural, W, H, W, H, R, 1, null, pad0, W, H, R, s, 0, 1, 1, default, true);
            int now = Center(natural);
            if (prevNatural >= 0) maxNatural = Math.Max(maxNatural, Math.Abs(now - prevNatural));
            prevNatural = now;
            alwaysVisible &= HasGlass(natural);

            int lateShift = Math.Max(0, s - 32);
            GlassCpuRenderer.Render(late, W, H, W, H, R, 1, null, pad0, W, H, R, lateShift, 0, 1, 1, default, true);
            int lateCenter = Center(late);
            // 无策略：先画迟到的旧光栅再画当前位，中间会大幅跳回再跳回。
            if (prevNaive >= 0) maxNaive = Math.Max(maxNaive, Math.Abs(lateCenter - prevNaive));
            maxNaive = Math.Max(maxNaive, Math.Abs(now - lateCenter));
            prevNaive = now;

            var lateGeo = view with { Left = lateShift };
            if (GlassFrameGeometry.ShouldPresent(lateGeo, view, 1, shown, hasShown))
            {
                Array.Copy(late, shownPx, shownPx.Length);
                shown = lateGeo;
                hasShown = true;
            }
            if (GlassFrameGeometry.ShouldPresent(view, view, 1, shown, hasShown))
            {
                Array.Copy(natural, shownPx, shownPx.Length);
                shown = view;
                hasShown = true;
            }

            Assert.True(GlassFrameGeometry.ShouldKeepDisplayedBitmap(shown, view, 1));
            Assert.True(HasGlass(shownPx));
            int policy = Center(shownPx);
            if (prevPolicy >= 0) maxPolicy = Math.Max(maxPolicy, Math.Abs(policy - prevPolicy));
            prevPolicy = policy;
        }

        Assert.True(alwaysVisible);
        Assert.True(hasShown);
        // 迟到 32px 的光栅若被提交，中心跳变远大于真实 8px 位移。
        Assert.True(maxNaive > maxNatural + 20, $"naive={maxNaive} natural={maxNatural}");
        // 策略只接受不比已显示帧更旧的结果，跳变应回到连续位移本身的幅度。
        Assert.True(maxPolicy <= maxNatural + 2, $"policy={maxPolicy} natural={maxNatural} naive={maxNaive}");
    }
}
