using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Motion;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

// Renders the island from the same squircle, palette, and spring the Windows app uses.
// This is not a screenshot of the WPF window. Fonts are Inter and WenQuanYi because
// Segoe UI and Microsoft YaHei are not on this machine.

var output = args.Length > 0 ? args[0] : Path.Combine("docs", "previews");
Directory.CreateDirectory(output);

using var latin = SKTypeface.FromFile("/usr/share/fonts/truetype/macos/Inter-SemiBold.ttf")
    ?? SKTypeface.Default;
using var latinRegular = SKTypeface.FromFile("/usr/share/fonts/truetype/macos/Inter-Regular.ttf")
    ?? latin;
using var cjk = SKTypeface.FromFile("/usr/share/fonts/truetype/wqy/wqy-microhei.ttc")
    ?? latin;

DrawSheet(Path.Combine(output, "states-light.png"), light: true);
DrawSheet(Path.Combine(output, "states-dark.png"), light: false);
var frames = DrawMotion(Path.Combine(output, "motion.png"), Path.Combine(output, "motion.gif"));
DrawCorners(Path.Combine(output, "corners.png"));
Console.WriteLine($"wrote previews to {output} ({frames} motion frames)");

void DrawSheet(string path, bool light)
{
    const int width = 1680;
    const int height = 1080;
    using var bitmap = new SKBitmap(width, height);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(light ? new SKColor(236, 232, 226) : new SKColor(22, 24, 28));
    var ink = light ? new SKColor(28, 28, 30) : new SKColor(236, 236, 238);
    var quiet = light ? new SKColor(80, 80, 84) : new SKColor(168, 168, 172);
    Text(canvas, "灵动岛", 56, 64, 28, latin, cjk, ink);
    Text(canvas, "形状和动效用同一套弹簧与超椭圆算出来。这不是 Windows 实机截图。", 56, 100, 15, latinRegular, cjk, quiet);

    float y = 160;
    Label(canvas, "紧凑", 56, y, quiet);
    y += 28;
    DrawIsland(canvas, 56, y, Frame(IslandMode.Idle, false), 1, null);
    DrawIsland(canvas, 240, y, Frame(IslandMode.Media, false), 1, (c, r) => CompactMedia(c, r));
    DrawIsland(canvas, 480, y, Frame(IslandMode.Timer, false), 1, (c, r) =>
        Text(c, "24:18", r.MidX, r.MidY + 6, 15, latin, cjk, SKColors.White, center: true));

    y = 280;
    Label(canvas, "展开", 56, y, quiet);
    y += 36;
    var media = Frame(IslandMode.Media, true);
    DrawIsland(canvas, 56, y, media, 1, (c, r) => ExpandedMedia(c, r));
    var idle = Frame(IslandMode.Idle, true);
    DrawIsland(canvas, 56 + (float)media.Width + 48, y, idle, 1, ExpandedIdle);

    y += (float)Math.Max(media.Height, idle.Height) + 48;
    var timer = Frame(IslandMode.Timer, true);
    DrawIsland(canvas, 56, y, timer, 1, ExpandedTimer);
    var volume = Frame(IslandMode.Volume, true);
    DrawIsland(canvas, 56 + (float)timer.Width + 40, y + 20, volume, 1, Volume);
    var battery = Frame(IslandMode.Battery, true);
    DrawIsland(canvas, 56 + (float)timer.Width + 40, y + 20 + (float)volume.Height + 28, battery, 1, Battery);

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(path);
    data.SaveTo(stream);
}

