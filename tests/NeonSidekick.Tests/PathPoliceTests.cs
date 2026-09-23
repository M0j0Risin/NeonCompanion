using NeonSidekick.Shell;

namespace NeonSidekick.Tests;

/// <summary>
/// The outside-paths police (2026-09-22): pure over a made-up root, <c>exists</c> a fake that knows a
/// few root folders — every rule of <see cref="PathPolice"/> with a token it refuses and one it lets by.
/// </summary>
public sealed class PathPoliceTests
{
    private const string Root = @"D:\Repo\Project";
    private const string Sub = @"D:\Repo\Project\sub";

    /// <summary>The disk as the single-segment rule sees it: <c>D:\Users</c> and <c>D:\Windows</c> exist, nothing else does.</summary>
    private static bool Exists(string path) =>
        string.Equals(path, @"D:\Users", StringComparison.OrdinalIgnoreCase) || string.Equals(path, @"D:\Windows", StringComparison.OrdinalIgnoreCase);

    private static string? Command(string text, string baseFolder = Root) => PathPolice.FirstOutside(text, Root, baseFolder, isScript: false, Exists);

    private static string? Script(string text) => PathPolice.FirstOutside(text, Root, Root, isScript: true, Exists);

    [Theory]
    [InlineData(@"type C:\Windows\win.ini", @"C:\Windows\win.ini")]
    [InlineData(@"type c:/windows/win.ini", @"c:/windows/win.ini")]
    [InlineData(@"copy x D:\Repo\Other\y", @"D:\Repo\Other\y")]
    [InlineData(@"copy x D:\Repo\Project2\y", @"D:\Repo\Project2\y")]   // a sibling that merely starts with the root's spelling
    [InlineData(@"dir ""C:\Program Files""", @"C:\Program")]   // a quote is a cut: the piece the rule reads
    [InlineData(@"\\server\share\x", @"\\server\share\x")]
    [InlineData(@"//server/share/x", @"//server/share/x")]
    [InlineData(@"cat /etc/hosts", "/etc/hosts")]
    [InlineData(@"type \Windows\win.ini", @"\Windows\win.ini")]
    [InlineData(@"ls /c/Users/me", "/c/Users/me")]
    [InlineData(@"cd /", "/")]
    [InlineData(@"ls -la /", "/")]
    [InlineData(@"Get-ChildItem -Path /", "/")]
    [InlineData(@"cd /Users", "/Users")]   // one segment that exists at the drive's root
    [InlineData(@"dir /Windows", "/Windows")]
    [InlineData(@"cd ..", "..")]
    [InlineData(@"type ..\secret.txt", @"..\secret.txt")]
    [InlineData(@"type sub/../../x", "sub/../../x")]
    [InlineData(@"ls ~", "~")]
    [InlineData(@"ls ~/Documents", "~/Documents")]
    [InlineData(@"ls ~\Documents", @"~\Documents")]
    [InlineData(@"dir %USERPROFILE%\Desktop", "%USERPROFILE%")]
    [InlineData(@"dir %ProgramFiles(x86)%", "%ProgramFiles(x86)%")]
    [InlineData(@"Get-ChildItem $env:TEMP", "$env:TEMP")]
    [InlineData(@"Get-ChildItem ${env:APPDATA}\x", "${env:APPDATA}")]
    [InlineData(@"ls $HOME/x", "$HOME")]
    [InlineData(@"ls ${HOME}/x", "${HOME}")]
    [InlineData(@"echo $tmpdir", "$tmpdir")]
    [InlineData(@"[Environment]::GetFolderPath('Desktop')", "GetFolderPath(")]
    public void ACommandLine_NamingAnOutsidePath_IsRefused_AndTheTokenIsNamed(string command, string token) =>
        Assert.Equal(token, Command(command));

