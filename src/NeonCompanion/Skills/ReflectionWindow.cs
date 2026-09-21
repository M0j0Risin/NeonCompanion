using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;

namespace NeonCompanion.Skills;

/// <summary>
/// The <c>Reflection window</c> setting: how many of the last turns the skill-learning reflection
/// (<see cref="SkillLearner"/>) reads — the last in full, the earlier ones as its lead-up — and
/// over how many the trigger may add up (the tally since the last reflection, <c>TurnTrace.Absorb</c>).
/// One is the last turn alone. <see cref="Resolve"/> is the one place the saved number is read: a
/// hand-edited value out of range warns and uses the default, never clamps.
/// </summary>
public static class ReflectionWindow
{
    /// <summary>The compiled default, pinned by <c>AppSettingsTests</c>: the ask, the attempt and the fix.</summary>
    public const int Default = AppSettingsData.DefaultReflectionWindow;

    public const int Min = AppSettingsData.MinReflectionWindow;

    public const int Max = AppSettingsData.MaxReflectionWindow;

    /// <summary>The turns the reflection reads for <paramref name="effective"/>; an out-of-range saved value warns and uses <see cref="Default"/>.</summary>
    public static int Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        int window = effective.ReflectionWindow;
        if (window >= Min && window <= Max)
        {
            return window;
        }

        DiagnosticLog.Warn(SkillCatalog.Category,
            $"{nameof(AppSettingsData.ReflectionWindow)}={window.ToString(System.Globalization.CultureInfo.InvariantCulture)} is not {Min.ToString(System.Globalization.CultureInfo.InvariantCulture)} to {Max.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Using {Default.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        return Default;
    }
}
