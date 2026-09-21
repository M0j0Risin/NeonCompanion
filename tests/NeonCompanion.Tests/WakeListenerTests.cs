using NeonCompanion.Audio;
using NeonCompanion.Diagnostics;
using NeonCompanion.Speech;
using NeonCompanion.Tests.Fakes;

namespace NeonCompanion.Tests;

/// <summary>
/// The listener over a fake capture and a fake detector. Buffers are delivered by hand (or from
/// the capture's <c>OnStart</c> hook, which runs on the pool the way the pump thread would), so
/// nothing depends on timing except the one test about which thread the continuation runs on.
/// </summary>
public class WakeListenerTests : IDisposable
{
    private const int Buffer50 = 1600;   // 50 ms at 16 kHz mono PCM16

    private readonly FakeAudioCapture _capture = new();
    private readonly FakeWakeWordDetector _detector = new();
    private readonly WakeListener _listener;

    public WakeListenerTests()
    {
        _listener = new WakeListener(_capture, _detector, "neon", preRollMs: 1000);   // 32 000 bytes
    }

    public void Dispose() => _listener.Dispose();

    /// <summary>A 50 ms buffer whose every byte is <paramref name="value"/>, so a seed's origin can be read back.</summary>
    private static byte[] Filled(byte value)
    {
        var pcm = new byte[Buffer50];
        Array.Fill(pcm, value);
        return pcm;
    }

    private void Deliver(int buffers, int startingValue = 0)
    {
        for (int i = 0; i < buffers; i++)
        {
            _capture.Deliver(Filled((byte)(startingValue + i)), Buffer50);
        }
    }

    [Fact]
    public void Arm_ResetsBoth_SubscribesAndStartsTheCapture()
    {
        _listener.Arm();

        Assert.True(_listener.IsArmed);
        Assert.Equal(1, _detector.Resets);
        Assert.Equal(1, _capture.Started);
        Assert.True(_capture.IsCapturing);
        Assert.False(_listener.Detected.IsCompleted);
        Assert.Equal("neon", _listener.Phrase);
        Assert.Equal(1000, _listener.PreRollMs);
    }

    [Fact]
    public void Buffers_ReachTheDetector_WithTheCountNotTheArrayLength()
    {
        _listener.Arm();

        _capture.Deliver(new byte[4000], 1000);
        _capture.Deliver(new byte[4000], 1600);

        Assert.Equal(new[] { 1000, 1600 }, _detector.Counts);
        Assert.Equal(2600, _detector.BytesFed);
    }

    [Fact]
    public void FinalWithoutThePhrase_KeepsListening()
    {
        _detector.FinalAfterBuffers = 1;
        _detector.Text = "hello there";
        _listener.Arm();

        Deliver(3);

        Assert.True(_listener.IsArmed);
        Assert.False(_listener.Detected.IsCompleted);
        Assert.Equal(3, _detector.Fed);
        Assert.Null(_listener.Disarm());
    }

    [Fact]
    public async Task MatchingFinal_CompletesDetected_WithTheWholePreRoll_WhenThereIsNoTiming()
    {
        _detector.FinalAfterBuffers = 3;
        _detector.Text = "neon what time is it";
        _listener.Arm();

        Deliver(3, startingValue: 10);
        var hit = await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("neon what time is it", hit.Text);
        Assert.True(hit.HasRequest);
        Assert.Equal(3 * Buffer50, hit.Seed.Length);
        Assert.Equal(10, hit.Seed[0]);
        Assert.Equal(12, hit.Seed[^1]);
        Assert.False(_listener.IsArmed);

        // Later buffers are neither fed nor kept: the hit is final until the next Arm.
        Deliver(2);
        Assert.Equal(3, _detector.Fed);

        Assert.Same(hit, _listener.Disarm());
        Assert.Equal(1, _capture.Stopped);
    }

    [Fact]
    public async Task MatchingFinal_TrimsTheSeedToJustBeforeThePhrase_WhenTheTimingIsKnown()
    {
        // 40 buffers = 2.0 s = 64 000 bytes fed; the 1 s pre-roll holds the last 32 000 (from 1.0 s).
        // The phrase starts at 1.5 s; keep from 1.2 s = byte 38 400 = buffer 24.
        _detector.FinalAfterBuffers = 40;
        _detector.Text = "neon";
        _detector.Words = new[] { new WakeWordTiming("neon", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1.9)) };
        _listener.Arm();

