using System.Diagnostics;
using System.Windows.Threading;
using DynamicIsland.Core.Motion;
using DynamicIsland.Visuals;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var buffer=new GlassRefractionBuffer { AllowLiveInterior=true };
        buffer.Diagnostics=Console.WriteLine;
        var dispatcher=Dispatcher.CurrentDispatcher;
        int submitted=0,ticks=0;
        bool stopped=false;
        var elapsed=Stopwatch.StartNew();
        buffer.FrameReady=()=>
        {
            if(!stopped) dispatcher.BeginInvoke(()=> { if(!stopped && buffer.PresentReady()) submitted++; },DispatcherPriority.Render);
        };
        var timer=new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(16) };
        timer.Tick+=(_,_)=>
        {
            if(elapsed.Elapsed.TotalSeconds>=11)
            {
                stopped=true; timer.Stop(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); return;
            }
            ticks++;
            int w=320+(ticks%100<50 ? ticks%50 : 50-ticks%50)*2;
            // Runner desktop only: no user machine; includes real capture, upload, readback and WritePixels.
            buffer.TryBeginCapture(200,200,w,140,200,200,w,140,35,1);
            if(buffer.TryPresent(w,140,35,null,false,1,200,200,1,1,LiquidOpticsFrame.Identity)) submitted++;
            if(ticks%120==0) buffer.Clear(); // invalidates an in-flight epoch and drains pooled images safely
        };
        timer.Start(); Dispatcher.Run();
        buffer.FrameReady=null;
        buffer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine($"PIPELINE runner-desktop ticks={ticks} ui-changes-including-clear={submitted} elapsed-ms={elapsed.Elapsed.TotalMilliseconds:F1}; synthetic resize/clear/exit; not user hardware FPS");
        if(submitted<5) throw new Exception("Pipeline produced too few UI submissions.");
    }
}
