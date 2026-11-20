using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Motion;
using DynamicIsland.Native;

namespace DynamicIsland.Visuals;

/// <summary>
/// 只采集岛外一圈桌面，再弯到玻璃边缘上。中心保持透明，桌面仍从分层窗口透出来。
/// 完整捕获放到后台线程；界面线程只用上一帧周围图按当前形状再扭曲。
/// </summary>
sealed class GlassRefractionBuffer : IDisposable
{
    public const int Pad = LiquidRefraction.DefaultPad;

    WriteableBitmap? _bitmap;
    WriteableBitmap? _shade;
    int[] _pad = Array.Empty<int>();
    int[] _backPad = Array.Empty<int>();
    int[] _output = Array.Empty<int>();
    int[] _shadePixels = Array.Empty<int>();
    int _padW;
    int _padH;
    int _backW;
    int _backH;
    int _padIslandW;
    int _padIslandH;
    int _pendingIslandW;
    int _pendingIslandH;
    volatile bool _captureBusy;
    volatile bool _disposed;
    GlassGpuRenderer? _gpu;
    DateTime _gpuRetryUtc;
    bool _gpuRejected;

    public ImageSource? Image => _bitmap;
    public ImageSource? Shade => _shade;
    public ImageSource? GpuImage { get; private set; }
    public bool CaptureInFlight => _captureBusy;
    public bool HasCachedPad => _padW >= 2 && _pad.Length == _padW * _padH;

    public void Clear()
    {
        _bitmap = null;
        _shade = null;
        GpuImage = null;
    }

    public bool CanReusePad(int width, int height) =>
        GlassWork.ShouldReuseCapture(HasCachedPad);

    public bool TryUpdate(int pillLeft, int pillTop, int pillW, int pillH, int visualLeft, int visualTop, int visualW, int visualH, double radius, LiquidSurface? surface = null)
    {
        if (pillW < 8 || pillH < 8 || !CaptureInto(ref _pad, out _padW, out _padH, pillLeft, pillTop, pillW, pillH, visualLeft, visualTop, visualW, visualH))
        {
            _bitmap = null;
            return false;
        }

        _padIslandW = pillW;
        _padIslandH = pillH;

        WriteShade(pillW, pillH, radius, surface, moving: false);
        return Warp(pillW, pillH, radius, surface, moving: false);
    }

    public bool TryPresent(int pillW, int pillH, double radius, LiquidSurface? surface, bool moving)
    {
        if (pillW < 8 || pillH < 8)
            return false;

        int srcW = _padIslandW > 0 ? _padIslandW : pillW;
        int srcH = _padIslandH > 0 ? _padIslandH : pillH;
        if (GlassWork.ChoosePath(EnsureGpu()) == GlassRenderPath.Gpu)
        {
            int[]? pad = HasCachedPad ? _pad : null;
            if (_gpu!.TryPresent(pad, _padW, _padH, pillW, pillH, srcW, srcH, radius, surface, moving))
            {
                GpuImage = _gpu.Image;
                _bitmap = null;
                _shade = null;
                return true;
            }

            _gpu.Dispose();
            _gpu = null;
            GpuImage = null;
            _gpuRejected = true;
            _gpuRetryUtc = DateTime.UtcNow;
        }
        else
        {
            GpuImage = null;
        }

        bool shade = WriteShade(pillW, pillH, radius, surface, moving);
        if (CanReusePad(pillW, pillH))
            return Warp(pillW, pillH, radius, surface, moving) || shade;

        _bitmap = null;
        return shade;
    }

    bool EnsureGpu()
    {
        if (_disposed || !GlassWork.PreferGpu)
            return false;
        if (_gpu is { IsAvailable: true })
            return true;
        if (_gpuRejected && (DateTime.UtcNow - _gpuRetryUtc).TotalSeconds < 10)
            return false;
        _gpu?.Dispose();
        _gpu = GlassGpuRenderer.TryCreate();
        if (_gpu == null)
        {
            _gpuRejected = true;
            _gpuRetryUtc = DateTime.UtcNow;
            return false;
        }

        _gpuRejected = false;
        return true;
    }

    public bool TryUpdateShade(int pillW, int pillH, double radius, LiquidSurface? surface)
    {
        if (pillW < 8 || pillH < 8)
        {
            _shade = null;
            return false;
        }

        return WriteShade(pillW, pillH, radius, surface, moving: false);
    }

