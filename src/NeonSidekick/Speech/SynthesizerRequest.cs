namespace NeonSidekick.Speech;

/// <summary>
/// What the speech session asks its factory for: the engine and, per engine, the one thing it
/// needs — the normalised <c>/v1</c> URL over HTTP, the model file's path in-process. A value, so
/// the session can tell "the source I connected to" from "the source the menu is asking about"
/// by equality alone (the picker lists from the connected synthesizer when they match, a
/// throwaway one otherwise).
/// </summary>
/// <param name="Engine">Which synthesizer.</param>
/// <param name="Url">The server, normalised (<see cref="TtsEngine.Http"/> only).</param>
/// <param name="ModelPath">The absolute path of <c>kokoro.onnx</c> (<see cref="TtsEngine.InProcess"/> only).</param>
public readonly record struct SynthesizerRequest(TtsEngine Engine, Uri? Url, string? ModelPath)
{
    public static SynthesizerRequest Http(Uri v1)
    {
        ArgumentNullException.ThrowIfNull(v1);
        return new(TtsEngine.Http, v1, null);
    }

    public static SynthesizerRequest InProcess(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        return new(TtsEngine.InProcess, null, modelPath);
    }

    /// <summary>What the <c>TTS:</c> line prints: the URL, or <see cref="TtsSource.InProcessSource"/>.</summary>
    public string Source => Engine == TtsEngine.InProcess ? TtsSource.InProcessSource : Url?.AbsoluteUri ?? "";
}
