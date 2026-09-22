using System.Text;
using System.Text.RegularExpressions;
using NeonCompanion.Files;

namespace NeonCompanion.Shell;

/// <summary>
/// The outside-paths police (2026-09-22, the user's ask; the setting <c>Shell police outside paths</c>,
/// on by default): reads the text the model sends a shell — a <c>run_command</c> line, an
/// <c>execute_code</c> script, the text <c>process</c> writes to a background process's stdin — and names
/// the first token that points outside the working directory, so the tool can refuse the call before
/// the gate is asked. It is lexical, and says so: it sees the text, not what runs, so a script that
/// computes a path (<c>os.environ["X"]</c>, a registry read) is not seen; it is a guard against the
/// model's ordinary attempts, paired with tool descriptions and rules that no longer say the shell can
/// reach outside. Pure but for <c>exists</c>, an injected "is there a file or folder here" the
/// single-segment rule needs (the real file system in <see cref="Judge"/>, a fake in <c>PathPoliceTests</c>).
///
/// <para>A command line is cut into its segments first (<see cref="CommandPrefix.Segments"/>: the pieces
/// between <c>&amp;&amp;</c>, <c>|</c>, <c>;</c>…), a script is one piece; each piece is cut into tokens on
/// whitespace, quotes and the shell's other punctuation (<see cref="Delimiters"/>) — never on <c>:</c>,
/// <c>\</c>, <c>/</c>, <c>.</c>, <c>~</c>, <c>$</c> or <c>%</c>, which paths are made of. A quote is a cut,
/// not a bracket: a <c>'</c> in a comment would otherwise swallow the rest of a script, and a quoted
/// path with a space still starts with the piece the rules read (<c>"C:\Program Files\x"</c> is refused
/// as <c>C:\Program</c>). The rules, each pinned:</para>
/// <list type="number">
/// <item>A drive-absolute token (<c>C:\…</c>, <c>C:/…</c>, the separator required so <c>x[a:b]</c> is not one) is outside unless it lies under the root.</item>
/// <item>A UNC token (<c>\\server\share…</c>, <c>//server/share…</c>: a server <em>and</em> a share separator, so a JS <c>//comment</c> is not one) is outside.</item>
/// <item>A bash drive path (<c>/d/Repo/…</c>) reads as <c>D:\Repo\…</c> and takes rule 1, so Git Bash under the root passes.</item>
/// <item>A rooted token (<c>/etc/hosts</c>, <c>\Windows\x</c>) resolves against the root's drive on Windows, so it is an escape in every shell and interpreter: two or more segments are outside; a bare <c>/</c> is outside as a command's first argument (<c>cd /</c>, <c>ls -la /</c>: the drive root) and nothing elsewhere (<c>-replace '\\', '/'</c>, division); a single segment (<c>/s</c>, <c>/MIR</c>, <c>/t:Build</c> — but also <c>/Users</c>) is outside only when something by that name exists at the drive's root, since a switch names nothing on the disk. <c>//…</c> and <c>/*</c> are comments, <c>\\…</c> without a share (<c>\\d+</c>) and a token of separators alone are nothing, and in a script a backslash-rooted token is not read at all — <c>"\t\n"</c> and <c>\d+\s</c> are escapes and regexes far more often than paths.</item>
/// <item>A token with a <c>..</c> segment (<c>../x</c>, <c>sub\..\..\x</c>; <c>...</c> and <c>1..10</c> are not one) resolves against the folder the text runs in and is outside unless it lands under the root.</item>
/// <item><c>~</c>, <c>~/…</c> and <c>~\…</c> are the home folder (<c>~x</c> is a bitwise not).</item>
/// <item>A folder variable anywhere in the text — <see cref="FolderVariables"/> as <c>%NAME%</c>, <c>$env:NAME</c>, <c>${env:NAME}</c>, <c>$NAME</c> or <c>${NAME}</c> — and a runtime call that means the same (<see cref="FolderCalls"/>: <c>Path.home(</c>, <c>expanduser(</c>, <c>GetFolderPath(</c>…) is outside; the app cannot see where it points, and every one of them points away from the root. Any other variable (<c>%PATH%</c>, <c>$env:CI</c>) passes: the user's call, folder variables only.</item>
/// </list>
/// A URL never trips a rule: <c>https://host/path</c> starts with its scheme, not a separator.
/// </summary>
public static partial class PathPolice
{
    /// <summary>The characters that cut the text into tokens, beside whitespace: quotes and the punctuation a shell or a script puts around a path.</summary>
    public const string Delimiters = "\"'`(),;=<>|&[]{}";

    /// <summary>The punctuation trimmed off a token's end (<c>C:\x;</c>); a single sentence-ending dot goes too (<c>C:\x.</c>), the dots of <c>..</c> never.</summary>
    private const string TrailingPunctuation = ",:;?!";

