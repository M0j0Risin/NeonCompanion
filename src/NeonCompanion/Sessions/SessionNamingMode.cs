using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;

namespace NeonCompanion.Sessions;

/// <summary>Where a new session's title comes from (<c>Session naming mode</c>).</summary>
public enum SessionNaming
{
    /// <summary>The first sent line, cut to <see cref="SessionText.MaxTitleChars"/>. No model request.</summary>
    FirstLine,

    /// <summary>A background request after the first turn asks the model for a short title; the first line stands until it answers.</summary>
    ModelWritten,
}

/// <summary>
/// The naming-mode setting: the two words the operator picks from (<c>first-line</c>,
/// <c>model-written</c>) and their mapping to <see cref="SessionNaming"/>, the
/// <see cref="Llm.CompactType"/> shape. <see cref="Resolve"/> is the one place the saved string
/// becomes the enum: a hand-edited value that is neither falls back to <see cref="Default"/> with a warning.
/// </summary>
public static class SessionNamingMode
{
    /// <summary>The model-written title. The compiled default (the user's call, 2026-09-18; <c>first-line</c> that morning), pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "model-written";

    /// <summary>The modes in menu order.</summary>
    public static readonly string[] Names = { "first-line", "model-written" };

    private const string Category = "Sessions";

    /// <summary>Trims and ignores case; false for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out SessionNaming mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "first-line": mode = SessionNaming.FirstLine; return true;
            case "model-written": mode = SessionNaming.ModelWritten; return true;
            default: mode = SessionNaming.FirstLine; return false;
        }
    }

    /// <summary>The saved word for <paramref name="mode"/>.</summary>
    public static string Name(SessionNaming mode) => mode switch
    {
        SessionNaming.ModelWritten => "model-written",
        _ => "first-line",
    };

    /// <summary>The menu hint next to a mode. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        "first-line" => "the session is named after its first sent line",
        "model-written" => "the model writes a short title after the first turn",
        _ => "",
    };

    /// <summary>The mode in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static SessionNaming Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.SessionNamingMode, out var mode))
        {
            return mode;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.SessionNamingMode)}='{effective.SessionNamingMode}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        TryParse(Default, out mode);
        return mode;
    }
}
