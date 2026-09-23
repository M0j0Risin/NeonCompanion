using NeonSidekick.Audio;
using NeonSidekick.Diagnostics;
using NeonSidekick.Llm;

namespace NeonSidekick.Speech;

/// <summary>What the listener heard: the recogniser's text, the audio to seed the utterance with, and whether the request was already spoken.</summary>
public sealed record WakeHit(string Text, byte[] Seed, bool HasRequest);

/// <summary>
/// The always-on half of the wake word: while armed it keeps the microphone open, keeps the last
/// few seconds in a <see cref="PreRollBuffer"/>, feeds every buffer to the detector, and on a
/// final result containing the phrase completes <see cref="Detected"/> with the trimmed pre-roll
/// as the seed. One utterance's worth of state; <see cref="Arm"/> starts over.
///
/// <para><b>Everything on the capture thread is a write and a signal.</b> <c>OnData</c> copies
/// into the pre-roll, calls <see cref="IWakeWordDetector.Feed"/> and, on a hit, completes a
/// <see cref="TaskCompletionSource{T}"/> created with <c>RunContinuationsAsynchronously</c>. It
/// never stops the capture (that joins the pump thread it is standing on), never touches the
/// console, and never cancels a token inline (a token's callbacks run on the cancelling thread,
/// and the input line's continuation would then end the row and stop the device from the pump).
/// <see cref="Disarm"/> runs on the session's thread and is the one that stops the device.</para>
///
/// <para>The detector reports final results only, which Vosk settles on roughly half a second
/// after the phrase; the user is silent during the hand-off, so the audio lost between
/// <see cref="Disarm"/> closing the device and the pipeline reopening it is nothing anyone said.
/// Vosk's small model costs some 5–20 ms per 50 ms buffer here, inside the capture's 200 ms of
/// header slack.</para>
/// </summary>
internal sealed class WakeListener : IDisposable
{
    private const string Category = "Voice";

    public const int DefaultPreRollMs = 5000;

    private readonly IAudioCapture _capture;
    private readonly IWakeWordDetector _detector;
    private readonly string _phrase;
    private readonly PreRollBuffer _preRoll;
    private readonly object _gate = new();

    private TaskCompletionSource<WakeHit>? _detected;
    private WakeHit? _hit;
    private Func<WakeUtterance, bool>? _ignore;
    private int _armed;
    private bool _subscribed;
    private bool _disposed;
    private int _feedFailureLogged;
    private int _ignoreFailureLogged;

    /// <param name="capture">The microphone, shared with the utterance pipeline; started by <see cref="Arm"/>, stopped by <see cref="Disarm"/>.</param>
    /// <param name="detector">A loaded detector.</param>
    /// <param name="phrase">The wake phrase, normalised by <see cref="WakeWordMatch.NormalizePhrase"/>.</param>
    /// <param name="preRollMs">How much audio before the hit the seed can reach back to.</param>
    public WakeListener(IAudioCapture capture, IWakeWordDetector detector, string phrase, int preRollMs = DefaultPreRollMs)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _phrase = WakeWordMatch.NormalizePhrase(phrase);
        if (_phrase.Length == 0)
        {
            throw new ArgumentException("The wake phrase must not be empty.", nameof(phrase));
        }

