using System.Collections.Concurrent;

namespace DynamicIsland.Visuals;

/// <summary>One lazily started owner thread, one bounded mailbox. DISPLAY HDC stays owned by a live thread.</summary>
sealed class GlassCaptureWorker(Action cleanup) : IDisposable
{
    readonly BlockingCollection<Action> _queue = new(1);
    readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly object _gate = new();
    bool _started, _closed;
    public Task Completion => _stopped.Task;
    public Task Run(Action action)
    {
        lock (_gate)
        {
            if (_closed) return Task.FromException(new ObjectDisposedException(nameof(GlassCaptureWorker)));
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_queue.TryAdd(() => { try { action(); done.SetResult(); } catch(Exception ex) { done.SetException(ex); } }))
                return Task.FromException(new InvalidOperationException("Capture mailbox full"));
            if (!_started)
            {
                _started = true;
                new Thread(() =>
                {
                    try { foreach(var work in _queue.GetConsumingEnumerable()) work(); }
                    finally { cleanup(); _queue.Dispose(); _stopped.TrySetResult(); }
                }) { IsBackground=true, Name="Glass capture" }.Start();
            }
            return done.Task;
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed=true;
            if (_started) _queue.CompleteAdding();
            else { cleanup(); _queue.Dispose(); _stopped.TrySetResult(); }
        }
    }
}
