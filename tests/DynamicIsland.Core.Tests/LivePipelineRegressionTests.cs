using System.Collections.Concurrent;
using DynamicIsland.Core.LiveTranslate;
using DynamicIsland.Core.Translation;

namespace DynamicIsland.Core.Tests;
public class LivePipelineRegressionTests
{
    static readonly DateTime Origin = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
    static readonly TranslateApiOptions DefaultApi = new(TranslateApiKind.Default, null, null, null);
    static LiveTranslationWork Work(int id, string text, TranslateApiOptions? api = null) => new(id, text, new(text, "en", "zh-CN", false), api ?? DefaultApi);
    static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public void EmptyFinalAndNewUtteranceCannotReplaceWholePreviousParagraph()
    {
        var chain = new LiveTranslateCaptionChain();
        const string paragraph = "This entire paragraph must survive an empty final at the fifteen second boundary";
        var first = chain.AcceptUtterance(1, paragraph, Origin, false);
        chain.AcceptUtterance(1, "", Origin.AddSeconds(15), true);
        var next = chain.AcceptUtterance(2, "Next", Origin.AddSeconds(16), false);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal(paragraph, chain.Items[0].Primary);
        Assert.False(chain.Items[0].Open);
        Assert.Equal(2, chain.Items.Count);
    }
    [Fact]
    public void RejectedOrMissingFinalStillCannotAliasFollowingUtterance()
    {
        var chain = new LiveTranslateCaptionChain();
        var a = chain.AcceptUtterance(10, "A complete thought without a usable final", Origin, false);
        chain.AcceptUtterance(11, "please", Origin, false);
        Assert.Equal(2, chain.Items.Count);
        Assert.Equal(a.Id, chain.Items[0].Id);
        Assert.False(chain.Items[0].Open);
    }
    [Fact]
    public void CatastrophicShrinkIsRejectedButRealCorrectionsCanShorten()
    {
        var chain = new LiveTranslateCaptionChain();
        const string before = "Please open the very large window near the garden now";
        var first = chain.AcceptUtterance(1, before, Origin, false);
        chain.AcceptUtterance(1, "Thank you", Origin, false);
        Assert.Equal(before, chain.RecognizedText(first.Id));
        const string corrected = "Please open the large window near the garden";
        chain.AcceptUtterance(1, corrected, Origin, true);
        Assert.Equal(corrected, chain.RecognizedText(first.Id));
        Assert.Single(chain.Items);
        Assert.False(chain.Items[0].Open);
    }
    [Fact]
    public void LateTranslationNeverRewindsNewerOriginalAndFailureKeepsBoth()
    {
        var chain = new LiveTranslateCaptionChain();
        var edit = chain.AcceptUtterance(1, "This is the first recognized revision", Origin, false);
        chain.AcceptUtterance(1, "This is the first recognized revision with additional words", Origin, false);
        Assert.True(chain.Complete(edit.Id, edit.Recognized, edit.Recognized)); // unchanged translation still must preserve newer original
        Assert.EndsWith("additional words", chain.Items[0].Original);
        Assert.True(chain.Complete(edit.Id, "第一份译文", edit.Recognized));
        Assert.EndsWith("additional words", chain.Items[0].Original);
        Assert.True(chain.Items[0].TranslationPending);
        Assert.False(chain.Complete(edit.Id, " ", edit.Recognized));
        Assert.False(chain.FailRevision(edit.Id, edit.Recognized));
        chain.Fail(edit.Id);
        Assert.Equal("第一份译文", chain.Items[0].Primary);
        Assert.EndsWith("additional words", chain.Items[0].Original);
    }
    [Fact]
    public void FullHistoryTextIsNotClippedToOldTwoHundredTwentyCharacterPreview()
    {
        var chain = new LiveTranslateCaptionChain(); string longText = string.Join(' ', Enumerable.Repeat("continuous", 90));
        var edit = chain.AcceptUtterance(1, longText, Origin, false);
        Assert.Equal(longText, edit.Recognized);
        Assert.Equal(longText, chain.Items[0].Primary);
    }
    [Fact]
    public void FortyItemEvictionIsOldestOnlyAndLateEvictedUtteranceCannotReappear()
    {
        var chain = new LiveTranslateCaptionChain();
        for (int i = 1; i <= 75; i++) chain.AcceptUtterance(i, $"Utterance number {i} is complete.", Origin.AddSeconds(i), true);
        Assert.Equal(40, chain.Items.Count);
        Assert.Contains("36", chain.Items[0].Primary);
        Assert.Equal(0, chain.AcceptUtterance(1, "Late old utterance", Origin, true).Id);
        Assert.Equal(40, chain.Items.Count);
        Assert.False(chain.Complete(1, "迟到", "old"));
    }
    [Fact]
    public void ActualSegmenterQueueAndChainSurviveTwelveMinutesOfUnbrokenAudio()
    {
        var segmenter = new SpeechSegmenter(); var queue = new RecognitionWorkQueue(); var chain = new LiveTranslateCaptionChain();
        float[] frame = Enumerable.Repeat(.1f, SpeechSegmenter.FrameSamples).ToArray();
        int finals = 0;
        for (int t = 0; t < 36000; t++)
        {
            foreach (var chunk in segmenter.Push(frame)) queue.Enqueue(chunk);
            if (t % 31 != 0 && t != 35999) continue; // slower simulated decoder coalesces actual queued partials
            while (queue.Dequeue() is { } chunk)
            {
                if (chunk.Final) finals++;
                int words = Math.Max(1, chunk.Samples.Length / SpeechSegmenter.FrameSamples / 13);
                string text = chunk.Final && finals % 2 == 0 ? "" : string.Join(' ', Enumerable.Repeat($"topic{chunk.UtteranceId}", words));
                chain.AcceptUtterance(chunk.UtteranceId, text, Origin.AddMilliseconds(t * 20), chunk.Final);
            }
        }
        Assert.Equal(48, finals);
        Assert.Equal(40, chain.Items.Count);
        Assert.All(chain.Items, item => { Assert.NotEmpty(item.Primary); Assert.False(item.Open); });
        Assert.Equal(chain.Items.Count, chain.Items.Select(i => i.Id).Distinct().Count());
    }
    [Fact]
    public void RecognitionQueueMemoryIsBoundedWhileFinalsCoalesceByIdentity()
    {
        var queue = new RecognitionWorkQueue();
        for (int id = 1; id <= 2000; id++) queue.Enqueue(new(id, new float[32], true));
        Assert.Equal(RecognitionWorkQueue.Capacity, queue.Count);
        Assert.Equal(2000 - RecognitionWorkQueue.Capacity, queue.Dropped);
    }
    [Fact]
    public async Task StreamingPartialsDoNotCancelActiveTranslationAndLatestRevisionRunsNext()
    {
        var entered = Signal<LiveTranslationWork>(); var finish = Signal<string>(); var second = Signal<string>(); var complete = Signal<string>();
        int requests = 0;
        var pump = new LiveTranslationPump(async (request, api, token) =>
        {
            if (Interlocked.Increment(ref requests) == 1) { entered.SetResult(Work(1, request.Text)); return await finish.Task.WaitAsync(token); }
            second.TrySetResult(request.Text); await Task.Delay(Timeout.InfiniteTimeSpan, token); return "";
        }, action => action(), TimeSpan.Zero);
        pump.Completed += (_, text) => complete.TrySetResult(text);
        pump.Start(); pump.Enqueue(Work(1, "revision 1")); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 2; i <= 1000; i++) pump.Enqueue(Work(1, $"revision {i}"));
        Assert.Equal(1, pump.PendingCount);
        finish.SetResult("第一份译文");
        Assert.Equal("第一份译文", await complete.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("revision 1000", await second.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await pump.StopAsync(close: true);
        Assert.Equal(2, requests);
    }
    [Fact]
    public async Task StopRejectsAlreadyPostedLateCallbacksAndRepeatedStopIsSafe()
    {
        var posts = new ConcurrentQueue<Action>(); var posted = Signal<bool>(); int called = 0;
        var pump = new LiveTranslationPump((_, _, _) => Task.FromResult("译文"), a => { posts.Enqueue(a); posted.TrySetResult(true); }, TimeSpan.Zero);
        pump.Completed += (_, _) => called++;
        pump.Start(); pump.Enqueue(Work(1, "hello")); await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => pump.StopAsync()));
        while (posts.TryDequeue(out var action)) action();
        Assert.Equal(0, called);
        await pump.StopAsync(close: true);
    }
    [Fact]
    public async Task NativeOwnerWaitsForUncooperativeDecodeBeforeDisposalAndRestart()
    {
        var owner = new AsyncSessionOwner(); var entered = Signal<bool>(); var release = Signal<bool>(); var next = Signal<bool>();
        bool disposed = false;
        owner.Start(async (_, _) => { entered.SetResult(true); try { await release.Task; } finally { disposed = true; } });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = owner.StopAsync(); Assert.False(stop.IsCompleted); Assert.False(disposed);
        owner.Start((_, token) => { Assert.True(disposed); next.SetResult(true); return Task.Delay(Timeout.Infinite, token); });
        Assert.False(next.Task.IsCompleted); release.SetResult(true);
        await next.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.WaitAsync(TimeSpan.FromSeconds(5)); // does not accidentally wait for the newly running session
        await owner.StopAsync(close: true);
    }
    [Fact]
    public async Task ConcurrentStartStopStressHasOneOwnerNoCancelDisposeRaceAndNoUnobservedFaults()
    {
        var owner = new AsyncSessionOwner(); int active = 0, max = 0, faults = 0;
        owner.Faulted += _ => Interlocked.Increment(ref faults);
        async Task Work(int _, CancellationToken token)
        {
            int count = Interlocked.Increment(ref active); Interlocked.Exchange(ref max, Math.Max(max, count));
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Interlocked.Decrement(ref active); }
        }
        await Task.WhenAll(Enumerable.Range(0, 300).Select(i => Task.Run(() => { owner.Start(Work); _ = owner.StopAsync(); })));
        await owner.StopAsync(close: true).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.InRange(max, 0, 1); Assert.Equal(0, active); Assert.Equal(0, faults);
    }
    [Fact]
    public async Task ProviderSwitchUpdatesPendingOnlyWithoutParallelFanoutOrHistoryReset()
    {
        var entered = Signal<bool>(); var release = Signal<string>(); var next = Signal<TranslateApiKind>(); int count = 0;
        var pump = new LiveTranslationPump(async (_, api, token) =>
        {
            if (Interlocked.Increment(ref count) == 1) { entered.SetResult(true); return await release.Task.WaitAsync(token); }
            next.TrySetResult(api.Kind); await Task.Delay(Timeout.Infinite, token); return "";
        }, action => action(), TimeSpan.Zero);
        pump.Start(); pump.Enqueue(Work(1, "first")); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pump.Enqueue(Work(2, "second")); pump.ChangeProvider(new(TranslateApiKind.Azure, null, null, "FAKE")); release.SetResult("一");
        Assert.Equal(TranslateApiKind.Azure, await next.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await pump.StopAsync(true); Assert.Equal(2, count);
    }
    [Fact]
    public async Task CancelRegistrationCanConsultOwnerFromAnotherThreadWithoutLockInversion()
    {
        var owner = new AsyncSessionOwner(); var entered = Signal<bool>(); var checkedOwner = Signal<bool>();
        owner.Start(async (epoch, token) =>
        {
            using var registration = token.Register(() =>
            {
                bool current = Task.Run(() => owner.IsCurrent(epoch)).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                checkedOwner.SetResult(current);
            });
            entered.SetResult(true);
            await Task.Delay(Timeout.Infinite, token);
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Run(() => owner.StopAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await checkedOwner.Task);
    }

    [Fact]
    public async Task NetworkQueueCannotGrowBeyondFortyCaptionsDuringBlockedRequest()
    {
        var entered = Signal<bool>();
        var pump = new LiveTranslationPump(async (_, _, token) =>
        { entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); return ""; }, action => action(), TimeSpan.Zero);
        pump.Start(); pump.Enqueue(Work(1, "blocked")); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 2; i < 2000; i++) pump.Enqueue(Work(i, $"Caption {i}"));
        Assert.Equal(40, pump.PendingCount);
        await pump.StopAsync(true).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void ShortNewPartialCannotReopenAndThenReplaceAnAlreadyFinalizedPreviousThought()
    {
        var chain = new LiveTranslateCaptionChain();
        const string previous = "The previous complete thought has no sentence punctuation";
        chain.AcceptUtterance(1, previous, Origin, true);
        chain.AcceptUtterance(2, "Next", Origin.AddSeconds(16), false);
        chain.AcceptUtterance(2, "Next subject starts with a different idea", Origin.AddSeconds(17), false);
        Assert.Equal(2, chain.Items.Count);
        Assert.Equal(previous, chain.Items[0].Primary);
        Assert.False(chain.Items[0].Open);
    }

    [Fact]
    public async Task RapidRestartsCoalesceToOnePendingOwnerInsteadOfAnUnboundedTaskChain()
    {
        var owner = new AsyncSessionOwner(); var entered = Signal<bool>(); var release = Signal<bool>(); var last = Signal<bool>(); int starts = 0;
        owner.Start(async (_, _) => { entered.SetResult(true); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 1000; i++)
        {
            bool latest = i == 999;
            owner.Start((_, _) => { Interlocked.Increment(ref starts); if (latest) last.SetResult(true); return Task.CompletedTask; });
        }
        Assert.Equal(1, owner.PendingStarts); release.SetResult(true);
        await last.Task.WaitAsync(TimeSpan.FromSeconds(5)); await owner.StopAsync(true);
        Assert.Equal(1, starts);
    }

    [Fact]
    public void UnchangedFinalAfterCompletedTranslationNeedsNoNewRequestButCorrectionsDo()
    {
        var chain = new LiveTranslateCaptionChain();
        var edit = chain.AcceptUtterance(1, "This is a complete thought", Origin, false);
        chain.Complete(edit.Id, "完整译文", edit.Recognized);
        chain.AcceptUtterance(1, edit.Recognized, Origin, true);
        Assert.False(chain.NeedsTranslation(edit.Id));
        chain.AcceptUtterance(1, "This is a corrected complete thought", Origin, true);
        Assert.True(chain.NeedsTranslation(edit.Id));
    }

    [Fact]
    public async Task PumpStopDoesNotHoldQueueLockDuringCancellationCallbacks()
    {
        LiveTranslationPump? pump = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump = new(async (_, _, token) =>
        {
            using var registration = token.Register(() =>
            {
                var probe = Task.Run(() => pump!.PendingCount);
                if (probe.Wait(TimeSpan.FromSeconds(2))) inspected.TrySetResult();
            });
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return "unused";
        }, action => action(), TimeSpan.Zero);
        pump.Start();
        pump.Enqueue(Work(1, "original"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await pump.StopAsync(true).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(inspected.Task.IsCompletedSuccessfully);
    }

}