    public bool TryBeginCapture(
        int pillLeft,
        int pillTop,
        int pillW,
        int pillH,
        int visualLeft,
        int visualTop,
        int visualW,
        int visualH,
        Action<bool> completed)
    {
        if (_disposed || _captureBusy || pillW < 8 || pillH < 8)
            return false;

        _captureBusy = true;
        _pendingIslandW = pillW;
        _pendingIslandH = pillH;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool ok = false;
            try
            {
                ok = !_disposed && CaptureInto(ref _backPad, out _backW, out _backH,
                    pillLeft, pillTop, pillW, pillH, visualLeft, visualTop, visualW, visualH);
            }
            catch
            {
                ok = false;
            }

            try
            {
                completed(ok && !_disposed);
            }
            catch
            {
                // 回调里的调度失败不能把捕获锁卡住。
            }
        });
        return true;
    }

    public void CompleteCapture()
    {
        var swap = _pad;
        _pad = _backPad;
        _backPad = swap;
        _padW = _backW;
        _padH = _backH;
        _padIslandW = _pendingIslandW;
        _padIslandH = _pendingIslandH;
        _captureBusy = false;
    }

    public void AbandonCapture() => _captureBusy = false;

    bool CaptureInto(
        ref int[] dest,
        out int padW,
        out int padH,
        int left,
        int top,
        int width,
        int height,
        int visualLeft,
        int visualTop,
        int visualW,
        int visualH)
    {
        int pad = Pad;
        padW = width + pad * 2;
        padH = height + pad * 2;
        if (dest.Length != padW * padH)
            dest = new int[padW * padH];
        else
            Array.Clear(dest);

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

            Marshal.Copy(bits, dest, 0, dest.Length);
            EraseVisual(dest, padW, padH, left - pad, top - pad, visualLeft, visualTop, visualW, visualH);
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

    static void EraseVisual(int[] dest, int padW, int padH, int originX, int originY, int visualLeft, int visualTop, int visualW, int visualH)
    {
        int x0 = Math.Clamp(visualLeft - originX, 0, padW);
        int y0 = Math.Clamp(visualTop - originY, 0, padH);
        int x1 = Math.Clamp(visualLeft - originX + Math.Max(0, visualW), 0, padW);
        int y1 = Math.Clamp(visualTop - originY + Math.Max(0, visualH), 0, padH);
        for (int y = y0; y < y1; y++)
        {
            int row = y * padW;
            for (int x = x0; x < x1; x++)
                dest[row + x] = 0;
        }
    }

    bool WriteShade(int width, int height, double radius, LiquidSurface? surface, bool moving)
    {
        var (outW, outH) = OutputSize(width, height, moving);
        if (_shadePixels.Length != outW * outH)
            _shadePixels = new int[outW * outH];
        else
            Array.Clear(_shadePixels);

        bool any = false;
        if (radius < 0)
            radius = Math.Min(width, height) * 0.5;
        for (int y = 0; y < outH; y++)
        {
            double py = (y + 0.5) * height / outH;
            for (int x = 0; x < outW; x++)
            {
                double px = (x + 0.5) * width / outW;
                float field = 0;
                if (surface != null)
                    field = surface.Sample(px / width, py / height);
                int color = IslandOutline.ShadeArgb(px, py, width, height, radius, field);
                if (color == 0)
                    continue;
                _shadePixels[y * outW + x] = color;
                any = true;
            }
        }

        if (!any)
        {
            _shade = null;
            return false;
        }

        if (_shade == null || _shade.PixelWidth != outW || _shade.PixelHeight != outH)
            _shade = new WriteableBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32, null);
        _shade.WritePixels(new Int32Rect(0, 0, outW, outH), _shadePixels, outW * 4, 0);
        return true;
    }

    static (int Width, int Height) OutputSize(int width, int height, bool moving)
    {
        double scale = Math.Min(1, GlassWork.OutputLongSide(moving) / (double)Math.Max(width, height));
        return (Math.Max(8, (int)Math.Round(width * scale)), Math.Max(8, (int)Math.Round(height * scale)));
    }

    bool Warp(int width, int height, double radius, LiquidSurface? surface, bool moving)
    {
        var (outW, outH) = OutputSize(width, height, moving);
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
                if (!LiquidRefraction.TryMap(px, py, width, height, pad, radius, slopeX, slopeY, surfaceH,
                        out var u, out var v, out var alpha,
                        out var ru, out var rv, out var bu, out var bv))
                    continue;
                int green = Sample(u, v, width, height);
                if (green == 0)
                    continue;
                int red = Sample(ru, rv, width, height);
                int blue = Sample(bu, bv, width, height);
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

    int Sample(double u, double v, int islandW, int islandH)
    {
        if (_padW < 2 || _padH < 2)
            return 0;
        int srcW = _padIslandW > 0 ? _padIslandW : islandW;
        int srcH = _padIslandH > 0 ? _padIslandH : islandH;
        (u, v) = GlassSample.MapPad(u, v, srcW, srcH, islandW, islandH, Pad);
        return GlassSample.Bilinear(_pad, _padW, _padH, u, v);
    }

    public void Dispose()
    {
        _disposed = true;
        _captureBusy = false;
        _gpu?.Dispose();
        _gpu = null;
        GpuImage = null;
        _bitmap = null;
        _shade = null;
        _pad = Array.Empty<int>();
        _backPad = Array.Empty<int>();
        _output = Array.Empty<int>();
        _shadePixels = Array.Empty<int>();
        _padW = _padH = _backW = _backH = 0;
        _padIslandW = _padIslandH = 0;
    }
}
