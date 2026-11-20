using System.Diagnostics;
using DynamicIsland.Core.Motion;
using DynamicIsland.Core.Island;
const int pad = LiquidRefraction.DefaultPad;
foreach (var (w,h) in new[] { (320,140), (640,280) })
{
    int[] src = new int[(w+2*pad)*(h+2*pad)], dst = new int[w*h];
    for (int i=0;i<src.Length;i++) src[i]=unchecked((int)0xFF000000) | (i*17 & 0xFFFFFF);
    foreach (bool live in new[] {false,true})
    {
        int ow=live ? Math.Min(w,GlassWork.CpuLiveOutputLongSide) : w;
        int oh=h*ow/w;
        void Render() => GlassCpuRenderer.Render(dst,ow,oh,w,h,35,1,null,src,w,h,35,optics:new(18,0,1,1,1),liveInterior:live);
        for(int i=0;i<12;i++) Render();
        double[] times=new double[80];
        long alloc=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<times.Length;i++) {long start=Stopwatch.GetTimestamp(); Render(); times[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}
        alloc=GC.GetAllocatedBytesForCurrentThread()-alloc;
        Array.Sort(times);
        Console.WriteLine($"CPU synthetic {w}x{h} output={ow}x{oh} live={live} n={times.Length} p50-ms={times[40]:F3} p95-ms={times[75]:F3} alloc/frame={alloc/times.Length} checksum={dst[w*h/2]:X8} (compute only; not desktop fps)");
    }
}