    [Theory]
    [InlineData(@"dir")]
    [InlineData(@"type D:\Repo\Project\notes.txt")]
    [InlineData(@"type d:/repo/project/sub/x")]
    [InlineData(@"type D:\\Repo\\Project\\x")]   // an escaped spelling of the same path
    [InlineData(@"ls /d/Repo/Project/sub")]   // Git Bash's drive form, under the root
    [InlineData(@"type sub\..\notes.txt")]
    [InlineData(@"type sub/deeper/../x")]
    [InlineData(@"dir /s /b")]
    [InlineData(@"msbuild /t:Build /p:Configuration=Release /nologo")]
    [InlineData(@"robocopy src dst /MIR /XD .git")]
    [InlineData(@"findstr /i /c:""foo"" *.cs")]
    [InlineData(@"cd /etc")]   // one segment, nothing by that name at the drive's root: a switch as far as the text says
    [InlineData(@"$x -replace '\\', '/'")]   // a bare / that is not the first argument, and \\ alone
    [InlineData(@"echo a / b")]
    [InlineData(@"git log --format=%H")]
    [InlineData(@"echo %PATH% $env:CI $HOMEBREW ${env:PROCESSOR_LEVEL}")]
    [InlineData(@"curl https://example.invalid/api/users")]
    [InlineData(@"echo 1..10 ... x[a:b] ~x")]
    [InlineData(@"echo //TODO /* comment */")]
    [InlineData(@"python -c ""print('\\d+')""")]
    public void ACommandLine_UnderTheRoot_Passes(string command) =>
        Assert.Null(Command(command));

    [Fact]
    public void ARelativePath_ResolvesFromTheWorkdir()
    {
        Assert.Equal("..", Command("cd ..", Root));
        Assert.Null(Command("cd ..", Sub));   // sub\.. is the root
        Assert.Equal(@"..\..", Command(@"cd ..\..", Sub));
        Assert.Null(Command(@"type ..\notes.txt", Sub));
    }

    [Fact]
    public void ACompoundLine_IsJudgedSegmentBySegment()
    {
        Assert.Equal("/", Command("dotnet build && cd / && dir"));   // the first argument of its own segment
        Assert.Null(Command("dotnet build && dir | findstr x"));
        Assert.Equal(@"C:\x", Command("echo a; type C:\\x"));
    }

    [Theory]
    [InlineData("open(r'C:\\Users\\x.txt').read()", @"C:\Users\x.txt")]
    [InlineData("open(\"C:\\\\Users\\\\x.txt\")", @"C:\\Users\\x.txt")]
    [InlineData("fs.readFileSync('/etc/passwd')", "/etc/passwd")]
    [InlineData("fs.readFileSync('//server/share/x')", "//server/share/x")]
    [InlineData("open('../secret')", "../secret")]
    [InlineData("os.listdir('~')", "~")]
    [InlineData("os.path.expanduser('~/x')", "expanduser(")]
    [InlineData("from pathlib import Path\nprint(Path.home())", "Path.home(")]
    [InlineData("require('os').homedir()", "homedir(")]
    [InlineData("os.tmpdir()", "tmpdir(")]
    [InlineData("tempfile.gettempdir()", "gettempdir(")]
    [InlineData("[IO.Path]::GetTempPath()", "GetTempPath(")]
    [InlineData("Get-ChildItem $env:LOCALAPPDATA", "$env:LOCALAPPDATA")]
    [InlineData("x = '%SystemRoot%'", "%SystemRoot%")]
    [InlineData("shutil.copy('a', '/Users/me/b')", "/Users/me/b")]
    public void AScript_NamingAnOutsidePath_IsRefused(string code, string token) =>
        Assert.Equal(token, Script(code));

