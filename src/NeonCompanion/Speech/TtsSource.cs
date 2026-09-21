using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;

namespace NeonCompanion.Speech;

/// <summary>Where speech output is synthesised.</summary>
public enum TtsEngine
{
    /// <summary>A Kokoro-FastAPI server at <c>TTS HTTP URL</c> (<see cref="KokoroHttpSynthesizer"/>).</summary>
    Http,

    /// <summary>KokoroSharp over ONNX Runtime in this process (<see cref="KokoroInProcessSynthesizer"/>); <c>kokoro.onnx</c> downloaded on first use.</summary>
    InProcess,
}

/// <summary>
/// The <c>TTS source</c> setting (2026-09-16): the two words the operator picks from (<c>http</c>,
/// <c>in-process</c>) and their mapping to <see cref="TtsEngine"/>, the way <c>LlmScanMode</c>
/// maps the scan words. <see cref="Resolve"/> is the one place the saved string becomes the enum:
/// a hand-edited value that is neither falls back to <see cref="Default"/> with a warning.
/// </summary>
public static class TtsSource
{
    /// <summary>Kokoro in this process: the compiled default (the user's call, 2026-09-16, after the field check; <c>http</c> for the first hours), pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "in-process";

    /// <summary>The sources in menu order.</summary>
    public static readonly string[] Names = { "http", "in-process" };

    /// <summary>What the <c>TTS:</c> line prints in place of a URL for the in-process engine. Pinned.</summary>
    public const string InProcessSource = "in-process Kokoro";

    private const string Category = "Speech";

    /// <summary>Trims and ignores case; false (and <see cref="TtsEngine.InProcess"/>, the default) for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out TtsEngine engine)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "http": engine = TtsEngine.Http; return true;
            case "in-process": engine = TtsEngine.InProcess; return true;
            default: engine = TtsEngine.InProcess; return false;
        }
    }

    /// <summary>The saved word for <paramref name="engine"/>.</summary>
    public static string Name(TtsEngine engine) => engine == TtsEngine.InProcess ? "in-process" : "http";

    /// <summary>The menu hint next to a source. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        "http" => "a Kokoro-FastAPI server at TTS HTTP URL",
        "in-process" => "KokoroSharp in this process; kokoro.onnx (326 MB) downloads on first use",
        _ => "",
    };

    /// <summary>The engine in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static TtsEngine Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.TtsSource, out var engine))
        {
            return engine;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.TtsSource)}='{effective.TtsSource}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        TryParse(Default, out engine);
        return engine;
    }
}
