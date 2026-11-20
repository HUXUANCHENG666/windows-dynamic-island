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
/// 捕获和渲染各最多一个后台任务；界面线程仅提交快照并显示已完成的冻结帧。
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

    readonly object _renderGate = new();
    GlassRefractionBuffer? _renderer;
    bool _renderBusy;
    int _presentationEpoch;
    record Frame(ImageSource? Refraction, ImageSource? Shade, ImageSource? Gpu);
    Frame? _readyFrame;

    public ImageSource? Image => _bitmap;
    public ImageSource? Shade => _shade;
    public ImageSource? GpuImage { get; private set; }
    public bool CaptureInFlight => _captureBusy;
    public bool HasCachedPad => _padW >= 2 && _pad.Length == _padW * _padH;

    public void Clear()
    {
        lock (_renderGate) { _presentationEpoch++; _readyFrame = null; }
        _bitmap = null;
        _shade = null;
        GpuImage = null;
    }

    public bool CanReusePad(int width, int height) =>
        GlassWork.ShouldReuseCapture(HasCachedPad);

    // 一帧运行、零帧排队。GPU Map/readback 和 CPU warp 都不占 UI 线程。
    // 忙时直接保留最近完成帧；下一次 Rendering 只提交当时最新形状，不积压旧形状。
    public bool TryPresent(int pillW, int pillH, double radius, LiquidSurface? surface, bool moving)
    {
        lock (_renderGate)
        {
            if (_disposed) return false;
            if (_readyFrame is { } ready)
            {
                _bitmap = ready.Refraction as WriteableBitmap;
                _shade = ready.Shade as WriteableBitmap;
                GpuImage = ready.Gpu;
                _readyFrame = null;
            }
            if (!_renderBusy && pillW >= 8 && pillH >= 8)
            {
                _renderBusy = true;
                _renderer ??= new GlassRefractionBuffer();
                var renderer = _renderer;
                var pad = (int[])_pad.Clone();
                var field = surface?.Snapshot();
                int padW = _padW, padH = _padH, srcW = _padIslandW, srcH = _padIslandH;
                int epoch = _presentationEpoch;
                _ = Task.Run(() =>
                {
                    Frame? frame = null;
                    try
                    {
                        renderer._pad = pad;
                        renderer._padW = padW; renderer._padH = padH;
                        renderer._padIslandW = srcW; renderer._padIslandH = srcH;
                        if (renderer.RenderFrame(pillW, pillH, radius, field, moving))
                        {
                            renderer._bitmap?.Freeze(); renderer._shade?.Freeze();
                            renderer.GpuImage?.Freeze();
                            frame = new Frame(renderer.Image, renderer.Shade, renderer.GpuImage);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Glass render skipped: {ex.GetType().Name}");
                        // 即便不是 DXGI 的标准 device-lost 异常，下次也先安全退回 CPU。
                        renderer._gpu?.Dispose(); renderer._gpu = null;
                        renderer._gpuRejected = true; renderer._gpuRetryUtc = DateTime.UtcNow;
                        renderer._bitmap = null; renderer._shade = null; renderer.GpuImage = null;
                    }
                    finally
                    {
                        bool dispose;
                        lock (_renderGate)
                        {
                            dispose = _disposed;
                            if (!dispose && epoch == _presentationEpoch) _readyFrame = frame;
                            _renderBusy = false;
                            if (dispose) _renderer = null;
                        }
                        if (dispose) renderer.Dispose();
                    }
                });
            }
        }
        return _bitmap != null || _shade != null || GpuImage != null;
    }

    bool RenderFrame(int pillW, int pillH, double radius, LiquidSurface? surface, bool moving)
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
            unsafe { new Span<byte>((void*)bits, checked(padW * padH * 4)).Clear(); }

            // 只拷岛外四条边，中间保持空，避免把岛自己折进去。
            if (!BitBltStrip(dc, screen, 0, 0, padW, pad, left - pad, top - pad)
                || !BitBltStrip(dc, screen, 0, pad + height, padW, pad, left - pad, top + height)
                || !BitBltStrip(dc, screen, 0, pad, pad, height, left - pad, top)
                || !BitBltStrip(dc, screen, pad + width, pad, pad, height, left + width, top))
                return false;

            Marshal.Copy(bits, dest, 0, dest.Length);
            GlassSample.NormalizeCapture(dest, width, height, pad);
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

    static bool BitBltStrip(IntPtr dest, IntPtr screen, int dx, int dy, int width, int height, int sx, int sy)
    {
        if (width <= 0 || height <= 0)
            return false;
        return NativeMethods.BitBlt(dest, dx, dy, width, height, screen, sx, sy, NativeMethods.SRCCOPY);
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

        if (_shade == null || _shade.IsFrozen || _shade.PixelWidth != outW || _shade.PixelHeight != outH)
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
                int a = Math.Clamp((int)Math.Round(alpha * 255), 0, 255);
                if (a <= 2)
                    continue;
                int r = ((green >> 16) & 0xFF) * a / 255;
                int g = ((green >> 8) & 0xFF) * a / 255;
                int b = (green & 0xFF) * a / 255;
                _output[y * outW + x] = (a << 24) | (r << 16) | (g << 8) | b;
                any = true;
            }
        }

        if (!any)
        {
            _bitmap = null;
            return false;
        }

        if (_bitmap == null || _bitmap.IsFrozen || _bitmap.PixelWidth != outW || _bitmap.PixelHeight != outH)
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
        return GlassSample.Footprint(_pad, _padW, _padH, u, v, srcW, srcH, islandW, islandH, Pad);
    }

    public void Dispose()
    {
        GlassRefractionBuffer? renderer = null;
        lock (_renderGate)
        {
            _disposed = true;
            _readyFrame = null;
            if (!_renderBusy) { renderer = _renderer; _renderer = null; }
        }
        renderer?.Dispose();
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
