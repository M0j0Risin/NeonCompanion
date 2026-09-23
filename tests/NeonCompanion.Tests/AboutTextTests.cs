using System.Text.RegularExpressions;
using NeonCompanion.App;
using NeonCompanion.Llm;
using NeonCompanion.Llm.Tools;
using NeonCompanion.Speech;
using Spectre.Console.Testing;

namespace NeonCompanion.Tests;

public class AboutTextTests
{
    private static readonly AboutFacts Facts = new(
        "0.2.0",
        ".NET 10.0.0",
        NativeAot: true,
        "x64",
        "Microsoft Windows 10.0.26200",
        @"D:\Apps\NeonCompanion\NeonCompanion.exe",
        @"C:\Users\chris\.neoncompanion",
        @"C:\Users\chris\.neoncompanion\profiles\default",
        @"C:\Users\chris\.neoncompanion\models");

    /// <summary>The repository root: the folder above the test binaries that holds the solution file.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NeonCompanion.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    // ── The pinned lists against the repository ─────────────────────────────

    /// <summary>Every package the project file names has a row carrying that version, so a bump fails here until the tab follows.</summary>
    [Fact]
    public void Components_CarryTheProjectFilesPackageVersions()
    {
        string csproj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "NeonCompanion", "NeonCompanion.csproj"));
        var references = Regex.Matches(csproj, "<PackageReference Include=\"([^\"]+)\" Version=\"([^\"]+)\"");
        Assert.NotEmpty(references);

        // Which row a package id folds into: the runtimes sit on their binding's row, the OpenAI adapter on the abstraction's.
        var rows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Spectre.Console"] = "Spectre.Console",
            ["Microsoft.Extensions.AI"] = "Microsoft.Extensions.AI (+ .OpenAI)",
            ["Microsoft.Extensions.AI.OpenAI"] = "Microsoft.Extensions.AI (+ .OpenAI)",
            ["Whisper.net"] = "Whisper.net",
            ["Whisper.net.Runtime"] = "Whisper.net",
            ["Whisper.net.Runtime.NoAvx"] = "Whisper.net",
            ["Vosk"] = "Vosk",
            ["PhotoSauce.MagicScaler"] = "PhotoSauce.MagicScaler",
            ["KokoroSharp"] = "KokoroSharp",
            ["Microsoft.ML.OnnxRuntime"] = "Microsoft.ML.OnnxRuntime",
            ["Markdig"] = "Markdig",
            ["Microsoft.Data.Sqlite"] = "Microsoft.Data.Sqlite",
            ["ModelContextProtocol.Core"] = "ModelContextProtocol.Core",
            ["LibGit2Sharp"] = "LibGit2Sharp",
            ["LibGit2Sharp.NativeBinaries"] = "LibGit2Sharp.NativeBinaries",
            ["Microsoft.Data.SqlClient"] = "Microsoft.Data.SqlClient",
            ["Microsoft.SqlServer.TransactSql.ScriptDom"] = "SqlServer.TransactSql.ScriptDom",   // the package id is 41 characters, past the 40-cell name column
        };
        foreach (Match reference in references)
        {
            string id = reference.Groups[1].Value;
            Assert.True(rows.TryGetValue(id, out var row), $"{id} is referenced by the project but has no row in AboutText.Components (add one, and map it here)");
            var component = Assert.Single(AboutText.Components, c => c.Name == row);
            Assert.True(component.Version == reference.Groups[2].Value, $"{row}: AboutText.Components says {component.Version}, the project file {reference.Groups[2].Value}");
        }

        // Every mapped row exists even when the project drops a package: then the row is stale and this points at it.
        foreach (string row in rows.Values.Distinct())
        {
            Assert.Contains(references.Cast<Match>(), r => rows[r.Groups[1].Value] == row);
        }
    }

    /// <summary>The transitive OpenAI SDK's row matches what the restore resolved, when the restore's file is there to read.</summary>
    [Fact]
    public void Components_OpenAiSdkRow_MatchesTheResolvedPackage()
    {
        string assets = Path.Combine(RepoRoot(), "src", "NeonCompanion", "obj", "project.assets.json");
        var row = Assert.Single(AboutText.Components, c => c.Name == "OpenAI (.NET SDK)");
        if (!File.Exists(assets))
        {
            return;
        }

        var resolved = Regex.Match(File.ReadAllText(assets), "\"OpenAI/([0-9][^\"]*)\"");
        Assert.True(resolved.Success, "the restore names no OpenAI package");
        Assert.Equal(resolved.Groups[1].Value, row.Version);
    }

    [Fact]
    public void Components_ModelRows_NameTheStoresFiles()
    {
        var silero = Assert.Single(AboutText.Components, c => c.Name == "Silero VAD");   // the file name left the row 2026-09-17; the version still tracks the store's file
        Assert.Contains("v" + silero.Version, ModelStore.SileroFileName);
        // The Whisper and Vosk rows name the component alone (2026-09-17): the file lists made the
        // NoWrap name column 107 cells wide and pushed the Role column off a normal window.
        var whisper = Assert.Single(AboutText.Components, c => c.Name == "Whisper ggml models");
        Assert.Equal(AboutText.Unversioned, whisper.Version);
        var vosk = Assert.Single(AboutText.Components, c => c.Name == "Vosk models");
        Assert.Equal(AboutText.Unversioned, vosk.Version);
        Assert.All(AboutText.Components, c => Assert.DoesNotContain(ModelStore.WhisperModelNames.Concat(ModelStore.VoskModelNames), name => c.Name.Contains(name, StringComparison.Ordinal)));
        Assert.Equal("wake-word models, downloaded on first use", vosk.Role);
        var kokoro = Assert.Single(AboutText.Components, c => c.Name == "Kokoro-82M in-process");
        Assert.Contains("downloaded on first use", kokoro.Role);
        // The TTS server row and the timezone-table row went 2026-09-20 (the user's call); the order is the user's.
        Assert.DoesNotContain(AboutText.Components, c => c.Name.StartsWith("CLDR", StringComparison.Ordinal) || c.Name.Contains("Kokoro-FastAPI", StringComparison.Ordinal));
        Assert.Equal("MCP client", Assert.Single(AboutText.Components, c => c.Name == "ModelContextProtocol.Core").Role);
        Assert.Equal(
            ["Spectre.Console", "Microsoft.Extensions.AI (+ .OpenAI)", "OpenAI (.NET SDK)", "Microsoft.ML.OnnxRuntime", "Microsoft.Data.Sqlite", "ModelContextProtocol.Core", "LibGit2Sharp", "LibGit2Sharp.NativeBinaries", "Microsoft.Data.SqlClient", "SqlServer.TransactSql.ScriptDom", "KokoroSharp", "Kokoro-82M in-process", "Whisper.net", "Whisper ggml models", "Vosk", "Vosk models", "Silero VAD", "PhotoSauce.MagicScaler", "Markdig"],
            AboutText.Components.Select(c => c.Name));
        Assert.Equal(19, AboutText.Components.Count);   // SqlClient and ScriptDom since 2026-09-23
        Assert.All(AboutText.Components, c => Assert.False(string.IsNullOrWhiteSpace(c.License)));
    }

    [Fact]
    public void LicenseText_IsTheRepositoryLicense()
    {
        // The file embedded by the project file (GPL-3.0 since 2026-09-20), LF-normalised with one trailing line break.
        string license = File.ReadAllText(Path.Combine(RepoRoot(), "LICENSE")).Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        Assert.Equal(license, AboutText.LicenseText);
        Assert.StartsWith("                    GNU GENERAL PUBLIC LICENSE\n                       Version 3, 29 June 2007\n", AboutText.LicenseText);
        Assert.Equal("GNU GPL v3", AboutText.LicenseName);
        Assert.Equal("LICENSE", AboutText.LicenseResourceName);
    }

    // ── The lines ───────────────────────────────────────────────────────────

    [Fact]
    public void TitleAndCopyright()
    {
        Assert.Equal("NeonCompanion 0.2.0", AboutText.TitleLine(Facts));
        Assert.Equal("© 2026 Christopher Nelson · GNU GPL v3 (the License tab)", AboutText.CopyrightLine);
    }

    [Fact]
    public void RuntimeLine_NamesTheBuild()
    {
        Assert.Equal(".NET 10.0.0 · native AOT · x64", AboutText.RuntimeLine(Facts));
        Assert.Equal(".NET 10.0.0 · JIT · x64", AboutText.RuntimeLine(Facts with { NativeAot = false }));
    }

    [Fact]
    public void ServersLine_IsTheProbesPortsInOrder_WithTheirOwners()
    {
        Assert.Equal(
            "any OpenAI-compatible /v1 endpoint; /server looks on 127.0.0.1 for LM Studio :1234, vLLM :8000, SGLang :30000, llama.cpp :8080, Ollama :11434, Unsloth :8888",
            AboutText.ServersLine());
        foreach (int port in LlmEndpointProbe.CandidatePorts)
        {
            Assert.Contains(LlmServer.PortNames[port] + " :" + port, AboutText.ServersLine());
        }
    }

    [Fact]
    public void AboutRows_InOrder_UnknownExecutableNamed()
    {
        var rows = AboutText.AboutRows(Facts);
        Assert.Equal(["Runtime", "OS", "Executable", "Home", "Profile", "Models", "LLM servers", "Speech"], rows.Select(r => r.Label));
        Assert.Equal(@"D:\Apps\NeonCompanion\NeonCompanion.exe", rows[2].Value);
        Assert.Equal(@"C:\Users\chris\.neoncompanion\profiles\default", rows[4].Value);
        Assert.Equal(AboutText.SpeechLine, rows[7].Value);
        Assert.Equal("(unknown)", AboutText.AboutRows(Facts with { ExecutablePath = null })[2].Value);
    }

    [Fact]
    public void Lines_AreTheThreeTabs_Headed()
    {
        string[] lines = AboutText.Lines(Facts).ToArray();
        Assert.Equal("General", lines[0]);
        Assert.Equal("  NeonCompanion 0.2.0", lines[1]);
        Assert.Equal("  " + AboutText.CopyrightLine, lines[2]);
        Assert.Equal("  Runtime      .NET 10.0.0 · native AOT · x64", lines[3]);
        Assert.Equal(@"  Executable   D:\Apps\NeonCompanion\NeonCompanion.exe", lines[5]);
        Assert.Equal("  Speech       " + AboutText.SpeechLine, lines[10]);
        Assert.Equal("Components", lines[11]);
        Assert.Equal("  Spectre.Console 0.57.2 · MIT · the terminal UI: the transcript, the panes, the thumbnails", lines[12]);
        int license = Array.IndexOf(lines, "License");
        Assert.Equal(12 + AboutText.Components.Count, license);
        Assert.Equal("                      GNU GENERAL PUBLIC LICENSE", lines[license + 1]);   // the FSF's own centring, verbatim
        Assert.Equal("  <https://www.gnu.org/licenses/why-not-lgpl.html>.", lines[^1]);
    }

    // ── The tabs through a console ──────────────────────────────────────────

    [Fact]
    public void AboutTab_RendersTheRowsInThreeBlocks()
    {
        var console = new TestConsole { Profile = { Width = 240 } };
        console.Write(AboutText.AboutTab(Facts));

        string output = string.Join("\n", console.Output.Split('\n').Select(l => l.TrimEnd()));
        Assert.StartsWith("NeonCompanion 0.2.0\n© 2026 Christopher Nelson · GNU GPL v3 (the License tab)\n\nRuntime      .NET 10.0.0 · native AOT · x64\nOS           Microsoft Windows 10.0.26200\nExecutable   D:\\Apps\\NeonCompanion\\NeonCompanion.exe\n\nHome         C:\\Users\\chris\\.neoncompanion\nProfile      C:\\Users\\chris\\.neoncompanion\\profiles\\default\nModels       C:\\Users\\chris\\.neoncompanion\\models\n\nLLM servers  any OpenAI-compatible /v1 endpoint", output);
        Assert.Contains("\nSpeech       " + AboutText.SpeechLine + "\n", output);
    }

    [Fact]
    public void ComponentsTab_IsAHeadedTable()
    {
        var console = new TestConsole { Profile = { Width = 240 } };
        console.Write(AboutText.ComponentsTab());

        string[] lines = console.Output.TrimEnd('\n').Split('\n').Select(l => l.TrimEnd()).ToArray();
        Assert.Equal(AboutText.Components.Count + 1, lines.Length);
        Assert.StartsWith("Component", lines[0]);
        Assert.Contains("Version", lines[0]);
        Assert.Contains("Licence", lines[0]);
        Assert.EndsWith("Role", lines[0]);
        Assert.Matches(@"^Spectre\.Console\s+0\.57\.2\s+MIT\s+the terminal UI", lines[1]);
        Assert.Matches(@"^Vosk\s+0\.3\.38\s+Apache-2\.0\s+the wake-word recogniser", lines[15]);   // the two LibGit2Sharp rows since 2026-09-20, the two SQL rows since 2026-09-23
    }

    [Fact]
    public void ComponentsTab_FitsANormalWindow_OneLinePerRow()
    {
        // 100 columns: the name column ends under 40 cells, so every Role still starts on its own row's line.
        var console = new TestConsole { Profile = { Width = 100 } };
        console.Write(AboutText.ComponentsTab());

        string[] lines = console.Output.TrimEnd('\n').Split('\n').Select(l => l.TrimEnd()).ToArray();
        // The Role column wraps (the only one that may); every row still opens with its name and the start of its role on one line.
        Assert.All(AboutText.Components, c => Assert.Contains(lines, l => l.StartsWith(c.Name, StringComparison.Ordinal) && l.Contains(c.Role.Split(' ')[0], StringComparison.Ordinal)));
        Assert.All(lines, l => Assert.True(l.Length <= 100, l));
        Assert.All(AboutText.Components, c => Assert.True(c.Name.Length <= 40, c.Name));
    }

    [Fact]
    public void LicenseTab_IsTheTextVerbatim()
    {
        var console = new TestConsole { Profile = { Width = 240 } };
        console.Write(AboutText.LicenseTab());

        Assert.Equal(AboutText.LicenseText.TrimEnd(), console.Output);
    }
}
