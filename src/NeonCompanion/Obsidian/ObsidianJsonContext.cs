using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeonCompanion.Obsidian;

/// <summary>
/// <c>.obsidian/daily-notes.json</c>, the core Daily notes plugin's settings as Obsidian writes them
/// (2026-09-22): every field optional — Obsidian leaves out what the user never changed, and an absent
/// file means the defaults (<see cref="DailyNotes"/>). Read only: the vault's config is Obsidian's.
/// </summary>
public sealed class DailyNotesConfigFile
{
    public string? Folder { get; set; }
    public string? Format { get; set; }
    public string? Template { get; set; }
}

/// <summary>
/// <c>.obsidian/app.json</c>, the few fields the vault tools read (2026-09-22): where a new note goes
/// (<c>newFileLocation</c> <c>root</c> / <c>current</c> / <c>folder</c>, with <c>newFileFolderPath</c>).
/// Everything else in the file is ignored. Read only.
/// </summary>
public sealed class ObsidianAppConfigFile
{
    public string? NewFileLocation { get; set; }
    public string? NewFileFolderPath { get; set; }
}

/// <summary>
/// The source-generated context for the vault's <c>.obsidian/*.json</c> files (2026-09-22); a sibling of
/// <c>McpJsonContext</c>. Reflection serialisation is off. Camel-case keys (Obsidian's own), comments and
/// trailing commas tolerated, unknown keys skipped.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DailyNotesConfigFile))]
[JsonSerializable(typeof(ObsidianAppConfigFile))]
public sealed partial class ObsidianJsonContext : JsonSerializerContext;
