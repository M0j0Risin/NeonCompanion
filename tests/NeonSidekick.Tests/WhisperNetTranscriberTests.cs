using System.Text;
using System.Text.RegularExpressions;
using NeonSidekick.Audio;
using NeonSidekick.Diagnostics;
using NeonSidekick.Speech;

namespace NeonSidekick.Tests;

public class WhisperNetTranscriberTests
{
    [Fact]
    public void WrapPcmAsWav_ProducesACanonicalRiffHeader()
    {
        var pcm = new byte[320];   // 10 ms of 16 kHz mono
        pcm[0] = 0x11;
        pcm[^1] = 0x22;

        var wav = WhisperNetTranscriber.WrapPcmAsWav(pcm, PcmFormat.Whisper);

        Assert.Equal(44 + pcm.Length, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));

        Assert.Equal(36 + pcm.Length, BitConverter.ToInt32(wav, 4));   // RIFF chunk size
        Assert.Equal(16, BitConverter.ToInt32(wav, 16));               // PCM fmt chunk size
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));                // uncompressed PCM
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));                // mono
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));            // sample rate
        Assert.Equal(32000, BitConverter.ToInt32(wav, 28));            // byte rate
        Assert.Equal(2, BitConverter.ToInt16(wav, 32));                // block align
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));               // bits per sample
        Assert.Equal(pcm.Length, BitConverter.ToInt32(wav, 40));       // data chunk size
        Assert.Equal(0x11, wav[44]);
        Assert.Equal(0x22, wav[^1]);
    }

    [Fact]
    public void WrapPcmAsWav_Stereo24k_FollowsTheFormat()
    {
        var wav = WhisperNetTranscriber.WrapPcmAsWav(new byte[8], new PcmFormat(24000, 16, 2));
        Assert.Equal(2, BitConverter.ToInt16(wav, 22));
        Assert.Equal(24000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(96000, BitConverter.ToInt32(wav, 28));
        Assert.Equal(4, BitConverter.ToInt16(wav, 32));
    }

    [Fact]
    public async Task Load_MissingModel_IsFalse_AndTranscribeIsAFailedResult()
    {
        using var t = new WhisperNetTranscriber(Path.Combine(Path.GetTempPath(), "no-such-whisper-" + Guid.NewGuid().ToString("N") + ".bin"));
        Assert.False(t.Load(out var detail));
        Assert.Contains("not found", detail);
        Assert.False(t.IsLoaded);
        Assert.Equal(PcmFormat.Whisper, t.Format);

        var result = await t.TranscribeAsync(new byte[3200], CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("model not loaded", result.Detail);
    }

    [Fact]
    public void Ctor_RejectsBlankArguments()
    {
        Assert.Throws<ArgumentException>(() => new WhisperNetTranscriber(""));
        Assert.Throws<ArgumentException>(() => new WhisperNetTranscriber("x.bin", " "));
        Assert.Equal("", RecognitionResult.Failed("why").Text);
        Assert.False(RecognitionResult.Failed("why").Ok);
    }

    // ── With the model ──────────────────────────────────────────────────────

    [WhisperModelFact]
    public async Task Fixture_TranscribesToHello_AndLogsTheTiming()
    {
        using var t = new WhisperNetTranscriber(VoiceTestModels.WhisperPath!);
        Assert.True(t.Load(out var detail), detail);
        Assert.True(t.Load(out _));   // idempotent

        var lines = new List<string>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Voice" && e.Level == DiagnosticLevel.Info) { lock (lines) { lines.Add(e.Message); } } };
        DiagnosticLog.Emitted += capture;
        RecognitionResult result;
        try
        {
            result = await t.TranscribeAsync(VoiceTestModels.FixturePcm(), CancellationToken.None);
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        Assert.True(result.Ok, result.Detail);
        Assert.Contains("hello", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(result.Audio.TotalSeconds, 2.0, 3.0);
        Assert.True(result.Elapsed > TimeSpan.Zero);
        // The line carries the text since 2026-09-19 (LogText.Quoted): what the microphone heard is in the --log file.
        Assert.Contains(lines, l => Regex.IsMatch(l, @"^Transcribed \d+\.\ds in \d+ms \(\d+\.\dx realtime\): "".*[Hh]ello.*""$"));
    }

    [WhisperModelFact]
    public async Task Cancellation_ThrowsOperationCanceled()
    {
        using var t = new WhisperNetTranscriber(VoiceTestModels.WhisperPath!);
        Assert.True(t.Load(out var detail), detail);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t.TranscribeAsync(VoiceTestModels.FixturePcm(), cts.Token));
    }

    // A silence test (one second of zeros → "" after SpeechTranscript.Clean) stood here from M4 until
    // 2026-09-19: ggml-base.en.bin hallucinates a word into pure silence on this machine, so it failed
    // every full run and build.ps1 stopped on it. Removed at the user's call; the pipeline is green
    // again. Bring it back only with a model that stays quiet, or an assertion on the app's own
    // silence gate rather than the model's output.
}
