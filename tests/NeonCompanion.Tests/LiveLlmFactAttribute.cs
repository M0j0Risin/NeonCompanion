using NeonCompanion.Llm;
using NeonCompanion.Settings;

namespace NeonCompanion.Tests;

/// <summary>
/// Resolves a live OpenAI-compatible server once per assembly.
///
/// <para>Probing in a <c>static readonly</c> rather than per attribute instance: xUnit constructs
/// a gating attribute during discovery for every method it decorates, and a network round trip
/// per test method would be paid on every run, including runs that skip.</para>
/// </summary>
internal static class LiveLlmServer
{
    /// <summary>Test-specific, so pointing the app somewhere does not silently redirect the suite.</summary>
    public const string UrlVariable = "NEONCOMPANION_TEST_LLM_URL";

    public static readonly LlmEndpoint? Endpoint;
    public static readonly string Unavailable;

    static LiveLlmServer()
    {
        var settings = new AppSettingsData
        {
            LlmUrl = Environment.GetEnvironmentVariable(UrlVariable)
                     ?? Environment.GetEnvironmentVariable(EnvironmentOverrides.LlmUrlVariable)
                     ?? "",
            LlmModel = Environment.GetEnvironmentVariable(EnvironmentOverrides.LlmModelVariable) ?? "",
            LlmApiKey = Environment.GetEnvironmentVariable(EnvironmentOverrides.LlmApiKeyVariable) ?? "empty",
        };

        try
        {
            using var http = new HttpClient();
            var probe = new LlmEndpointProbe(http);
            var endpoint = probe.ResolveAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
            if (endpoint is null || endpoint.Source.Contains("not answering", StringComparison.Ordinal))
            {
                Unavailable = $"No live OpenAI-compatible server found; set {UrlVariable} to run live-model tests.";
                return;
            }

            Endpoint = endpoint;
            Unavailable = "";
        }
        catch (Exception ex)
        {
            Unavailable = "Live server probe failed: " + ex.Message;
        }
    }
}

/// <summary>Skips unless a live server is reachable. A local pre-merge gate, never a CI safety net.</summary>
public sealed class LiveLlmFactAttribute : FactAttribute
{
    public LiveLlmFactAttribute()
    {
        if (LiveLlmServer.Endpoint is null)
        {
            Skip = LiveLlmServer.Unavailable;
        }
    }
}

/// <summary>One loaded model serves these tests; running them alone keeps a slow round trip from looking like a hang.</summary>
[CollectionDefinition("LiveLlm", DisableParallelization = true)]
public sealed class LiveLlmCollection
{
}
