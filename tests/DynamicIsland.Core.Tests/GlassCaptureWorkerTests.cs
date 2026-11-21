using DynamicIsland.Visuals;
using Xunit;
namespace DynamicIsland.Core.Tests;
public class GlassCaptureWorkerTests
{
    [Fact]
    public async Task OwnerThreadIsStableAndShutdownDrainsBeforeCleanup()
    {
        int first=0,second=0,cleanup=0;
        using var worker=new GlassCaptureWorker(()=>cleanup=Environment.CurrentManagedThreadId);
        await worker.Run(()=>first=Environment.CurrentManagedThreadId);
        await worker.Run(()=>second=Environment.CurrentManagedThreadId);
        Assert.Equal(first,second);
        worker.Dispose(); await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(first,cleanup);
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>worker.Run(()=>{}));
    }
    [Fact]
    public async Task MailboxIsBoundedAndDisposalDrainsAcceptedWork()
    {
        using var entered=new ManualResetEventSlim();
        using var release=new ManualResetEventSlim();
        int ran=0;
        using var worker=new GlassCaptureWorker(()=>{});
        var one=worker.Run(()=>{ entered.Set(); if(!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var two=worker.Run(()=>ran++);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>worker.Run(()=>ran++));
        worker.Dispose(); release.Set();
        await Task.WhenAll(one,two,worker.Completion).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1,ran);
    }
}
