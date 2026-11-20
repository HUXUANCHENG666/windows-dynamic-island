using System.Diagnostics;
using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;
using DynamicIsland.Visuals;

// Headless software D3D11 WARP. This checks shader/readback math, NOT hardware FPS or a WPF window.
using var gpu = GlassGpuRenderer.TryCreateWarpForVerification()
    ?? throw new InvalidOperationException("WARP initialization/runtime HLSL compilation failed.");
const int pad = 72;
int cases = 0, worst = 0;
foreach (double dpi in new[] { 1.0, 1.25, 2.0 })
foreach (int kind in new[] { 0, 1, 2, 3 })
{
    int w = (int)(320 * dpi), h = (int)(140 * dpi);
    double r = 35 * dpi;
    var source = new int[(w + 2 * pad) * (h + 2 * pad)];
    for (int y = 0; y < h + 2 * pad; y++)
    for (int x = 0; x < w + 2 * pad; x++)
    {
        double px = x - pad, py = y - pad;
        int c;
        if (kind == 0) c = px < w / 2.0 ? unchecked((int)0xFF101010) : unchecked((int)0xFFE0B030);
        else if (kind == 1) c = px < w / 2.0 ? unchecked((int)0xFFFF0000) : unchecked((int)0xFF0000FF);
        else
        {
            int v = (int)Math.Round(127.5 + 127.5 * Math.Sin((px + (kind == 2 ? py : -py)) * 0.10));
            c = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
        }
        source[y * (w + 2 * pad) + x] = IslandOutline.SignedDistance(px + 0.5, py + 0.5, w, h, r) <= dpi ? 0 : c;
    }
    var field = new LiquidSurface(); field.EnsureSize(w, h);
    field.InjectForce(0.1, 0.15, 0.2f); field.Step(1.0 / 60, 0, 0, false, false);
    var expected = new int[w * h];
    GlassCpuRenderer.Render(expected, w, h, w, h, r, dpi, field, source, w, h, r, 0.3, -0.4, 1.08, 0.96);
    long id = Stopwatch.GetTimestamp();
    GlassPixels? actual = null;
    var deadline = Stopwatch.StartNew();
    while (actual == null && deadline.Elapsed.TotalSeconds < 15)
    {
        if (!gpu.TryPresent(source, w + 2 * pad, h + 2 * pad, w, h, w, h, r, field, false, dpi, r, 0.3, -0.4, id, cases + 1, 1.08, 0.96))
            throw new InvalidOperationException("WARP render/readback device failure.");
        var candidate = gpu.TakeFrame();
        if (candidate?.Started == id) actual = candidate;
        else candidate?.Dispose();
        if (actual == null) await Task.Delay(1);
    }
    if (actual == null) throw new TimeoutException("WARP nonblocking readback never completed.");
    using (actual)
    {
        if (actual.Width != w || actual.Height != h) throw new Exception("Readback dimensions differ.");
        int max = 0;
        for (int i = 0; i < expected.Length; i++)
        for (int channel = 0; channel < 32; channel += 8)
            max = Math.Max(max, Math.Abs(((expected[i] >> channel) & 255) - ((actual.Pixels[i] >> channel) & 255)));
        Console.WriteLine($"WARP parity dpi={dpi} pattern={kind} pixels={expected.Length} max-channel-error={max}");
        if (max > 4) throw new Exception($"WARP/CPU difference {max} exceeds 4/255 tolerance.");
        worst = Math.Max(worst, max); cases++;
    }
}

// Additional software parity for interior optical lag, using a filled atlas (historical desktop pixels).
var lagOptics = new LiquidOpticsFrame(18, -6, 0.94, 1.0, 0.85);
foreach (double dpi in new[] { 1.0, 1.5 })
foreach (int kind in new[] { 0, 3 })
{
    int w = (int)(320 * dpi), h = (int)(140 * dpi);
    double r = 35 * dpi;
    var source = new int[(w + 2 * pad) * (h + 2 * pad)];
    for (int y = 0; y < h + 2 * pad; y++)
    for (int x = 0; x < w + 2 * pad; x++)
    {
        double px = x - pad, py = y - pad;
        int c = kind == 0
            ? (px < w / 2.0 ? unchecked((int)0xFF101010) : unchecked((int)0xFFE0B030))
            : unchecked((int)0xFF000000) | ((int)Math.Round(127.5 + 127.5 * Math.Sin((px + py) * 0.10)) * 0x010101);
        source[y * (w + 2 * pad) + x] = c;
    }
    var field = new LiquidSurface(); field.EnsureSize(w, h);
    field.InjectForce(0.2, 0.25, 0.18f); field.Step(1.0 / 60, 0, 0, false, false);
    var expected = new int[w * h];
    GlassCpuRenderer.Render(expected, w, h, w, h, r, dpi, field, source, w, h, r, 0.3, -0.4, 1.08, 0.96, lagOptics);
    long id = Stopwatch.GetTimestamp();
    GlassPixels? actual = null;
    var deadline = Stopwatch.StartNew();
    while (actual == null && deadline.Elapsed.TotalSeconds < 15)
    {
        if (!gpu.TryPresent(source, w + 2 * pad, h + 2 * pad, w, h, w, h, r, field, false, dpi, r, 0.3, -0.4, id, 100 + cases, 1.08, 0.96, lagOptics))
            throw new InvalidOperationException("WARP lag render/readback device failure.");
        var candidate = gpu.TakeFrame();
        if (candidate?.Started == id) actual = candidate;
        else candidate?.Dispose();
        if (actual == null) await Task.Delay(1);
    }
    if (actual == null) throw new TimeoutException("WARP lag readback never completed.");
    using (actual)
    {
        if (actual.Width != w || actual.Height != h) throw new Exception("Lag readback dimensions differ.");
        int max = 0;
        for (int i = 0; i < expected.Length; i++)
        for (int channel = 0; channel < 32; channel += 8)
            max = Math.Max(max, Math.Abs(((expected[i] >> channel) & 255) - ((actual.Pixels[i] >> channel) & 255)));
        Console.WriteLine($"WARP lag-parity dpi={dpi} pattern={kind} pixels={expected.Length} max-channel-error={max}");
        if (max > 4) throw new Exception($"WARP/CPU lag difference {max} exceeds 4/255 tolerance.");
        worst = Math.Max(worst, max); cases++;
    }
}
Console.WriteLine($"WARP {cases} cases passed; worst-channel-error={worst}; software D3D11, no hardware FPS, no WPF-present measurement.");
