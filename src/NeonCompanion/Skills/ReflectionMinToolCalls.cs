using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;

namespace NeonCompanion.Skills;

/// <summary>
/// The <c>Reflection min tool calls</c> setting: how many of the model's own tool calls, added up
/// across the turns since the last reflection (<c>TurnTrace.Absorb</c>), make a task worth a
/// skill — the first door of <see cref="SkillLearner.ShouldLearn"/>. The second door, an error
/// recovered from, is not a setting: every error is a call, so a count would be subsumed by this
/// one, and the single recovered error is the lesson worth keeping (2026-09-17). <see cref="Resolve"/>
/// is the one place the saved number is read: out of range warns and uses the default, never clamps.
/// </summary>
public static class ReflectionMinToolCalls
{
    /// <summary>The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const int Default = AppSettingsData.DefaultReflectionMinToolCalls;

    public const int Min = AppSettingsData.MinReflectionMinToolCalls;

    public const int Max = AppSettingsData.MaxReflectionMinToolCalls;

    /// <summary>The threshold for <paramref name="effective"/>; an out-of-range saved value warns and uses <see cref="Default"/>.</summary>
    public static int Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        int calls = effective.ReflectionMinToolCalls;
        if (calls >= Min && calls <= Max)
        {
            return calls;
        }

        DiagnosticLog.Warn(SkillCatalog.Category,
            $"{nameof(AppSettingsData.ReflectionMinToolCalls)}={calls.ToString(System.Globalization.CultureInfo.InvariantCulture)} is not {Min.ToString(System.Globalization.CultureInfo.InvariantCulture)} to {Max.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Using {Default.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        return Default;
    }
}
