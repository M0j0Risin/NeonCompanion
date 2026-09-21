namespace NeonCompanion.Speech;

/// <summary>One recognised word and where it sits in the recogniser's stream (seconds since the recogniser was created or reset).</summary>
public sealed record WakeWordTiming(string Word, TimeSpan Start, TimeSpan End);

/// <summary>
/// A recognition result: the text and, when the recogniser reports them, per-word timings.
/// <paramref name="IsPartial"/> marks an interim result (no timings; the text may still change),
/// reported only in <see cref="WakeDetectorMode.Keyword"/>.
/// </summary>
public sealed record WakeUtterance(string Text, IReadOnlyList<WakeWordTiming> Words, bool IsPartial = false);

/// <summary>
/// How the recogniser listens for one arm.
///
/// <para><see cref="Utterance"/> is the idle input line (M5): open vocabulary, <em>final</em>
/// results only, word timings. It needs silence to finalise (Vosk end-points on ~0.5 s of decoded
/// silence), which the user provides by going quiet after the phrase; the full text is what the
/// "request spoken with the phrase" shortcut and the seed trim read.</para>
///
/// <para><see cref="Keyword"/> is the interrupt (M6): the recogniser is restricted to the phrase
/// plus an unknown-word token and reports <em>partial</em> results too. Under the speakers the
/// microphone never hears silence (the assistant talks continuously), so a final would arrive
/// only at Vosk's utterance cap with the phrase buried in twenty seconds of decoded speech; a
/// partial fires the moment the phrase is decoded, and the grammar is what makes it decodable
/// at all against a louder voice. Clipping mid-sentence (why M5 chose finals) does not matter
/// here: the interrupt discards the audio anyway.</para>
/// </summary>
public enum WakeDetectorMode
{
    Utterance,
    Keyword,
}

/// <summary>
/// The wake-word seam: a small always-on recogniser fed PCM16 16 kHz mono buffers from the capture
/// thread. In <see cref="WakeDetectorMode.Utterance"/> it reports every <em>final</em> result it
/// settles on and nothing else; in <see cref="WakeDetectorMode.Keyword"/> partial results as well.
/// Whether a result contains the wake phrase is <see cref="WakeWordMatch"/>'s job, so the detector
/// takes the phrase only to build the keyword grammar, and a fake scripts results rather than
/// matches. Vosk implements it in the app.
/// </summary>
public interface IWakeWordDetector : IDisposable
{
    /// <summary>Loads the model. False, with <paramref name="detail"/>, when it cannot. Never throws.</summary>
    bool Load(out string detail);

    /// <summary>Starts a fresh <see cref="WakeDetectorMode.Utterance"/> stream: timings restart at zero and <see cref="BytesFed"/> at 0. Called before each arm.</summary>
    void Reset();

    /// <summary>
    /// Starts a fresh stream in <paramref name="mode"/>; <paramref name="phrase"/> (normalised) is
    /// the keyword grammar in <see cref="WakeDetectorMode.Keyword"/> and ignored otherwise.
    /// </summary>
    void Reset(WakeDetectorMode mode, string phrase);

    /// <summary>
    /// Feeds <paramref name="count"/> bytes of <paramref name="pcm"/>; returns a result when the
    /// recogniser finalised one with text (or, in keyword mode, when its partial text changed),
    /// otherwise null. Runs on the capture thread. Never throws.
    /// </summary>
    WakeUtterance? Feed(byte[] pcm, int count);

    /// <summary>Bytes fed since the last <see cref="Reset"/>; the position the stream's timings are measured against.</summary>
    long BytesFed { get; }

    /// <summary>
    /// A second, independent recognition stream over the same loaded model (the interrupt's
    /// echo probe listens to the assistant's own audio with it while this one listens to the
    /// microphone). Already loaded; its own <see cref="Reset(WakeDetectorMode, string)"/> and
    /// <see cref="Feed"/>, safe from a different thread than this detector's. Dispose the fork
    /// before its parent. Throws <see cref="InvalidOperationException"/> when not loaded.
    /// </summary>
    IWakeWordDetector Fork();
}