int DrawMotion(string stripPath, string gifPath)
{
    var from = Frame(IslandMode.Media, false);
    var to = Frame(IslandMode.Media, true);
    var width = new Spring(SpringConfig.Island, from.Width) { Target = to.Width };
    var height = new Spring(SpringConfig.Island, from.Height) { Target = to.Height };
    var radius = new Spring(SpringConfig.Island, from.Radius) { Target = to.Radius };
    var corner = new Spring(SpringConfig.Island, from.CornerN) { Target = to.CornerN };
    var expand = new Spring(SpringConfig.Island, 0) { Target = 1 };

    var times = new[] { 0.0, 0.08, 0.14, 0.22, 0.30, 0.40, 0.55, 0.75 };
    var shots = new List<SKBitmap>();
    double elapsed = 0;
    foreach (var t in times)
    {
        while (elapsed < t - 1e-6)
        {
            width.Step(1.0 / 120);
            height.Step(1.0 / 120);
            radius.Step(1.0 / 120);
            corner.Step(1.0 / 120);
            expand.Step(1.0 / 120);
            elapsed += 1.0 / 120;
        }

        shots.Add(RenderFrame(width.Value, height.Value, radius.Value, corner.Value, expand.Value, t));
    }

    int cellW = shots.Max(s => s.Width);
    int cellH = shots.Max(s => s.Height);
    using var strip = new SKBitmap(cellW * shots.Count, cellH + 36);
    using var canvas = new SKCanvas(strip);
    canvas.Clear(new SKColor(236, 232, 226));
    for (int i = 0; i < shots.Count; i++)
    {
        canvas.DrawBitmap(shots[i], i * cellW, 0);
        Text(canvas, $"{times[i] * 1000:0} ms", i * cellW + cellW / 2f, cellH + 24, 13, latin, cjk, new SKColor(60, 60, 64), center: true);
    }

    SavePng(strip, stripPath);

    using var first = Image.Load<Rgba32>(PngBytes(shots[0]));
    for (int i = 1; i < shots.Count; i++)
    {
        using var next = Image.Load<Rgba32>(PngBytes(shots[i]));
        first.Frames.AddFrame(next.Frames[0]);
    }

    for (int i = 0; i < first.Frames.Count; i++)
    {
        var meta = first.Frames[i].Metadata.GetGifMetadata();
        meta.FrameDelay = i == first.Frames.Count - 1 ? 80 : 12;
    }

    first.Metadata.GetGifMetadata().RepeatCount = 0;
    first.SaveAsGif(gifPath);
    foreach (var shot in shots)
        shot.Dispose();
    return shots.Count;
}

SKBitmap RenderFrame(double w, double h, double radius, double n, double expand, double time)
{
    int bw = 520;
    int bh = 280;
    var bitmap = new SKBitmap(bw, bh);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(new SKColor(236, 232, 226));
    float x = (bw - (float)w) / 2f;
    float y = 18;
    var frame = new IslandFrame(w, h, Squircle.SafeRadius(w, h, radius), n);
    float expandFade = (float)Easing.ExpandFade(expand);
    float collapseFade = (float)Easing.CollapseFade(expand);
    DrawIsland(canvas, x, y, frame, 1, (c, rect) =>
    {
        if (collapseFade > 0.02)
            CompactMedia(c, rect, collapseFade);
        if (expandFade > 0.02)
            ExpandedMedia(c, rect, expandFade, fit: Math.Min((float)w / (float)IslandMetrics.MediaExpandedW, (float)h / (float)IslandMetrics.MediaExpandedH));
    });
    Text(canvas, time.ToString("0.00") + "s", 16, bh - 16, 12, latin, cjk, new SKColor(70, 70, 74));
    return bitmap;
}

void DrawCorners(string path)
{
    using var bitmap = new SKBitmap(1280, 640);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.White);
    Text(canvas, "圆角对照", 48, 52, 26, latin, cjk, new SKColor(20, 20, 22));
    Text(canvas, "同一块 300×180、半径 56 的矩形。红线是轮廓。右边是弹簧从 0 走到 1，峰值大约高出 4.5%。", 48, 86, 15, latinRegular, cjk, new SKColor(70, 70, 74));

    DrawStudy(canvas, 48, 140, 300, 180, 56, 2, "n = 2   正圆圆角");
    DrawStudy(canvas, 400, 140, 300, 180, 56, 4.5, "n = 4.5   连续圆角");
    DrawSpringPlot(canvas, 760, 150, 460, 360);
    SavePng(bitmap, path);
}

void DrawStudy(SKCanvas canvas, float x, float y, float w, float h, float radius, double n, string caption)
{
    using var path = PathOf(x, y, w, h, radius, n, 32);
    using var fill = new SKPaint { IsAntialias = true, Color = SKColors.Black };
    using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = new SKColor(220, 60, 50) };
    canvas.DrawPath(path, fill);
    canvas.DrawPath(path, stroke);
    Text(canvas, caption, x, y + h + 36, 16, latin, cjk, new SKColor(30, 30, 32));
}

