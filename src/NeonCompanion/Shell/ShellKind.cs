using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;

namespace NeonCompanion.Shell;

/// <summary>The shell a <c>run_command</c> line runs in (2026-09-21).</summary>
public enum ShellKind
{
    /// <summary><c>pwsh.exe</c> when installed, else Windows PowerShell 5.1: the default.</summary>
    PowerShell,

    /// <summary><c>cmd.exe</c>: batch syntax, <c>dir</c>, <c>set</c>, redirects the old way.</summary>
    Cmd,

    /// <summary>Git Bash (or any <c>bash.exe</c> on the PATH that is not the WSL launcher): offered only when found.</summary>
    Bash,
}

/// <summary>
/// The setting <c>Shell default</c> (2026-09-21): the three words the operator picks from
/// (<c>powershell</c>, <c>cmd</c>, <c>bash</c>) and their mapping to <see cref="ShellKind"/>, the
/// <see cref="Web.NetworkMode"/> shape. <see cref="Resolve"/> is the one place the saved string
/// becomes the enum: a hand-edited value that is none of them falls back to <see cref="Default"/>
/// with a warning. The same words are the <c>shell</c> argument's enum.
/// </summary>
public static class ShellKinds
{
    /// <summary>PowerShell. The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "powershell";

    /// <summary>The shells in menu order.</summary>
    public static readonly string[] Names = { "powershell", "cmd", "bash" };

    /// <summary>The log category of every shell line: the runner's, the gate's, the tools'.</summary>
    public const string Category = "Shell";

    /// <summary>Trims and ignores case; false (and <see cref="ShellKind.PowerShell"/>) for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out ShellKind kind)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "powershell": kind = ShellKind.PowerShell; return true;
            case "cmd": kind = ShellKind.Cmd; return true;
            case "bash": kind = ShellKind.Bash; return true;
            default: kind = ShellKind.PowerShell; return false;
        }
    }

    /// <summary>The saved word for <paramref name="kind"/>.</summary>
    public static string Name(ShellKind kind) => kind switch
    {
        ShellKind.Cmd => "cmd",
        ShellKind.Bash => "bash",
        _ => "powershell",
    };

    /// <summary>The menu hint next to a shell. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        "powershell" => "pwsh when installed, else Windows PowerShell 5.1",
        "cmd" => "cmd.exe: batch syntax",
        "bash" => "Git Bash, when bash.exe is found",
        _ => "",
    };

    /// <summary>The executable's file name, for the not-installed sentence and the log: <c>powershell.exe</c>, <c>cmd.exe</c>, <c>bash.exe</c>.</summary>
    public static string FileName(ShellKind kind) => kind switch
    {
        ShellKind.Cmd => "cmd.exe",
        ShellKind.Bash => "bash.exe",
        _ => "powershell.exe",
    };

    /// <summary>The shell in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static ShellKind Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.ShellDefault, out var kind))
        {
            return kind;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.ShellDefault)}='{effective.ShellDefault}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        TryParse(Default, out kind);
        return kind;
    }
}
