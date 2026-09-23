using System.Diagnostics;

namespace NeonSidekick.Shell;

/// <summary>
/// The environment every child gets (2026-09-21): the app's own, inherited whole — there is no
/// secret model to strip anything by, and a build or a test wants the user's PATH and variables —
/// plus <see cref="Overrides"/> on top: no colour and no pager (the output is read, not shown;
/// ANSI codes and a waiting <c>less</c> would only get in the way), UTF-8 and unbuffered Python,
/// no git credential prompt (nothing could answer it on a hidden console), a quiet dotnet. A
/// launch's own variables (<see cref="ProcessLaunch.Environment"/>: the bridge's address and token)
/// go on last. Pure over <see cref="ProcessStartInfo.Environment"/>, the child's own snapshot —
/// nothing reads the process environment here.
/// </summary>
public static class ChildEnvironment
{
    /// <summary>The variables set on every child, in the order they are applied. Pinned.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Overrides =
    [
        new("NO_COLOR", "1"),
        new("FORCE_COLOR", "0"),
        new("CLICOLOR", "0"),
        new("TERM", "dumb"),
        new("PYTHONIOENCODING", "utf-8"),
        new("PYTHONUTF8", "1"),
        new("PYTHONUNBUFFERED", "1"),
        new("GIT_TERMINAL_PROMPT", "0"),
        new("GIT_PAGER", "cat"),
        new("PAGER", "cat"),
        new("DOTNET_NOLOGO", "1"),
        new("DOTNET_CLI_TELEMETRY_OPTOUT", "1"),
    ];

    /// <summary>Applies <see cref="Overrides"/>, then <paramref name="launch"/>'s own variables, to <paramref name="start"/>.</summary>
    public static void Apply(ProcessStartInfo start, ProcessLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(launch);
        foreach (var (name, value) in Overrides)
        {
            start.Environment[name] = value;
        }

        if (launch.Environment is { } own)
        {
            foreach (var (name, value) in own)
            {
                start.Environment[name] = value;
            }
        }
    }
}
