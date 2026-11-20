using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Motion;
using DynamicIsland.Services;

namespace DynamicIsland.Visuals;

/// <summary>捕获/渲染各一任务、完成槽一个；引用计数 pad 和池化像素不复制整张捕获、不冻结新 WPF 图。</summary>
sealed class GlassRefractionBuffer : IDisposable, IAsyncDisposable
{
    public const int Pad = LiquidRefraction.DefaultPad;
    readonly object _gate = new();
    readonly GlassCapture _capture = new();
    readonly GlassDiagnostics _stats = new();
    readonly LiquidSurface _field = new();
    GlassGpuRenderer? _gpu;
    CapturedPad? _pad, _pending;
    GlassPixels? _ready;
    WriteableBitmap? _bitmap;
    bool _renderBusy, _captureBusy, _disposed;
    bool _gpuImage;
    Task _renderTask = Task.CompletedTask, _captureTask = Task.CompletedTask;
    int _epoch;
    DateTime _retry;
    public ImageSource? Image => _gpuImage ? null : _bitmap;
    public ImageSource? Shade => null;
    public ImageSource? GpuImage => _gpuImage ? _bitmap : null;
    public bool AllowLiveInterior;
    public bool CaptureInFlight { get { lock (_gate) return _captureBusy; } }
    public void Clear()
    {
        lock (_gate) { _epoch++; _ready?.Dispose(); _ready = null; }
        _bitmap = null;
    }

