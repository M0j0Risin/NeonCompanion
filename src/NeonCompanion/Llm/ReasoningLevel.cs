using Microsoft.Extensions.AI;
using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;

namespace NeonCompanion.Llm;

/// <summary>
/// The reasoning-effort setting: the five words the operator picks from (<c>none</c>, <c>low</c>,
/// <c>medium</c>, <c>high</c>, <c>xhigh</c>) and their mapping to <see cref="ReasoningEffort"/>.
/// The words are the wire values (<c>reasoning_effort</c>), which is why <c>xhigh</c> is spelled
/// the way the servers spell it.
///
/// <para><see cref="Resolve"/> is the one place the saved string becomes the enum, next to
/// <see cref="LlmTimeouts.Resolve"/>: a hand-edited value that is none of the five falls back to
/// <see cref="Default"/> with a warning, never to an exception.</para>
/// </summary>
public static class ReasoningLevel
{
    /// <summary>Thinking off. The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "none";

    /// <summary>The levels in menu order.</summary>
    public static readonly string[] Levels = { "none", "low", "medium", "high", "xhigh" };

    private const string Category = "Llm";

    /// <summary>Trims and ignores case; false for anything that is not one of <see cref="Levels"/>.</summary>
    public static bool TryParse(string? text, out ReasoningEffort effort)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "none": effort = ReasoningEffort.None; return true;
            case "low": effort = ReasoningEffort.Low; return true;
            case "medium": effort = ReasoningEffort.Medium; return true;
            case "high": effort = ReasoningEffort.High; return true;
            case "xhigh": effort = ReasoningEffort.ExtraHigh; return true;
            default: effort = ReasoningEffort.None; return false;
        }
    }

    /// <summary>The wire word for <paramref name="effort"/>: <c>xhigh</c> for <see cref="ReasoningEffort.ExtraHigh"/>.</summary>
    public static string Name(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.None => "none",
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        ReasoningEffort.ExtraHigh => "xhigh",
        _ => effort.ToString().ToLowerInvariant(),
    };

    /// <summary>The menu hint next to a level. Pinned.</summary>
    public static string Describe(string level) => level switch
    {
        "none" => "thinking off",
        "low" => "a little thinking, fast replies",
        "medium" => "balanced",
        "high" => "extensive thinking",
        "xhigh" => "maximum thinking, slowest",
        _ => "",
    };

    /// <summary>
    /// The glyph for a level beside the model on the hint row: a disc filling with the effort
    /// (◔ ◑ ◕ ●); nothing for <c>none</c> or an unknown word. The user's call, 2026-09-15. Pinned.
    /// </summary>
    public static string Glyph(string level) => level switch
    {
        "low" => "◔",
        "medium" => "◑",
        "high" => "◕",
        "xhigh" => "●",
        _ => "",
    };

    /// <summary>The effort in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static ReasoningEffort Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.LlmReasoning, out var effort))
        {
            return effort;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.LlmReasoning)}='{effective.LlmReasoning}' is not one of {string.Join(", ", Levels)}. Using {Default}.");
        TryParse(Default, out effort);
        return effort;
    }
}
