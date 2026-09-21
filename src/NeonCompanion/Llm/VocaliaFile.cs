namespace NeonCompanion.Llm;

/// <summary>
/// The editable voice directive: <c>vocalia.md</c> in the profile directory. When the file exists
/// and has text, that text replaces <see cref="Assistant.VoiceDirective"/> — the block appended
/// last while a turn speaks. The file changes what is appended, never whether: a silent turn
/// carries no directive whatever the file says, and headless never speaks. Absent or blank, the
/// default applies. <c>/vocalia</c> seeds the file with the default so the edit starts from the
/// sentences being replaced — the first of which exempts tool calls, and dropping it is how a
/// small model comes to invent a spoken answer instead of looking one up. The mechanics are
/// <see cref="PromptFile"/>'s.
/// </summary>
public sealed class VocaliaFile : PromptFile
{
    public const string FileName = "vocalia.md";

    /// <summary>Characters kept. A directive is a paragraph (the default is about 560); the prompt goes out on every spoken request.</summary>
    public const int MaxLength = 4000;

    public const string Category = "Vocalia";

    /// <param name="directory">The profile directory; the file is <see cref="FileName"/> under it.</param>
    public VocaliaFile(string directory)
        : base(directory, FileName, Assistant.VoiceDirective, Category, "Voice directive", MaxLength)
    {
    }

    /// <summary>The directive text as the prompt carries it: CRLF folded to LF, trimmed, cut at <see cref="MaxLength"/> with an ellipsis. Pure; pinned.</summary>
    public static string Normalize(string raw) => Normalize(raw, out _);

    public static string Normalize(string raw, out bool truncated) => Normalize(raw, MaxLength, out truncated);

    public static string TruncatedWarning(int length) => TruncatedWarning(FileName, MaxLength, length);

    public static string UnreadableWarning(string detail) => UnreadableWarning(FileName, "voice directive", detail);
}