    /// <summary>
    /// The environment variables that name a folder away from the working directory, matched without
    /// case in every spelling a shell or a script reads them by. <c>PROGRAMFILES(X86)</c> is spelled with
    /// its parentheses only in <c>%…%</c>; the other spellings cannot carry them.
    /// </summary>
    public static readonly IReadOnlyList<string> FolderVariables =
    [
        "USERPROFILE", "HOMEPATH", "HOMEDRIVE", "HOME", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "TMPDIR",
        "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432", "PROGRAMDATA", "ALLUSERSPROFILE", "PUBLIC", "ONEDRIVE",
        "SYSTEMROOT", "SYSTEMDRIVE", "WINDIR",
    ];

    /// <summary>The calls that read a folder variable for a script (<c>Path.home()</c>, <c>os.path.expanduser</c>, <c>[Environment]::GetFolderPath</c>, <c>tempfile.gettempdir()</c>…), matched without case.</summary>
    public static readonly IReadOnlyList<string> FolderCalls =
    [
        "expanduser(", "Path.home(", "homedir(", "tmpdir(", "gettempdir(", "GetFolderPath(", "GetTempPath(",
    ];

    // %NAME% | $env:NAME | ${env:NAME} | $NAME | ${NAME}, the name not continued by a word character ($HOMEPAGE is not $HOME).
    [GeneratedRegex(@"(?:%(?:USERPROFILE|HOMEPATH|HOMEDRIVE|HOME|APPDATA|LOCALAPPDATA|TEMP|TMP|TMPDIR|PROGRAMFILES|PROGRAMFILES\(X86\)|PROGRAMW6432|PROGRAMDATA|ALLUSERSPROFILE|PUBLIC|ONEDRIVE|SYSTEMROOT|SYSTEMDRIVE|WINDIR)%)|(?:\$\{?(?:env:)?(?:USERPROFILE|HOMEPATH|HOMEDRIVE|HOME|APPDATA|LOCALAPPDATA|TEMP|TMP|TMPDIR|PROGRAMFILES|PROGRAMW6432|PROGRAMDATA|ALLUSERSPROFILE|PUBLIC|ONEDRIVE|SYSTEMROOT|SYSTEMDRIVE|WINDIR)(?![A-Za-z0-9_])\}?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FolderVariablePattern();

    // \\server\share or //server/share: two separators, a server, a separator — the share is what makes it a path.
    [GeneratedRegex(@"^(?:\\\\|//)[^\\/\s]+[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex UncPattern();

    /// <summary>
    /// The first token of <paramref name="text"/> that names a path outside <paramref name="root"/>, or
    /// null when every path stays under it. <paramref name="baseFolder"/> is the folder a relative path
    /// resolves against (the command's workdir; the root for a script or a process); <paramref name="isScript"/>
    /// reads the text as one piece and leaves backslash-rooted tokens alone; <paramref name="exists"/>
    /// answers whether a full path names a file or a folder (the single-segment rule).
    /// </summary>
    public static string? FirstOutside(string text, string root, string baseFolder, bool isScript, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(baseFolder);
        ArgumentNullException.ThrowIfNull(exists);
        var variable = FolderVariablePattern().Match(text);
        if (variable.Success)
        {
            return variable.Value;
        }

        foreach (string call in FolderCalls)
        {
            if (text.Contains(call, StringComparison.OrdinalIgnoreCase))
            {
                return call;
            }
        }

        foreach (string piece in isScript ? [text] : CommandPrefix.Segments(text))
        {
            var tokens = Tokens(piece);
            int firstArgument = FirstArgument(tokens);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (IsOutside(tokens[i], root, baseFolder, isScript, firstArgument: !isScript && i == firstArgument, exists))
                {
                    return tokens[i];
                }
            }
        }

        return null;
    }

    /// <summary>The app's entry: <see cref="FirstOutside"/> over the sandbox's root with the real file system answering <c>exists</c>.</summary>
    public static string? Judge(string text, WorkingDirectory files, string baseFolder, bool isScript)
    {
        ArgumentNullException.ThrowIfNull(files);
        return FirstOutside(text, files.Root, baseFolder, isScript, static path => Directory.Exists(path) || File.Exists(path));
    }

    /// <summary>
    /// Whether one token, as cut by <see cref="Tokens"/>, names a path outside the root (rules 1 to 6);
    /// <paramref name="firstArgument"/> says it is a command's first argument after its options, where a
    /// bare <c>/</c> is the drive root.
    /// </summary>
    public static bool IsOutside(string token, string root, string baseFolder, bool isScript, bool firstArgument, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(baseFolder);
        ArgumentNullException.ThrowIfNull(exists);
        if (token.Length == 0)
        {
            return false;
        }

        // 6. The home folder.
        if (token == "~" || token.StartsWith("~/", StringComparison.Ordinal) || token.StartsWith("~\\", StringComparison.Ordinal))
        {
            return true;
        }

        // 1. A drive-absolute path: under the root or not.
        if (IsDriveAbsolute(token))
        {
            return !Under(root, Collapse(token));
        }

        // In a script a token that opens with a backslash is an escape ("\t\n") or a regex (\d+\s) far more often than a path.
        if (token[0] == '\\' && isScript)
        {
            return false;
        }

        // 2. A UNC path is never under a local root; a UNC root would be spelled the same, so the judge still runs.
        if (UncPattern().IsMatch(token))
        {
            return !Under(root, token);
        }

        if (token[0] == '/' || token[0] == '\\')
        {
            // 3. Git Bash's drive form.
            if (token.Length >= 3 && token[0] == '/' && char.IsAsciiLetter(token[1]) && token[2] == '/')
            {
                return !Under(root, char.ToUpperInvariant(token[1]) + ":\\" + Collapse(token[3..]));
            }

            // Comments, not paths: // and /*; two backslashes without a share (\\d+, a regex) are not a UNC path either.
            if (token.StartsWith("//", StringComparison.Ordinal) || token.StartsWith("/*", StringComparison.Ordinal) || token.StartsWith("\\\\", StringComparison.Ordinal))
            {
                return false;
            }

            // 4. Rooted. Separators alone: the drive root as a command's first argument, nothing elsewhere.
            if (token.AsSpan().IndexOfAnyExcept(Separators) < 0)
            {
                return firstArgument && token == "/";
            }

            string drive = Path.GetPathRoot(root) ?? root;
            if (token.IndexOfAny(Separators, 1) >= 0)
            {
                return !Under(root, Path.Combine(drive, Collapse(token)[1..]));
            }

            // One segment: a switch (dir /s, msbuild /t:Build) names nothing on the disk; /Users does.
            return Full(Path.Combine(drive, token[1..])) is { } rooted && !Under(root, rooted) && exists(rooted);
        }

        // 5. A .. segment: resolved from where the text runs.
        if (HasParentSegment(token))
        {
            return !Under(root, Path.Combine(baseFolder, Collapse(token)));
        }

        return false;
    }

    /// <summary>The tokens of <paramref name="text"/>: cut on whitespace and <see cref="Delimiters"/>, trailing punctuation off, the empty ones dropped.</summary>
    public static IReadOnlyList<string> Tokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || Delimiters.Contains(c))
            {
                Flush(tokens, current);
            }
            else
            {
                current.Append(c);
            }
        }

        Flush(tokens, current);
        return tokens;
    }

    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>The index of a command's first argument: the token after its program and its options (<c>-x</c>, <c>--x</c>), or -1.</summary>
    private static int FirstArgument(IReadOnlyList<string> tokens)
    {
        for (int i = 1; i < tokens.Count; i++)
        {
            if (tokens[i][0] != '-')
            {
                return i;
            }
        }

        return -1;
    }

