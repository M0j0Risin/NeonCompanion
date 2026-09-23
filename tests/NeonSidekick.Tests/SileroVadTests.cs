using NeonSidekick.Audio;
using NeonSidekick.Speech;
using Whisper.net;

namespace NeonSidekick.Tests;

public class SileroVadTests
{
    private static VadSegmentData Segment(int startMs, int endMs) => new(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs));

    [Fact]
    public void Judge_NoSegments_IsNone()
    {
        Assert.Equal(VadVerdict.None, SileroVad.Judge(Array.Empty<VadSegmentData>(), 5000, VadOptions.Default));
    }

    [Fact]
    public void Judge_SegmentReachingTheEnd_IsSpeaking()
    {
        Assert.Equal(VadVerdict.Speaking, SileroVad.Judge(new[] { Segment(100, 2900) }, 3000, VadOptions.Default));
    }

    [Fact]
    public void Judge_ShortTrailingSilence_IsStillSpeaking()
    {
        // 500 ms of silence is a pause for breath, not the end of the turn.
        Assert.Equal(VadVerdict.Speaking, SileroVad.Judge(new[] { Segment(100, 1000), Segment(1200, 2500) }, 3000, VadOptions.Default));
    }

    [Fact]
    public void Judge_EnoughTrailingSilence_IsEndOfSpeech()
    {
        Assert.Equal(VadVerdict.EndOfSpeech, SileroVad.Judge(new[] { Segment(100, 2300) }, 3000, VadOptions.Default));
        Assert.Equal(VadVerdict.EndOfSpeech, SileroVad.Judge(new[] { Segment(100, 2300) }, 3000, new VadOptions(EndOfSpeechSilenceMs: 700)));
        Assert.Equal(VadVerdict.Speaking, SileroVad.Judge(new[] { Segment(100, 2300) }, 3000, new VadOptions(EndOfSpeechSilenceMs: 800)));
    }

    [Fact]
    public void Defaults_EndOfSpeechIsMuchLongerThanSilerosOwnGap()
    {
        var o = VadOptions.Default;
        Assert.Equal(700, o.EndOfSpeechSilenceMs);
        Assert.Equal(100, o.MinSilenceMs);
        Assert.True(o.EndOfSpeechSilenceMs > 3 * o.MinSilenceMs);
        Assert.True(o.AnalysisIntervalMs < o.EndOfSpeechSilenceMs);
        Assert.True(o.IdleTailMs > o.AnalysisIntervalMs);
    }

    [Fact]
    public void Pcm16ToFloat_IsLittleEndianAndNormalised()
    {
        var pcm = new byte[] { 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80, 0x01, 0x00 };
        var f = SileroVad.Pcm16ToFloat(pcm);
        Assert.Equal(4, f.Length);
        Assert.Equal(0f, f[0]);
        Assert.Equal(32767f / 32768f, f[1]);
        Assert.Equal(-1f, f[2]);
        Assert.Equal(1f / 32768f, f[3]);
        Assert.Empty(SileroVad.Pcm16ToFloat(new byte[] { 0x01 }));
    }

    [Fact]
    public void Load_MissingModel_IsFalse_AndFeedIsNone()
    {
        using var vad = new SileroVad(Path.Combine(Path.GetTempPath(), "no-such-silero-" + Guid.NewGuid().ToString("N") + ".bin"));
        Assert.False(vad.Load(out var detail));
        Assert.Contains("not found", detail);
        Assert.False(vad.IsLoaded);
        Assert.Equal(VadVerdict.None, vad.Feed(new byte[1600], 1600));
        vad.Reset();
    }

    [Fact]
    public void Ctor_RejectsABlankPath()
    {
        Assert.Throws<ArgumentException>(() => new SileroVad(" "));
    }

    // ── With the model ──────────────────────────────────────────────────────

    private static SileroVad Loaded()
    {
        var vad = new SileroVad(VoiceTestModels.SileroPath!);
        Assert.True(vad.Load(out var detail), detail);
        return vad;
    }

    /// <summary>Feeds <paramref name="pcm"/> in 50 ms buffers, the way the capture pump does, and returns every verdict.</summary>
    private static List<VadVerdict> FeedInBuffers(SileroVad vad, byte[] pcm)
    {
        int size = PcmFormat.Whisper.BytesFor(50);
        var verdicts = new List<VadVerdict>();
        for (int offset = 0; offset < pcm.Length; offset += size)
        {
            int count = Math.Min(size, pcm.Length - offset);
            var buffer = new byte[size];
            Buffer.BlockCopy(pcm, offset, buffer, 0, count);
            verdicts.Add(vad.Feed(buffer, count));
        }

        return verdicts;
    }

    [SileroModelFact]
    public void Fixture_FollowedBySilence_EndsSpeech()
    {
        using var vad = Loaded();
        vad.Reset();
        var speech = VoiceTestModels.FixturePcm();
        var silence = new byte[PcmFormat.Whisper.BytesFor(1500)];

        var verdicts = FeedInBuffers(vad, speech);
        Assert.Contains(VadVerdict.Speaking, verdicts);
        Assert.DoesNotContain(VadVerdict.EndOfSpeech, verdicts);   // the fixture ends 300 ms after the last word

        verdicts = FeedInBuffers(vad, silence);
        Assert.Contains(VadVerdict.EndOfSpeech, verdicts);
    }

    [SileroModelFact]
    public void Silence_NeverReportsSpeech()
    {
        using var vad = Loaded();
        vad.Reset();
        var verdicts = FeedInBuffers(vad, new byte[PcmFormat.Whisper.BytesFor(3000)]);
        Assert.All(verdicts, v => Assert.Equal(VadVerdict.None, v));
    }

    [SileroModelFact]
    public void Reset_ForgetsTheUtterance()
    {
        using var vad = Loaded();
        vad.Reset();
        FeedInBuffers(vad, VoiceTestModels.FixturePcm());
        vad.Reset();

        // After a reset the earlier speech is gone: pure silence produces nothing, not EndOfSpeech.
        var verdicts = FeedInBuffers(vad, new byte[PcmFormat.Whisper.BytesFor(1500)]);
        Assert.All(verdicts, v => Assert.Equal(VadVerdict.None, v));
    }
}
