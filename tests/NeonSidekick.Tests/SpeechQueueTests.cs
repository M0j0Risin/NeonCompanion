using System.Collections.Concurrent;
using System.Diagnostics;
using NeonSidekick.Speech;

namespace NeonSidekick.Tests;

/// <summary>Exercised with a fake speak delegate — no server, no device.</summary>
public class SpeechQueueTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Chunks_AreSpokenInOrder()
    {
        var spoken = new ConcurrentQueue<string>();
        var queue = new SpeechQueue(
            (text, _) => { spoken.Enqueue(text); return Task.CompletedTask; },
            onChunkStarting: null,
            onCancelled: null,
            CancellationToken.None);

        queue.TryEnqueue("one");
        queue.TryEnqueue("two");
        queue.TryEnqueue("three");
        queue.CompleteAdding();

        await queue.Completion.WaitAsync(Timeout);

        Assert.Equal(new[] { "one", "two", "three" }, spoken.ToArray());
    }

    [Fact]
    public async Task Enqueue_DoesNotBlockWhilePlaybackIsInFlight()
    {
        // The whole point of the class: the producer must keep consuming the model stream while
        // a sentence is still playing.
        var release = new TaskCompletionSource();
        var queue = new SpeechQueue(
            async (_, ct) => await release.Task.WaitAsync(ct),
            onChunkStarting: null,
            onCancelled: null,
            CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(queue.TryEnqueue($"sentence {i}"));
        }

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Enqueue blocked for {stopwatch.ElapsedMilliseconds} ms while playback was pending.");

        release.SetResult();
        queue.CompleteAdding();
        await queue.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Completion_WaitsForEveryChunk()
    {
        var spoken = 0;
        var queue = new SpeechQueue(
            async (_, ct) => { await Task.Delay(20, ct); Interlocked.Increment(ref spoken); },
            onChunkStarting: null,
            onCancelled: null,
            CancellationToken.None);

        for (var i = 0; i < 4; i++)
        {
            queue.TryEnqueue($"s{i}");
        }

        queue.CompleteAdding();
        await queue.Completion.WaitAsync(Timeout);

        Assert.Equal(4, spoken);
    }

    [Fact]
    public async Task OnChunkStarting_FiresOncePerChunkInOrder()
    {
        var started = new ConcurrentQueue<string>();
        var queue = new SpeechQueue(
            (_, _) => Task.CompletedTask,
            chunk => started.Enqueue(chunk),
            onCancelled: null,
            CancellationToken.None);

        queue.TryEnqueue("alpha");
        queue.TryEnqueue("beta");
        queue.CompleteAdding();

        await queue.Completion.WaitAsync(Timeout);

        Assert.Equal(new[] { "alpha", "beta" }, started.ToArray());
    }

    [Fact]
    public async Task Cancellation_AbandonsRemainingChunksAndCompletesNormally()
    {
        using var cts = new CancellationTokenSource();
        var spoken = new ConcurrentQueue<string>();
        var firstSpeaking = new TaskCompletionSource();

        var queue = new SpeechQueue(
            async (text, ct) =>
            {
                spoken.Enqueue(text);
                firstSpeaking.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            },
            onChunkStarting: null,
            onCancelled: null,
            cts.Token);

        for (var i = 0; i < 5; i++)
        {
            queue.TryEnqueue($"s{i}");
        }

        await firstSpeaking.Task.WaitAsync(Timeout);
        cts.Cancel();
        queue.CompleteAdding();

        // Cancellation is not a failure — it must not fault Completion.
        await queue.Completion.WaitAsync(Timeout);
        Assert.True(queue.Completion.IsCompletedSuccessfully);

        // Only the sentence already in flight was ever started.
        Assert.Single(spoken);
    }

    [Fact]
    public async Task Cancellation_InvokesFlushCallback()
    {
        using var cts = new CancellationTokenSource();
        var flushed = false;
        var speaking = new TaskCompletionSource();

        var queue = new SpeechQueue(
            async (_, ct) => { speaking.TrySetResult(); await Task.Delay(TimeSpan.FromSeconds(30), ct); },
            onChunkStarting: null,
            onCancelled: () => flushed = true,
            cts.Token);

        queue.TryEnqueue("hello");
        await speaking.Task.WaitAsync(Timeout);

        cts.Cancel();
        queue.CompleteAdding();
        await queue.Completion.WaitAsync(Timeout);

        Assert.True(flushed, "Cancellation must flush the device — a chunk mid-synthesis would otherwise play afterwards.");
    }

    [Fact]
    public async Task SpeakFailure_SurfacesOnCompletion()
    {
        var queue = new SpeechQueue(
            (_, _) => throw new InvalidOperationException("synth exploded"),
            onChunkStarting: null,
            onCancelled: null,
            CancellationToken.None);

        queue.TryEnqueue("boom");
        queue.CompleteAdding();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await queue.Completion.WaitAsync(Timeout));

        Assert.Equal("synth exploded", ex.Message);
    }

    [Fact]
    public async Task TryEnqueue_AfterCompleteAdding_ReturnsFalse()
    {
        var queue = new SpeechQueue((_, _) => Task.CompletedTask, onChunkStarting: null, onCancelled: null, CancellationToken.None);

        queue.CompleteAdding();
        await queue.Completion.WaitAsync(Timeout);

        Assert.False(queue.TryEnqueue("too late"));
    }

    [Fact]
    public async Task TryEnqueue_IgnoresBlankChunks()
    {
        var spoken = new ConcurrentQueue<string>();
        var queue = new SpeechQueue(
            (text, _) => { spoken.Enqueue(text); return Task.CompletedTask; },
            onChunkStarting: null,
            onCancelled: null,
            CancellationToken.None);

        Assert.False(queue.TryEnqueue("   "));
        Assert.False(queue.TryEnqueue(string.Empty));
        Assert.True(queue.TryEnqueue("real"));
        queue.CompleteAdding();

        await queue.Completion.WaitAsync(Timeout);

        Assert.Equal(new[] { "real" }, spoken.ToArray());
    }
}