    private static void Flush(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        string token = current.ToString();
        current.Clear();
        int end = token.Length;
        while (end > 1)
        {
            char c = token[end - 1];
            if (TrailingPunctuation.Contains(c))
            {
                end--;
            }
            else if (c == '.' && token[end - 2] != '.' && token[end - 2] != '/' && token[end - 2] != '\\')
            {
                // One sentence-ending dot after a name (C:\x.) goes; the dots of .., ..\.. and x... stay.
                end--;
            }
            else
            {
                break;
            }
        }

        tokens.Add(token[..end]);
    }

    private static bool IsDriveAbsolute(string token) =>
        token.Length >= 3 && char.IsAsciiLetter(token[0]) && token[1] == ':' && (token[2] == '\\' || token[2] == '/');

    /// <summary>Whether a segment of <paramref name="token"/> is exactly <c>..</c>.</summary>
    private static bool HasParentSegment(string token)
    {
        int at = 0;
        while (true)
        {
            int next = token.IndexOfAny(Separators, at);
            int length = (next < 0 ? token.Length : next) - at;
            if (length == 2 && token[at] == '.' && token[at + 1] == '.')
            {
                return true;
            }

            if (next < 0)
            {
                return false;
            }

            at = next + 1;
        }
    }

    /// <summary>Repeated separators folded to one past the first character, so a Python <c>"D:\\Repo\\x"</c> reads as the path it means; a leading <c>\\</c> (UNC) is kept.</summary>
    private static string Collapse(string token)
    {
        if (token.IndexOf("\\\\", 1, StringComparison.Ordinal) < 0 && token.IndexOf("//", 1, StringComparison.Ordinal) < 0)
        {
            return token;
        }

        var sb = new StringBuilder(token.Length);
        sb.Append(token[0]);
        for (int i = 1; i < token.Length; i++)
        {
            char c = token[i];
            if (i > 1 && (c == '\\' || c == '/') && (sb[^1] == '\\' || sb[^1] == '/'))
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Whether <paramref name="path"/>, made full, lies under <paramref name="root"/>; a path that cannot be made full is not.</summary>
    private static bool Under(string root, string path) => Full(path) is { } full && WorkingDirectory.IsInside(root, full);

    private static string? Full(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
