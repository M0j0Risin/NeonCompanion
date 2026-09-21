using System.Threading.Channels;
using NeonCompanion.Audio;
using NeonCompanion.Diagnostics;
using NeonCompanion.Llm;

namespace NeonCompanion.Speech;

/// <summary>
/// The interrupt's second echo guard: the assistant's own audio, run through the same keyword
/// recogniser the microphone is listened to with, <em>before</em> it reaches the speaker.
///
/// <para>The phrase-only grammar (<c>["velora","[unk]"]</c>) is what makes the phrase decodable
/// under the speakers, and it is also why any stretch of the assistant's own voice that sounds
/// close enough is decoded as the phrase: "the veil or a mask" came back as "velora", then "and on a
/// Friday" did. Spelling is a poor proxy for what the decoder will confuse, so instead the same
/// decoder is asked: every byte <see cref="SpeechOutput"/> hands the device is copied here, resampled
/// to the recogniser's rate and fed to a <see cref="IWakeWordDetector.Fork"/> in
/// <see cref="WakeDetectorMode.Keyword"/>; a result that contains the phrase records a mark at the
/// point of the written stream where the phrase appeared — once it has persisted as the
/// listener's own confirm window requires (a final at once), so a one-slice flicker the
/// listener would ride out leaves no dead zone. Synthesis runs ahead of playback, so the marks
/// are usually in place before the audio is heard. <see cref="HeardNear"/> then answers the
/// guard: is there a mark within a window around the play head?</para>
///
/// <para>Runs on its own pool task, fed through a queue, so a slow recogniser never delays the
/// bytes going to the device. Never touches the console; a failure is logged once and the probe
/// goes quiet (the text guard still stands). <see cref="Dispose"/> ends the task without waiting.</para>
/// </summary>
internal sealed class EchoProbe : IDisposable
{
    private const string Category = "Voice";

    /// <summary>
    /// Audio is fed in uniform slices this long — the microphone's own buffer size — carried
    /// across chunks, so a mark lands near the phrase and the persistence rule counts the same
    /// audio the listener's does. (Sliced per chunk, a Kokoro chunk of 20 ms made a "slice" of
    /// 20 ms and a one-partial flicker spanning two of them passed for persistence: the third
    /// field log, nine such marks over one reply, each a dead zone.)
    /// </summary>
    public static readonly TimeSpan SliceLength = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How far behind the play head <see cref="HeardNear"/> looks by default. A mark sits at the
    /// end of the slice in which the phrase was decoded, so a little after the phrase; the
    /// microphone's hit lands after the device's latency, the decoder's lag and the confirm
    /// window — measured in the field at 0.3–0.9 s past the mark. Two seconds covers a slow
    /// device; more would only take real interruptions away.
    /// </summary>
    public static readonly TimeSpan DefaultLookBack = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How far ahead of the play head <see cref="HeardNear"/> looks by default: the probe's
    /// partial lags the phrase by up to a slice or two of decoding, and the microphone's hit
    /// arrives after the device's latency plus the listener's confirmation, so a mark can sit a
    /// little past where the device reports it is.
    /// </summary>
    public static readonly TimeSpan DefaultLookAhead = TimeSpan.FromSeconds(1);

    private readonly IWakeWordDetector _detector;
    private readonly string _phrase;
    private readonly LinearResampler _resampler;
    private readonly Channel<(byte[] Pcm, long StreamEnd)> _queue = Channel.CreateUnbounded<(byte[], long)>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly object _gate = new();
    private readonly List<long> _marks = new();
    private readonly CancellationTokenSource _stop = new();
    private int _failureLogged;
    private bool _disposed;

    /// <param name="detector">A fork of the interrupt's detector; reset here in keyword mode, owned by the caller.</param>
    /// <param name="phrase">The wake phrase.</param>
    /// <param name="source">The format of the audio that will be fed (the playback's).</param>
    /// <param name="confirm">
    /// The listener's confirm window: a partial marks once the phrase has persisted for this
    /// long less one slice (the probe's slices are coarser than the microphone's buffers, and a
    /// mark that arrives one slice early costs nothing). Null = <see cref="WakeListener.KeywordConfirm"/>.
    /// </param>
    public EchoProbe(IWakeWordDetector detector, string phrase, PcmFormat source, TimeSpan? confirm = null)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _phrase = WakeWordMatch.NormalizePhrase(phrase);
        if (_phrase.Length == 0)
        {
            throw new ArgumentException("The wake phrase must not be empty.", nameof(phrase));
        }

