using NeonCompanion.Llm;
using NeonCompanion.Settings;
using NeonCompanion.Speech;

namespace NeonCompanion.Tests;

/// <summary>
/// Resolves a live Kokoro-FastAPI server once per assembly, the way <see cref="LiveLlmServer"/>
/// does for the LLM. Locally: <c>docker start kokoro-fastapi</c>.
/// </summary>
internal static class LiveTtsServer
{
    /// <summary>Test-specific, so pointing the app somewhere does not silently redirect the suite.</summary>
    public const string UrlVariable = "NEONCOMPANION_TEST_TTS_URL";

    public static readonly Uri? BaseUrl;
    public static readonly IReadOnlyList<string> Voices = Array.Empty<string>();
    public static readonly string Unavailable;

    static LiveTtsServer()
    {
        string raw = Environment.GetEnvironmentVariable(UrlVariable)
                     ?? Environment.GetEnvironmentVariable(EnvironmentOverrides.TtsUrlVariable)
                     ?? new AppSettingsData().TtsHttpUrl;

        try
        {
            var url = LlmEndpoint.NormalizeBaseUrl(raw);
            using var synth = new KokoroHttpSynthesizer(url);
            var result = synth.ListVoicesAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (!result.Exists)
            {
                Unavailable = $"No Kokoro-FastAPI server at {url} ({result.Detail}); run `docker start kokoro-fastapi` or set {UrlVariable}.";
                return;
            }

            BaseUrl = url;
            Voices = result.Voices;
            Unavailable = "";
        }
        catch (Exception ex)
        {
            Unavailable = "Live TTS probe failed: " + ex.Message;
        }
    }
}

/// <summary>Skips unless a Kokoro-FastAPI server answers. A local pre-merge gate, never a CI safety net.</summary>
public sealed class LiveTtsFactAttribute : FactAttribute
{
    public LiveTtsFactAttribute()
    {
        if (LiveTtsServer.BaseUrl is null)
        {
            Skip = LiveTtsServer.Unavailable;
        }
    }
}

/// <summary>Skips unless both a Kokoro-FastAPI server and a wave-out device are present: the real end-to-end path.</summary>
public sealed class LiveSpeechFactAttribute : FactAttribute
{
    public LiveSpeechFactAttribute()
    {
        if (LiveTtsServer.BaseUrl is null)
        {
            Skip = LiveTtsServer.Unavailable;
        }
        else if (AudioDevice.OutputCount == 0)
        {
            Skip = AudioDevice.Unavailable;
        }
    }
}

[CollectionDefinition("LiveTts", DisableParallelization = true)]
public sealed class LiveTtsCollection
{
}