        Deliver(40);
        var hit = await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(64_000 - 38_400, hit.Seed.Length);
        Assert.Equal(24, hit.Seed[0]);
        Assert.Equal(39, hit.Seed[^1]);
        Assert.False(hit.HasRequest);
    }

    [Fact]
    public void Arm_HandsTheDetectorTheModeAndThePhrase()
    {
        _listener.Arm();
        Assert.Equal(WakeDetectorMode.Utterance, _detector.LastMode);
        Assert.Equal("neon", _detector.LastPhrase);
        _listener.Disarm();

        _listener.Arm(mode: WakeDetectorMode.Keyword);
        Assert.Equal(WakeDetectorMode.Keyword, _detector.LastMode);
        Assert.Equal("neon", _detector.LastPhrase);
        Assert.Equal(new[] { WakeDetectorMode.Utterance, WakeDetectorMode.Keyword }, _detector.Modes);
    }

    [Fact]
    public async Task KeywordMode_FiresOnAPartial_OnceItHasPersisted_WithTheWholePreRoll()
    {
        // The interrupt's case: no silence ever arrives, so no final; the partial is the signal,
        // once it has stayed in the decoder's output for KeywordConfirm (400 ms = 8 buffers).
        _detector.PartialAfterBuffers = 2;
        _detector.PartialText = "[unk] neon";
        _listener.Arm(mode: WakeDetectorMode.Keyword);

        Deliver(9, startingValue: 20);   // buffer 2 carries the partial; buffers 3..9 are "unchanged"
        Assert.False(_listener.Detected.IsCompleted);   // 350 ms: not yet
        Deliver(1, startingValue: 29);
        var hit = await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("[unk] neon", hit.Text);
        Assert.Equal(10 * Buffer50, hit.Seed.Length);   // no timing on a partial: the seed is everything kept
        Assert.Equal(20, hit.Seed[0]);
        Assert.False(_listener.IsArmed);
        Assert.Equal(10, _detector.Fed);
    }

    [Fact]
    public void KeywordMode_AFlickeringPartial_NeverFires_AndLogsHowLongItHeld()
    {
        // The assistant's own speech: the decoder says "neon" for 250 ms and takes it back. The
        // length goes to --log: it is what the confirm window has to beat on this microphone.
        _detector.PartialAfterBuffers = 2;
        _detector.PartialText = "[unk] [unk] neon";
        _detector.PartialRetractAfterBuffers = 7;   // 250 ms later
        _detector.PartialRetractText = "[unk] [unk] [unk]";
        var lines = new List<string>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Voice" && e.Message.StartsWith("Wake phrase retracted", StringComparison.Ordinal)) { lines.Add(e.Message); } };
        DiagnosticLog.Emitted += capture;
        try
        {
            _listener.Arm(mode: WakeDetectorMode.Keyword);
            Deliver(40);
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        Assert.True(_listener.IsArmed);
        Assert.False(_listener.Detected.IsCompleted);
        Assert.Null(_listener.Disarm());
        Assert.Equal(new[] { "Wake phrase retracted after 250 ms: \"[unk] [unk] neon\" → \"[unk] [unk] [unk]\"." }, lines);
    }

    [Fact]
    public async Task KeywordMode_AShorterConfirm_FiresOnThePhraseTheDecoderHoldsFor200ms()
    {
        // The field shape of 2026-09-11 with "velora": the user's phrase shows in the partial for
        // 200 ms under the assistant's voice, then the decoder takes it back. At the listener's own
        // default (400 ms) it never fires; a 150 ms window like the app's (200 ms since 2026-09-17; the echo probe covers the
        // assistant's flickers now) catches it three buffers in.
        _detector.PartialAfterBuffers = 2;
        _detector.PartialText = "[unk] [unk] [unk] velora";
        _detector.PartialRetractAfterBuffers = 6;   // 200 ms later
        _detector.PartialRetractText = "[unk] [unk] [unk] [unk]";
        using var listener = new WakeListener(_capture, _detector, "velora", preRollMs: 1000);
        listener.Arm(mode: WakeDetectorMode.Keyword, keywordConfirm: TimeSpan.FromMilliseconds(150));
        Assert.Equal(TimeSpan.FromMilliseconds(150), listener.Confirm);

        Deliver(5);
        var hit = await listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("[unk] [unk] [unk] velora", hit.Text);
        Assert.Equal(5, _detector.Fed);   // buffer 2 + 150 ms = buffer 5, before the retraction on 6
    }

    [Fact]
    public void KeywordMode_ConfirmZero_FiresOnTheFirstPartial_AndANegativeConfirmIsRefused()
    {
        _detector.PartialAfterBuffers = 3;
        _detector.PartialText = "velora";
        using var listener = new WakeListener(_capture, _detector, "velora", preRollMs: 1000);
        Assert.Throws<ArgumentOutOfRangeException>(() => listener.Arm(mode: WakeDetectorMode.Keyword, keywordConfirm: TimeSpan.FromMilliseconds(-1)));
        listener.Arm(mode: WakeDetectorMode.Keyword, keywordConfirm: TimeSpan.Zero);

        Deliver(3);

        Assert.True(listener.Detected.IsCompletedSuccessfully);
        Assert.Equal(TimeSpan.Zero, listener.Confirm);
        Assert.Equal(TimeSpan.FromMilliseconds(400), WakeListener.KeywordConfirm);   // the no-argument default stays the mic-only rule
    }

    [Fact]
    public async Task KeywordMode_AFinalWithThePhrase_FiresAtOnce()
    {
        _detector.FinalAfterBuffers = 3;
        _detector.Text = "[unk] neon";
        _listener.Arm(mode: WakeDetectorMode.Keyword);

        Deliver(3);
        var hit = await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("[unk] neon", hit.Text);
        Assert.Equal(3, _detector.Fed);
    }

    [Fact]
    public async Task KeywordMode_APartialThenAFinalWithoutThePhrase_ClearsThePending()
    {
        // The partial said "neon", the final that closed the utterance did not: nothing fires,
        // and a later persisted partial starts the clock afresh.
        _detector.PartialAfterBuffers = 2;
        _detector.PartialText = "neon";
        _detector.FinalAfterBuffers = 4;
        _detector.Text = "[unk]";
        _detector.LaterText = "neon";   // every feed after the final: a partial-less final in the fake, so it fires at once
        _listener.Arm(mode: WakeDetectorMode.Keyword);

        Deliver(4);
        Assert.False(_listener.Detected.IsCompleted);

        Deliver(1);
        var hit = await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("neon", hit.Text);
    }

    [Fact]
    public async Task UtteranceMode_DropsAPartial_AndFiresOnTheFinal()
    {
        // A detector that reports partials in utterance mode anyway is not trusted with the hit:
        // the final carries the timings and the full text the idle path needs.
        _detector.PartialsInEveryMode = true;
        _detector.PartialAfterBuffers = 1;
        _detector.PartialText = "neon";
        _detector.FinalAfterBuffers = 3;
        _detector.Text = "neon what time is it";
        _listener.Arm();

        Deliver(1);
        Assert.True(_listener.IsArmed);
        Assert.False(_listener.Detected.IsCompleted);

        Deliver(2);
        var hit = await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("neon what time is it", hit.Text);
        Assert.Equal(3, _detector.Fed);
    }

    [Fact]
    public async Task KeywordMode_TheGuardSeesThePartial_AndAnIgnoredOneKeepsListening()
    {
        var seen = new List<WakeUtterance>();
        _detector.PartialAfterBuffers = 1;
        _detector.PartialText = "neon";
        _detector.FinalAfterBuffers = 12;
        _detector.Text = "neon";
        _listener.Arm(u => { seen.Add(u); return u.IsPartial; }, WakeDetectorMode.Keyword);   // ignore the partial, accept the final

        Deliver(12);   // the partial persists and is offered at buffer 9 (400 ms), ignored; the final at 12 stands
        var hit = await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, seen.Count);
        Assert.True(seen[0].IsPartial);
        Assert.False(seen[1].IsPartial);
        Assert.False(hit.Text.Length == 0);
        Assert.Equal(12, _detector.Fed);
    }

    [Fact]
    public async Task Detected_DoesNotContinueInsideTheCaptureCallback()
    {
        // A continuation that asks to run synchronously would run inside TrySetResult, i.e. inside
        // the capture callback, unless the source was created with RunContinuationsAsynchronously.
        _detector.FinalAfterBuffers = 1;
        bool continuedInline = false;
        bool insideDeliver = false;
        var continued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _listener.Arm();
        _ = _listener.Detected.ContinueWith(_ =>
        {
            continuedInline = Volatile.Read(ref insideDeliver);
            continued.TrySetResult();
        }, TaskContinuationOptions.ExecuteSynchronously);

        await Task.Run(() =>
        {
            Volatile.Write(ref insideDeliver, true);
            _capture.Deliver(Filled(1), Buffer50);
            Volatile.Write(ref insideDeliver, false);
        });
        await continued.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(continuedInline);
    }

    [Fact]
    public async Task IgnoredFinal_KeepsListening_AndALaterOneFires()
    {
        _detector.FinalAfterBuffers = 1;
        _detector.Text = "i'm neon";
        _detector.LaterText = "neon";
        var seen = new List<string>();
        int calls = 0;
        _listener.Arm(u => { seen.Add(u.Text); return calls++ == 0; });   // the first matching final is the assistant's own voice

        Deliver(1);
        Assert.True(_listener.IsArmed);
        Assert.False(_listener.Detected.IsCompleted);

        Deliver(1);
        var hit = await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("neon", hit.Text);
        Assert.Equal(new[] { "i'm neon", "neon" }, seen);
        Assert.Equal(2, _detector.Fed);
        Assert.Equal(2 * Buffer50, hit.Seed.Length);   // the pre-roll kept both buffers
    }

    [Fact]
    public async Task GuardThrows_WarnsOnce_AndTheHitStands()
    {
        _detector.FinalAfterBuffers = 1;
        var warnings = new List<string>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Voice" && e.Level == DiagnosticLevel.Warning) { warnings.Add(e.Message); } };
        DiagnosticLog.Emitted += capture;
        try
        {
            _listener.Arm(_ => throw new InvalidOperationException("guard boom"));
            Deliver(1);
            await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        Assert.Single(warnings);
        Assert.Contains("guard boom", warnings[0]);
    }

    [Fact]
    public void Disarm_WithoutAHit_StopsTheCapture_AndCancelsDetected()
    {
        _listener.Arm();
        var detected = _listener.Detected;
        Deliver(2);

        Assert.Null(_listener.Disarm());

        Assert.False(_listener.IsArmed);
        Assert.Equal(1, _capture.Stopped);
        Assert.False(_capture.IsCapturing);
        Assert.True(detected.IsCanceled);
        Assert.Equal(new[] { "start", "stop" }, _capture.Log);

        // Nothing reaches the detector once disarmed, even if the fake were still capturing.
        _capture.Start();
        Deliver(1);
        Assert.Equal(2, _detector.Fed);
    }

    [Fact]
    public void Disarm_WhenNotArmed_IsANoOp()
    {
        Assert.Null(_listener.Disarm());
        Assert.Equal(0, _capture.Stopped);
        Assert.True(_listener.Detected.IsCanceled);
    }

    [Fact]
    public void Arm_StartThrows_Propagates_WithNothingSubscribed()
    {
        _capture.ThrowOnStart = true;

        Assert.Throws<InvalidOperationException>(() => _listener.Arm());

        Assert.False(_listener.IsArmed);
        Assert.True(_listener.Detected.IsCanceled);
        _capture.ThrowOnStart = false;
        _capture.Start();
        Deliver(1);
        Assert.Equal(0, _detector.Fed);   // not subscribed

        _capture.Stop();
        _listener.Arm();                  // works again afterwards
        Assert.True(_listener.IsArmed);
    }

    [Fact]
    public void Arm_Twice_Throws()
    {
        _listener.Arm();
        Assert.Throws<InvalidOperationException>(() => _listener.Arm());
    }

    [Fact]
    public async Task Rearm_AfterAHit_StartsOver()
    {
        _detector.FinalAfterBuffers = 1;
        _listener.Arm();
        Deliver(1);
        await _listener.Detected.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(_listener.Disarm());

        _listener.Arm();

        Assert.True(_listener.IsArmed);
        Assert.Equal(2, _detector.Resets);
        Assert.Equal(2, _capture.Started);
        Assert.False(_listener.Detected.IsCompleted);
        Assert.Null(_listener.Disarm());   // the old hit is forgotten
    }

    [Fact]
    public void DetectorThrows_WarnsOnce_AndKeepsListening()
    {
        _detector.ThrowOnFeed = true;
        var warnings = new List<string>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Voice" && e.Level == DiagnosticLevel.Warning) { warnings.Add(e.Message); } };
        DiagnosticLog.Emitted += capture;
        try
        {
            _listener.Arm();
            Deliver(3);
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        Assert.Single(warnings);
        Assert.Contains("scripted wake-word failure", warnings[0]);
        Assert.True(_listener.IsArmed);
    }

    [Fact]
    public void Dispose_Stops_AndArmThrowsAfterwards()
    {
        _listener.Arm();

        _listener.Dispose();

        Assert.Equal(1, _capture.Stopped);
        Assert.False(_listener.IsArmed);
        Assert.Throws<ObjectDisposedException>(() => _listener.Arm());
        _listener.Dispose();   // idempotent
    }

    [Fact]
    public void BuildHit_TrimsAndDrains()
    {
        var preRoll = new PreRollBuffer(1000, PcmFormat.Whisper);
        preRoll.Write(new byte[32_000], 0, 32_000);
        var u = new WakeUtterance("neon hello", new[] { new WakeWordTiming("neon", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.8)) });

        var hit = WakeListener.BuildHit(u, bytesFed: 32_000, preRoll, "neon", PcmFormat.Whisper);

        Assert.Equal(32_000 - 6_400, hit.Seed.Length);   // keep from 0.2 s
        Assert.Equal(0, preRoll.ByteCount);
        Assert.False(hit.HasRequest);
    }

    [Fact]
    public void Ctor_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new WakeListener(null!, _detector, "neon"));
        Assert.Throws<ArgumentNullException>(() => new WakeListener(_capture, null!, "neon"));
        Assert.Throws<ArgumentException>(() => new WakeListener(_capture, _detector, "  "));
        Assert.Equal(5000, WakeListener.DefaultPreRollMs);
    }
}
