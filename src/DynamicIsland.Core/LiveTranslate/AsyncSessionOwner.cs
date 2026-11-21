namespace DynamicIsland.Core.LiveTranslate;

/// <summary>One running owner and one replaceable pending start. Never dispose resources while their work is active.</summary>
public sealed class AsyncSessionOwner
{
    sealed class CancellationLease : IDisposable
    {
        readonly object _gate = new();
        readonly CancellationTokenSource _source = new();
        bool _disposed, _disposeRequested;
        int _cancelDepth;
        public CancellationToken Token { get; }
        public CancellationLease() => Token = _source.Token;
        public void Cancel()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _cancelDepth++;
                try { _source.Cancel(); }
                finally { _cancelDepth--; if (_cancelDepth == 0 && _disposeRequested) Dispose(); }
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_cancelDepth > 0) { _disposeRequested = true; return; }
                _disposed = true; _source.Dispose();
            }
        }
    }
    sealed record StartRequest(int Epoch, CancellationLease Lease, Func<int, CancellationToken, Task> Work, TaskCompletionSource Finished);
    readonly object _gate = new();
    CancellationLease? _latest;
    StartRequest? _running;
    StartRequest? _pending;
    Task _runner = Task.CompletedTask;
    int _epoch;
    bool _closed, _workerRunning;
    public event Action<Exception>? Faulted;
    public int PendingStarts { get { lock (_gate) return _pending == null ? 0 : 1; } }
    public int Start(Func<int, CancellationToken, Task> work)
    {
        CancellationLease? running; StartRequest? replaced; int epoch;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            running = _running?.Lease; replaced = _pending;
            _latest = new CancellationLease(); epoch = ++_epoch;
            _pending = new(epoch, _latest, work, new(TaskCreationOptions.RunContinuationsAsynchronously));
            if (!_workerRunning) { _workerRunning = true; _runner = Task.Run(RunAsync); }
        }
        // Registrations may consult owner state or native producer locks: never cancel under _gate.
        running?.Cancel(); replaced?.Lease.Cancel(); replaced?.Lease.Dispose(); replaced?.Finished.TrySetResult();
        return epoch;
    }
    async Task RunAsync()
    {
        while (true)
        {
            StartRequest request;
            lock (_gate)
            {
                if (_pending == null) { _workerRunning = false; return; }
                request = _pending; _pending = null; _running = request;
            }
            try
            {
                request.Lease.Token.ThrowIfCancellationRequested();
                await request.Work(request.Epoch, request.Lease.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (request.Lease.Token.IsCancellationRequested) { }
            catch (Exception ex) { Faulted?.Invoke(ex); }
            finally
            {
                lock (_gate)
                {
                    _running = null;
                    if (ReferenceEquals(_latest, request.Lease)) _latest = null;
                }
                try { request.Lease.Dispose(); }
                finally { request.Finished.TrySetResult(); }
            }
        }
    }
    public bool IsCurrent(int epoch)
    { lock (_gate) return !_closed && epoch == _epoch && (_latest == null || !_latest.Token.IsCancellationRequested); }
    public Task StopAsync(bool close = false)
    {
        CancellationLease? running; StartRequest? pending; Task task;
        lock (_gate)
        {
            _closed |= close; ++_epoch; running = _running?.Lease; pending = _pending;
            task = close ? _runner : _running?.Finished.Task ?? Task.CompletedTask;
            _pending = null; _latest = null;
        }
        running?.Cancel(); pending?.Lease.Cancel(); pending?.Lease.Dispose(); pending?.Finished.TrySetResult();
        return task;
    }
}
