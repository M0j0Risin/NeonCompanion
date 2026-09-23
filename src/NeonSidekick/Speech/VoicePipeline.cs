using System.Diagnostics;
using NeonSidekick.Audio;
using NeonSidekick.Diagnostics;
using NeonSidekick.Llm;

namespace NeonSidekick.Speech;

/// <summary>What ended the listening phase.</summary>
public enum ListenEnd
{
    /// <summary>The VAD heard enough trailing silence.</summary>
    EndOfSpeech,

    /// <summary>The caller's finish-early token (the push-to-talk key or Enter).</summary>
    Key,

    /// <summary>No speech within the no-speech timeout; nothing is transcribed.</summary>
    NoSpeech,

    /// <summary>The hard cap on an utterance; what was captured is transcribed.</summary>
    MaxDuration,

    /// <summary>The microphone delivered nothing at all within the watchdog.</summary>
    NoAudio,

    /// <summary>The request arrived with the wake word: the seed was transcribed without opening the microphone.</summary>
    Seeded,
}

/// <summary>One listen: what was heard, how it ended, and how long it took.</summary>
/// <param name="Ok">False when capture or recognition failed; <paramref name="Detail"/> says why.</param>
/// <param name="Text">The cleaned transcript; empty when nothing usable was heard.</param>
/// <param name="RawText">What the recognizer returned before cleaning.</param>
/// <param name="Audio">How much audio was captured.</param>
/// <param name="Elapsed">How long transcription took; when nothing was transcribed, how long listening took.</param>
public sealed record ListenResult(bool Ok, string Text, string RawText, ListenEnd EndedBy, TimeSpan Audio, TimeSpan Elapsed, string Detail)
{
    /// <summary>Listening succeeded but there is nothing to send.</summary>
    public bool HeardNothing => Ok && Text.Length == 0;

    public static ListenResult Failed(ListenEnd endedBy, TimeSpan audio, TimeSpan elapsed, string detail) =>
        new(false, "", "", endedBy, audio, elapsed, detail);
}

/// <param name="NoSpeechTimeout">Give up when the VAD has heard no speech by then.</param>
/// <param name="MaxUtterance">Hard cap; what was captured is transcribed.</param>
/// <param name="DeliveryWatchdog">Fail when the microphone has delivered no buffer at all by then.</param>
public sealed record VoicePipelineOptions(TimeSpan NoSpeechTimeout, TimeSpan MaxUtterance, TimeSpan DeliveryWatchdog)
{
    public static VoicePipelineOptions Default { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
}

/// <summary>
/// One utterance: open the microphone, accumulate until the VAD (or a key, or a timer) says the
/// utterance is over, close the microphone, transcribe. The wake word (M5)
/// arms the same call with a <em>seed</em>: the pre-roll audio holding the phrase and whatever
/// followed it, written ahead of the live audio and never fed to the VAD (fed, the phrase is a
/// finished segment and the VAD ends the turn before the user has said anything). When the
/// request was spoken with the phrase the caller passes an already-cancelled finish token and
/// the seed is transcribed as it is (<see cref="ListenEnd.Seeded"/>), the microphone untouched.
///
/// <para><b>The microphone is stopped before anything else happens</b>: <see cref="ListenAsync"/>'s
/// <c>finally</c> calls <see cref="IAudioCapture.Stop"/> first, which joins the pump thread, and
/// only then unsubscribes and transcribes. The capture thread runs <c>OnData</c> (copy, feed the
/// VAD, complete the end signal) and never touches the console; the recognizer runs on the
/// thread pool. The end signal's continuations run asynchronously so the stop never executes
/// on the pump thread it is joining.</para>
///
/// <para>The delivery watchdog exists because a microphone that Windows privacy settings have
/// closed to desktop apps opens fine and delivers nothing; without it the user waits ten seconds
/// for "heard nothing" and blames the model.</para>
/// </summary>
internal sealed class VoicePipeline
{
    private const string Category = "Voice";

    public const string TranscribingLabel = "transcribing…";

    public const string NoAudioDetail =
        "the microphone delivered no audio in 2 s; check that it is not muted or in use by another app, " +
        "and that Windows Settings > Privacy & security > Microphone allows desktop apps";

    private readonly IAudioCapture _capture;
    private readonly IVoiceActivityDetector _vad;
    private readonly ISpeechRecognizer _recognizer;
    private readonly VoicePipelineOptions _options;
    private int _listening;

    public VoicePipeline(IAudioCapture capture, IVoiceActivityDetector vad, ISpeechRecognizer recognizer, VoicePipelineOptions? options = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _vad = vad ?? throw new ArgumentNullException(nameof(vad));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _options = options ?? VoicePipelineOptions.Default;
    }

    public bool IsListening => Volatile.Read(ref _listening) == 1;

    public VoicePipelineOptions Options => _options;

    /// <summary>
    /// Listens for one utterance and transcribes it. <paramref name="finishEarly"/> ends the
    /// listening phase and transcribes what there is; <paramref name="cancellationToken"/>
    /// abandons everything and propagates as <see cref="OperationCanceledException"/> (the
    /// microphone is stopped either way). <paramref name="phase"/> is told when transcription
    /// starts. <paramref name="seed"/> is audio that precedes the live capture (the wake word's
    /// pre-roll); with <paramref name="finishEarly"/> already cancelled it is transcribed alone.
    /// Throws <see cref="InvalidOperationException"/> when already listening.
    /// </summary>
    public async Task<ListenResult> ListenAsync(CancellationToken finishEarly, Action<string>? phase, CancellationToken cancellationToken, byte[]? seed = null)
    {
        if (Interlocked.CompareExchange(ref _listening, 1, 0) != 0)
        {
            throw new InvalidOperationException("Already listening.");
        }

        try
        {
            return await ListenCoreAsync(finishEarly, phase, cancellationToken, seed).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _listening, 0);
        }
    }

