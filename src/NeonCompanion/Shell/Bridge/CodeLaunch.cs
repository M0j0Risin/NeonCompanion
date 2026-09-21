namespace NeonCompanion.Shell.Bridge;

/// <summary>The two files a run writes into its folder: the script and, beside it where the interpreter finds it, the bridge module.</summary>
/// <param name="ScriptName">The script's file name (<c>script.py</c>, <c>script.js</c>, <c>script.ps1</c>).</param>
/// <param name="ScriptText">The script's text: the code as sent, PowerShell's under the same wrapper as <c>run_command</c> with the module imported first.</param>
/// <param name="ModulePath">The module's path relative to the run folder: <c>neon_tools.py</c>, <c>node_modules\neon_tools\index.js</c>, <c>NeonTools.psm1</c>.</param>
/// <param name="ModuleText">The module's text, from the embedded resource.</param>
public sealed record CodeFiles(string ScriptName, string ScriptText, string ModulePath, string ModuleText);

/// <summary>
/// How an <c>execute_code</c> run is laid out and started (2026-09-21), pure over its inputs and
/// pinned by <c>CodeLaunchTests</c>. The script and the module go into one folder per run, placed
/// so that each interpreter finds the module without an environment path: Python adds the script's
/// folder to <c>sys.path</c>; Node resolves a bare <c>require("neon_tools")</c> through the folder's
/// own <c>node_modules</c>; PowerShell imports the module by its full path from the wrapper. The
/// bridge's address and token are the only variables the launch adds. PowerShell runs under the
/// same wrapper as <c>run_command</c> (<see cref="ShellCommandLine.PowerShellScript"/>) from a file,
/// so 5.1's CLIXML never reaches the result and the exit code follows the same rules.
/// </summary>
public static class CodeLaunch
{
    public const string PythonModuleResource = "bridge/neon_tools.py";
    public const string NodeModuleResource = "bridge/neon_tools.js";
    public const string PowerShellModuleResource = "bridge/NeonTools.psm1";

    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>The embedded module for <paramref name="language"/>, read once from the manifest.</summary>
    public static string ModuleText(CodeLanguage language)
    {
        string name = language switch
        {
            CodeLanguage.Python => PythonModuleResource,
            CodeLanguage.Node => NodeModuleResource,
            _ => PowerShellModuleResource,
        };
        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out string? text))
            {
                using var stream = typeof(CodeLaunch).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The bridge module " + name + " is not embedded.");
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
                Cache[name] = text;
            }

            return text;
        }
    }

    /// <summary>The script and the module for <paramref name="code"/> under <paramref name="language"/>, to be written into <paramref name="runFolder"/>.</summary>
    public static CodeFiles Files(CodeLanguage language, string code, string runFolder)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(runFolder);
        string module = ModuleText(language);
        return language switch
        {
            CodeLanguage.Python => new CodeFiles("script.py", code, "neon_tools.py", module),
            CodeLanguage.Node => new CodeFiles("script.js", code, Path.Combine("node_modules", "neon_tools", "index.js"), module),
            _ => new CodeFiles("script.ps1", PowerShellScript(code, Path.Combine(runFolder, "NeonTools.psm1")), "NeonTools.psm1", module),
        };
    }

    /// <summary>The PowerShell script: <c>Import-Module</c> of the module by its full path (single quotes doubled), then the code, under the <c>run_command</c> wrapper. Pinned.</summary>
    public static string PowerShellScript(string code, string modulePath) =>
        ShellCommandLine.PowerShellScript("Import-Module -Force '" + modulePath.Replace("'", "''", StringComparison.Ordinal) + "'\n" + code);

    /// <summary>The launch: the interpreter over the script in <paramref name="runFolder"/>, starting in <paramref name="workingDirectory"/>, the bridge in its environment.</summary>
    public static ProcessLaunch For(CodeLanguage language, string executable, string runFolder, string scriptName, string workingDirectory, string address, string token, string label)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(runFolder);
        ArgumentNullException.ThrowIfNull(scriptName);
        string script = Path.Combine(runFolder, scriptName);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BridgeServer.AddressVariable] = address,
            [BridgeServer.TokenVariable] = token,
        };
        string name = CodeLanguages.Name(language);
        return language switch
        {
            CodeLanguage.Python => new ProcessLaunch(executable, ["-X", "utf8", script], null, workingDirectory, label, name, environment),
            CodeLanguage.Node => new ProcessLaunch(executable, [script], null, workingDirectory, label, name, environment),
            _ => new ProcessLaunch(executable, [.. ShellCommandLine.PowerShellSwitches, "-File", script], null, workingDirectory, label, name, environment),
        };
    }
}
