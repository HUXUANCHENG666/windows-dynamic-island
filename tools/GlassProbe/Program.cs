using System.Diagnostics;
using System.Text;
using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;

const int w = 534, h = 230, r = 35, pad = LiquidRefraction.DefaultPad;
string directory = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "glass-probe");
Directory.CreateDirectory(directory);
var field = new LiquidSurface(); field.EnsureSize(w, h);
field.InjectForce(0.1, 0.1, 0.3f); field.Step(1.0 / 60, 0, 0, false, false);
int[] background = new int[(w + 2 * pad) * (h + 2 * pad)];
int[] source = new int[(w + 2 * pad) * (h + 2 * pad)], pixels = new int[w * h];
for (int kind = 0; kind < 4; kind++)
{
    for (int y = 0; y < h + 2 * pad; y++)
    for (int x = 0; x < w + 2 * pad; x++)
    {
        double px = x - pad, py = y - pad;
        int value = kind switch
        {
            0 => (int)(127.5 + 127.5 * Math.Sin(px * .18) * Math.Cos(py * .18)),
            1 => (int)(127.5 + 127.5 * Math.Sin((px + py) * .2)),
            2 => ((int)px % 18 < 4 || (int)py % 14 < 3) ? 235 : 30,
            _ => px < w / 2 ? 24 : 200
        };
        int c = unchecked((int)0xFF000000) | (value << 16) | (value << 8) | value;
        background[y * (w + 2 * pad) + x] = c;
        source[y * (w + 2 * pad) + x] = IslandOutline.SignedDistance(px + .5, py + .5, w, h, r) <= 1 ? 0 : c;
    }
    GlassCpuRenderer.Render(pixels, w, h, w, h, r, 1, field, source, w, h, r);
    WritePpm(Path.Combine(directory, $"pattern-{kind}.ppm"), pixels, background);
}

// 合成对比：条纹实况垫边（挖洞），静止直透 vs 用惯性位移场扭曲「此刻」捕获。
// 条纹让垫边被弯进内沿时可见；中心仍是真透明叠在同一实况背景上，不是历史像素或冻结截图。
{
    for (int y = 0; y < h + 2 * pad; y++)
    for (int x = 0; x < w + 2 * pad; x++)
    {
        double px = x - pad, py = y - pad;
        int value = ((int)px % 22 < 8) ? 230 : 36;
        int c = unchecked((int)0xFF000000) | (value << 16) | (value << 8) | value;
        background[y * (w + 2 * pad) + x] = c;
        source[y * (w + 2 * pad) + x] = IslandOutline.SignedDistance(px + .5, py + .5, w, h, r) <= 1 ? 0 : c;
    }
    var before = new int[w * h];
    var after = new int[w * h];
    GlassCpuRenderer.Render(before, w, h, w, h, r, 1, field, source, w, h, r);
    GlassCpuRenderer.Render(after, w, h, w, h, r, 1, field, source, w, h, r, 0, 0, 1, 1, new LiquidOpticsFrame(28, 0, 1, 1, 1));
    WritePpm(Path.Combine(directory, "lag-before.ppm"), before, background);
    WritePpm(Path.Combine(directory, "lag-after.ppm"), after, background);
}

for (int i = 0; i < 20; i++) GlassCpuRenderer.Render(pixels, w, h, w, h, r, 1, field, source, w, h, r);
var samples = new double[180];
long allocation = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < samples.Length; i++)
{
    long start = Stopwatch.GetTimestamp();
    GlassCpuRenderer.Render(pixels, w, h, w, h, r, 1, field, source, w, h, r);
    samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
Array.Sort(samples);
Console.WriteLine($"SYNTHETIC actual Core CPU fallback only {w}x{h}, {samples.Length} runs: mean={samples.Average():F3}ms p95={samples[(int)(samples.Length * .95)]:F3}ms; measured-loop allocated={bytes} bytes.");
Console.WriteLine("Excludes capture, worker scheduling, snapshots, pooling, UI WritePixels, GPU and actual presentation. NOT Windows/GPU FPS.");
var lagSamples = new double[60];
var lagOptics = new LiquidOpticsFrame(28, 0, 1, 1, 1);
for (int i = 0; i < 8; i++) GlassCpuRenderer.Render(pixels, w, h, w, h, r, 1, field, source, w, h, r, 0, 0, 1, 1, lagOptics);
allocation = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < lagSamples.Length; i++)
{
    long start = Stopwatch.GetTimestamp();
    GlassCpuRenderer.Render(pixels, w, h, w, h, r, 1, field, source, w, h, r, 0, 0, 1, 1, lagOptics);
    lagSamples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
long lagBytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
Array.Sort(lagSamples);
Console.WriteLine($"SYNTHETIC live-warp CPU fallback {w}x{h}, {lagSamples.Length} runs: mean={lagSamples.Average():F3}ms p95={lagSamples[(int)(lagSamples.Length * .95)]:F3}ms; measured-loop allocated={lagBytes} bytes. Still not Windows/GPU FPS.");
Console.WriteLine($"Synthetic algorithm PPM images: {directory} (合成算法验证，非实际Windows截图)");

static void WritePpm(string path, int[] pixels, int[] background)
{
    const int w = 534, h = 230, r = 35, pad = LiquidRefraction.DefaultPad;
    using var file = File.Create(path);
    file.Write(Encoding.ASCII.GetBytes($"P6\n# 合成算法验证，非实际Windows截图\n{w} {h}\n255\n"));
    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        int c = pixels[y * w + x], a = (int)((uint)c >> 24);
        int bg = background[(y + pad) * (w + 2 * pad) + x + pad];
        if (IslandOutline.SignedDistance(x + .5, y + .5, w, h, r) >= 0) { c = 0; a = 0; }
        for (int shift = 16; shift >= 0; shift -= 8)
            file.WriteByte((byte)Math.Clamp(((c >> shift) & 255) + ((bg >> shift) & 255) * (255 - a) / 255, 0, 255));
    }
}
