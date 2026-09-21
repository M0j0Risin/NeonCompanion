using NeonCompanion.Speech;

namespace NeonCompanion.Tests.Fakes;

/// <summary>
/// An <see cref="IVoiceActivityDetector"/> scripted by buffer count: <see cref="SpeakingFromBuffer"/>
/// is the 1-based feed from which it reports <see cref="VadVerdict.Speaking"/> (0 = never), and
/// <see cref="EndAfterBuffers"/> the feed on which it reports <see cref="VadVerdict.EndOfSpeech"/>
/// (0 = never). Counts restart on <see cref="Reset"/>.
/// </summary>
public sealed class FakeVad : IVoiceActivityDetector
{
    public int SpeakingFromBuffer { get; set; } = 1;

    public int EndAfterBuffers { get; set; }

    public bool ThrowOnFeed { get; set; }

    public bool LoadFails { get; set; }

    public bool Loaded { get; private set; }

    public int Resets { get; private set; }

    /// <summary>Feeds since the last reset.</summary>
    public int Fed { get; private set; }

    public bool Disposed { get; private set; }

    public bool Load(out string detail)
    {
        if (LoadFails)
        {
            detail = "model file not found: ggml-silero-v6.2.0.bin";
            return false;
        }

        Loaded = true;
        detail = "loaded";
        return true;
    }

    public void Reset()
    {
        Resets++;
        Fed = 0;
    }

    public VadVerdict Feed(byte[] pcm, int count)
    {
        Fed++;
        if (ThrowOnFeed)
        {
            throw new InvalidOperationException("scripted VAD failure");
        }

        if (EndAfterBuffers > 0 && Fed >= EndAfterBuffers)
        {
            return VadVerdict.EndOfSpeech;
        }

        return SpeakingFromBuffer > 0 && Fed >= SpeakingFromBuffer ? VadVerdict.Speaking : VadVerdict.None;
    }

    public void Dispose() => Disposed = true;
}
