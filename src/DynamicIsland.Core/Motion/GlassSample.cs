using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>
/// 周围画面的取色：局部五 tap 双线性、RGB 同位，捕获 alpha 为有效性掩码。
/// 生产捕获在入口将有效像素（包括黑色）标为 FF；Prepare/Bilinear 保留为旧输入兼容。
/// </summary>
public static class GlassSample
{
    public static int Prepare(int color)
    {
        if (color == 0)
            return 0;
        if (((color >> 24) & 0xFF) == 0)
            return unchecked((int)0xFF000000) | (color & 0x00FFFFFF);
        return color;
    }

    public static int Lerp(int a, int b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        a = Prepare(a);
        b = Prepare(b);
        int aa = (a >> 24) & 0xFF;
        int ba = (b >> 24) & 0xFF;
        int ar = (a >> 16) & 0xFF;
        int br = (b >> 16) & 0xFF;
        int ag = (a >> 8) & 0xFF;
        int bg = (b >> 8) & 0xFF;
        int ab = a & 0xFF;
        int bb = b & 0xFF;
        return (((int)Math.Round(aa + (ba - aa) * t)) << 24)
            | (((int)Math.Round(ar + (br - ar) * t)) << 16)
            | (((int)Math.Round(ag + (bg - ag) * t)) << 8)
            | (int)Math.Round(ab + (bb - ab) * t);
    }

