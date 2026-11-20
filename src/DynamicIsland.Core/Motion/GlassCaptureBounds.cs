namespace DynamicIsland.Core.Motion;

/// <summary>
/// 桌面复制输出与捕获矩形的相交。部分越界时裁切，而不是整段放弃、改走另一条路径。
/// </summary>
public static class GlassCaptureBounds
{
    public static bool Intersects(
        int destLeft, int destTop, int destW, int destH,
        int outputLeft, int outputTop, int outputRight, int outputBottom) =>
        destW > 0 && destH > 0
        && destLeft < outputRight && destTop < outputBottom
        && destLeft + destW > outputLeft && destTop + destH > outputTop;

    public static bool TryClipToOutput(
        int destLeft, int destTop, int destW, int destH,
        int outputLeft, int outputTop, int outputRight, int outputBottom,
        out int srcX, out int srcY, out int destX, out int destY, out int copyW, out int copyH)
    {
        int clipL = Math.Max(destLeft, outputLeft);
        int clipT = Math.Max(destTop, outputTop);
        int clipR = Math.Min(destLeft + destW, outputRight);
        int clipB = Math.Min(destTop + destH, outputBottom);
        copyW = clipR - clipL;
        copyH = clipB - clipT;
        if (copyW < 1 || copyH < 1)
        {
            srcX = srcY = destX = destY = 0;
            copyW = copyH = 0;
            return false;
        }
        srcX = clipL - outputLeft;
        srcY = clipT - outputTop;
        destX = clipL - destLeft;
        destY = clipT - destTop;
        return true;
    }

    public static int IntersectionArea(
        int destLeft, int destTop, int destW, int destH,
        int outputLeft, int outputTop, int outputRight, int outputBottom)
    {
        if (!TryClipToOutput(destLeft, destTop, destW, destH, outputLeft, outputTop, outputRight, outputBottom,
            out _, out _, out _, out _, out int copyW, out int copyH))
            return 0;
        return copyW * copyH;
    }
}
