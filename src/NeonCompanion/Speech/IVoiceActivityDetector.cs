namespace NeonCompanion.Speech;

/// <summary>What the detector concluded from the audio so far.</summary>
public enum VadVerdict
{
    /// <summary>No speech in what it has, or not enough new audio to say.</summary>
    None,

    /// <summary>Speech has been heard and has not ended yet.</summary>
    Speaking,

    /// <summary>Speech was heard and enough silence has followed it: the utterance is over.</summary>
    EndOfSpeech,
}

/// <summary>
/// End-of-utterance detection over a stream of PCM16 16 kHz mono buffers, fed from the capture
/// thread. <see cref="Feed"/> is called per buffer and must be cheap most of the time; the
/// implementation decides when to actually analyse.
/// </summary>
public interface IVoiceActivityDetector : IDisposable
{
    /// <summary>Loads the model. False, with <paramref name="detail"/>, when it cannot. Never throws.</summary>
    bool Load(out string detail);

    /// <summary>Forgets everything heard so far; called before each utterance.</summary>
    void Reset();

    /// <summary>Accumulates <paramref name="count"/> bytes of <paramref name="pcm"/> and returns the current verdict. Never throws.</summary>
    VadVerdict Feed(byte[] pcm, int count);
}