void DrawSpringPlot(SKCanvas canvas, float x, float y, float w, float h)
{
    var spring = new Spring(SpringConfig.Island, 0) { Target = 1 };
    var samples = new List<double>();
    for (int i = 0; i < 140; i++)
    {
        spring.Step(1.0 / 120);
        samples.Add(spring.Value);
    }

    using var axis = new SKPaint { Color = new SKColor(180, 180, 184), StrokeWidth = 1.2f, IsAntialias = true };
    using var line = new SKPaint { Color = SKColors.Black, StrokeWidth = 2.4f, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    canvas.DrawLine(x, y + h, x + w, y + h, axis);
    canvas.DrawLine(x, y, x, y + h, axis);
    using var one = new SKPaint { Color = new SKColor(220, 60, 50), StrokeWidth = 1, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new[] { 6f, 6f }, 0) };
    float oneY = y + h - h * (1f / 1.12f);
    canvas.DrawLine(x, oneY, x + w, oneY, one);
    Text(canvas, "1.0", x - 36, oneY + 4, 12, latin, cjk, new SKColor(220, 60, 50));

    using var plot = new SKPath();
    for (int i = 0; i < samples.Count; i++)
    {
        float px = x + w * (i / (float)(samples.Count - 1));
        float py = y + h - h * (float)(samples[i] / 1.12);
        if (i == 0) plot.MoveTo(px, py);
        else plot.LineTo(px, py);
    }

    canvas.DrawPath(plot, line);
    Text(canvas, "弹簧  刚度 170 / 阻尼 18.3", x, y - 16, 15, latin, cjk, new SKColor(20, 20, 22));
    Text(canvas, "0.5 秒", x + w - 48, y + h + 22, 12, latin, cjk, new SKColor(80, 80, 84));
}

void DrawIsland(SKCanvas canvas, float x, float y, IslandFrame frame, float alpha, Action<SKCanvas, SKRect>? content, float fit = 1)
{
    using var path = PathOf(x, y, (float)frame.Width, (float)frame.Height, (float)frame.Radius, frame.CornerN, 24);
    using var shadow = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0, 0, 0, 80),
        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14)
    };
    canvas.Save();
    canvas.Translate(0, 8);
    canvas.DrawPath(path, shadow);
    canvas.Restore();

    using var fill = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(
            new SKPoint(x, y),
            new SKPoint(x, y + (float)frame.Height),
            new[] { new SKColor(24, 24, 24), new SKColor(8, 8, 8), new SKColor(0, 0, 0) },
            new[] { 0f, 0.42f, 1f },
            SKShaderTileMode.Clamp)
    };
    canvas.DrawPath(path, fill);
    using var hair = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.25f, Color = new SKColor(255, 255, 255, 40) };
    canvas.DrawPath(path, hair);
    if (content == null || alpha < 0.02)
        return;
    canvas.Save();
    canvas.ClipPath(path, antialias: true);
    if (alpha < 0.999f)
    {
        using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(alpha * 255)) };
        canvas.SaveLayer(layer);
    }

    content(canvas, new SKRect(x, y, x + (float)frame.Width, y + (float)frame.Height));
    if (alpha < 0.999f)
        canvas.Restore();
    canvas.Restore();
    _ = fit;
}

void CompactMedia(SKCanvas canvas, SKRect rect, float alpha = 1)
{
    canvas.Save();
    if (alpha < 0.999f)
        canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)(alpha * 255)) });
    var art = new SKRect(rect.Left + 10, rect.MidY - 14, rect.Left + 38, rect.MidY + 14);
    using var artPaint = new SKPaint { IsAntialias = true, Color = new SKColor(48, 42, 64) };
    canvas.DrawRoundRect(art, 8, 8, artPaint);
    float x = rect.Right - 38;
    for (int i = 0; i < 4; i++)
    {
        float h = 5 + (i % 2 == 0 ? 4 : 9);
        var bar = new SKRect(x + i * 4.6f, rect.MidY - h / 2, x + i * 4.6f + 2.4f, rect.MidY + h / 2);
        using var barPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        canvas.DrawRoundRect(bar, 1.2f, 1.2f, barPaint);
    }

    if (alpha < 0.999f)
        canvas.Restore();
    canvas.Restore();
}

