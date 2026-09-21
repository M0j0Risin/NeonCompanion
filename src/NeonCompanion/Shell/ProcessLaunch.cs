namespace NeonCompanion.Shell;

/// <summary>
/// Everything <see cref="ShellRunner.Start"/> needs to start one child (2026-09-21), built by a
/// pure function (<see cref="ShellCommandLine.For"/>, the code tool's launch) and tested as data.
/// Exactly one of <see cref="ArgumentList"/> and <see cref="Arguments"/> is set: the list is quoted
/// by .NET (MSVCRT rules, what every interpreter parses); the raw string is for <c>cmd.exe</c>,
/// whose <c>/s /c "…"</c> the list would re-quote and break.
/// </summary>
/// <param name="Executable">The full path of the program.</param>
/// <param name="ArgumentList">The arguments, one string each, or null with <paramref name="Arguments"/> set.</param>
/// <param name="Arguments">The raw argument string, or null with <paramref name="ArgumentList"/> set.</param>
/// <param name="WorkingDirectory">The folder the child starts in (the sandbox root or a folder under it).</param>
/// <param name="Label">The command as the model sent it: the header's, the log's, the alert's.</param>
/// <param name="Kind">The word after the exit code in the header: <c>powershell</c>, <c>cmd</c>, <c>bash</c>, or a language.</param>
/// <param name="Environment">Variables set on top of the inherited ones (<see cref="ChildEnvironment"/> adds its own); null for none.</param>
public sealed record ProcessLaunch(
    string Executable,
    IReadOnlyList<string>? ArgumentList,
    string? Arguments,
    string WorkingDirectory,
    string Label,
    string Kind,
    IReadOnlyDictionary<string, string>? Environment = null);
