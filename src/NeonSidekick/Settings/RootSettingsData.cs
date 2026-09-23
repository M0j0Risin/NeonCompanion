namespace NeonSidekick.Settings;

/// <summary>
/// The root <c>settings.json</c> since profiles: nothing but which profile is loaded. Everything
/// the user can change lives in that profile's <c>profile.json</c> (<see cref="AppSettingsData"/>).
///
/// <para><see cref="SchemaVersion"/> is 2 because the file changed meaning, not merely shape: a
/// version-1 root file holds the full settings and has no <see cref="Profile"/> property, which
/// is how <see cref="AppSettings"/> recognises one to migrate. Unknown properties are ignored on
/// read, so an old file deserialises as a pointer to <see cref="Profiles.DefaultName"/>.</para>
/// </summary>
public sealed class RootSettingsData
{
    public int SchemaVersion { get; set; } = 2;

    /// <summary>The loaded profile's directory name under <c>profiles</c>.</summary>
    public string Profile { get; set; } = Profiles.DefaultName;
}
