using NeonSidekick.Diagnostics;
using NeonSidekick.Settings;

namespace NeonSidekick.UI;

/// <summary>
/// The theme setting (2026-09-23, the user's ask): the names the operator picks from on the General
/// tab's <c>Theme</c> row or with <c>/theme</c>, the way <see cref="ThumbnailSize"/> maps its words.
/// <see cref="Resolve"/> is the one place the saved string becomes a <see cref="ThemePalette"/>: a
/// hand-edited value that is none of them falls back to <see cref="Default"/> with a warning.
/// Named <c>ThemeName</c>, not <c>Theme</c>, so it never clashes with the palette class.
/// </summary>
public static class ThemeName
{
    /// <summary>The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "synthwave";

    /// <summary>The names in menu order (<see cref="ThemePalette.All"/>'s).</summary>
    public static readonly string[] Names = ThemePalette.All.Select(p => p.Name).ToArray();

    private const string Category = "Theme";

    /// <summary>Trims and ignores case; false (and synthwave) for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out ThemePalette palette)
    {
        string? name = text?.Trim();
        foreach (var candidate in ThemePalette.All)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                palette = candidate;
                return true;
            }
        }

        palette = ThemePalette.Synthwave;
        return false;
    }

    /// <summary>The note beside a name in the picker and <c>/theme</c>'s argument list; empty for an unknown one. Pinned.</summary>
    public static string Describe(string name) => TryParse(name, out var palette) ? palette.Description : "";

    /// <summary>The palette in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static ThemePalette Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.Theme, out var palette))
        {
            return palette;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.Theme)}='{effective.Theme}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        return ThemePalette.Synthwave;
    }

    /// <summary>Puts <paramref name="effective"/>'s theme in force (<see cref="Theme.Use"/>).</summary>
    public static void Apply(AppSettingsData effective) => Theme.Use(Resolve(effective));
}
