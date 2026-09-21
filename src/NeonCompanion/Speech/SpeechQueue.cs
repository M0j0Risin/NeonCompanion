using System.Threading.Channels;
using NeonCompanion.Diagnostics;

namespace NeonCompanion.Speech;

/// <summary>
/// Sequential speak queue backing streamed responses.
///
/// <para>Sentences are enqueued by the loop consuming the model's output and spoken one at a time
/// by a background consumer. The point of the split is that awaiting synthesis must never stall
/// consumption of the model stream — otherwise generation and speech serialise and nothing is
/// gained over speaking the whole response at the end.</para>
///
/// <para>The channel is unbounded on purpose: a bounded channel with a waiting writer would
/// reintroduce exactly the producer stall this class exists to remove, and a turn's output is at
/// most a few kilobytes.</para>
/// </summary>
internal sealed class SpeechQueue
{
    private const string Category = "SpeechQueue";

    private readonly Channel<string> _channel;
    private readonly Func<string, CancellationToken, Task> _speakAsync;
    private readonly Action<string>? _onChunkStarting;
    private readonly Action? _onCancelled;
    private readonly CancellationToken _ct;

    /// <param name="speakAsync">Speaks one chunk and completes when it has been handed to playback.</param>
    /// <param name="onChunkStarting">Invoked on the consumer immediately before a chunk is spoken.</param>
    /// <param name="onCancelled">
    /// Invoked once the consumer observes cancellation. Used to flush the device: an interruption
    /// that lands while a chunk is still being synthesised would otherwise let that chunk play
    /// after the interruption.
    /// </param>
    /// <param name="ct">The turn token. Cancelling it abandons whatever is still queued.</param>
    public SpeechQueue(
        Func<string, CancellationToken, Task> speakAsync,
        Action<string>? onChunkStarting,
        Action? onCancelled,
        CancellationToken ct)
    {
        _speakAsync = speakAsync ?? throw new ArgumentNullException(nameof(speakAsync));
        _onChunkStarting = onChunkStarting;
        _onCancelled = onCancelled;
        _ct = ct;

        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            // Keep the consumer off the producer's thread, so enqueueing never runs playback.
            AllowSynchronousContinuations = false,
        });

        Completion = Task.Run(ConsumeAsync, CancellationToken.None);
    }

    /// <summary>
    /// Completes when the consumer has drained the queue or observed cancellation. Cancellation
    /// completes this task normally; any other failure faults it so the caller can surface it.
    /// </summary>
    public Task Completion { get; }

    /// <summary>Queues a chunk for speaking. False for a blank chunk or once the queue is closed.</summary>
    public bool TryEnqueue(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return false;
        }

        return _channel.Writer.TryWrite(sentence);
    }

    /// <summary>
    /// Signals that no further chunks will be enqueued. Must be called on every path, including
    /// cancellation, or the consumer never terminates.
    /// </summary>
    public void CompleteAdding() => _channel.Writer.TryComplete();

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var sentence in _channel.Reader.ReadAllAsync(_ct).ConfigureAwait(false))
            {
                if (_ct.IsCancellationRequested)
                {
                    break;
                }

                _onChunkStarting?.Invoke(sentence);
                await _speakAsync(sentence, _ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on interruption. Cancellation is not a failure — anything still queued is
            // abandoned unspoken, which is the whole point of interrupting.
        }
        finally
        {
            if (_ct.IsCancellationRequested)
            {
                try
                {
                    _onCancelled?.Invoke();
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error(Category, $"Cancellation flush failed: {ex.Message}", ex);
                }
            }
        }
    }
}
