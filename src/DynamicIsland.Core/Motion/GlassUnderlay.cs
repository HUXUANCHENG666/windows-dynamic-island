using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>捕获里岛内部像素的判定：实况正下方、黑块，或被挖空。</summary>
public enum GlassUnderlayKind
{
    /// <summary>内部 alpha 为空，没有可采样的正下方像素。</summary>
    Missing,
    /// <summary>垫边有实况，内部却几乎是纯黑——常见于 WDA_MONITOR 或排除失败。</summary>
    BlackHole,
    /// <summary>内部有有效像素且不是黑块，可当作这一帧的正下方实况来采样。</summary>
    Live
}

/// <summary>
/// 公开 API 的版本门槛，以及对已捕获像素是否露出正下方内容的合成判定。
/// 不调用 Win32；真实窗口捕获在 Theme 层。
/// </summary>
public static class GlassUnderlay
{
    /// <summary>WDA_EXCLUDEFROMCAPTURE 起始于 Windows 10 2004（build 19041）。更早版本会静默变成黑块。</summary>
    public const int ExcludeFromCaptureMinBuild = 19041;
    public const int NearBlack = 12;

    public static bool OsSupportsExcludeFromCapture(Version? os)
    {
        if (os == null || os.Major < 10)
            return false;
        return os.Build >= ExcludeFromCaptureMinBuild;
    }

    public static bool ShouldKeepInterior(GlassUnderlayKind kind) => kind == GlassUnderlayKind.Live;

    public static GlassUnderlayKind Classify(
        int[] pixels, int stride, int rows, int islandW, int islandH, int pad, double radius)
    {
        if (pixels == null || stride < 1 || rows < 1 || islandW < 2 || islandH < 2 || pad < 0)
            return GlassUnderlayKind.Missing;
        if (pixels.Length < stride * rows)
            return GlassUnderlayKind.Missing;
        if (radius < 0)
            radius = Math.Min(islandW, islandH) * 0.5;
        radius = Squircle.SafeRadius(islandW, islandH, radius);

        int interiorValid = 0, interiorBlack = 0, padValid = 0, padColorful = 0;
        const int step = 3;
        for (int y = 0; y < rows; y += step)
        for (int x = 0; x < stride; x += step)
        {
            double px = x - pad + 0.5, py = y - pad + 0.5;
            double d = IslandOutline.SignedDistance(px, py, islandW, islandH, radius);
            int color = pixels[y * stride + x];
            int a = (int)((uint)color >> 24);
            if (a < 0xF0)
                continue;
            bool black = IsNearBlack(color);
            if (d < -2)
            {
                interiorValid++;
                if (black) interiorBlack++;
            }
            else if (d >= 0.5)
            {
                padValid++;
                if (!black) padColorful++;
            }
        }

        if (interiorValid == 0)
            return GlassUnderlayKind.Missing;
        // 只有垫边明显有颜色、内部却几乎全黑时，才判黑块。两边都是黑（纯黑壁纸）仍视为实况。
        if (padColorful >= 8 && interiorBlack * 20 >= interiorValid * 19)
            return GlassUnderlayKind.BlackHole;
        return GlassUnderlayKind.Live;
    }

    static bool IsNearBlack(int color)
    {
        int r = (color >> 16) & 255, g = (color >> 8) & 255, b = color & 255;
        return r <= NearBlack && g <= NearBlack && b <= NearBlack;
    }
}
