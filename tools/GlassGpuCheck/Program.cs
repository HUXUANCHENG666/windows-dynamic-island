using System.Diagnostics;
using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;
using DynamicIsland.Visuals;

// Headless software D3D11 WARP. This checks shader/readback math, NOT hardware FPS or a WPF window.
using var gpu = GlassGpuRenderer.TryCreateWarpForVerification()
    ?? throw new InvalidOperationException("WARP initialization/runtime HLSL compilation failed.");
const int pad = LiquidRefraction.DefaultPad;
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


async Task Check(string label, int w, int h, double r, double dpi, int[] source, LiquidSurface? field,
    double shiftX, double shiftY, double scaleX, double scaleY, LiquidOpticsFrame optics, bool liveInterior = false)
{
    var expected = new int[w * h];
    GlassCpuRenderer.Render(expected, w, h, w, h, r, dpi, field, source, w, h, r, shiftX, shiftY, scaleX, scaleY, optics, liveInterior);
    long id = Stopwatch.GetTimestamp();
    GlassPixels? actual = null;
    var deadline = Stopwatch.StartNew();
    while (actual == null && deadline.Elapsed.TotalSeconds < 15)
    {
        if (!gpu.TryPresent(source, w + 2 * pad, h + 2 * pad, w, h, w, h, r, field, false, dpi, r, shiftX, shiftY, id, 100 + cases, scaleX, scaleY, optics, liveInterior, new(w,h,17,23,scaleX,scaleY)))
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
        if (actual.Geometry != new GlassFrameGeometry(w,h,17,23,scaleX,scaleY)) throw new Exception("Readback lost original geometry stamp.");
        int max = 0, bad = 0, at = 0, ev = 0, av = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int pixelMax = 0;
            for (int channel = 0; channel < 32; channel += 8)
                pixelMax = Math.Max(pixelMax, Math.Abs(((expected[i] >> channel) & 255) - ((actual.Pixels[i] >> channel) & 255)));
            if (pixelMax > max) { max = pixelMax; at = i; ev = expected[i]; av = actual.Pixels[i]; }
            if (pixelMax > 4) bad++;
        }
        int x = at % w, y = at / w;
        Console.WriteLine($"WARP {label} pixels={expected.Length} max-channel-error={max} at={x},{y} expected={ev:X8} actual={av:X8} disagree={bad}");
        if (max > 4) throw new Exception($"WARP/CPU lag difference {max} exceeds 4/255 tolerance at {x},{y} expected={ev:X8} actual={av:X8}.");
        worst = Math.Max(worst, max); cases++;
    }
}

int[] Fill(int w, int h, double r, int kind)
{
    var source = new int[(w + 2 * pad) * (h + 2 * pad)];
    for (int y = 0; y < h + 2 * pad; y++)
    for (int x = 0; x < w + 2 * pad; x++)
    {
        double px = x - pad, py = y - pad;
        int value = kind == 0
            ? (px < w / 2.0 ? 0x10 : 0xE0)
            : (int)Math.Round(127.5 + 127.5 * Math.Sin((px + py) * 0.10));
        int color = unchecked((int)0xFF000000) | (value << 16) | ((kind == 0 && px >= w / 2.0 ? 0xB0 : value) << 8) | (kind == 0 && px >= w / 2.0 ? 0x30 : value);
        // Current capture only: punch the island hole. Interior warp must not treat hole pixels as a stored backdrop.
        source[y * (w + 2 * pad) + x] = IslandOutline.SignedDistance(px + 0.5, py + 0.5, w, h, r) < 0.5 ? 0 : color;
    }
    return source;
}

int[] FillLive(int w, int h, double r, int kind)
{
    var source = Fill(w, h, r, kind);
    // Restore the island interior so WARP can check sampling of live underlay pixels (not a historical atlas).
    for (int y = 0; y < h + 2 * pad; y++)
    for (int x = 0; x < w + 2 * pad; x++)
    {
        double px = x - pad, py = y - pad;
        if (IslandOutline.SignedDistance(px + 0.5, py + 0.5, w, h, r) < 0.5)
        {
            int value = kind == 0 ? (px < w / 2.0 ? 0x10 : 0xE0) : (int)Math.Round(127.5 + 127.5 * Math.Sin((px + py) * 0.10));
            source[y * (w + 2 * pad) + x] = unchecked((int)0xFF000000) | (value << 16) | ((kind == 0 && px >= w / 2.0 ? 0xB0 : value) << 8) | (kind == 0 && px >= w / 2.0 ? 0x30 : value);
        }
    }
    return source;
}

// Isolated lag (no fluid, unit scale) then the live-pad warp cases. Sources are punched, never a historical atlas.
foreach (double dpi in new[] { 1.0, 1.5, 2.0 })
{
    int w = (int)(320 * dpi), h = (int)(140 * dpi);
    double r = 35 * dpi;
    await Check($"lag-simple dpi={dpi}", w, h, r, dpi, Fill(w, h, r, 0), null, 0, 0, 1, 1, new LiquidOpticsFrame(18 * dpi, 0, 1, 1, 1));
}
var lagOptics = new LiquidOpticsFrame(18, -6, 0.94, 1.0, 0.85);
foreach (double dpi in new[] { 1.0, 1.5 })
foreach (int kind in new[] { 0, 3 })
{
    int w = (int)(320 * dpi), h = (int)(140 * dpi);
    double r = 35 * dpi;
    var field = new LiquidSurface(); field.EnsureSize(w, h);
    field.InjectForce(0.2, 0.25, 0.18f); field.Step(1.0 / 60, 0, 0, false, false);
    await Check($"lag-parity dpi={dpi} pattern={kind}", w, h, r, dpi, Fill(w, h, r, kind == 0 ? 0 : 1), field, 0.3, -0.4, 1.08, 0.96, lagOptics);
}

foreach (double dpi in new[] { 1.0, 1.5 })
{
    int w = (int)(320 * dpi), h = (int)(140 * dpi);
    double r = 35 * dpi;
    await Check($"lag-live dpi={dpi}", w, h, r, dpi, FillLive(w, h, r, 0), null, 0, 0, 1, 1, new LiquidOpticsFrame(18 * dpi, 0, 1, 1, 1), true);
}

// Settled and expanding/shrinking live lens; every case has a fresh current texture/version.
foreach (double dpi in new[] {1.0,1.5,2.0})
foreach (int width in new[] {240,420,300})
{
    int w=(int)(width*dpi), h=(int)(140*dpi); double r=35*dpi;
    await Check($"water-settled-resize dpi={dpi} width={width}",w,h,r,dpi,FillLive(w,h,r,1),null,0,0,1,1,LiquidOpticsFrame.Identity,true);
    await Check($"water-moving-scale dpi={dpi} width={width}",w,h,r,dpi,FillLive(w,h,r,1),null,2,-3,1.08,0.96,lagOptics,true);
}

Console.WriteLine($"WARP {cases} cases passed; worst-channel-error={worst}; software D3D11, no hardware FPS, no WPF-present measurement.");
