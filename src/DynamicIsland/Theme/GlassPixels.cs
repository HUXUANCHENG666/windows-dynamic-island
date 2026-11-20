using DynamicIsland.Core.Motion;
using System.Buffers;
using System.Diagnostics;

namespace DynamicIsland.Visuals;

sealed class GlassPixels(int width, int height, bool gpu, long started) : IDisposable
{
    public readonly int Width = width, Height = height;
    public readonly bool Gpu = gpu;
    public readonly long Started = started;
    public GlassFrameGeometry Geometry;
    public long ReadyAt = 0;
    public readonly int[] Pixels = ArrayPool<int>.Shared.Rent(checked(width * height));
    public void Dispose() => ArrayPool<int>.Shared.Return(Pixels);
}

sealed class GlassDiagnostics
{
    readonly object _gate = new();
    readonly double[][] _times = Enumerable.Range(0, 7).Select(_ => new double[256]).ToArray();
    readonly int[] _count = new int[7];
    long _start = Stopwatch.GetTimestamp();
    int _completed, _gpu, _cpu, _ui, _capture, _busy, _gpuBusy, _replaced, _failed, _discarded;
    public void Time(int stage, long start)
    {
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        lock (_gate) { _times[stage][_count[stage]++ % 256] = ms; }
    }
    public void Capture(bool ok) { lock (_gate) { if (ok) _capture++; else _failed++; } }
    public void Completed(bool gpu) { lock (_gate) { _completed++; if (gpu) _gpu++; else _cpu++; } }
    public void Ui() { lock (_gate) _ui++; }
    public void Busy() { lock (_gate) _busy++; }
    public void GpuBusy() { lock (_gate) _gpuBusy++; }
    public void Dropped(int count) { lock (_gate) _discarded += count; }
    public void Replaced() { lock (_gate) _replaced++; }
    public string? Report()
    {
        lock (_gate)
        {
            double seconds = Stopwatch.GetElapsedTime(_start).TotalSeconds;
            if (seconds < 10) return null;
            double P95(int stage)
            {
                int count = Math.Min(256, _count[stage]);
                if (count == 0) return 0;
                var copy = _times[stage].AsSpan(0, count).ToArray();
                Array.Sort(copy); return copy[(int)Math.Ceiling(count * 0.95) - 1];
            }
            string s = $"glass completed/s={_completed / seconds:F1} ui-submit/s={_ui / seconds:F1} capture/s={_capture / seconds:F1} "
                + $"path gpu={_gpu} cpu={_cpu} p95-ms capture={P95(0):F2} render-worker={P95(1):F2} ui-write={P95(2):F2} gpu-submit-poll={P95(3):F2} capture-to-ui-age={P95(4):F2} gpu-poll-readback={P95(5):F2} ready-to-ui-age={P95(6):F2} "
                + $"skips render-busy={_busy} gpu-busy={_gpuBusy} replaced={_replaced} discarded={_discarded} capture-fail={_failed} presented-fps=unknown";
            _completed = _gpu = _cpu = _ui = _capture = _busy = _gpuBusy = _replaced = _failed = _discarded = 0;
            Array.Clear(_count); _start = Stopwatch.GetTimestamp(); return s;
        }
    }
}
