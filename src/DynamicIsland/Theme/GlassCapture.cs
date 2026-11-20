using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Geometry;
using DynamicIsland.Native;

namespace DynamicIsland.Visuals;

sealed class CapturedPad(int[] pixels, int w, int h, int left, int top, double radius, double dpi, bool hasLiveInterior = false, long started = 0, bool duplication = false) : IDisposable
{
    static long _nextId;
    public readonly long Started = started;
    public readonly bool Duplication = duplication;
    public readonly long Id = Interlocked.Increment(ref _nextId);
    int _references = 1;
    public readonly int[] Pixels = pixels;
    public readonly int Width = w, Height = h, Left = left, Top = top;
    public readonly double Radius = radius, Dpi = dpi;
    public readonly bool HasLiveInterior = hasLiveInterior;
    public CapturedPad Retain() { Interlocked.Increment(ref _references); return this; }
    public void Dispose() { if (Interlocked.Decrement(ref _references) == 0) ArrayPool<int>.Shared.Return(Pixels); }
}

/// <summary>捕获任务专用、可复用的 GDI DC/DIB；只保留内存，不写屏幕数据到磁盘。</summary>
sealed class GlassCapture : IDisposable
{
    IntPtr _dc, _dib, _old, _bits, _screen;
    int _width, _height;
    readonly GlassDesktopDuplication _duplication = new();
    public CapturedPad? Capture(int left, int top, int w, int h, double radius, double dpi,
        int visualLeft, int visualTop, int visualW, int visualH, bool allowLiveInterior = false)
    {
        long started = Stopwatch.GetTimestamp();
        const int pad = GlassRefractionBuffer.Pad;
        int pw = visualW + pad * 2, ph = visualH + pad * 2;
        int[]? rented = null;
        try
        {
            if (allowLiveInterior)
            {
                rented=ArrayPool<int>.Shared.Rent(pw*ph);
                if (_duplication.TryCapture(visualLeft-pad,visualTop-pad,pw,ph,rented,pw) &&
                    DynamicIsland.Core.Motion.GlassUnderlay.ShouldKeepInterior(DynamicIsland.Core.Motion.GlassUnderlay.Classify(rented,pw,ph,visualW,visualH,pad,radius)))
                {
                    var livePad=new CapturedPad(rented,visualW,visualH,visualLeft,visualTop,radius,dpi,true,started,true);
                    rented=null; return livePad;
                }
            }
            if (_screen == IntPtr.Zero) _screen = NativeMethods.CreateDC("DISPLAY", null, null, IntPtr.Zero);
            IntPtr screen = _screen;
            if (screen == IntPtr.Zero) return null;
            if (_dc == IntPtr.Zero || _width < pw || _height < ph)
            {
                DisposeGdi();
                _width = ((pw + 63) / 64) * 64; _height = ((ph + 63) / 64) * 64;
                _dc = NativeMethods.CreateCompatibleDC(screen);
                if (_dc == IntPtr.Zero) return null;
                var info = new NativeMethods.BITMAPINFO { Header = new NativeMethods.BITMAPINFOHEADER
                {
                    Size = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(), Width = _width,
                    Height = -_height, Planes = 1, BitCount = 32, Compression = NativeMethods.BI_RGB
                }};
                _dib = NativeMethods.CreateDIBSection(_dc, ref info, NativeMethods.DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
                if (_dib == IntPtr.Zero || _bits == IntPtr.Zero) { Dispose(); return null; }
                _old = NativeMethods.SelectObject(_dc, _dib);
            }
            // Clear only the requested rectangle: clipped/off-screen BitBlt must not leave last frame's pixels.
            unsafe { for (int y=0;y<ph;y++) new Span<int>((void*)(_bits+y*_width*4),pw).Clear(); }
            // 圆角外、矩形内的真实桌面也要捕获；随后仅排除真正的当前圆角岛。
            if (!NativeMethods.BitBlt(_dc, 0, 0, pw, ph, screen, visualLeft - pad, visualTop - pad, NativeMethods.SRCCOPY))
            { NativeMethods.DeleteDC(_screen); _screen = IntPtr.Zero; return null; }
            int[] dest = rented ??= ArrayPool<int>.Shared.Rent(pw * ph);
            for (int y = 0; y < ph; y++)
                Marshal.Copy(_bits + y * _width * 4, dest, y * pw, pw);
            bool live = false;
            if (allowLiveInterior)
            {
                StampOpaque(dest, pw, ph);
                var kind = DynamicIsland.Core.Motion.GlassUnderlay.Classify(dest, pw, ph, visualW, visualH, pad, radius);
                live = DynamicIsland.Core.Motion.GlassUnderlay.ShouldKeepInterior(kind);
            }
            if (!live)
            {
                var shape = new GlassGeometry(w, h, radius);
                double sx = visualW / (double)w, sy = visualH / (double)h;
                for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                {
                    double worldX = visualLeft - pad + x + 0.5, worldY = visualTop - pad + y + 0.5;
                    dest[y * pw + x] = shape.CaptureExcluded(worldX, worldY, visualLeft, visualTop, sx, sy, dpi)
                        ? 0 : dest[y * pw + x] | unchecked((int)0xFF000000);
                }
            }
            var result = new CapturedPad(dest, visualW, visualH, visualLeft, visualTop, radius, dpi, live, started);
            rented = null;
            return result;
        }
        finally { if (rented != null) ArrayPool<int>.Shared.Return(rented); }
    }

    static void StampOpaque(int[] dest, int pw, int ph)
    {
        int count = pw * ph;
        for (int i = 0; i < count; i++)
            dest[i] |= unchecked((int)0xFF000000);
    }

    public void Dispose()
    {
        _duplication.Dispose();
        if (_screen != IntPtr.Zero) NativeMethods.DeleteDC(_screen);
        _screen = IntPtr.Zero;
        DisposeGdi();
    }

    void DisposeGdi()
    {
        if (_old != IntPtr.Zero && _dc != IntPtr.Zero) NativeMethods.SelectObject(_dc, _old);
        if (_dib != IntPtr.Zero) NativeMethods.DeleteObject(_dib);
        if (_dc != IntPtr.Zero) NativeMethods.DeleteDC(_dc);
        _dc = _dib = _old = _bits = IntPtr.Zero; _width = _height = 0;
    }
}
