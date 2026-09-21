using NeonCompanion.Diagnostics;

namespace NeonCompanion.Settings;

/// <summary>
/// The new-profile-mode setting: the two words the operator picks from (<c>basic</c>, <c>advanced</c>)
/// and what <c>/profile add</c> does under each, the way <see cref="Llm.CompactType"/> maps the compact
/// words. <c>basic</c> (the default since 2026-09-16; <c>advanced</c> before) seeds the new profile with
/// its <c>profile.json</c> and its memories (<see cref="Profiles.BasicCompanionFiles"/> — the user's call
/// 2026-09-15; the settings alone before); <c>advanced</c> copies the prompt files too
/// (<see cref="Profiles.CompanionFiles"/>, each when it exists). <see cref="Resolve"/> is the one place
/// the saved string becomes a decision: a hand-edited value that is neither falls back to
/// <see cref="Default"/> with a warning.
/// </summary>
public static class NewProfileMode
{
    /// <summary>Copy the settings and the memories. The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "basic";

    /// <summary>The modes in menu order.</summary>
    public static readonly string[] Names = { "basic", "advanced" };

    private const string Category = "Settings";

    /// <summary>Trims and ignores case; false (and <c>basic</c>) for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out bool copyPromptFiles)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "basic": copyPromptFiles = false; return true;
            case "advanced": copyPromptFiles = true; return true;
            default: copyPromptFiles = false; return false;
        }
    }

    /// <summary>The menu hint next to a mode. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        "basic" => "copy the settings and memories",
        "advanced" => "copy the settings and memories · copy persona, operata and vocalia if present",
        _ => "",
    };

    /// <summary>
    /// The companion files <c>/profile add</c> copies: every one under <c>advanced</c>
    /// (<paramref name="copyPromptFiles"/> true, <see cref="Profiles.CompanionFiles"/>), the memories
    /// alone under <c>basic</c> (<see cref="Profiles.BasicCompanionFiles"/>).
    /// </summary>
    public static IReadOnlyList<string> FilesFor(bool copyPromptFiles) =>
        copyPromptFiles ? Profiles.CompanionFiles : Profiles.BasicCompanionFiles;

    /// <summary>Whether <c>/profile add</c> copies the prompt files too under <paramref name="effective"/> (<c>advanced</c>); an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static bool Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.NewProfileMode, out bool copyPromptFiles))
        {
            return copyPromptFiles;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.NewProfileMode)}='{effective.NewProfileMode}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        TryParse(Default, out copyPromptFiles);
        return copyPromptFiles;
    }
}
