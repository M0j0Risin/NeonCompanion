using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeonSidekick.Shell.Bridge;

/// <summary>One request on the bridge (2026-09-21): the run's token, the tool's name and its arguments, as one JSON line.</summary>
public sealed class BridgeRequest
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("tool")]
    public string? Tool { get; set; }

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}

/// <summary>One answer on the bridge: the tool's text under <c>result</c>, or the <c>Error:</c> sentence under <c>error</c> — never both.</summary>
public sealed class BridgeResponse
{
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

/// <summary>The source-generated context for the two bridge records: NativeAOT needs it, reflection is off.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(BridgeRequest))]
[JsonSerializable(typeof(BridgeResponse))]
public sealed partial class BridgeJsonContext : JsonSerializerContext;