    private async Task<ListenResult> ListenCoreAsync(CancellationToken finishEarly, Action<string>? phase, CancellationToken cancellationToken, byte[]? seed)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        int maxBytes = _capture.Format.BytesFor((int)_options.MaxUtterance.TotalMilliseconds);

        if (seed is { Length: > 0 } && finishEarly.IsCancellationRequested)
        {
            // The request was spoken with the wake word: nothing to listen for.
            var spoken = seed.Length <= maxBytes ? seed : seed[..maxBytes];
            return await TranscribeAsync(spoken, ListenEnd.Seeded, clock, phase, cancellationToken).ConfigureAwait(false);
        }

        var utterance = new MemoryStream();
        if (seed is { Length: > 0 })
        {
            utterance.Write(seed, 0, Math.Min(seed.Length, maxBytes));
        }

        var ended = new TaskCompletionSource<ListenEnd>(TaskCreationOptions.RunContinuationsAsynchronously);
        int anyBuffer = 0;
        int anySpeech = 0;
        int vadFailureLogged = 0;

        void OnData(byte[] pcm, int count)
        {
            if (pcm is null || count <= 0)
            {
                return;
            }

            Volatile.Write(ref anyBuffer, 1);
            count = Math.Min(count, pcm.Length);
            lock (utterance)
            {
                int room = (int)Math.Min(count, maxBytes - utterance.Length);
                if (room > 0)
                {
                    utterance.Write(pcm, 0, room);
                }

                if (utterance.Length >= maxBytes)
                {
                    ended.TrySetResult(ListenEnd.MaxDuration);
                }
            }

            VadVerdict verdict;
            try
            {
                verdict = _vad.Feed(pcm, count);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref vadFailureLogged, 1) == 0)
                {
                    DiagnosticLog.Warn(Category, "Voice activity detection threw: " + Assistant.Explain(ex), ex);
                }

                return;
            }

            if (verdict == VadVerdict.Speaking)
            {
                Volatile.Write(ref anySpeech, 1);
            }
            else if (verdict == VadVerdict.EndOfSpeech)
            {
                Volatile.Write(ref anySpeech, 1);
                ended.TrySetResult(ListenEnd.EndOfSpeech);
            }
        }

        _vad.Reset();
        _capture.DataAvailable += OnData;
        using var timers = new CancellationTokenSource();
        ListenEnd end;
        try
        {
            using var finishRegistration = finishEarly.Register(() => ended.TrySetResult(ListenEnd.Key));
            using var cancelRegistration = cancellationToken.Register(() => ended.TrySetCanceled(cancellationToken));

            try
            {
                _capture.Start();
            }
            catch (Exception ex)
            {
                return ListenResult.Failed(ListenEnd.NoAudio, TimeSpan.Zero, clock.Elapsed, "microphone unavailable: " + Assistant.Explain(ex));
            }

            _ = AfterAsync(_options.DeliveryWatchdog, () => Volatile.Read(ref anyBuffer) == 0, ListenEnd.NoAudio, ended, timers.Token);
            _ = AfterAsync(_options.NoSpeechTimeout, () => Volatile.Read(ref anySpeech) == 0, ListenEnd.NoSpeech, ended, timers.Token);
            _ = AfterAsync(_options.MaxUtterance, () => true, ListenEnd.MaxDuration, ended, timers.Token);

            end = await ended.Task.ConfigureAwait(false);
        }
        finally
        {
            // Stop first: it joins the pump, so no OnData runs after this line.
            _capture.Stop();
            _capture.DataAvailable -= OnData;
            timers.Cancel();
        }

        byte[] pcm;
        lock (utterance)
        {
            pcm = utterance.ToArray();
        }

        var audio = TimeSpan.FromSeconds(pcm.Length / (double)_capture.Format.BytesPerSecond);
        DiagnosticLog.Debug(Category, $"Listening ended by {end} after {audio.TotalSeconds:F1}s of audio.");

        switch (end)
        {
            case ListenEnd.NoAudio:
                return ListenResult.Failed(end, audio, clock.Elapsed, NoAudioDetail);
            case ListenEnd.NoSpeech:
                return new ListenResult(true, "", "", end, audio, clock.Elapsed, "no speech");
        }

        if (pcm.Length == 0)
        {
            return new ListenResult(true, "", "", end, audio, clock.Elapsed, "no audio captured");
        }

        return await TranscribeAsync(pcm, end, clock, phase, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ListenResult> TranscribeAsync(byte[] pcm, ListenEnd end, Stopwatch clock, Action<string>? phase, CancellationToken cancellationToken)
    {
        var audio = TimeSpan.FromSeconds(pcm.Length / (double)_capture.Format.BytesPerSecond);
        cancellationToken.ThrowIfCancellationRequested();
        phase?.Invoke(TranscribingLabel);
        var recognised = await _recognizer.TranscribeAsync(pcm, cancellationToken).ConfigureAwait(false);
        if (!recognised.Ok)
        {
            return ListenResult.Failed(end, audio, clock.Elapsed, recognised.Detail);
        }

        string text = SpeechTranscript.Clean(recognised.Text);
        return new ListenResult(true, text, recognised.Text, end, audio, recognised.Elapsed, recognised.Detail);
    }

    private static async Task AfterAsync(TimeSpan delay, Func<bool> condition, ListenEnd end, TaskCompletionSource<ListenEnd> ended, CancellationToken stop)
    {
        try
        {
            await Task.Delay(delay, stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (condition())
        {
            ended.TrySetResult(end);
        }
    }
}
