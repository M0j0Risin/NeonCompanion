using System.Text.Json.Serialization;

namespace NeonSidekick.Memory;

/// <summary>The source-generated context for <c>memory.json</c>; a sibling of <c>SettingsJsonContext</c>. Reflection serialisation is off.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MemoryFile))]
public sealed partial class MemoryJsonContext : JsonSerializerContext;