void ExpandedMedia(SKCanvas canvas, SKRect rect, float alpha = 1, float fit = 1)
{
    canvas.Save();
    canvas.Translate(rect.MidX, rect.MidY);
    canvas.Scale(Math.Clamp(fit, 0.45f, 1.06f));
    canvas.Translate(-(float)IslandMetrics.MediaExpandedW / 2f, -(float)IslandMetrics.MediaExpandedH / 2f);
    if (alpha < 0.999f)
        canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)(alpha * 255)) });
    var art = new SKRect(16, 14, 120, 118);
    using var artPaint = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(16, 14), new SKPoint(120, 118),
            new[] { new SKColor(255, 122, 89), new SKColor(88, 54, 168) },
            SKShaderTileMode.Clamp)
    };
    canvas.DrawRoundRect(art, 24, 24, artPaint);
    Text(canvas, "夜曲", 136, 42, 16, latin, cjk, SKColors.White);
    Text(canvas, "周杰伦", 136, 66, 13, latinRegular, cjk, new SKColor(255, 255, 255, 166));
    Text(canvas, "1:12", 136, 112, 11, latin, cjk, new SKColor(255, 255, 255, 170));
    using var track = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 46) };
    canvas.DrawRoundRect(new SKRect(176, 104, 390, 107.5f), 2, 2, track);
    using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White };
    canvas.DrawRoundRect(new SKRect(176, 104, 250, 107.5f), 2, 2, fill);
    Text(canvas, "3:46", 396, 112, 11, latin, cjk, new SKColor(255, 255, 255, 130));
    DrawTransport(canvas, 214, 142);
    if (alpha < 0.999f)
        canvas.Restore();
    canvas.Restore();
}

void ExpandedIdle(SKCanvas canvas, SKRect rect)
{
    Text(canvas, "21:41", rect.MidX, rect.Top + 62, 36, latin, cjk, SKColors.White, center: true);
    Text(canvas, "11月10日 星期二", rect.MidX, rect.Top + 88, 13, latinRegular, cjk, new SKColor(255, 255, 255, 166), center: true);
    Chip(canvas, rect.MidX - 78, rect.Bottom - 48, "秒表");
    Chip(canvas, rect.MidX + 8, rect.Bottom - 48, "专注 25:00");
}

void ExpandedTimer(SKCanvas canvas, SKRect rect)
{
    Text(canvas, "24:18", rect.MidX, rect.Top + 64, 40, latin, cjk, SKColors.White, center: true);
    Text(canvas, "倒计时", rect.MidX, rect.Top + 90, 13, latinRegular, cjk, new SKColor(255, 255, 255, 166), center: true);
    Chip(canvas, rect.MidX - 86, rect.Top + 108, "−");
    using var play = new SKPaint { IsAntialias = true, Color = SKColors.White };
    var button = new SKRect(rect.MidX - 36, rect.Top + 108, rect.MidX + 36, rect.Top + 140);
    canvas.DrawRoundRect(button, 16, 16, play);
    Text(canvas, "暂停", button.MidX, button.MidY + 5, 13, latin, cjk, SKColors.Black, center: true);
    Chip(canvas, rect.MidX + 46, rect.Top + 108, "+");
}

void Volume(SKCanvas canvas, SKRect rect)
{
    Text(canvas, "音量", rect.Left + 72, rect.Top + 32, 15, latin, cjk, SKColors.White);
    Text(canvas, "62%", rect.Right - 28, rect.Top + 32, 15, latin, cjk, SKColors.White, center: true);
    using var track = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 46) };
    canvas.DrawRoundRect(new SKRect(rect.Left + 72, rect.Top + 46, rect.Right - 28, rect.Top + 50), 2, 2, track);
    using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White };
    float end = rect.Left + 72 + (rect.Width - 100) * 0.62f;
    canvas.DrawRoundRect(new SKRect(rect.Left + 72, rect.Top + 46, end, rect.Top + 50), 2, 2, fill);
    using var icon = new SKPaint { IsAntialias = true, Color = SKColors.White };
    canvas.DrawRoundRect(new SKRect(rect.Left + 30, rect.MidY - 6, rect.Left + 42, rect.MidY + 6), 2, 2, icon);
}

