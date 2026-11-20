namespace DynamicIsland.Core.Motion;

/// <summary>
/// 世界坐标背景缓存：只吸收捕获时位于岛外的有效像素，岛移动/展开后这些像素可被滞后采样。
/// 不会把当前岛洞里的自身像素当背景；静止后渲染层不再覆盖，动态桌面从透明中心透过。
/// </summary>
public static class LiquidBackdrop
{
    public static void Composite(
        int[] dest, int destLeft, int destTop, int destW, int destH,
        int[]? previous, int prevLeft, int prevTop, int prevW, int prevH,
        int[] capture, int capLeft, int capTop, int capW, int capH,
        int pad)
    {
        int dw = destW + 2 * pad, dh = destH + 2 * pad;
        if (dest.Length < dw * dh)
            throw new ArgumentException("destination is smaller than the padded island.", nameof(dest));
        Array.Clear(dest, 0, dw * dh);
        if (previous != null && prevW > 0 && prevH > 0)
            Blit(dest, dw, dh, destLeft, destTop, previous, prevW + 2 * pad, prevH + 2 * pad, prevLeft, prevTop, onlyValid: false);
        Blit(dest, dw, dh, destLeft, destTop, capture, capW + 2 * pad, capH + 2 * pad, capLeft, capTop, onlyValid: true);
    }

    public static (int X, int Y) MapPixel(int destLeft, int destTop, int srcLeft, int srcTop, int x, int y) =>
        (x + (destLeft - srcLeft), y + (destTop - srcTop));

    static void Blit(int[] dest, int dw, int dh, int destLeft, int destTop,
        int[] src, int sw, int sh, int srcLeft, int srcTop, bool onlyValid)
    {
        if (src.Length < sw * sh || sw < 1 || sh < 1)
            return;
        int ox = srcLeft - destLeft;
        int oy = srcTop - destTop;
        int x0 = Math.Max(0, ox);
        int y0 = Math.Max(0, oy);
        int x1 = Math.Min(dw, ox + sw);
        int y1 = Math.Min(dh, oy + sh);
        for (int y = y0; y < y1; y++)
        {
            int srcRow = (y - oy) * sw - ox;
            int destRow = y * dw;
            for (int x = x0; x < x1; x++)
            {
                int color = src[srcRow + x];
                if (onlyValid && ((uint)color >> 24) == 0)
                    continue;
                dest[destRow + x] = color;
            }
        }
    }
}
