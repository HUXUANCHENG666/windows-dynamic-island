using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core.Motion;
using DynamicIsland.Native;

namespace DynamicIsland.Visuals;

/// <summary>
/// 只采集岛外一圈桌面，再弯到玻璃边缘上。中心保持透明，桌面仍从分层窗口透出来。
/// </summary>
sealed class GlassRefractionBuffer : IDisposable
{
    public const int Pad = LiquidRefraction.DefaultPad;

    WriteableBitmap? _bitmap;
    int[] _pad = Array.Empty<int>();
    int[] _output = Array.Empty<int>();
    int _padW;
    int _padH;

    public ImageSource? Image => _bitmap;

    public void Clear() => _bitmap = null;

    public bool TryUpdate(int pillLeft, int pillTop, int pillW, int pillH, int visualLeft, int visualTop, int visualW, int visualH, LiquidSurface? surface = null)
    {
        if (pillW < 8 || pillH < 8 || !Capture(pillLeft, pillTop, pillW, pillH, visualLeft, visualTop, visualW, visualH))
        {
            _bitmap = null;
            return false;
        }

        return Warp(pillW, pillH, surface);
    }

    bool Capture(int left, int top, int width, int height, int visualLeft, int visualTop, int visualW, int visualH)
    {
        int pad = Pad;
        int padW = width + pad * 2;
        int padH = height + pad * 2;
        if (_pad.Length != padW * padH)
            _pad = new int[padW * padH];
        else
            Array.Clear(_pad);
        _padW = padW;
        _padH = padH;

        IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
            return false;
        IntPtr dc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            dc = NativeMethods.CreateCompatibleDC(screen);
            if (dc == IntPtr.Zero)
                return false;

            var info = new NativeMethods.BITMAPINFO
            {
                Header = new NativeMethods.BITMAPINFOHEADER
                {
                    Size = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    Width = padW,
                    Height = -padH,
                    Planes = 1,
                    BitCount = 32,
                    Compression = NativeMethods.BI_RGB
                }
            };
            dib = NativeMethods.CreateDIBSection(dc, ref info, NativeMethods.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                return false;
            old = NativeMethods.SelectObject(dc, dib);

            // 只拷岛外四条边，中间保持空，避免把岛自己折进去。
            BitBltStrip(dc, screen, 0, 0, padW, pad, left - pad, top - pad);
            BitBltStrip(dc, screen, 0, pad + height, padW, pad, left - pad, top + height);
            BitBltStrip(dc, screen, 0, pad, pad, height, left - pad, top);
            BitBltStrip(dc, screen, pad + width, pad, pad, height, left + width, top);

            Marshal.Copy(bits, _pad, 0, _pad.Length);
            EraseVisual(left - pad, top - pad, visualLeft, visualTop, visualW, visualH);
            return true;
        }
        finally
        {
            if (old != IntPtr.Zero && dc != IntPtr.Zero)
                NativeMethods.SelectObject(dc, old);
            if (dib != IntPtr.Zero)
                NativeMethods.DeleteObject(dib);
            if (dc != IntPtr.Zero)
                NativeMethods.DeleteDC(dc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    static void BitBltStrip(IntPtr dest, IntPtr screen, int dx, int dy, int width, int height, int sx, int sy)
    {
        if (width <= 0 || height <= 0)
            return;
        NativeMethods.BitBlt(dest, dx, dy, width, height, screen, sx, sy, NativeMethods.SRCCOPY);
    }

    void EraseVisual(int originX, int originY, int visualLeft, int visualTop, int visualW, int visualH)
    {
        int x0 = Math.Clamp(visualLeft - originX, 0, _padW);
        int y0 = Math.Clamp(visualTop - originY, 0, _padH);
        int x1 = Math.Clamp(visualLeft - originX + Math.Max(0, visualW), 0, _padW);
        int y1 = Math.Clamp(visualTop - originY + Math.Max(0, visualH), 0, _padH);
        for (int y = y0; y < y1; y++)
        {
            int row = y * _padW;
            for (int x = x0; x < x1; x++)
                _pad[row + x] = 0;
        }
    }

    bool Warp(int width, int height, LiquidSurface? surface)
    {
        double scale = Math.Min(1, 280.0 / Math.Max(width, height));
        int outW = Math.Max(8, (int)Math.Round(width * scale));
        int outH = Math.Max(8, (int)Math.Round(height * scale));
        if (_output.Length != outW * outH)
            _output = new int[outW * outH];
        else
            Array.Clear(_output);

        bool any = false;
        int pad = Pad;
        for (int y = 0; y < outH; y++)
        {
            double py = (y + 0.5) * height / outH;
            for (int x = 0; x < outW; x++)
            {
                double px = (x + 0.5) * width / outW;
                float slopeX = 0, slopeY = 0, surfaceH = 0;
                if (surface != null)
                    surface.SampleSlope(px / width, py / height, out surfaceH, out slopeX, out slopeY);
                if (!LiquidRefraction.TryMap(px, py, width, height, pad, slopeX, slopeY, surfaceH,
                        out var u, out var v, out var alpha,
                        out var ru, out var rv, out var bu, out var bv))
                    continue;
                int green = Sample(u, v);
                if (green == 0)
                    continue;
                int red = Sample(ru, rv);
                int blue = Sample(bu, bv);
                if (red == 0)
                    red = green;
                if (blue == 0)
                    blue = green;
                int a = Math.Clamp((int)Math.Round(alpha * 255), 0, 255);
                if (a <= 2)
                    continue;
                int r = ((red >> 16) & 0xFF) * a / 255;
                int g = ((green >> 8) & 0xFF) * a / 255;
                int b = (blue & 0xFF) * a / 255;
                _output[y * outW + x] = (a << 24) | (r << 16) | (g << 8) | b;
                any = true;
            }
        }

        if (!any)
        {
            _bitmap = null;
            return false;
        }

        if (_bitmap == null || _bitmap.PixelWidth != outW || _bitmap.PixelHeight != outH)
            _bitmap = new WriteableBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32, null);
        _bitmap.WritePixels(new Int32Rect(0, 0, outW, outH), _output, outW * 4, 0);
        return true;
    }

    int Sample(double u, double v)
    {
        if (_padW < 2 || _padH < 2)
            return 0;
        u = Math.Clamp(u, 0, _padW - 1.001);
        v = Math.Clamp(v, 0, _padH - 1.001);
        int x0 = (int)Math.Floor(u);
        int y0 = (int)Math.Floor(v);
        int x1 = Math.Min(x0 + 1, _padW - 1);
        int y1 = Math.Min(y0 + 1, _padH - 1);
        double fx = u - x0;
        double fy = v - y0;
        return Mix(Mix(_pad[y0 * _padW + x0], _pad[y0 * _padW + x1], fx),
            Mix(_pad[y1 * _padW + x0], _pad[y1 * _padW + x1], fx), fy);
    }

    static int Mix(int a, int b, double t)
    {
        if (a == 0)
            return t >= 0.5 ? b : 0;
        if (b == 0)
            return t >= 0.5 ? 0 : a;
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

    public void Dispose()
    {
        _bitmap = null;
        _pad = Array.Empty<int>();
        _output = Array.Empty<int>();
    }
}
