using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeonCompanion.Sql;

/// <summary>
/// The source-generated context for <c>sql.json</c> (2026-09-23), the <see cref="Mcp.McpJsonContext"/> shape:
/// a dictionary of connection objects keyed by name. Reflection serialisation is off. Camel-case keys,
/// comments and a trailing comma are tolerated on the way in.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SqlConfigFile))]
[JsonSerializable(typeof(SqlConnectionConfig))]
public sealed partial class SqlJsonContext : JsonSerializerContext;
