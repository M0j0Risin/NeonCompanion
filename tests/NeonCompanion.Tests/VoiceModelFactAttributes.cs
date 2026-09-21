using NeonCompanion.Settings;
using NeonCompanion.Speech;

namespace NeonCompanion.Tests;

/// <summary>
/// Locates the ggml models the model-gated facts need, once per assembly. A test-specific
/// variable first, then the app's own models directory (<c>NEONCOMPANION_HOME</c> or
/// <c>%USERPROFILE%\.neoncompanion</c>), where <c>--voice-check</c> downloads them. Locally:
/// copy <c>ggml-tiny.en.bin</c> and <c>ggml-silero-v6.2.0.bin</c> there, or run the published
/// exe with <c>--voice-check</c> once.
/// </summary>
internal static class VoiceTestModels
{
    public const string WhisperVariable = "NEONCOMPANION_TEST_WHISPER_MODEL";
    public const string SileroVariable = "NEONCOMPANION_TEST_SILERO_MODEL";
    public const string VoskVariable = "NEONCOMPANION_TEST_VOSK_MODEL";
    public const string KokoroVariable = "NEONCOMPANION_TEST_KOKORO_MODEL";

    public static readonly string? WhisperPath;
    public static readonly string? SileroPath;

    /// <summary>The Vosk model directory (complete per <see cref="ModelStore.VoskRequiredFiles"/>), or null.</summary>
    public static readonly string? VoskPath;

    /// <summary>The Kokoro ONNX model (passing <see cref="ModelStore.LooksLikeOnnx"/>), or null; the voices must also be beside the test binary (the csproj's Content items put them there).</summary>
    public static readonly string? KokoroPath;
    public static readonly string WhisperUnavailable;
    public static readonly string KokoroUnavailable;
    public static readonly string SileroUnavailable;
    public static readonly string VoskUnavailable;

    /// <summary>The fixture: ~1.8 s of Kokoro saying "Hello, how are you doing today?" resampled to 16 kHz, with 300 ms of silence each side.</summary>
    public static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hello-16k.wav");

    /// <summary>The wake fixture: Kokoro saying "Neon, what time is it?" the same way.</summary>
    public static readonly string WakeFixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "neon-16k.wav");

    static VoiceTestModels()
    {
        string home = AppSettings.ResolveStorageDirectory(Environment.GetEnvironmentVariable(EnvironmentOverrides.HomeVariable));
        string models = Path.Combine(home, "models");

        WhisperPath = FirstExisting(
            Environment.GetEnvironmentVariable(WhisperVariable),
            Path.Combine(models, "ggml-tiny.en.bin"),
            Path.Combine(models, "ggml-base.en.bin"));
        SileroPath = FirstExisting(
            Environment.GetEnvironmentVariable(SileroVariable),
            Path.Combine(models, ModelStore.SileroFileName));

        VoskPath = FirstCompleteDirectory(
            Environment.GetEnvironmentVariable(VoskVariable),
            Path.Combine(models, ModelStore.VoskModelDirectoryName));

        string? kokoro = FirstExisting(
            Environment.GetEnvironmentVariable(KokoroVariable),
            Path.Combine(models, ModelStore.KokoroFileName));
        bool voices = File.Exists(Path.Combine(KokoroInProcessSynthesizer.VoicesDirectory(AppContext.BaseDirectory), "af_heart" + KokoroInProcessSynthesizer.VoiceExtension));
        KokoroPath = kokoro is not null && ModelStore.LooksLikeOnnx(kokoro) && voices ? kokoro : null;

        WhisperUnavailable = WhisperPath is null ? $"No Whisper ggml model; set {WhisperVariable} or put ggml-tiny.en.bin in {models}." : "";
        KokoroUnavailable = KokoroPath is null ? $"No Kokoro ONNX model (or no voices folder beside the test binary); set {KokoroVariable} or put {ModelStore.KokoroFileName} in {models}." : "";
        SileroUnavailable = SileroPath is null ? $"No Silero ggml model; set {SileroVariable} or put {ModelStore.SileroFileName} in {models}." : "";
        VoskUnavailable = VoskPath is null ? $"No Vosk model; set {VoskVariable} or put {ModelStore.VoskModelDirectoryName} in {models}." : "";
    }

    private static string? FirstCompleteDirectory(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && ModelStore.IsCompleteModelDirectory(candidate, ModelStore.VoskRequiredFiles))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The fixture's PCM (the 44-byte header skipped).</summary>
    public static byte[] FixturePcm() => File.ReadAllBytes(FixturePath)[44..];

    /// <summary>The wake fixture's PCM (the 44-byte header skipped).</summary>
    public static byte[] WakeFixturePcm() => File.ReadAllBytes(WakeFixturePath)[44..];

    /// <summary>The story fixture: two seconds of the assistant (Kokoro, af_bella) reading a story with no wake word in it, which the keyword grammar mislabels "neon" for one partial.</summary>
    public static readonly string StoryFixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "story-16k.wav");

    /// <summary>The story fixture's PCM (the 44-byte header skipped).</summary>
    public static byte[] StoryFixturePcm() => File.ReadAllBytes(StoryFixturePath)[44..];

    private static string? FirstExisting(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>Skips unless a Whisper ggml model is available locally. A local check, never a CI safety net.</summary>
public sealed class WhisperModelFactAttribute : FactAttribute
{
    public WhisperModelFactAttribute()
    {
        if (VoiceTestModels.WhisperPath is null)
        {
            Skip = VoiceTestModels.WhisperUnavailable;
        }
    }
}

/// <summary>Skips unless the Silero ggml model is available locally.</summary>
public sealed class SileroModelFactAttribute : FactAttribute
{
    public SileroModelFactAttribute()
    {
        if (VoiceTestModels.SileroPath is null)
        {
            Skip = VoiceTestModels.SileroUnavailable;
        }
    }
}

/// <summary>Skips unless a complete Vosk model directory is available locally.</summary>
public sealed class VoskModelFactAttribute : FactAttribute
{
    public VoskModelFactAttribute()
    {
        if (VoiceTestModels.VoskPath is null)
        {
            Skip = VoiceTestModels.VoskUnavailable;
        }
    }
}

/// <summary>Skips unless <c>kokoro.onnx</c> and the <c>voices</c> folder are available locally (a run of the app with <c>TTS source</c> = <c>in-process</c> downloads the model).</summary>
public sealed class KokoroModelFactAttribute : FactAttribute
{
    public KokoroModelFactAttribute()
    {
        if (VoiceTestModels.KokoroPath is null)
        {
            Skip = VoiceTestModels.KokoroUnavailable;
        }
    }
}
