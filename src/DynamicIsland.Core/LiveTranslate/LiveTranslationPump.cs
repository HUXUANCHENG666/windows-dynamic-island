using DynamicIsland.Core.Translation;
namespace DynamicIsland.Core.LiveTranslate;

public readonly record struct LiveTranslationWork(int CaptionId, string Recognized, TranslateRequest Request, TranslateApiOptions Api);

/// <summary>One in-flight request plus at most forty latest caption revisions. A new partial never cancels useful work.</summary>
public sealed class LiveTranslationPump
{
    readonly object _gate = new();
    readonly Dictionary<int, LiveTranslationWork> _pending = new();
    LiveTranslationWork? _activeWork;
    readonly SemaphoreSlim _wake = new(0, 1);
    readonly AsyncSessionOwner _owner = new();
    readonly Func<TranslateRequest, TranslateApiOptions, CancellationToken, Task<string>> _translate;
    readonly Action<Action> _post;
    readonly TimeSpan _interval;
    public event Action<LiveTranslationWork, string>? Completed;
    public event Action<LiveTranslationWork>? Failed;
    public event Action<string>? Diagnostic;
    public LiveTranslationPump(Func<TranslateRequest, TranslateApiOptions, CancellationToken, Task<string>> translate, Action<Action> post, TimeSpan? interval = null)
    {
        _translate = translate; _post = post; _interval = interval ?? TimeSpan.FromMilliseconds(800);
        _owner.Faulted += ex => Diagnostic?.Invoke(ex.GetType().Name);
    }
    public int PendingCount { get { lock (_gate) return _pending.Count; } }
    public void Start()
    {
        lock (_gate) { _pending.Clear(); _activeWork = null; }
        _owner.Start(RunAsync); // cancellation registrations must not execute under the queue lock
    }
    public void Enqueue(LiveTranslationWork work)
    {
        lock (_gate)
        {
            if (_activeWork is { } active && active.CaptionId == work.CaptionId && active.Recognized == work.Recognized && active.Api == work.Api) return;
            if (_pending.Count >= LiveTranslateCaptionChain.MaxItems && !_pending.ContainsKey(work.CaptionId)) _pending.Remove(_pending.Keys.First());
            _pending[work.CaptionId] = work;
            if (_wake.CurrentCount == 0) _wake.Release();
        }
    }
    public void ChangeProvider(TranslateApiOptions api)
    {
        lock (_gate)
        {
            foreach (int id in _pending.Keys.ToArray()) _pending[id] = _pending[id] with { Api = api };
        }
    }
    public void Remove(int id) { lock (_gate) _pending.Remove(id); }
    public Task StopAsync(bool close = false)
    {
        lock (_gate) _pending.Clear();
        return _owner.StopAsync(close);
    }
    async Task RunAsync(int epoch, CancellationToken token)
    {
        while (true)
        {
            await _wake.WaitAsync(token).ConfigureAwait(false);
            while (true)
            {
                LiveTranslationWork work;
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    if (_pending.Count == 0) break;
                    int id = _pending.Keys.First(); work = _pending[id]; _pending.Remove(id); _activeWork = work;
                }
                try
                {
                    string result = await _translate(work.Request, work.Api, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        _post(() => { if (_owner.IsCurrent(epoch)) Completed?.Invoke(work, result); });
                    }
                    else _post(() => { if (_owner.IsCurrent(epoch)) Failed?.Invoke(work); });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Diagnostic?.Invoke(ex is InvalidOperationException ? "translate: " + ex.Message : ex.GetType().Name); // 只带我们自己写的提示，不含请求、端点、转写或密钥
                    _post(() => { if (_owner.IsCurrent(epoch)) Failed?.Invoke(work); });
                }
                finally { lock (_gate) _activeWork = null; }
                await Task.Delay(_interval, token).ConfigureAwait(false);
            }
        }
    }
}
