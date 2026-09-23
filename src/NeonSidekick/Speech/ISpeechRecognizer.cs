using NeonSidekick.Audio;

namespace NeonSidekick.Speech;

/// <summary>What one transcription produced.</summary>
/// <param name="Ok">False when the recognizer could not run; <paramref name="Detail"/> says why.</param>
/// <param name="Text">The raw transcript (annotations included; <see cref="SpeechTranscript.Clean"/> is the caller's job).</param>
/// <param name="Audio">How much audio was transcribed.</param>
/// <param name="Elapsed">How long it took.</param>
/// <param name="Detail">A one-line explanation, for the transcript.</param>
public readonly record struct RecognitionResult(bool Ok, string Text, TimeSpan Audio, TimeSpan Elapsed, string Detail)
{
    public static RecognitionResult Failed(string detail) => new(false, "", TimeSpan.Zero, TimeSpan.Zero, detail);
}

/// <summary>
/// Speech to text over one loaded model. <see cref="Load"/> binds the model (never throws);
/// <see cref="TranscribeAsync"/> turns PCM in <see cref="Format"/> into text and never throws
/// except for the caller's own cancellation. One transcription at a time.
/// </summary>
public interface ISpeechRecognizer : IDisposable
{
    /// <summary>The PCM the recognizer consumes.</summary>
    PcmFormat Format { get; }

    /// <summary>Loads the model. False, with <paramref name="detail"/>, when it cannot; true when it is (or already was) loaded.</summary>
    bool Load(out string detail);

    /// <summary>Transcribes <paramref name="pcm"/>. A result, never an exception, except <see cref="OperationCanceledException"/> for <paramref name="cancellationToken"/>.</summary>
    Task<RecognitionResult> TranscribeAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken);
}
