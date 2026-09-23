using System.Text.Json.Serialization;

namespace NeonSidekick.Sessions;

/// <summary>The source-generated context for a session row's stored history (<see cref="SessionHistory"/>); compact, since it is rewritten after every turn. Reflection serialisation is off.</summary>
[JsonSerializable(typeof(StoredHistory))]
public sealed partial class SessionJsonContext : JsonSerializerContext;