        var window = confirm ?? WakeListener.KeywordConfirm;
        if (window < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(confirm), window, "The confirm window cannot be negative.");
        }

        Confirm = window;
        _persistBytes = PcmFormat.Whisper.BytesFor((int)Math.Max(0, (window - SliceLength).TotalMilliseconds));
        Source = source;
        _resampler = new LinearResampler(source, PcmFormat.Whisper);
        _detector.Reset(WakeDetectorMode.Keyword, _phrase);
        // The token is taken here, once: Dispose frees the source, and a token already in hand
        // keeps answering after that.
        var stop = _stop.Token;
        Completion = Task.Run(() => RunAsync(stop));
    }

    public PcmFormat Source { get; }

    public string Phrase => _phrase;

    /// <summary>The confirm window the persistence rule was derived from.</summary>
    public TimeSpan Confirm { get; }

    /// <summary>How many resampled bytes a partial must persist through before it marks: the confirm window less one slice.</summary>
    private readonly int _persistBytes;

    /// <summary>The partial that contains the phrase and is waiting out the persistence, its stream position, and the detector position it appeared at.</summary>
    private WakeUtterance? _pending;
    private long _pendingPosition;
    private long _pendingSince;

    /// <summary>Completes when the queue is drained after <see cref="Complete"/>, or at once after <see cref="Dispose"/>. Never faults.</summary>
    public Task Completion { get; }

    /// <summary>Positions in the written stream (bytes, the source format) at which the assistant's own audio decoded as the phrase. Oldest first.</summary>
    public IReadOnlyList<long> Marks
    {
        get
        {
            lock (_gate)
            {
                return _marks.ToArray();
            }
        }
    }

    /// <summary>
    /// Queues a copy of the first <paramref name="count"/> bytes of <paramref name="pcm"/>, which end
    /// at <paramref name="streamEnd"/> in the written stream. The speech consumer's thread; returns at once.
    /// </summary>
    public void Feed(byte[] pcm, int count, long streamEnd)
    {
        if (pcm is null || count <= 0 || _disposed)
        {
            return;
        }

        count = Math.Min(count, pcm.Length);
        var copy = new byte[count];
        Buffer.BlockCopy(pcm, 0, copy, 0, count);
        _queue.Writer.TryWrite((copy, streamEnd));
    }

    /// <summary>No more audio this turn; the task finishes what is queued and ends.</summary>
    public void Complete() => _queue.Writer.TryComplete();

    /// <summary>
    /// Whether a mark lies in <c>[played − lookBack, played + lookAhead]</c>, with <paramref name="mark"/>
    /// the newest such position. Any thread; arithmetic under one short lock.
    /// </summary>
    public bool HeardNear(long played, TimeSpan lookBack, TimeSpan lookAhead, out long mark)
    {
        long from = played - Math.Max(0, Source.BytesFor((int)Math.Min(int.MaxValue, lookBack.TotalMilliseconds)));
        long to = played + Math.Max(0, Source.BytesFor((int)Math.Min(int.MaxValue, lookAhead.TotalMilliseconds)));
        lock (_gate)
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                if (_marks[i] >= from && _marks[i] <= to)
                {
                    mark = _marks[i];
                    return true;
                }
            }
        }

        mark = -1;
        return false;
    }

    /// <summary>A stream position as seconds of audio, for the log line.</summary>
    public double Seconds(long position) => Source.BytesPerSecond > 0 ? position / (double)Source.BytesPerSecond : 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Writer.TryComplete();
        _stop.Cancel();
        _stop.Dispose();
    }

    /// <summary>Resampled audio not yet fed: less than one slice, waiting for the next chunk.</summary>
    private readonly MemoryStream _carry = new();

    /// <summary>The written-stream position the first chunk started at, and how many resampled bytes have been fed since; together they place a slice in the stream.</summary>
    private long _origin = -1;
    private long _targetFed;
    private long _latestEnd;

    private async Task RunAsync(CancellationToken stop)
    {
        try
        {
            int sliceBytes = Math.Max(PcmFormat.Whisper.BlockAlign, PcmFormat.Whisper.BytesFor((int)SliceLength.TotalMilliseconds));
            double sourcePerTarget = (double)Source.BytesPerSecond / PcmFormat.Whisper.BytesPerSecond;
            var reader = _queue.Reader;
            while (await reader.WaitToReadAsync(stop).ConfigureAwait(false))
            {
                while (reader.TryRead(out var item))
                {
                    if (stop.IsCancellationRequested)
                    {
                        return;
                    }

                    Probe(item.Pcm, item.StreamEnd, sliceBytes, sourcePerTarget);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed mid-queue.
        }
        catch (Exception ex)
        {
            LogFailureOnce(ex);
        }
    }

    /// <summary>
    /// One chunk: resample, then feed slice by slice. The listener's rule, mirrored: a final with
    /// the phrase marks at once; a partial with it is held as pending from the slice it appeared
    /// in, a null ("unchanged") keeps it, a result without the phrase clears it, and it marks once
    /// the persistence is met — at the position it appeared, which is nearest the phrase.
    /// </summary>
    private void Probe(byte[] pcm, long streamEnd, int sliceBytes, double sourcePerTarget)
    {
        byte[] resampled;
        try
        {
            resampled = _resampler.Resample(pcm, pcm.Length);
        }
        catch (Exception ex)
        {
            LogFailureOnce(ex);
            return;
        }

        if (_origin < 0)
        {
            _origin = streamEnd - pcm.Length;
        }

        _latestEnd = streamEnd;
        _carry.Write(resampled, 0, resampled.Length);
        if (_carry.Length < sliceBytes)
        {
            return;
        }

        // Whole slices only; the remainder waits for the next chunk (and is dropped at the end of the turn).
        var buffered = _carry.GetBuffer();
        int available = (int)_carry.Length;
        int consumed = 0;
        while (available - consumed >= sliceBytes)
        {
            var slice = new byte[sliceBytes];
            Buffer.BlockCopy(buffered, consumed, slice, 0, sliceBytes);
            consumed += sliceBytes;
            _targetFed += sliceBytes;
            long position = Math.Min(_latestEnd, _origin + (long)(_targetFed * sourcePerTarget));

            WakeUtterance? result;
            try
            {
                result = _detector.Feed(slice, sliceBytes);
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                Rewind(buffered, available, consumed);
                return;
            }

            if (result is not null)
            {
                bool has = WakeWordMatch.Contains(result.Text, _phrase);
                if (!result.IsPartial)
                {
                    _pending = null;
                    if (has)
                    {
                        Mark(position, result);
                    }

                    continue;
                }

                if (!has)
                {
                    _pending = null;
                }
                else if (_pending is null)
                {
                    _pending = result;
                    _pendingPosition = position;
                    _pendingSince = _detector.BytesFed;
                }
            }

            if (_pending is { } pending && _detector.BytesFed - _pendingSince >= _persistBytes)
            {
                _pending = null;
                Mark(_pendingPosition, pending);
            }
        }

        Rewind(buffered, available, consumed);
    }

    /// <summary>Keeps the unfed remainder of the carry, at the front.</summary>
    private void Rewind(byte[] buffered, int available, int consumed)
    {
        int remaining = available - consumed;
        var rest = new byte[remaining];
        Buffer.BlockCopy(buffered, consumed, rest, 0, remaining);
        _carry.SetLength(0);
        _carry.Write(rest, 0, rest.Length);
    }

    private void Mark(long position, WakeUtterance result)
    {
        lock (_gate)
        {
            _marks.Add(position);
        }

        DiagnosticLog.Debug(Category, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Echo probe: the assistant's own audio decodes as \"{result.Text}\" at {Seconds(position):F1}s{(result.IsPartial ? " (partial)" : "")}."));
    }

    private void LogFailureOnce(Exception ex)
    {
        if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
        {
            DiagnosticLog.Warn(Category, "Echo probe failed; the text guard stands alone: " + Assistant.Explain(ex), ex);
        }
    }
}