        _preRoll = new PreRollBuffer(preRollMs, capture.Format);
    }

    public string Phrase => _phrase;

    public int PreRollMs => _preRoll.CaptureMilliseconds;

    /// <summary>Whether it is listening now (armed and not yet hit).</summary>
    public bool IsArmed => Volatile.Read(ref _armed) == 1;

    /// <summary>
    /// Completes with the hit, on the thread pool, never inside the capture callback; cancelled by
    /// <see cref="Disarm"/> when nothing was heard. A fresh task per <see cref="Arm"/>.
    /// </summary>
    public Task<WakeHit> Detected => _detected?.Task ?? Task.FromCanceled<WakeHit>(new CancellationToken(canceled: true));

    /// <summary>
    /// Forgets the previous utterance, resets the detector and the pre-roll, subscribes and opens
    /// the microphone. <paramref name="ignore"/>, when given, is asked (on the capture thread)
    /// whether a matching result should be dropped; a dropped result does not consume the hit
    /// and listening continues (the interrupt's echo guard: the assistant just said the phrase
    /// itself). <paramref name="keywordConfirm"/> is how long a keyword-mode partial must hold the
    /// phrase before it counts (<see cref="KeywordConfirm"/> when null). Throws what
    /// <see cref="IAudioCapture.Start"/> throws, with nothing left subscribed.
    /// Throws <see cref="InvalidOperationException"/> when already armed or disposed.
    /// </summary>
    public void Arm(Func<WakeUtterance, bool>? ignore = null, WakeDetectorMode mode = WakeDetectorMode.Utterance, TimeSpan? keywordConfirm = null)
    {
        if (keywordConfirm is { } confirm && confirm < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(keywordConfirm), confirm, "The confirm window cannot be negative.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_subscribed)
            {
                throw new InvalidOperationException("Already armed.");
            }

            _ignore = ignore;
            _mode = mode;
            _confirm = keywordConfirm ?? KeywordConfirm;
            _pending = null;
            _hit = null;
            _detected = new TaskCompletionSource<WakeHit>(TaskCreationOptions.RunContinuationsAsynchronously);
            _detector.Reset(mode, _phrase);
            _preRoll.Reset();
            _capture.DataAvailable += OnData;
            _subscribed = true;
            Volatile.Write(ref _armed, 1);
        }

        try
        {
            _capture.Start();
        }
        catch
        {
            lock (_gate)
            {
                Volatile.Write(ref _armed, 0);
                _capture.DataAvailable -= OnData;
                _subscribed = false;
                _detected?.TrySetCanceled();
            }

            throw;
        }
    }

    /// <summary>
    /// Stops listening: unsubscribes, stops the microphone (joining the pump, so no callback runs
    /// after this returns) and returns the hit, or null when nothing was heard (then
    /// <see cref="Detected"/> is cancelled). Safe to call when not armed.
    /// </summary>
    public WakeHit? Disarm()
    {
        TaskCompletionSource<WakeHit>? detected;
        WakeHit? hit;
        bool wasSubscribed;
        lock (_gate)
        {
            Volatile.Write(ref _armed, 0);
            wasSubscribed = _subscribed;
            if (wasSubscribed)
            {
                _capture.DataAvailable -= OnData;
                _subscribed = false;
            }

            detected = _detected;
            hit = _hit;
        }

        if (wasSubscribed)
        {
            _capture.Stop();
        }

        detected?.TrySetCanceled();
        return hit;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Disarm();
    }

    /// <summary>The mode of the current arm; a partial result counts only in <see cref="WakeDetectorMode.Keyword"/>.</summary>
    private WakeDetectorMode _mode;

    /// <summary>The capture thread: pre-roll first, then the detector, then at most one signal.</summary>
    private void OnData(byte[] pcm, int count)
    {
        if (pcm is null || count <= 0 || !IsArmed)
        {
            return;
        }

        count = Math.Min(count, pcm.Length);
        _preRoll.Write(pcm, 0, count);

        WakeUtterance? utterance;
        try
        {
            utterance = _detector.Feed(pcm, count);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _feedFailureLogged, 1) == 0)
            {
                DiagnosticLog.Warn(Category, "Wake-word detection threw: " + Assistant.Explain(ex), ex);
            }

            return;
        }

        if (_mode == WakeDetectorMode.Keyword)
        {
            // A partial is interim: the decoder labels a fragment of the assistant's own speech
            // "neon" for a buffer or two and takes it back (the lab measured every such flicker at
            // 250 ms or less over three spoken stories); a real "neon" stays in the partial until
            // the final. So a partial counts only once the phrase has persisted for
            // KeywordConfirm; a final that contains it is committed and counts at once. A null
            // from the detector means "unchanged", which keeps a pending phrase pending.
            if (utterance is not null)
            {
                bool has = WakeWordMatch.Contains(utterance.Text, _phrase);
                if (!utterance.IsPartial)
                {
                    _pending = null;
                    if (has)
                    {
                        TryFire(utterance);
                    }

                    return;
                }

                if (!has)
                {
                    if (_pending is not null)
                    {
                        // The decoder took the phrase back before the confirm window: a flicker. Its length is
                        // what the window has to beat on this microphone, so it goes to --log.
                        long held = _detector.BytesFed - _pendingSince;
                        DiagnosticLog.Debug(Category, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Wake phrase retracted after {held * 1000 / Math.Max(1, _capture.Format.BytesPerSecond)} ms: \"{_pending.Text}\" → \"{utterance.Text}\"."));
                    }

                    _pending = null;
                }
                else if (_pending is null)
                {
                    _pending = utterance;
                    _pendingSince = _detector.BytesFed;
                }
            }

            if (_pending is { } pending && _detector.BytesFed - _pendingSince >= _capture.Format.BytesFor((int)_confirm.TotalMilliseconds))
            {
                _pending = null;
                TryFire(pending);
            }

            return;
        }

        if (utterance is null || utterance.IsPartial || !WakeWordMatch.Contains(utterance.Text, _phrase))
        {
            // Utterance mode wants the final: its timings trim the seed and its full text decides
            // whether the request was spoken with the phrase. A detector that reports partials
            // anyway is not trusted with the hit.
            return;
        }

        TryFire(utterance);
    }

    /// <summary>
    /// How long the phrase must persist in keyword-mode partials before it counts, when
    /// <see cref="Arm"/> is given nothing else: the lab's false positives (fragments of the
    /// assistant's own speech) lasted 250 ms at most, so a listener with no other guard needs
    /// this much. The app passes its <c>SttInterruptConfirmMs</c> setting instead (default 200 ms):
    /// with the echo probe covering the assistant's own voice, a shorter window lets a real
    /// phrase the decoder holds for about 200 ms fire.
    /// </summary>
    public static readonly TimeSpan KeywordConfirm = TimeSpan.FromMilliseconds(400);

    /// <summary>The confirm window of the current arm.</summary>
    private TimeSpan _confirm = KeywordConfirm;

    /// <summary>The confirm window of the current arm, for tests.</summary>
    public TimeSpan Confirm => _confirm;

    /// <summary>The partial that contains the phrase and is waiting out <see cref="KeywordConfirm"/>, and the stream position it appeared at.</summary>
    private WakeUtterance? _pending;
    private long _pendingSince;

    /// <summary>The guard, the one-hit latch, the hit. Capture thread.</summary>
    private void TryFire(WakeUtterance utterance)
    {
        if (_ignore is { } ignore)
        {
            bool ignored;
            try
            {
                ignored = ignore(utterance);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _ignoreFailureLogged, 1) == 0)
                {
                    DiagnosticLog.Warn(Category, "Wake-word guard threw; the hit stands: " + Assistant.Explain(ex), ex);
                }

                ignored = false;
            }

            if (ignored)
            {
                DiagnosticLog.Info(Category, $"Wake word ignored (the assistant said it): \"{utterance.Text}\".");
                return;
            }
        }

        // First hit wins; later buffers are ignored until the next Arm.
        if (Interlocked.CompareExchange(ref _armed, 0, 1) != 1)
        {
            return;
        }

        var hit = BuildHit(utterance, _detector.BytesFed, _preRoll, _phrase, _capture.Format);
        TaskCompletionSource<WakeHit>? detected;
        lock (_gate)
        {
            _hit = hit;
            detected = _detected;
        }

        DiagnosticLog.Info(Category, $"Wake word heard{(_mode == WakeDetectorMode.Keyword ? " (the interrupt)" : "")}: \"{utterance.Text}\" ({hit.Seed.Length / (double)_capture.Format.BytesPerSecond:F1}s seed{(hit.HasRequest ? ", request included" : "")}).");
        detected?.TrySetResult(hit);
    }

    /// <summary>Trims the pre-roll to start just before the phrase (when the timings allow) and drains it into the seed. Pure apart from the drain.</summary>
    public static WakeHit BuildHit(WakeUtterance utterance, long bytesFed, PreRollBuffer preRoll, string phrase, PcmFormat format)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        ArgumentNullException.ThrowIfNull(preRoll);
        int discard = WakeWordMatch.SeedDiscard(WakeWordMatch.WakeStart(utterance.Words, phrase), bytesFed, preRoll.ByteCount, format);
        preRoll.DiscardOldest(discard);
        return new WakeHit(utterance.Text, preRoll.Flush(), WakeWordMatch.HasRequest(utterance.Text, phrase));
    }
}
