using System.Text.Json.Serialization;

namespace NeonCompanion.Speech;

/// <summary>The body of <c>POST /v1/audio/speech</c> as Kokoro-FastAPI reads it. Property order is wire order.</summary>
internal sealed class KokoroSpeechRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = KokoroHttpSynthesizer.ModelName;

    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    [JsonPropertyName("voice")]
    public string Voice { get; set; } = "";

    [JsonPropertyName("response_format")]
    public string ResponseFormat { get; set; } = KokoroHttpSynthesizer.ResponseFormat;

    /// <summary>A <see cref="double"/>, so System.Text.Json writes it culture-invariantly.</summary>
    [JsonPropertyName("speed")]
    public double Speed { get; set; } = 1.0;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = true;
}

/// <summary>
/// AOT-safe JSON context for the speech wire types, a sibling of
/// <see cref="Settings.SettingsJsonContext"/>. Reflection serialisation is off project-wide.
/// </summary>
[JsonSerializable(typeof(KokoroSpeechRequest))]
internal sealed partial class SpeechJsonContext : JsonSerializerContext;
