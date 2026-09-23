using NeonSidekick.Audio;
using NeonSidekick.Diagnostics;
using NeonSidekick.Speech;
using NeonSidekick.Tests.Fakes;

namespace NeonSidekick.Tests;

public class VoskWakeWordDetectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NeonSidekick.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Load guards (no model needed; nothing native is constructed) ────────

    [Fact]
    public void Load_MissingDirectory_FailsWithoutThrowing()
    {
        using var detector = new VoskWakeWordDetector(Path.Combine(_dir, "nope"), "neon");

        Assert.False(detector.Load(out var detail));
        Assert.Equal(VoskWakeWordDetector.IncompleteDirectoryDetail(Path.Combine(_dir, "nope")), detail);
        Assert.False(detector.IsLoaded);
        Assert.Null(detector.Feed(new byte[1600], 1600));
        Assert.Equal(0, detector.BytesFed);
    }

    [Fact]
    public void Load_IncompleteDirectory_Fails()
    {
        string model = FakeModelFiles.WriteVoskModel(Path.Combine(_dir, "vosk"), "graph/Gr.fst");
        using var detector = new VoskWakeWordDetector(model, "neon");

        Assert.False(detector.Load(out var detail));
        Assert.StartsWith("model directory missing or incomplete", detail);
    }

    [Fact]
    public void Load_NonAsciiPath_Fails_NamingTheHomeVariable()
    {
        string model = FakeModelFiles.WriteVoskModel(Path.Combine(_dir, "modèles", "vosk"));
        using var detector = new VoskWakeWordDetector(model, "neon");

        Assert.False(detector.Load(out var detail));
        Assert.Contains("non-ASCII", detail);
        Assert.Contains("NEONSIDEKICK_HOME", detail);
        Assert.False(VoskWakeWordDetector.IsAsciiPath(model));
        Assert.True(VoskWakeWordDetector.IsAsciiPath(_dir));
    }

    [Fact]
    public void Ctor_Guards()
    {
        Assert.Throws<ArgumentException>(() => new VoskWakeWordDetector(" ", "neon"));
    }

    [Fact]
    public void Disposed_LoadFails_AndFeedIsQuiet()
    {
        var detector = new VoskWakeWordDetector(Path.Combine(_dir, "vosk"), "neon");
        detector.Dispose();
        detector.Dispose();

        Assert.False(detector.Load(out var detail));
        Assert.Equal("disposed", detail);
        Assert.Null(detector.Feed(new byte[100], 100));
        detector.Reset();
    }

    // ── ParseResult ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseResult_ReadsTextAndTimings()
    {
        const string json = "{\"result\":[{\"conf\":1.0,\"end\":0.84,\"start\":0.45,\"word\":\"neon\"},{\"conf\":0.9,\"end\":1.2,\"start\":0.9,\"word\":\"what\"}],\"text\":\"neon what\"}";

        var u = VoskWakeWordDetector.ParseResult(json);

        Assert.NotNull(u);
        Assert.Equal("neon what", u.Text);
        Assert.Equal(2, u.Words.Count);
        Assert.Equal(new WakeWordTiming("neon", TimeSpan.FromSeconds(0.45), TimeSpan.FromSeconds(0.84)), u.Words[0]);
        Assert.Equal("what", u.Words[1].Word);
    }

    [Fact]
    public void ParseResult_TextWithoutTimings_HasNoWords()
    {
        var u = VoskWakeWordDetector.ParseResult("{\"text\": \" hello there \"}");

        Assert.NotNull(u);
        Assert.Equal("hello there", u.Text);
        Assert.Empty(u.Words);
    }

    [Theory]
    [InlineData("{\"text\": \"\"}")]            // silence finalises as empty text
    [InlineData("{\"partial\": \"neon\"}")]     // a partial, not a final
    [InlineData("{\"text\": 5}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseResult_EmptyOrForeign_IsNull(string? json)
    {
        Assert.Null(VoskWakeWordDetector.ParseResult(json));
    }

    [Theory]
    [InlineData("{\"partial\": \"neon what time\"}", "neon what time")]
    [InlineData("{\"partial\": \"  neon \"}", "neon")]
    [InlineData("{\"partial\": \"\"}", null)]           // nothing decoded yet
    [InlineData("{\"text\": \"neon\"}", null)]          // a final, not a partial
    [InlineData("{\"partial\": 5}", null)]
    [InlineData("not json", null)]
    [InlineData(null, null)]
    public void ParsePartial_ReadsThePartialField_OrNull(string? json, string? expected)
    {
        Assert.Equal(expected, VoskWakeWordDetector.ParsePartial(json));
    }

    [Theory]
    [InlineData("neon", "[\"neon\",\"[unk]\"]")]
    [InlineData("  Hey   Neon ", "[\"hey neon\",\"[unk]\"]")]   // normalised: lowercase, single-spaced
    [InlineData("", "[\"[unk]\"]")]
    public void KeywordGrammar_IsThePhrasePlusUnknown_Pinned(string phrase, string expected)
    {
        Assert.Equal(expected, VoskWakeWordDetector.KeywordGrammar(phrase));
    }

    [Fact]
    public void Reset_DefaultsToUtteranceMode_AndRecordsKeywordMode()
    {
        // No model needed: the mode is bookkeeping until a recogniser exists.
        using var detector = new VoskWakeWordDetector(Path.Combine(Path.GetTempPath(), "no-such-vosk-model"), "neon");
        Assert.Equal(WakeDetectorMode.Utterance, detector.Mode);
        detector.Reset(WakeDetectorMode.Keyword, "neon");
        Assert.Equal(WakeDetectorMode.Keyword, detector.Mode);
        detector.Reset();
        Assert.Equal(WakeDetectorMode.Utterance, detector.Mode);
    }

    // ── With the real model ─────────────────────────────────────────────────

    [VoskModelFact]
    public void Fixture_FinalisesWithTheWakeWord_AndATiming()
    {
        using var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(detector.Load(out var detail), detail);
        Assert.True(detector.IsLoaded);

        var finals = FeedInBuffers(detector, VoiceTestModels.WakeFixturePcm(), trailingSilenceMs: 1500);

        var hit = Assert.Single(finals, f => WakeWordMatch.Contains(f.Text, "neon"));
        Assert.False(hit.IsPartial);
        Assert.True(WakeWordMatch.HasRequest(hit.Text, "neon"), hit.Text);
        var start = WakeWordMatch.WakeStart(hit.Words, "neon");
        Assert.NotNull(start);
        Assert.InRange(start.Value.TotalSeconds, 0.0, 1.5);
        Assert.True(detector.BytesFed > 0);
    }

    [VoskModelFact]
    public void UtteranceMode_NeverReportsAPartial()
    {
        using var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(detector.Load(out var detail), detail);

        var results = FeedInBuffers(detector, VoiceTestModels.WakeFixturePcm(), trailingSilenceMs: 1500);

        Assert.DoesNotContain(results, r => r.IsPartial);
    }

    [VoskModelFact]
    public void KeywordMode_ReportsThePhraseAsAPartial_BeforeTheFinal_AndDecodesTheRestAsUnknown()
    {
        using var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(detector.Load(out var detail), detail);
        detector.Reset(WakeDetectorMode.Keyword, "neon");

        var results = FeedInBuffers(detector, VoiceTestModels.WakeFixturePcm(), trailingSilenceMs: 1500);

        int firstPartial = results.FindIndex(r => r.IsPartial && WakeWordMatch.Contains(r.Text, "neon"));
        int firstFinal = results.FindIndex(r => !r.IsPartial);
        Assert.True(firstPartial >= 0, "no partial containing the phrase; heard: " + string.Join(" | ", results.Select(r => r.Text)));
        Assert.True(firstFinal < 0 || firstPartial < firstFinal, "the partial must precede the final");

        // The request ("what time is it") is outside the grammar, so the final is the phrase and unknown tokens, never the words.
        var final = results.LastOrDefault(r => !r.IsPartial);
        if (final is not null)
        {
            Assert.Contains("neon", final.Text);
            Assert.DoesNotContain("time", final.Text);
        }
    }

    [VoskModelFact]
    public void KeywordMode_AssistantStory_FlickersThePhrase_ButTheListenerNeverFires()
    {
        // The field failure of 2026-09-11: the keyword grammar labels a fragment of the assistant's
        // own speech "neon" for one partial and takes it back. The fixture must still flicker (or
        // it proves nothing), and the listener's persistence rule must ride it out.
        byte[] story = VoiceTestModels.StoryFixturePcm();

        using (var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon"))
        {
            Assert.True(detector.Load(out var detail), detail);
            detector.Reset(WakeDetectorMode.Keyword, "neon");
            var results = FeedInBuffers(detector, story, trailingSilenceMs: 0);
            Assert.Contains(results, r => r.IsPartial && WakeWordMatch.Contains(r.Text, "neon"));
            Assert.DoesNotContain(results, r => !r.IsPartial && WakeWordMatch.Contains(r.Text, "neon"));
        }

        using (var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon"))
        {
            Assert.True(detector.Load(out var detail), detail);
            var capture = new FakeAudioCapture();
            using var listener = new WakeListener(capture, detector, "neon");
            listener.Arm(mode: WakeDetectorMode.Keyword);
            int buffer = PcmFormat.Whisper.BytesFor(50);
            var scratch = new byte[buffer];
            for (int offset = 0; offset < story.Length; offset += buffer)
            {
                int count = Math.Min(buffer, story.Length - offset);
                Array.Copy(story, offset, scratch, 0, count);
                capture.Deliver(scratch, count);
            }

            Assert.Null(listener.Disarm());
        }
    }

    [VoskModelFact]
    public void KeywordMode_WakeFixture_TheListenerFires_AfterTheConfirmWindow()
    {
        using var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(detector.Load(out var detail), detail);
        var capture = new FakeAudioCapture();
        using var listener = new WakeListener(capture, detector, "neon");
        listener.Arm(mode: WakeDetectorMode.Keyword);
        byte[] pcm = VoiceTestModels.WakeFixturePcm();
        int buffer = PcmFormat.Whisper.BytesFor(50);
        var scratch = new byte[buffer];
        double firedAt = -1;
        for (int offset = 0; offset < pcm.Length && firedAt < 0; offset += buffer)
        {
            int count = Math.Min(buffer, pcm.Length - offset);
            Array.Copy(pcm, offset, scratch, 0, count);
            capture.Deliver(scratch, count);
            if (listener.Detected.IsCompletedSuccessfully)
            {
                firedAt = offset / 32000.0;
            }
        }

        var hit = listener.Disarm();
        Assert.NotNull(hit);
        Assert.Contains("neon", hit.Text);
        Assert.InRange(firedAt, 0.5, 2.0);   // the lab measured 1.00 s: the partial at 0.60 s plus KeywordConfirm
    }

    [VoskModelFact]
    public void KeywordMode_HelloFixture_DoesNotContainThePhrase()
    {
        using var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(detector.Load(out var detail), detail);
        detector.Reset(WakeDetectorMode.Keyword, "neon");

        var results = FeedInBuffers(detector, VoiceTestModels.FixturePcm(), trailingSilenceMs: 1500);

        Assert.DoesNotContain(results, r => WakeWordMatch.Contains(r.Text, "neon"));
    }

    [VoskModelFact]
    public void HelloFixture_NeverContainsTheWakeWord()
    {
        using var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(detector.Load(out var detail), detail);

        var finals = FeedInBuffers(detector, VoiceTestModels.FixturePcm(), trailingSilenceMs: 1500);

        Assert.DoesNotContain(finals, f => WakeWordMatch.Contains(f.Text, "neon"));
    }

    [VoskModelFact]
    public void Reset_StartsANewStream()
    {
        using var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(detector.Load(out var detail), detail);
        detector.Feed(new byte[3200], 3200);
        Assert.Equal(3200, detector.BytesFed);

        detector.Reset();

        Assert.Equal(0, detector.BytesFed);
        Assert.True(detector.IsLoaded);
        var finals = FeedInBuffers(detector, VoiceTestModels.WakeFixturePcm(), trailingSilenceMs: 1500);
        Assert.Contains(finals, f => WakeWordMatch.Contains(f.Text, "neon"));
    }

    [VoskModelFact]
    public void Load_WarnsOnce_WhenAPhraseWordIsOutsideTheVocabulary()
    {
        var warnings = new List<string>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Voice" && e.Level == DiagnosticLevel.Warning) { warnings.Add(e.Message); } };
        DiagnosticLog.Emitted += capture;
        try
        {
            using var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon zxqvbn");
            Assert.True(detector.Load(out var detail), detail);
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("\"zxqvbn\"", warning);
        Assert.Contains("vocabulary", warning);
    }

    [VoskModelFact]
    public void Load_Twice_IsIdempotent_AndDisposeIsSafe()
    {
        var detector = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(detector.Load(out _));
        Assert.True(detector.Load(out var again));
        Assert.Equal("already loaded", again);

        detector.Dispose();
        Assert.Null(detector.Feed(new byte[1600], 1600));
        Assert.False(detector.IsLoaded);
    }

    /// <summary>Feeds <paramref name="pcm"/> in 50 ms buffers (the capture's shape) then silence, collecting every final result.</summary>
    private static List<WakeUtterance> FeedInBuffers(IWakeWordDetector detector, byte[] pcm, int trailingSilenceMs)
    {
        int buffer = PcmFormat.Whisper.BytesFor(50);
        var finals = new List<WakeUtterance>();
        var scratch = new byte[buffer];
        for (int offset = 0; offset < pcm.Length; offset += buffer)
        {
            int count = Math.Min(buffer, pcm.Length - offset);
            Array.Copy(pcm, offset, scratch, 0, count);
            if (detector.Feed(scratch, count) is { } u)
            {
                finals.Add(u);
            }
        }

        Array.Clear(scratch);
        for (int ms = 0; ms < trailingSilenceMs; ms += 50)
        {
            if (detector.Feed(scratch, buffer) is { } u)
            {
                finals.Add(u);
            }
        }

        return finals;
    }

    // ── Fork (the echo probe's recogniser) ─────────────────────────────────

    [Fact]
    public void Fork_BeforeLoad_Throws()
    {
        using var detector = new VoskWakeWordDetector(Path.Combine(Path.GetTempPath(), "no-such-model"), "neon");
        Assert.Throws<InvalidOperationException>(() => detector.Fork());
    }

    [VoskModelFact]
    public void Fork_IsASecondStreamOverTheSameModel_HeardIndependently()
    {
        using var parent = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(parent.Load(out var detail), detail);

        var fork = (VoskWakeWordDetector)parent.Fork();
        try
        {
            Assert.False(fork.OwnsModel);
            Assert.True(fork.IsLoaded);
            Assert.True(fork.Load(out var forkDetail));
            Assert.Equal("already loaded", forkDetail);

            fork.Reset(WakeDetectorMode.Keyword, "neon");
            var heard = FeedInBuffers(fork, VoiceTestModels.WakeFixturePcm(), trailingSilenceMs: 1500);
            Assert.Contains(heard, r => WakeWordMatch.Contains(r.Text, "neon"));
            Assert.Equal(0, parent.BytesFed);   // the parent's stream is untouched

            var finals = FeedInBuffers(parent, VoiceTestModels.WakeFixturePcm(), trailingSilenceMs: 1500);
            Assert.Contains(finals, f => WakeWordMatch.Contains(f.Text, "neon"));
        }
        finally
        {
            fork.Dispose();   // before the parent frees the model
        }

        Assert.True(parent.IsLoaded);
        Assert.NotEmpty(FeedInBuffers(parent, VoiceTestModels.WakeFixturePcm(), trailingSilenceMs: 1500));   // still answers after the fork is gone
    }

    [VoskModelFact]
    public async Task EchoProbe_OverTheWakeFixture_MarksThePhrase_ThroughTheResampler()
    {
        using var parent = new VoskWakeWordDetector(VoiceTestModels.VoskPath!, "neon");
        Assert.True(parent.Load(out var detail), detail);
        using var fork = parent.Fork();

        // The fixture is 16 kHz; the probe is fed what Kokoro produces, so upsample it 2:3 by
        // linear interpolation and let the probe bring it back down.
        byte[] pcm16 = VoiceTestModels.WakeFixturePcm();
        byte[] pcm24 = Upsample2To3(pcm16);
        using var probe = new EchoProbe(fork, "neon", PcmFormat.Kokoro, TimeSpan.FromMilliseconds(150));   // the app's default window
        int chunk = PcmFormat.Kokoro.BytesFor(250);
        long end = 0;
        for (int offset = 0; offset < pcm24.Length; offset += chunk)
        {
            int count = Math.Min(chunk, pcm24.Length - offset);
            var piece = new byte[count];
            Array.Copy(pcm24, offset, piece, 0, count);
            end += count;
            probe.Feed(piece, count, end);
        }

        probe.Complete();
        await probe.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotEmpty(probe.Marks);
        double first = probe.Seconds(probe.Marks[0]);
        Assert.InRange(first, 0.3, 2.0);   // "neon" opens the fixture after 300 ms of silence; the lab saw the partial at 0.60 s
        Assert.True(probe.HeardNear(probe.Marks[0], SpeechOutput.DefaultEchoLookBack, EchoProbe.DefaultLookAhead, out _));
    }

    private static byte[] Upsample2To3(byte[] pcm16)
    {
        int n = pcm16.Length / 2;
        var output = new List<byte>(pcm16.Length * 3 / 2 + 2);
        for (int i = 0; i + 1 < n; i += 2)
        {
            short a = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
            short b = (short)(pcm16[(i + 1) * 2] | (pcm16[(i + 1) * 2 + 1] << 8));
            short c = i + 2 < n ? (short)(pcm16[(i + 2) * 2] | (pcm16[(i + 2) * 2 + 1] << 8)) : b;
            foreach (short v in new[] { a, (short)((a + 2 * b) / 3), (short)((2 * b + c) / 3) })
            {
                output.Add((byte)(v & 0xFF));
                output.Add((byte)((v >> 8) & 0xFF));
            }
        }

        return output.ToArray();
    }
}