void Battery(SKCanvas canvas, SKRect rect)
{
    using var pen = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = SKColors.White };
    canvas.DrawRoundRect(new SKRect(rect.Left + 26, rect.MidY - 8, rect.Left + 52, rect.MidY + 8), 3, 3, pen);
    using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White };
    canvas.DrawRoundRect(new SKRect(rect.Left + 29, rect.MidY - 5, rect.Left + 46, rect.MidY + 5), 1.5f, 1.5f, fill);
    Text(canvas, "正在充电", rect.Left + 72, rect.Top + 32, 15, latin, cjk, SKColors.White);
    Text(canvas, "80%", rect.Left + 72, rect.Top + 54, 13, latin, cjk, new SKColor(255, 255, 255, 166));
}

void DrawTransport(SKCanvas canvas, float x, float y)
{
    using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
    using var black = new SKPaint { IsAntialias = true, Color = SKColors.Black };
    canvas.DrawCircle(x + 64, y + 16, 18, white);
    using var tri = new SKPath();
    tri.MoveTo(x + 58, y + 8);
    tri.LineTo(x + 74, y + 16);
    tri.LineTo(x + 58, y + 24);
    tri.Close();
    canvas.DrawPath(tri, black);
    canvas.DrawCircle(x, y + 16, 3, white);
    canvas.DrawCircle(x + 128, y + 16, 3, white);
}

void Chip(SKCanvas canvas, float x, float y, string text)
{
    using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 28) };
    float width = Math.Max(36, Measure(text, 13, latin, cjk) + 28);
    var rect = new SKRect(x, y, x + width, y + 32);
    canvas.DrawRoundRect(rect, 16, 16, paint);
    Text(canvas, text, rect.MidX, rect.MidY + 5, 13, latin, cjk, SKColors.White, center: true);
}

void Label(SKCanvas canvas, string text, float x, float y, SKColor color) =>
    Text(canvas, text, x, y, 13, latin, cjk, color);

static IslandFrame Frame(IslandMode mode, bool expanded) => IslandMetrics.FrameFor(mode, expanded);

static SKPath PathOf(float x, float y, float w, float h, float radius, double n, int steps)
{
    var pts = Squircle.Build(w, h, Squircle.SafeRadius(w, h, radius), n, steps);
    var path = new SKPath();
    if (pts.Count == 0)
        return path;
    path.MoveTo(x + (float)pts[0].X, y + (float)pts[0].Y);
    for (int i = 1; i < pts.Count; i++)
        path.LineTo(x + (float)pts[i].X, y + (float)pts[i].Y);
    path.Close();
    return path;
}

void Text(SKCanvas canvas, string text, float x, float y, float size, SKTypeface latinFace, SKTypeface cjkFace, SKColor color, bool center = false)
{
    if (center)
        x -= Measure(text, size, latinFace, cjkFace) / 2f;
    using var paint = new SKPaint { IsAntialias = true, Color = color, TextSize = size };
    foreach (var (value, cjkRun) in Runs(text))
    {
        paint.Typeface = cjkRun ? cjkFace : latinFace;
        canvas.DrawText(value, x, y, paint);
        x += paint.MeasureText(value);
    }
}

float Measure(string text, float size, SKTypeface latinFace, SKTypeface cjkFace)
{
    using var paint = new SKPaint { IsAntialias = true, TextSize = size };
    float width = 0;
    foreach (var (value, cjkRun) in Runs(text))
    {
        paint.Typeface = cjkRun ? cjkFace : latinFace;
        width += paint.MeasureText(value);
    }

    return width;
}

static IEnumerable<(string text, bool cjk)> Runs(string text)
{
    if (text.Length == 0)
        yield break;
    bool cjk = IsCjk(text[0]);
    int start = 0;
    for (int i = 1; i < text.Length; i++)
    {
        bool next = IsCjk(text[i]);
        if (next == cjk)
            continue;
        yield return (text[start..i], cjk);
        start = i;
        cjk = next;
    }

    yield return (text[start..], cjk);
}

static bool IsCjk(char c) => c >= 0x2E80;

static void SavePng(SKBitmap bitmap, string path)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(path);
    data.SaveTo(stream);
}

static byte[] PngBytes(SKBitmap bitmap)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}
