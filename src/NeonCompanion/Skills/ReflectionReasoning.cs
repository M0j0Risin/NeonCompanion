using Microsoft.Extensions.AI;
using NeonCompanion.Diagnostics;
using NeonCompanion.Llm;
using NeonCompanion.Settings;

namespace NeonCompanion.Skills;

/// <summary>
/// The <c>Reflection reasoning</c> setting: the reasoning level of the skill-learning reflection
/// (<see cref="SkillLearner"/>) — <c>profile</c>, the profile's own <c>LLM reasoning</c> level
/// (<c>profile-default</c> until later on 2026-09-19, no old spelling kept), or one of the five
/// levels outright; <c>none</c> the default since then (the profile's level before), the user's
/// call: a reflection thinks only when asked to. The <c>CompactType</c> shape: <see cref="Resolve"/>
/// is the one place the saved word is read, a hand-edited stranger warns and uses the default.
/// </summary>
public static class ReflectionReasoning
{
    public const string Profile = "profile";

    /// <summary>Thinking off (<see cref="ReasoningLevel.Default"/>). The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = ReasoningLevel.Default;

    /// <summary>The words in menu order: the profile's level first, then the five levels.</summary>
    public static readonly string[] Names = [Profile, .. ReasoningLevel.Levels];

    /// <summary>Trims and ignores case; <paramref name="effort"/> is null for <see cref="Profile"/>. False for anything not in <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out ReasoningEffort? effort)
    {
        string word = text?.Trim().ToLowerInvariant() ?? "";
        if (word == Profile)
        {
            effort = null;
            return true;
        }

        if (ReasoningLevel.TryParse(word, out var level))
        {
            effort = level;
            return true;
        }

        effort = null;
        return false;
    }

    /// <summary>The menu hint next to a word. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        Profile => "the profile's LLM reasoning level",
        _ => ReasoningLevel.Describe(name),
    };

    /// <summary>The effort the reflection runs at for <paramref name="effective"/>: the profile's level under <see cref="Profile"/>; an unknown saved value warns and uses <see cref="Default"/> (thinking off).</summary>
    public static ReasoningEffort Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.ReflectionReasoning, out var effort))
        {
            return effort ?? ReasoningLevel.Resolve(effective);
        }

        DiagnosticLog.Warn(SkillCatalog.Category,
            $"{nameof(AppSettingsData.ReflectionReasoning)}='{effective.ReflectionReasoning}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        return ReasoningEffort.None;
    }
}