    public bool TryPresent(int w, int h, double radius, LiquidSurface? surface, bool moving,
        double dpi = 1, int left = 0, int top = 0, double scaleX = 1, double scaleY = 1,
        LiquidOpticsFrame optics = default)
    {
        GlassPixels? ready;
        lock (_gate) { ready = _ready; _ready = null; }
        bool updated = false;
        if (ready != null)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                if (_bitmap == null || _bitmap.PixelWidth != ready.Width || _bitmap.PixelHeight != ready.Height)
                    _bitmap = new WriteableBitmap(ready.Width, ready.Height, 96, 96, PixelFormats.Pbgra32, null);
                _bitmap.WritePixels(new Int32Rect(0, 0, ready.Width, ready.Height), ready.Pixels, ready.Width * 4, 0);
                _gpuImage = ready.Gpu; updated = true; _stats.Ui();
                _stats.Time(4, ready.Started);
            }
            finally { ready.Dispose(); _stats.Time(2, start); }
        }
        lock (_gate)
        {
            if (_disposed) return false;
            if (_renderBusy) _stats.Busy();
            else if (w >= 8 && h >= 8)
            {
                _renderBusy = true;
                int epoch = _epoch;
                var pad = _pad?.Retain();
                surface?.CopySnapshotTo(_field);
                _renderTask = Task.Run(() =>
                {
                    long start = Stopwatch.GetTimestamp();
                    GlassPixels? frame = null;
                    try { frame = Render(pad, w, h, radius, surface == null ? null : _field, moving, dpi, left, top, start, scaleX, scaleY, optics); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Glass skipped: {ex.GetType().Name}");
                        _gpu?.Dispose(); _gpu = null; _retry = DateTime.UtcNow.AddSeconds(10);
                    }
                    finally
                    {
                        pad?.Dispose(); _stats.Time(1, start);
                        lock (_gate)
                        {
                            if (frame != null)
                            {
                                _stats.Completed(frame.Gpu);
                                if (_disposed || epoch != _epoch) frame.Dispose();
                                else { if (_ready != null) { _ready.Dispose(); _stats.Replaced(); } _ready = frame; }
                            }
                            _renderBusy = false;
                            if (_disposed) { _gpu?.Dispose(); _gpu = null; }
                        }
                    }
                });
            }
        }
        if (_stats.Report() is { } report) Log.Info(report);
        return updated;
    }

    GlassPixels? Render(CapturedPad? pad, int w, int h, double radius, LiquidSurface? field, bool moving,
        double dpi, int left, int top, long start, double scaleX, double scaleY, LiquidOpticsFrame optics)
    {
        if (GlassWork.PreferGpu && _gpu == null && DateTime.UtcNow >= _retry)
        {
            _gpu = GlassGpuRenderer.TryCreate();
            if (_gpu == null) _retry = DateTime.UtcNow.AddSeconds(10);
        }
        if (_gpu != null)
        {
            long submit = Stopwatch.GetTimestamp();
            bool healthy = _gpu.TryPresent(pad?.Pixels, pad == null ? 0 : pad.Width + 2 * Pad,
                pad == null ? 0 : pad.Height + 2 * Pad, w, h, pad?.Width ?? w, pad?.Height ?? h,
                radius, field, moving, dpi, pad?.Radius ?? radius, left - (pad?.Left ?? left), top - (pad?.Top ?? top), start, pad?.Id ?? 0, scaleX, scaleY, optics, pad?.HasLiveInterior ?? false);
            _stats.Time(3, submit);
            if (healthy)
            {
                _stats.Dropped(_gpu.Dropped);
                if (_gpu.Skipped) _stats.GpuBusy();
                return _gpu.TakeFrame();
            }
            _gpu.Dispose(); _gpu = null; _retry = DateTime.UtcNow.AddSeconds(10);
        }
        double scale = Math.Min(1, GlassWork.OutputLongSide(moving) / (double)Math.Max(w, h));
        int ow = Math.Max(8, (int)Math.Round(w * scale)), oh = Math.Max(8, (int)Math.Round(h * scale));
        var result = new GlassPixels(ow, oh, false, start);
        GlassCpuRenderer.Render(result.Pixels, ow, oh, w, h, radius, dpi, field,
            pad?.Pixels, pad?.Width ?? w, pad?.Height ?? h, pad?.Radius ?? radius,
            left - (pad?.Left ?? left), top - (pad?.Top ?? top), scaleX, scaleY, optics, pad?.HasLiveInterior ?? false);
        return result;
    }

    public bool TryBeginCapture(int left, int top, int w, int h, int visualLeft, int visualTop,
        int visualW, int visualH, double radius, double dpi, Action<bool> completed)
    {
        lock (_gate)
        {
        if (_disposed || _captureBusy) return false;
        _captureBusy = true;
        _captureTask = Task.Run(() =>
        {
            long start = Stopwatch.GetTimestamp(); CapturedPad? frame = null;
            bool underlay; lock (_gate) underlay = AllowLiveInterior;
            try { frame = _capture.Capture(left, top, w, h, radius, dpi, visualLeft, visualTop, visualW, visualH, underlay); }
            catch (Exception ex) { Debug.WriteLine($"Glass capture skipped: {ex.GetType().Name}"); }
            bool ok;
            lock (_gate)
            {
                ok = frame != null && !_disposed;
                if (ok) { _pending?.Dispose(); _pending = frame; }
                else frame?.Dispose();
                if (_disposed) { _capture.Dispose(); _captureBusy = false; }
            }
            _stats.Time(0, start); _stats.Capture(ok);
            try { completed(ok); } catch { AbandonCapture(); }
        });
        return true;
        }
    }
    public void CompleteCapture()
    {
        lock (_gate)
        {
            if (_pending != null)
            {
                _pad?.Dispose();
                _pad = _pending;
                _pending = null;
            }
            _captureBusy = false;
        }
    }
    public void AbandonCapture()
    {
        lock (_gate) { _pending?.Dispose(); _pending = null; _captureBusy = false; }
    }

    public async ValueTask DisposeAsync()
    {
        Task draining;
        lock (_gate) { Dispose(); draining = Task.WhenAll(_captureTask, _renderTask); }
        try { await draining.ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                _captureBusy = _renderBusy = false;
                _capture.Dispose(); _gpu?.Dispose(); _gpu = null;
            }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true; _ready?.Dispose(); _ready = null;
            _pad?.Dispose(); _pad = null; _pending?.Dispose(); _pending = null;
            if (!_captureBusy) _capture.Dispose();
            if (!_renderBusy) { _gpu?.Dispose(); _gpu = null; }
        }
    }
}
