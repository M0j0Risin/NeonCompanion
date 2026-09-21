using NeonCompanion.Speech;

namespace NeonCompanion.Tests.Fakes;

/// <summary>
/// An <see cref="IWakeWordDetector"/> scripted by buffer count: on the <see cref="FinalAfterBuffers"/>-th
/// feed since the last reset (0 = never) it reports a final result of <see cref="Text"/> with
/// <see cref="Words"/>, and on every later feed <see cref="LaterText"/> (null = nothing). In
/// <see cref="WakeDetectorMode.Keyword"/> mode only (mirroring the real detector) the
/// <see cref="PartialAfterBuffers"/>-th feed reports a partial of <see cref="PartialText"/>, once.
/// Counts restart on <see cref="Reset()"/>, as the real recogniser's timings do; the mode and
/// phrase of the last reset are recorded so a test can pin which arm used which.
/// </summary>
public sealed class FakeWakeWordDetector : IWakeWordDetector
{
    public int FinalAfterBuffers { get; set; }

    public string Text { get; set; } = "neon";

    public IReadOnlyList<WakeWordTiming> Words { get; set; } = Array.Empty<WakeWordTiming>();

    /// <summary>What every feed after the scripted final reports; null keeps quiet.</summary>
    public string? LaterText { get; set; }

    /// <summary>The feed (since the last reset) on which a partial of <see cref="PartialText"/> is reported in keyword mode; 0 = never.</summary>
    public int PartialAfterBuffers { get; set; }

    public string PartialText { get; set; } = "neon";

    /// <summary>When set, a partial is reported in utterance mode too: a detector that misbehaves, for the listener's gate.</summary>
    public bool PartialsInEveryMode { get; set; }

    /// <summary>The feed on which a second partial of <see cref="PartialRetractText"/> is reported (the flicker: the phrase taken back); 0 = never.</summary>
    public int PartialRetractAfterBuffers { get; set; }

    public string PartialRetractText { get; set; } = "[unk] [unk]";

    public bool ThrowOnFeed { get; set; }

    public bool LoadFails { get; set; }

    public bool Loaded { get; private set; }

    public int Resets { get; private set; }

    /// <summary>The mode of the last <see cref="Reset(WakeDetectorMode, string)"/>.</summary>
    public WakeDetectorMode LastMode { get; private set; }

    /// <summary>The phrase handed to the last <see cref="Reset(WakeDetectorMode, string)"/>.</summary>
    public string LastPhrase { get; private set; } = "";

    /// <summary>Every mode armed, in order.</summary>
    public List<WakeDetectorMode> Modes { get; } = new();

    /// <summary>Feeds since the last reset.</summary>
    public int Fed { get; private set; }

    /// <summary>The <c>count</c> of every feed, in order; pins that the capture's count, not its array length, is used.</summary>
    public List<int> Counts { get; } = new();

    public long BytesFed { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>What <see cref="Fork"/> hands out (the echo probe's detector); a fresh silent fake when unset. Script it to make the assistant's own audio "decode" as the phrase.</summary>
    public FakeWakeWordDetector? ForkResult { get; set; }

    public int Forks { get; private set; }

    public bool ForkThrows { get; set; }

    public IWakeWordDetector Fork()
    {
        Forks++;
        if (ForkThrows)
        {
            throw new InvalidOperationException("The wake-word model is not loaded.");
        }

        var fork = ForkResult ??= new FakeWakeWordDetector();
        fork.Loaded = true;
        return fork;
    }

    public bool Load(out string detail)
    {
        if (LoadFails)
        {
            detail = "model directory missing or incomplete: vosk-model-small-en-us-0.15";
            return false;
        }

        Loaded = true;
        detail = "loaded";
        return true;
    }

    public void Reset() => Reset(WakeDetectorMode.Utterance, "");

    public void Reset(WakeDetectorMode mode, string phrase)
    {
        Resets++;
        Fed = 0;
        BytesFed = 0;
        LastMode = mode;
        LastPhrase = phrase;
        Modes.Add(mode);
    }

    public WakeUtterance? Feed(byte[] pcm, int count)
    {
        if (ThrowOnFeed)
        {
            throw new InvalidOperationException("scripted wake-word failure");
        }

        Fed++;
        Counts.Add(count);
        BytesFed += count;
        if (FinalAfterBuffers > 0 && Fed == FinalAfterBuffers)
        {
            return new WakeUtterance(Text, Words);
        }

        if (FinalAfterBuffers > 0 && Fed > FinalAfterBuffers && LaterText is { } later)
        {
            return new WakeUtterance(later, Array.Empty<WakeWordTiming>());
        }

        if (PartialAfterBuffers > 0 && Fed == PartialAfterBuffers && (LastMode == WakeDetectorMode.Keyword || PartialsInEveryMode))
        {
            return new WakeUtterance(PartialText, Array.Empty<WakeWordTiming>(), IsPartial: true);
        }

        if (PartialRetractAfterBuffers > 0 && Fed == PartialRetractAfterBuffers && (LastMode == WakeDetectorMode.Keyword || PartialsInEveryMode))
        {
            return new WakeUtterance(PartialRetractText, Array.Empty<WakeWordTiming>(), IsPartial: true);
        }

        return null;
    }

    public void Dispose() => Disposed = true;
}
