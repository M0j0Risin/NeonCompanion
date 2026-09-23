using System.Text.Json;

namespace NeonSidekick.Llm.Tools;

/// <summary>A tool's hand-written JSON schema literal, parsed once into a detached <see cref="JsonElement"/>.</summary>
public static class ToolSchema
{
    public static JsonElement Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