    public static int Bilinear(int[] pixels, int width, int height, double u, double v)
    {
        if (pixels == null || width < 1 || height < 1 || pixels.Length < width * height)
            return 0;

        u = Math.Clamp(u, 0, Math.Max(0, width - 1.001));
        v = Math.Clamp(v, 0, Math.Max(0, height - 1.001));
        int x0 = (int)Math.Floor(u);
        int y0 = (int)Math.Floor(v);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);
        double fx = u - x0;
        double fy = v - y0;
        int row0 = y0 * width;
        int row1 = y1 * width;
        return Lerp(
            Lerp(pixels[row0 + x0], pixels[row0 + x1], fx),
            Lerp(pixels[row1 + x0], pixels[row1 + x1], fx),
            fy);
    }

    // 捕获的 alpha 是有效性掩码：纯黑也必须为 FF，未捕获/遮挡像素为 0。
    // 每点 5 个局部双线性 tap（中心 + 十字 0.65px），所有通道共用权重。
    public const double TapRadius = 0.65;
    public static int Footprint(int[] pixels, int width, int height, double u, double v,
        int sourceW, int sourceH, int targetW, int targetH, int pad, double sourceRadius = -1,
        double dpi = 1, double shiftX = 0, double shiftY = 0, bool screenCoordinates = false, double scaleX = 1, double scaleY = 1)
    {
        if (width < 1 || height < 1 || pixels.Length < (long)width * height) return 0;
        if (screenCoordinates)
        {
            u = pad + (u - pad) * scaleX + shiftX;
            v = pad + (v - pad) * scaleY + shiftY;
            if (u < 0 || v < 0 || u > width - 1 || v > height - 1) return 0;
            // 旧捕获洞里的背景未知：不把点强行拉到另一条边，按有效覆盖率淡出等新捕获。
        }
        else
        {
            (u, v) = MapPad(u, v, sourceW, sourceH, targetW, targetH, pad);
            (u, v) = sourceRadius >= 0
                ? SafeRoundedPoint(u, v, sourceW, sourceH, sourceRadius, pad, dpi)
                : SafePadPoint(u, v, sourceW, sourceH, pad);
        }
        double a = 0, r = 0, g = 0, b = 0;
        Tap(u, v); Tap(u - TapRadius, v); Tap(u + TapRadius, v);
        Tap(u, v - TapRadius); Tap(u, v + TapRadius);
        if (a < 1e-6) return 0;
        return ((int)Math.Clamp(Math.Round(a / 5 * 255), 0, 255) << 24) | ((int)Math.Round(r / a) << 16)
            | ((int)Math.Round(g / a) << 8) | (int)Math.Round(b / a);

        void Tap(double x, double y)
        {
            x = Math.Clamp(x, 0, width - 1);
            y = Math.Clamp(y, 0, height - 1);
            int x0 = (int)x, y0 = (int)y;
            double fx = x - x0, fy = y - y0;
            Add(x0, y0, (1 - fx) * (1 - fy));
            Add(Math.Min(x0 + 1, width - 1), y0, fx * (1 - fy));
            Add(x0, Math.Min(y0 + 1, height - 1), (1 - fx) * fy);
            Add(Math.Min(x0 + 1, width - 1), Math.Min(y0 + 1, height - 1), fx * fy);
        }
        void Add(int x, int y, double weight)
        {
            int c = pixels[y * width + x];
            weight *= ((uint)c >> 24) / 255.0;
            a += weight;
            r += ((c >> 16) & 255) * weight;
            g += ((c >> 8) & 255) * weight;
            b += (c & 255) * weight;
        }
    }

    public static (double U, double V) SafeRoundedPoint(double u, double v, double w, double h,
        double radius, int pad, double dpi = 1)
    {
        double x = u - pad, y = v - pad;
        double d = IslandOutline.SignedDistance(x, y, w, h, radius);
        double guard = 3 * dpi;
        if (d < guard)
        {
            var (nx, ny) = IslandOutline.Normal(x, y, w, h, radius);
            u += nx * (guard - d); v += ny * (guard - d);
        }
        return (Math.Clamp(u, 1, w + 2 * pad - 2), Math.Clamp(v, 1, h + 2 * pad - 2));
    }

    // 旧矩形 pad 的兼容采样工具；实时渲染不再使用此分支投影。
    public static (double U, double V) SafePadPoint(double u, double v, double w, double h, int pad)
    {
        u = Math.Clamp(u, 1, w + 2 * pad - 2);
        v = Math.Clamp(v, 1, h + 2 * pad - 2);
        // 双线性 + 小 footprint 不接触空洞；最近一条边只作用于回到洞内的点。
        double l = u - (pad - 2), r = pad + w + 1 - u;
        double t = v - (pad - 2), b = pad + h + 1 - v;
        double near = Math.Min(Math.Min(l, r), Math.Min(t, b));
        if (near > 0)
        {
            if (near == l) u = pad - 2;
            else if (near == r) u = pad + w + 1;
            else if (near == t) v = pad - 2;
            else v = pad + h + 1;
        }
        return (u, v);
    }

    /// <summary>清除 DIB 未写的岛洞，捕获条带（包括黑色）显式标记有效。</summary>
    public static void NormalizeCapture(int[] pixels, int w, int h, int pad)
    {
        int stride = w + 2 * pad;
        for (int y = 0; y < h + 2 * pad; y++)
            for (int x = 0; x < stride; x++)
                pixels[y * stride + x] = LiquidRefraction.InPad(x, y, w, h, pad)
                    ? pixels[y * stride + x] | unchecked((int)0xFF000000) : 0;
    }

    /// <summary>
    /// 把当前岛尺寸下的采样点，映射到另一帧（尺寸不同）的周围图上。
    /// 垫边宽度不变，只缩放岛洞，展开动画期间也能用上一帧周围图。
    /// </summary>
    public static (double U, double V) MapPad(
        double u,
        double v,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        int pad)
    {
        return (MapAxis(u, sourceWidth, targetWidth, pad), MapAxis(v, sourceHeight, targetHeight, pad));
    }

    static double MapAxis(double x, int source, int target, int pad)
    {
        if (x < pad)
            return x;
        if (x >= pad + target)
            return x - target + source;
        if (target <= 0)
            return pad;
        return pad + (x - pad) * source / (double)target;
    }
}
