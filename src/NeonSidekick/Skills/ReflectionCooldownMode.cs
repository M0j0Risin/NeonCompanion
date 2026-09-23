using NeonSidekick.Diagnostics;
using NeonSidekick.Settings;

namespace NeonSidekick.Skills;

/// <summary>What the reflection cooldown holds back (<c>Reflection cooldown mode</c>, 2026-09-19).</summary>
public enum ReflectionCooldownScope
{
    /// <summary>After any skill is written, every automatic reflection waits out the cooldown.</summary>
    AllSkills,

    /// <summary>Only a turn that loaded the skill just written waits; another lesson reflects at once.</summary>
    LastWrittenSkill,
}

/// <summary>
/// The cooldown-mode setting: the two words the operator picks from (<c>all-skills</c>,
/// <c>last-written-skill</c>) and their mapping to <see cref="ReflectionCooldownScope"/>, the
/// <c>SessionShowName</c> shape. <see cref="Resolve"/> is the one place the saved string becomes
/// the enum: a hand-edited value that is neither falls back to <see cref="Default"/> with a warning.
/// </summary>
public static class ReflectionCooldownMode
{
    /// <summary>The compiled default (the user's call, 2026-09-19), pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "last-written-skill";

    /// <summary>The choices in menu order.</summary>
    public static readonly string[] Names = { "all-skills", "last-written-skill" };

    /// <summary>Trims and ignores case; false for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out ReflectionCooldownScope scope)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "all-skills": scope = ReflectionCooldownScope.AllSkills; return true;
            case "last-written-skill": scope = ReflectionCooldownScope.LastWrittenSkill; return true;
            default: scope = ReflectionCooldownScope.LastWrittenSkill; return false;
        }
    }

    /// <summary>The saved word for <paramref name="scope"/>.</summary>
    public static string Name(ReflectionCooldownScope scope) => scope switch
    {
        ReflectionCooldownScope.AllSkills => "all-skills",
        _ => "last-written-skill",
    };

    /// <summary>The menu hint next to a choice. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        "all-skills" => "after any skill is written, every automatic reflection waits out the cooldown",
        "last-written-skill" => "only a turn that loaded the skill just written waits; another lesson reflects at once",
        _ => "",
    };

    /// <summary>The choice in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static ReflectionCooldownScope Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.ReflectionCooldownMode, out var scope))
        {
            return scope;
        }

        DiagnosticLog.Warn(SkillCatalog.Category,
            $"{nameof(AppSettingsData.ReflectionCooldownMode)}='{effective.ReflectionCooldownMode}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        TryParse(Default, out scope);
        return scope;
    }
}
