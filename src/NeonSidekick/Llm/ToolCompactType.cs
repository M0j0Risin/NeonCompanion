using NeonSidekick.Diagnostics;
using NeonSidekick.Settings;

namespace NeonSidekick.Llm;

/// <summary>
/// What the tool loop does when a request's usage reaches the <c>LLM auto compact (%)</c> share of
/// the window mid-turn (<see cref="Assistant.ContextGuard"/>) — the automatic compact checks only
/// at the top of a message, and one message can walk the context to the ceiling by itself.
/// </summary>
public enum ToolCompactMode
{
    /// <summary>This turn's older tool results (every iteration's but the last) become stubs and the loop carries on.</summary>
    Prune,

    /// <summary>The turn ends with an error notice; the results so far stay in the history.</summary>
    Stop,

    /// <summary>No check: the server's own limit answers.</summary>
    Nothing,
}

/// <summary>
/// The tool-compact-type setting: the three words the operator picks from (<c>prune</c>, <c>stop</c>,
/// <c>nothing</c>) and their mapping to <see cref="ToolCompactMode"/>, the <see cref="CompactType"/>
/// shape. <see cref="Resolve"/> is the one place the saved string becomes the enum: a hand-edited
/// value that is none of them falls back to <see cref="Default"/> with a warning.
/// </summary>
public static class ToolCompactType
{
    /// <summary>Prune and carry on. The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "prune";

    /// <summary>The types in menu order.</summary>
    public static readonly string[] Names = { "prune", "stop", "nothing" };

    private const string Category = "Llm";

    /// <summary>Trims and ignores case; false for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out ToolCompactMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "prune": mode = ToolCompactMode.Prune; return true;
            case "stop": mode = ToolCompactMode.Stop; return true;
            case "nothing": mode = ToolCompactMode.Nothing; return true;
            default: mode = ToolCompactMode.Prune; return false;
        }
    }

    /// <summary>The saved word for <paramref name="mode"/>.</summary>
    public static string Name(ToolCompactMode mode) => mode switch
    {
        ToolCompactMode.Stop => "stop",
        ToolCompactMode.Nothing => "nothing",
        _ => "prune",
    };

    /// <summary>The menu hint next to a type. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        "prune" => "stub this turn's older tool results and carry on",
        "stop" => "end the turn with a notice; /compact or /clear first",
        "nothing" => "no check; the server's own limit answers",
        _ => "",
    };

    /// <summary>The mode in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static ToolCompactMode Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.LlmToolCompactType, out var mode))
        {
            return mode;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.LlmToolCompactType)}='{effective.LlmToolCompactType}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        TryParse(Default, out mode);
        return mode;
    }
}
