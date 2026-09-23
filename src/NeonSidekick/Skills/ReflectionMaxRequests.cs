using NeonSidekick.Diagnostics;
using NeonSidekick.Settings;

namespace NeonSidekick.Skills;

/// <summary>
/// The <c>Reflection max requests</c> setting: how many model requests one reflection may make
/// before it is given up as <see cref="SkillLearnOutcome.Exhausted"/> — a load or two, then the
/// write, out of the box (2026-09-17, the user's call; a constant until then). The reflection's
/// own cap, nothing to do with <c>LLM max tool iterations</c> (the conversation turn's).
/// <see cref="Resolve"/> is the one place the saved number is read: out of range warns and uses
/// the default, never clamps.
/// </summary>
public static class ReflectionMaxRequests
{
    /// <summary>The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const int Default = AppSettingsData.DefaultReflectionMaxRequests;

    public const int Min = AppSettingsData.MinReflectionMaxRequests;

    public const int Max = AppSettingsData.MaxReflectionMaxRequests;

    /// <summary>The cap for <paramref name="effective"/>; an out-of-range saved value warns and uses <see cref="Default"/>.</summary>
    public static int Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        int requests = effective.ReflectionMaxRequests;
        if (requests >= Min && requests <= Max)
        {
            return requests;
        }

        DiagnosticLog.Warn(SkillCatalog.Category,
            $"{nameof(AppSettingsData.ReflectionMaxRequests)}={requests.ToString(System.Globalization.CultureInfo.InvariantCulture)} is not {Min.ToString(System.Globalization.CultureInfo.InvariantCulture)} to {Max.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Using {Default.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        return Default;
    }
}