    [Theory]
    [InlineData("print('hi')")]
    [InlineData("open('notes.txt').read()")]
    [InlineData("open(r'D:\\Repo\\Project\\notes.txt')")]
    [InlineData("open('D:\\\\Repo\\\\Project\\\\sub\\\\x')")]
    [InlineData("open('sub/../notes.txt')")]
    [InlineData("print(\"\\t\\n\")")]   // escapes, not rooted paths
    [InlineData("re.match(r'\\d+\\s', x)")]   // a regex
    [InlineData("re.match('\\\\d+\\\\s', x)")]   // \\d+\\s is not \\server\\share
    [InlineData("const n = a / b; // a comment\n/* block */")]
    [InlineData("fetch('https://host/api/users')")]
    [InlineData("for i in range(1, 10): x[a:b]")]
    [InlineData("y = ~x; z = ...")]
    [InlineData("1..10 | ForEach-Object { $_ }")]
    [InlineData("Write-Output $env:NEONSIDEKICK_BRIDGE_ADDRESS $env:CI")]
    [InlineData("os.environ['PATH']")]
    [InlineData("route('/api')")]   // one rooted segment, nothing at the drive's root by that name
    [InlineData("x = 10/2")]
    public void AScript_UnderTheRoot_Passes(string code) =>
        Assert.Null(Script(code));

    [Fact]
    public void TheFirstOffender_IsTheOneNamed()
    {
        Assert.Equal(@"C:\a", Command(@"type C:\a D:\b ~"));
        Assert.Equal("%TEMP%", Command(@"copy C:\a %TEMP%"));   // a folder variable anywhere in the text comes first: it is read over the whole line
    }

    [Fact]
    public void Tokens_CutOnWhitespaceQuotesAndPunctuation_AndTrimTrailingMarks()
    {
        Assert.Equal(["dir", @"C:\x", "y", "--out", @"C:\z", "a", "b", "."], PathPolice.Tokens(@"dir ""C:\x"" 'y' --out=C:\z (a, b)."));
        Assert.Equal(["..", "...", "x..", "..", @"..\..", @"C:\x", "y"], PathPolice.Tokens(@".. ... x.. ..; ..\.. C:\x. y.,"));   // one sentence-ending dot goes, the dots of .. never
        Assert.Empty(PathPolice.Tokens("  \n\t "));
        Assert.Equal("\"'`(),;=<>|&[]{}", PathPolice.Delimiters);
    }

    [Fact]
    public void TheLists_ArePinned()
    {
        Assert.Equal(["USERPROFILE", "HOMEPATH", "HOMEDRIVE", "HOME", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "TMPDIR", "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432", "PROGRAMDATA", "ALLUSERSPROFILE", "PUBLIC", "ONEDRIVE", "SYSTEMROOT", "SYSTEMDRIVE", "WINDIR"], PathPolice.FolderVariables);
        Assert.Equal(["expanduser(", "Path.home(", "homedir(", "tmpdir(", "gettempdir(", "GetFolderPath(", "GetTempPath("], PathPolice.FolderCalls);
        foreach (string name in PathPolice.FolderVariables)
        {
            Assert.Equal("%" + name + "%", Command("echo %" + name + "%"));
            if (!name.Contains('('))
            {
                Assert.Equal("$env:" + name, Command("echo $env:" + name));
                Assert.Equal("$" + name.ToLowerInvariant(), Command("echo $" + name.ToLowerInvariant()));
            }
        }
    }

    [Fact]
    public void Judge_ReadsTheRealDisk()
    {
        string dir = Path.Combine(Path.GetTempPath(), "neon-police-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = new Files.WorkingDirectory(() => dir, TimeProvider.System);
            Assert.Null(PathPolice.Judge("dir " + dir, files, dir, isScript: false));
            Assert.Equal("..", PathPolice.Judge("cd ..", files, dir, isScript: false));
            Assert.Equal("/Windows", PathPolice.Judge("dir /Windows", files, dir, isScript: false));   // exists on every Windows system drive; the temp folder lives there
            Assert.Null(PathPolice.Judge("dir /nothing-here-" + Guid.NewGuid().ToString("N"), files, dir, isScript: false));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
