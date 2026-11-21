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
    readonly GlassCaptureWorker _captureWorker;
    public GlassRefractionBuffer() { _captureWorker = new GlassCaptureWorker(_capture.Dispose); }
    readonly GlassDiagnostics _stats = new();
    readonly LiquidSurface _field = new();
    GlassGpuRenderer? _gpu;
    CapturedPad? _pad;
    GlassPixels? _ready;
    WriteableBitmap? _bitmap;
    bool _renderBusy, _captureBusy, _disposed, _renderQueued, _useField, _moving;
    bool _gpuImage, _visible;
    GlassFrameGeometry _view;
    double _viewDpi = 1, _radius;
    LiquidOpticsFrame _optics;
    GlassFrameGeometry _shown;
    bool _shownLive;
    long _shownCapture, _minCapture;
    public Action? FrameReady;
    public Action<string>? Diagnostics;
    Task _renderTask = Task.CompletedTask, _captureTask = Task.CompletedTask;
    int _epoch;
    DateTime _retry;
    public ImageSource? Image => _visible && !_gpuImage ? _bitmap : null;
    public ImageSource? Shade => null;
    public ImageSource? GpuImage => _visible && _gpuImage ? _bitmap : null;
    public bool AllowLiveInterior;
    public bool HasVisibleGlass => _visible && _bitmap != null;
    public bool CaptureInFlight { get { lock (_gate) return _captureBusy; } }
    public void Clear()
    {
        lock (_gate) { _epoch++; _minCapture = Stopwatch.GetTimestamp(); _ready?.Dispose(); _ready = null; _pad?.Dispose(); _pad = null; }
        _bitmap = null; _visible = false; _shownLive = false;
    }

    public bool TryPresent(int w, int h, double radius, LiquidSurface? surface, bool moving,
        double dpi = 1, int left = 0, int top = 0, double scaleX = 1, double scaleY = 1,
        LiquidOpticsFrame optics = default)
    {
        lock (_gate)
        {
            _view = new(w, h, left, top, scaleX, scaleY); _viewDpi = dpi;
            _radius = radius; _moving = moving; _optics = optics;
            _useField = surface != null;
            surface?.CopySnapshotTo(_field);
        }
        bool updated = false;
        // 只在输出尺寸会对旧位图明显拉伸时撤下；快拖造成的位移/帧龄绝不能把玻璃关成全透明。
        if (_visible && _bitmap != null && !GlassFrameGeometry.ShouldKeepDisplayedBitmap(_shown, _view, dpi, _moving))
        { _visible = false; updated = true; }
        updated |= PresentReady();
        lock (_gate)
        {
            if (_disposed) return false;
            if (_renderBusy) { _renderQueued = true; _stats.Busy(); }
            else if (w >= 8 && h >= 8) QueueRender_NoLock();
        }
        if (_stats.Report() is { } report) { Log.Info(report); Diagnostics?.Invoke(report); }
        return updated;
    }

    // 容量为 1 的最新几何邮箱：忙时记住当前位，工人一空就立刻按最新位置重采样，不排队积压。
    void QueueRender_NoLock()
    {
        _renderBusy = true;
        _renderQueued = false;
        int epoch = _epoch;
        var capture = _captureTask;
        var view = _view;
        double dpi = _viewDpi, radius = _radius, scaleX = view.ScaleX, scaleY = view.ScaleY;
        bool moving = _moving, useField = _useField;
        var optics = _optics;
        int w = view.Width, h = view.Height, left = view.Left, top = view.Top;
        _renderTask = Task.Run(async () =>
        {
            long start = Stopwatch.GetTimestamp();
            GlassPixels? frame = null;
            CapturedPad? pad = null;
            bool invokeReady = false;
            try
            {
                lock (_gate) { if (!_disposed) pad = _pad?.Retain(); }
                // 已有可重采样的垫边时不要等捕获：位置按当前几何立即扭曲，捕获在旁路更新。
                if (pad == null || !UsablePad(pad, left, top, w, h, scaleX, scaleY))
                {
                    pad?.Dispose(); pad = null;
                    await capture.ConfigureAwait(false);
                    lock (_gate) { if (!_disposed) pad = _pad?.Retain(); }
                    if (pad != null && !GlassFrameGeometry.CanResample(Stopwatch.GetElapsedTime(pad.Started).TotalMilliseconds))
                    { pad.Dispose(); pad = null; }
                    // 不完全覆盖也保留：越界样本为 0，总比整段没有玻璃好。
                }
                var submitted = new GlassFrameGeometry(w, h, left, top, scaleX, scaleY);
                frame = Render(pad, w, h, radius, useField ? _field : null, moving, dpi, left, top, start, scaleX, scaleY, optics);
                // 有界、非阻塞回读。优先留下更接近本次提交几何的帧，避免把上一拍的旧光栅当成当前结果。
                for (int i = 0; _gpu != null && i < 3; i++)
                {
                    long poll = Stopwatch.GetTimestamp();
                    var newer = _gpu.PollFrame(); _stats.Time(5, poll);
                    frame = PreferCloser(frame, newer, submitted);
                    if (frame != null && GlassFrameGeometry.PositionError(frame.Geometry, submitted) <= 0.5)
                        break;
                    if (i == 2 || !_gpu.WaitForReadbackProbe()) break;
                }
            }
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
                    bool published = false;
                    if (frame != null)
                    {
                        _stats.Completed(frame.Gpu);
                        if (_disposed || epoch != _epoch) frame.Dispose();
                        else if (_ready != null && !GlassFrameGeometry.ShouldPresent(frame.Geometry, _view, _viewDpi, _ready.Geometry, true, frame.HasLiveInterior, _ready.HasLiveInterior, _moving))
                        { frame.Dispose(); _stats.Dropped(1); }
                        else
                        {
                            frame.ReadyAt = Stopwatch.GetTimestamp();
                            if (_ready != null) { _ready.Dispose(); _stats.Replaced(); }
                            _ready = frame;
                            published = true;
                        }
                    }
                    if (published) invokeReady = true;
                    if (!_disposed && epoch == _epoch && _renderQueued && _view.Width >= 8 && _view.Height >= 8)
                        QueueRender_NoLock();
                    else
                    {
                        _renderBusy = false;
                        if (_disposed) { _gpu?.Dispose(); _gpu = null; }
                    }
                }
                if (invokeReady) FrameReady?.Invoke();
            }
        });
    }

    // UI thread; completion can commit before the next animation tick without launching more work.
    public bool PresentReady()
    {
        GlassPixels? ready;
        lock (_gate) { ready = _ready; _ready = null; }
        bool updated = false;
        if (ready != null)
        {
            if (ready.Started < _minCapture
                || !GlassFrameGeometry.ShouldPresent(ready.Geometry, _view, _viewDpi, _shown, _visible && _bitmap != null, ready.HasLiveInterior, _shownLive, _moving))
            { ready.Dispose(); _stats.Dropped(1); return false; }
            long start = Stopwatch.GetTimestamp();
            try
            {
                if (_bitmap == null || _bitmap.PixelWidth != ready.Width || _bitmap.PixelHeight != ready.Height)
                    _bitmap = new WriteableBitmap(ready.Width, ready.Height, 96, 96, PixelFormats.Pbgra32, null);
                _bitmap.WritePixels(new Int32Rect(0, 0, ready.Width, ready.Height), ready.Pixels, ready.Width * 4, 0);
                _shown = ready.Geometry; _shownLive = ready.HasLiveInterior; _shownCapture = ready.Started;
                _gpuImage = ready.Gpu; _visible = true; updated = true; _stats.Ui();
                _stats.Time(4, ready.Started);
                _stats.Time(6, ready.ReadyAt);
            }
            finally { ready.Dispose(); _stats.Time(2, start); }
        }
        return updated;
    }



    static GlassPixels? PreferCloser(GlassPixels? current, GlassPixels? candidate, GlassFrameGeometry target)
    {
        if (candidate == null) return current;
        if (current == null) return candidate;
        if (GlassFrameGeometry.PositionError(candidate.Geometry, target)
            < GlassFrameGeometry.PositionError(current.Geometry, target) - 0.5)
        { current.Dispose(); return candidate; }
        candidate.Dispose();
        return current;
    }

    static bool UsablePad(CapturedPad pad, int left, int top, int w, int h, double scaleX, double scaleY) =>
        GlassFrameGeometry.CanResample(Stopwatch.GetElapsedTime(pad.Started).TotalMilliseconds);

    GlassPixels? Render(CapturedPad? pad, int w, int h, double radius, LiquidSurface? field, bool moving,
        double dpi, int left, int top, long start, double scaleX, double scaleY, LiquidOpticsFrame optics)
    {
        long captured = pad?.Started ?? start;
        var geometry = new GlassFrameGeometry(w, h, left, top, scaleX, scaleY);
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
                radius, field, moving, dpi, pad?.Radius ?? radius, left - (pad?.Left ?? left), top - (pad?.Top ?? top), captured, pad?.Id ?? 0, scaleX, scaleY, optics, pad?.HasLiveInterior ?? false, geometry);
            _stats.Time(3, submit);
            if (healthy)
            {
                _stats.Dropped(_gpu.Dropped);
                if (_gpu.Skipped) _stats.GpuBusy();
                return _gpu.TakeFrame();
            }
            _gpu.Dispose(); _gpu = null; _retry = DateTime.UtcNow.AddSeconds(10);
        }
        double scale = Math.Min(1, Math.Min(GlassWork.OutputLongSide(moving), pad?.HasLiveInterior == true ? GlassWork.CpuLiveOutputLongSide : GlassWork.IdleOutputLongSide) / (double)Math.Max(w, h));
        int ow = Math.Max(8, (int)Math.Round(w * scale)), oh = Math.Max(8, (int)Math.Round(h * scale));
        var result = new GlassPixels(ow, oh, false, captured) { Geometry = geometry };
        bool live = pad?.HasLiveInterior ?? false;
        GlassCpuRenderer.Render(result.Pixels, ow, oh, w, h, radius, dpi, field,
            pad?.Pixels, pad?.Width ?? w, pad?.Height ?? h, pad?.Radius ?? radius,
            left - (pad?.Left ?? left), top - (pad?.Top ?? top), scaleX, scaleY, optics, live);
        result.HasLiveInterior = live && GlassUnderlay.OutputHasOpaqueCenter(result.Pixels, ow, oh);
        return result;
    }

    public bool TryBeginCapture(int left, int top, int w, int h, int visualLeft, int visualTop,
        int visualW, int visualH, double radius, double dpi)
    {
        lock (_gate)
        {
        if (_disposed || _captureBusy) return false;
        _captureBusy = true;
        int epoch = _epoch;
        _captureTask = _captureWorker.Run(() =>
        {
            long start = Stopwatch.GetTimestamp(); CapturedPad? frame = null;
            bool underlay; lock (_gate) underlay = AllowLiveInterior;
            try { frame = _capture.Capture(left, top, w, h, radius, dpi, visualLeft, visualTop, visualW, visualH, underlay); }
            catch (Exception ex) { Debug.WriteLine($"Glass capture skipped: {ex.GetType().Name}"); }
            bool ok;
            lock (_gate)
            {
                ok = frame != null && !_disposed && epoch == _epoch;
                if (ok) { _pad?.Dispose(); _pad = frame; }
                else frame?.Dispose();
                _captureBusy = false;
            }
            _stats.Time(0, start); _stats.Capture(ok, frame?.Duplication ?? false);
        });
        return true;
        }
    }
    public async ValueTask DisposeAsync()
    {
        while (true)
        {
            Task draining;
            lock (_gate)
            {
                Dispose();
                draining = Task.WhenAll(_captureTask, _renderTask, _captureWorker.Completion);
                if (!_renderBusy && !_captureBusy)
                {
                    _gpu?.Dispose(); _gpu = null;
                    break;
                }
            }
            try { await draining.ConfigureAwait(false); }
            catch { /* 工人收尾异常不能挡住释放。 */ }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true; _ready?.Dispose(); _ready = null;
            _pad?.Dispose(); _pad = null;
            _captureWorker.Dispose();
            if (!_renderBusy) { _gpu?.Dispose(); _gpu = null; }
        }
    }
}
