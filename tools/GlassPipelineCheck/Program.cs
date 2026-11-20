using System.Diagnostics;
using System.Windows.Threading;
using DynamicIsland.Core.Motion;
using DynamicIsland.Visuals;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var buffer = new GlassRefractionBuffer { AllowLiveInterior = true };
        buffer.Diagnostics = Console.WriteLine;
        var dispatcher = Dispatcher.CurrentDispatcher;
        int submitted = 0, ticks = 0, dragTicks = 0, dragVisible = 0;
        bool stopped = false;
        var elapsed = Stopwatch.StartNew();
        buffer.FrameReady = () =>
        {
            if (!stopped) dispatcher.BeginInvoke(() => { if (!stopped && buffer.PresentReady()) submitted++; }, DispatcherPriority.Render);
        };
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (elapsed.Elapsed.TotalSeconds >= 11)
            {
                stopped = true; timer.Stop(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); return;
            }
            ticks++;
            int w, left;
            // 前 2 秒变尺寸，其后约 6 秒以约 48px/tick 平移（远超旧 3px 拒绝阈值），再做 Clear 回归。
            if (elapsed.Elapsed.TotalSeconds < 2)
            {
                w = 320 + (ticks % 100 < 50 ? ticks % 50 : 50 - ticks % 50) * 2;
                left = 200;
            }
            else if (elapsed.Elapsed.TotalSeconds < 8)
            {
                w = 320;
                left = 200 + ticks * 48;
                dragTicks++;
                if (buffer.HasVisibleGlass) dragVisible++;
            }
            else
            {
                w = 320;
                left = 200;
            }
            buffer.TryBeginCapture(left, 200, w, 140, left, 200, w, 140, 35, 1);
            if (buffer.TryPresent(w, 140, 35, null, true, 1, left, 200, 1, 1, LiquidOpticsFrame.Identity)) submitted++;
            if (elapsed.Elapsed.TotalSeconds >= 8 && ticks % 40 == 0) buffer.Clear();
        };
        timer.Start(); Dispatcher.Run();
        buffer.FrameReady = null;
        buffer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine($"PIPELINE runner-desktop ticks={ticks} ui-changes-including-clear={submitted} drag-ticks={dragTicks} drag-visible={dragVisible} elapsed-ms={elapsed.Elapsed.TotalMilliseconds:F1}; synthetic resize/fast-drag/clear/exit; not user hardware FPS");
        if (submitted < 5) throw new Exception("Pipeline produced too few UI submissions.");
        if (dragTicks > 30 && dragVisible < dragTicks * 0.7)
            throw new Exception($"Fast-drag glass vanished too often: visible {dragVisible}/{dragTicks}");
    }
}
