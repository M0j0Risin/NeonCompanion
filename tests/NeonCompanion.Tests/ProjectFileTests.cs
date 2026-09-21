using NeonCompanion.Diagnostics;
using NeonCompanion.Llm;

namespace NeonCompanion.Tests;

public class ProjectFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NeonCompanion.Tests", Guid.NewGuid().ToString("N"));
    private string _root;

    public ProjectFileTests()
    {
        _root = Path.Combine(_dir, "one");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Neon => Path.Combine(_root, ProjectFile.PrimaryFileName);

    private string Agents => Path.Combine(_root, ProjectFile.SecondaryFileName);

    private void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private ProjectFile File_() => new(() => _root);

    [Fact]
    public void TheNames_TheCap_AndTheCategory_ArePinned()
    {
        Assert.Equal(["NEON.md", "AGENTS.md"], ProjectFile.FileNames);
        Assert.Equal("NEON.md", ProjectFile.PrimaryFileName);
        Assert.Equal("AGENTS.md", ProjectFile.SecondaryFileName);
        Assert.Equal(16_000, ProjectFile.MaxLength);
        Assert.Equal("Project", ProjectFile.Category);
    }

    [Fact]
    public void NeitherFile_IsNull_AndNotActive_ThePathTheSecondary()
    {
        var project = File_();

        Assert.Null(project.Read());
        Assert.Null(project.ReadNotes());
        Assert.False(project.IsActive);
        Assert.Equal(Agents, project.FilePath);
        Assert.Equal("AGENTS.md", project.CurrentFileName);
    }

    [Fact]
    public void AgentsMd_IsRead_WhenAlone()
    {
        Write(Agents, "  Build with dotnet build.\r\n\r\nTests under tests/.  ");
        var project = File_();

        var notes = project.ReadNotes();
        Assert.NotNull(notes);
        Assert.Equal("AGENTS.md", notes.FileName);
        Assert.Equal("Build with dotnet build.\n\nTests under tests/.", notes.Text);
        Assert.True(project.IsActive);
        Assert.Equal(Agents, project.FilePath);
    }

    [Fact]
    public void NeonMd_Wins_WhenBothExist_AndAgentsMdTakesOverWhenItGoes()
    {
        Write(Agents, "the agents file");
        Write(Neon, "the neon file");
        var project = File_();

        Assert.Equal(new ProjectNotes("NEON.md", "the neon file"), project.ReadNotes());
        Assert.Equal(Neon, project.FilePath);

        File.Delete(Neon);
        Assert.Equal(new ProjectNotes("AGENTS.md", "the agents file"), project.ReadNotes());

        Write(Neon, "the neon file again");
        Assert.Equal(new ProjectNotes("NEON.md", "the neon file again"), project.ReadNotes());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t\n")]
    public void BlankFile_IsNull(string content)
    {
        Write(Neon, content);
        Assert.Null(File_().ReadNotes());
    }

    [Fact]
    public void TheRoot_IsAskedOnEveryRead_SoAMovedWorkingDirectoryReadsItsOwnNotes()
    {
        Write(Neon, "notes of one");
        string other = Path.Combine(_dir, "two");
        Write(Path.Combine(other, "AGENTS.md"), "notes of two");
        var project = File_();

        Assert.Equal(new ProjectNotes("NEON.md", "notes of one"), project.ReadNotes());
        _root = other;
        Assert.Equal(new ProjectNotes("AGENTS.md", "notes of two"), project.ReadNotes());
        _root = Path.Combine(_dir, "three");
        Assert.Null(project.ReadNotes());
        Assert.False(project.IsActive);
        _root = Path.Combine(_dir, "one");
        Assert.Equal(new ProjectNotes("NEON.md", "notes of one"), project.ReadNotes());
    }

    [Fact]
    public void Rewrite_IsPickedUpOnTheNextRead()
    {
        Write(Neon, "first");
        var project = File_();
        Assert.Equal("first", project.Read());

        Write(Neon, "second, longer");
        Assert.Equal("second, longer", project.Read());
        Assert.Equal("second, longer", project.Read());
    }

    [Fact]
    public void OverLength_IsCutWithAnEllipsis_WithOneWarning_NamingTheFile()
    {
        Write(Agents, new string('a', ProjectFile.MaxLength + 500));
        var warnings = CaptureWarnings(out var stop);
        try
        {
            var project = File_();
            string? text = project.Read();
            Assert.NotNull(text);
            Assert.Equal(ProjectFile.MaxLength, text.Length);
            Assert.EndsWith("…", text, StringComparison.Ordinal);
            Assert.Equal(text, project.Read());
        }
        finally
        {
            stop();
        }

        Assert.Equal(ProjectFile.TruncatedWarning("AGENTS.md", ProjectFile.MaxLength + 500), Assert.Single(warnings));
        Assert.Equal("AGENTS.md is 16500 characters; using the first 16000.", warnings[0]);
    }

    [Fact]
    public void ALockedFile_WarnsOnce_IsNull_AndIsReadOnceReleased()
    {
        Write(Neon, "notes");
        var warnings = CaptureWarnings(out var stop);
        try
        {
            var project = File_();
            using (new FileStream(Neon, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Null(project.Read());
                Assert.Null(project.Read());
            }

            Assert.Equal("notes", project.Read());
        }
        finally
        {
            stop();
        }

        string warning = Assert.Single(warnings);
        Assert.StartsWith("Could not read NEON.md; using the default project notes: ", warning, StringComparison.Ordinal);
        Assert.StartsWith(ProjectFile.UnreadableWarning("NEON.md", ""), warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_IsPinned()
    {
        Assert.Equal("a\nb\n\nc", ProjectFile.Normalize("  a\r\nb\r\r\nc \n"));
        string cut = ProjectFile.Normalize(new string('x', ProjectFile.MaxLength + 1), out bool truncated);
        Assert.True(truncated);
        Assert.Equal(ProjectFile.MaxLength, cut.Length);
    }

    [Fact]
    public void Locate_IsPure()
    {
        Assert.Equal(Path.Combine(_root, "AGENTS.md"), ProjectFile.Locate(_root));
        Write(Neon, "x");
        Assert.Equal(Neon, ProjectFile.Locate(_root));
        Assert.Throws<ArgumentNullException>(() => new ProjectFile(null!));
    }

    private static List<string> CaptureWarnings(out Action stop)
    {
        var warnings = new List<string>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == ProjectFile.Category && e.Level == DiagnosticLevel.Warning) warnings.Add(e.Message); };
        DiagnosticLog.Emitted += capture;
        stop = () => DiagnosticLog.Emitted -= capture;
        return warnings;
    }
}
