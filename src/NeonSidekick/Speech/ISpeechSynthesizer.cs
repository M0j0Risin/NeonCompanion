using NeonSidekick.Audio;

namespace NeonSidekick.Speech;

/// <summary>What one <c>GET /v1/audio/voices</c> found.</summary>
/// <param name="Exists">A speech server answered (200, or 401/403 asking for a key).</param>
/// <param name="Voices">Voice ids it listed, sorted ordinally so English (<c>a*</c>, <c>b*</c>) comes first.</param>
/// <param name="Detail">A human phrase for logs and the status line.</param>
public readonly record struct VoiceListResult(bool Exists, IReadOnlyList<string> Voices, string Detail)
{
    public static VoiceListResult Missing(string detail) => new(false, Array.Empty<string>(), detail);
}

/// <summary>The outcome of synthesising one chunk.</summary>
/// <param name="Ok">Every byte the server produced reached the sink.</param>
/// <param name="PcmBytes">Bytes delivered to the sink.</param>
/// <param name="Detail">A human phrase; the failure reason when <paramref name="Ok"/> is false.</param>
public readonly record struct SynthesisResult(bool Ok, long PcmBytes, string Detail)
{
    public static SynthesisResult Failed(string detail) => new(false, 0, detail);
}

/// <summary>
/// Text in, PCM out — over HTTP (<see cref="KokoroHttpSynthesizer"/>) or in this process
/// (<see cref="KokoroInProcessSynthesizer"/>); playback is <see cref="IAudioPlayback"/>'s job, and
/// the two meet in <c>SpeechOutput</c>. Implementations never throw from the three calls except
/// <see cref="OperationCanceledException"/> for the caller's own token: a server or engine failure
/// is a result, so one bad sentence cannot take a turn down. Which implementation a
/// <see cref="SynthesizerRequest"/> gets is the session's factory's business; the synthesizer
/// itself carries no identity.
/// </summary>
public interface ISpeechSynthesizer : IDisposable
{
    /// <summary>The format every byte handed to the sink is in.</summary>
    PcmFormat Format { get; }

    /// <summary>
    /// The readiness probe the session runs once at connect: whatever makes
    /// <see cref="SynthesizeAsync"/> able to answer, then the voice list. Over HTTP that is the
    /// same GET as <see cref="ListVoicesAsync"/>; in-process it is the ONE place the model loads
    /// (seconds, hundreds of megabytes), so nothing else calls it.
    /// </summary>
    Task<VoiceListResult> PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The voices, cheaply: the server's list, or the voice files beside the exe. The picker calls
    /// this on a throwaway synthesizer for a source the session has not connected yet, so it must
    /// never load a model.
    /// </summary>
    Task<VoiceListResult> ListVoicesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Synthesises <paramref name="text"/> and streams PCM to <paramref name="pcmSink"/> as it
    /// arrives, in sample-aligned (even-length) chunks. <b>The sink must copy</b>: the buffer is
    /// reused for the next read. Completes when the server has sent everything.
    /// </summary>
    Task<SynthesisResult> SynthesizeAsync(string text, string voice, double speed, Action<byte[], int> pcmSink, CancellationToken cancellationToken);
}
