using NeonCompanion.Audio;
using NeonCompanion.Diagnostics;
using NeonCompanion.Speech;
using NeonCompanion.Tests.Fakes;

namespace NeonCompanion.Tests;

public class EchoProbeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly PcmFormat Kokoro = PcmFormat.Kokoro;   // 48 000 bytes per second

    private readonly FakeWakeWordDetector _detector = new();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition not met in time");
    }

    [Fact]
    public void Construct_ResetsTheDetectorInKeywordMode_WithThePhrase()
    {
        using var probe = new EchoProbe(_detector, "  Velora ", Kokoro);

        Assert.Equal(WakeDetectorMode.Keyword, _detector.LastMode);
        Assert.Equal("velora", _detector.LastPhrase);
        Assert.Equal("velora", probe.Phrase);
        Assert.Equal(Kokoro, probe.Source);
        Assert.Equal(WakeListener.KeywordConfirm, probe.Confirm);   // the listener's own default when none is given
        Assert.Throws<ArgumentException>(() => new EchoProbe(_detector, " ", Kokoro));
        Assert.Throws<ArgumentNullException>(() => new EchoProbe(null!, "velora", Kokoro));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EchoProbe(_detector, "velora", Kokoro, TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task AOneSliceFlicker_LeavesNoMark_ButAPersistedPartialMarksWhereItAppeared()
    {
        // The listener's rule, mirrored: the phrase for one 100 ms slice, retracted on the next,
        // is what the listener rides out at 150 ms (the app's default is 200 since 2026-09-17); a mark there would only be a dead zone.
        _detector.PartialAfterBuffers = 2;
        _detector.PartialText = "[unk] velora";
        _detector.PartialRetractAfterBuffers = 3;
        _detector.PartialRetractText = "[unk] [unk]";
        using (var probe = new EchoProbe(_detector, "velora", Kokoro, TimeSpan.FromMilliseconds(150)))
        {
            probe.Feed(new byte[48_000], 48_000, 48_000);
            probe.Complete();
            await probe.Completion.WaitAsync(Timeout);
            Assert.Empty(probe.Marks);
        }

        // Persisted (no retraction: every later slice is "unchanged"): the mark is at the slice the
        // phrase appeared in, once 150 − 50 = 100 ms more audio has passed, i.e. two slices later.
        var held = new FakeWakeWordDetector { PartialAfterBuffers = 2, PartialText = "[unk] velora" };
        using (var probe = new EchoProbe(held, "velora", Kokoro, TimeSpan.FromMilliseconds(150)))
        {
            probe.Feed(new byte[48_000], 48_000, 48_000);
            probe.Complete();
            await probe.Completion.WaitAsync(Timeout);
            Assert.Equal(new long[] { 4800 }, probe.Marks);   // the end of slice 2 in source bytes
            Assert.Equal(TimeSpan.FromMilliseconds(150), probe.Confirm);
        }
    }

    [Fact]
    public async Task ConfirmZero_MarksOnTheFirstPartial()
    {
        _detector.PartialAfterBuffers = 3;
        _detector.PartialText = "velora";
        using var probe = new EchoProbe(_detector, "velora", Kokoro, TimeSpan.Zero);

        probe.Feed(new byte[14_400], 14_400, 14_400);   // 300 ms: six slices
        probe.Complete();
        await probe.Completion.WaitAsync(Timeout);

        Assert.Equal(new long[] { 7200 }, probe.Marks);   // the end of slice 3
    }

    [Fact]
    public async Task Feed_SlicesAt50ms_AndMarksWhereTheDecoderReportsThePhrase()
    {
        // One second of 24 kHz audio is twenty 50 ms slices at 16 kHz (1600 bytes each); the fourth reports the
        // phrase and it is never retracted, so at the default 400 ms it marks seven slices later, at slice 4's end.
        _detector.PartialAfterBuffers = 4;
        _detector.PartialText = "velora";
        using var probe = new EchoProbe(_detector, "velora", Kokoro);

        probe.Feed(new byte[48_000], 48_000, streamEnd: 48_000);
        probe.Complete();
        await probe.Completion.WaitAsync(Timeout);

        Assert.Equal(20, _detector.Fed);
        Assert.All(_detector.Counts, c => Assert.Equal(1600, c));
        Assert.Equal(new long[] { 9600 }, probe.Marks);   // the end of the fourth slice, in source bytes: 4 × 2400
    }

    [Fact]
    public async Task Feed_SmallChunks_AreCarriedIntoUniformSlices_SoAFlickerCannotSpanTwoTinyOnes()
    {
        // The third field log: Kokoro streams 20 ms pieces; sliced per chunk, a one-partial flicker
        // over two such pieces met a 50 ms persistence and marked. Carried into 50 ms slices it cannot.
        _detector.PartialAfterBuffers = 2;
        _detector.PartialText = "[unk] velora";
        _detector.PartialRetractAfterBuffers = 3;
        _detector.PartialRetractText = "[unk] [unk]";
        using var probe = new EchoProbe(_detector, "velora", Kokoro, TimeSpan.FromMilliseconds(150));

        long end = 0;
        for (int i = 0; i < 10; i++)
        {
            end += 960;                                   // 20 ms at 24 kHz
            probe.Feed(new byte[960], 960, end);
        }

        probe.Complete();
        await probe.Completion.WaitAsync(Timeout);

        Assert.Equal(4, _detector.Fed);                   // 200 ms of audio = four 50 ms slices
        Assert.All(_detector.Counts, c => Assert.Equal(1600, c));
        Assert.Empty(probe.Marks);
    }

    [Fact]
    public async Task Feed_TheStreamPosition_IsTheChunksOwn_NotTheProbesCount()
    {
        _detector.PartialAfterBuffers = 1;
        _detector.PartialText = "[unk] velora";
        using var probe = new EchoProbe(_detector, "velora", Kokoro, TimeSpan.Zero);

        probe.Feed(new byte[4800], 4800, streamEnd: 1_000_000);   // a 100 ms chunk written late in the stream: two slices
        probe.Complete();
        await probe.Completion.WaitAsync(Timeout);

        Assert.Equal(new long[] { 997_600 }, probe.Marks);   // the first slice's end: the chunk's start + 50 ms
    }

    [Fact]
    public async Task Feed_AResultWithoutThePhrase_LeavesNoMark()
    {
        _detector.PartialAfterBuffers = 1;
        _detector.PartialText = "[unk]";
        using var probe = new EchoProbe(_detector, "velora", Kokoro);

        probe.Feed(new byte[4800], 4800, 4800);
        probe.Complete();
        await probe.Completion.WaitAsync(Timeout);

        Assert.Empty(probe.Marks);
        Assert.False(probe.HeardNear(0, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), out long mark));
        Assert.Equal(-1, mark);
    }

    [Fact]
    public async Task HeardNear_LooksBackAndAhead_FromThePlayHead()
    {
        _detector.FinalAfterBuffers = 4;   // a final counts too
        _detector.Text = "velora";
        using var probe = new EchoProbe(_detector, "velora", Kokoro);
        probe.Feed(new byte[48_000], 48_000, 48_000);
        probe.Complete();
        await probe.Completion.WaitAsync(Timeout);
        Assert.Equal(new long[] { 9600 }, probe.Marks);   // 0.2 s

        var back = TimeSpan.FromSeconds(3);
        var ahead = TimeSpan.FromSeconds(1);
        Assert.True(probe.HeardNear(9600, back, ahead, out long mark));                   // at the mark
        Assert.Equal(9600, mark);
        Assert.True(probe.HeardNear(0, back, ahead, out _));                              // 0.2 s ahead of a play head at 0
        Assert.True(probe.HeardNear(9600 + 3 * 48_000, back, ahead, out _));             // exactly 3 s later
        Assert.False(probe.HeardNear(9600 + 3 * 48_000 + 2, back, ahead, out _));        // just past the look-back
        Assert.False(probe.HeardNear(9600 - 48_000 - 2, back, ahead, out _));            // more than 1 s before the mark
        Assert.True(probe.HeardNear(9600, TimeSpan.Zero, TimeSpan.Zero, out _));         // zero windows still hit the exact position
        Assert.Equal(0.2, probe.Seconds(9600), 3);
    }

    [Fact]
    public async Task Feed_AfterDispose_IsDropped_AndTheTaskEnds()
    {
        _detector.PartialAfterBuffers = 1;
        _detector.PartialText = "velora";
        var probe = new EchoProbe(_detector, "velora", Kokoro);
        probe.Dispose();

        probe.Feed(new byte[4800], 4800, 4800);
        await probe.Completion.WaitAsync(Timeout);

        Assert.Empty(probe.Marks);
        Assert.Equal(0, _detector.Fed);
        probe.Dispose();   // idempotent
    }

    [Fact]
    public async Task ADetectorThatThrows_WarnsOnce_AndTheProbeGoesQuiet()
    {
        _detector.ThrowOnFeed = true;
        var warnings = new List<string>();
        Action<DiagnosticEvent> capture = e => { if (e.Level == DiagnosticLevel.Warning && e.Message.Contains("Echo probe", StringComparison.Ordinal)) { warnings.Add(e.Message); } };
        DiagnosticLog.Emitted += capture;
        try
        {
            using var probe = new EchoProbe(_detector, "velora", Kokoro);
            probe.Feed(new byte[9600], 9600, 9600);
            probe.Feed(new byte[9600], 9600, 19_200);
            probe.Complete();
            await probe.Completion.WaitAsync(Timeout);

            Assert.Single(warnings);
            Assert.Contains("the text guard stands alone", warnings[0]);
            Assert.Empty(probe.Marks);
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }
    }

    [Fact]
    public async Task Feed_CopiesTheBuffer_SoTheCallerMayReuseIt()
    {
        _detector.PartialAfterBuffers = 1;
        _detector.PartialText = "velora";
        using var probe = new EchoProbe(_detector, "velora", Kokoro, TimeSpan.Zero);
        var buffer = new byte[4800];

        probe.Feed(buffer, 4800, 4800);
        Array.Fill(buffer, (byte)0xFF);   // the caller's buffer is reused at once
        probe.Complete();
        await probe.Completion.WaitAsync(Timeout);

        Assert.Equal(new long[] { 2400 }, probe.Marks);
        await WaitUntilAsync(() => _detector.Fed == 2);
    }
}
