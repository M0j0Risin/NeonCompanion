using NeonCompanion.Settings;

namespace NeonCompanion.Skills;

/// <summary>
/// The three folders a <see cref="SkillCatalog"/> scans, in precedence order (<see cref="SkillScope"/>).
/// Built per scan from the live settings (<see cref="For"/>), so a profile switch moves the first
/// one without a rebuild; the external one is fixed at composition (<see cref="DefaultExternalDirectory"/>,
/// or a test's temp folder).
/// </summary>
public sealed record SkillRoots(string Profile, string Global, string External)
{
    /// <summary>The folder name under the home and under a profile: <c>skills</c>.</summary>
    public const string DirectoryName = "skills";

    /// <summary>The cross-client convention under the user's home: <c>.agents\skills</c>.</summary>
    public const string ExternalDirectoryName = ".agents";

    /// <summary>The roots for the loaded profile: <c>&lt;profile&gt;\skills</c>, <c>&lt;home&gt;\skills</c>, <paramref name="external"/>.</summary>
    public static SkillRoots For(AppSettings settings, string external)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(external);
        return new SkillRoots(settings.ProfileSkillsDirectory, settings.GlobalSkillsDirectory, Path.GetFullPath(external));
    }

    /// <summary>
    /// <c>%USERPROFILE%\.agents\skills</c>, the folder other agent clients share skills through;
    /// beside the binary when the process has no profile folder (the <see cref="AppSettings.ResolveStorageDirectory"/> rule).
    /// </summary>
    public static string DefaultExternalDirectory()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string home = string.IsNullOrWhiteSpace(profile) ? AppContext.BaseDirectory : profile;
        return Path.Combine(home, ExternalDirectoryName, DirectoryName);
    }

    /// <summary>The root for <paramref name="scope"/>.</summary>
    public string Of(SkillScope scope) => scope switch
    {
        SkillScope.Profile => Profile,
        SkillScope.Global => Global,
        _ => External,
    };
}
