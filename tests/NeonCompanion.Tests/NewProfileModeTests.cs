using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;

namespace NeonCompanion.Tests;

public class NewProfileModeTests
{
    [Fact]
    public void Names_ArePinned_InMenuOrder_AndBasicIsTheDefault()
    {
        Assert.Equal(new[] { "basic", "advanced" }, NewProfileMode.Names);
        Assert.Equal("basic", NewProfileMode.Default);
        Assert.Equal(NewProfileMode.Default, new AppSettingsData().NewProfileMode);
    }

    [Theory]
    [InlineData("basic", false)]
    [InlineData("advanced", true)]
    [InlineData("  Basic ", false)]
    [InlineData("ADVANCED", true)]
    public void TryParse_TrimsAndIgnoresCase(string text, bool copies)
    {
        Assert.True(NewProfileMode.TryParse(text, out bool copyPromptFiles));
        Assert.Equal(copies, copyPromptFiles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("full")]
    [InlineData("simple")]
    public void TryParse_RejectsAnythingElse_AndHandsBackBasic(string? text)
    {
        Assert.False(NewProfileMode.TryParse(text, out bool copyPromptFiles));
        Assert.False(copyPromptFiles);
    }

    [Fact]
    public void EveryModeHasAHint()
    {
        foreach (var name in NewProfileMode.Names)
        {
            Assert.True(NewProfileMode.TryParse(name, out _));
            Assert.NotEqual("", NewProfileMode.Describe(name));
        }

        Assert.Equal("copy the settings and memories", NewProfileMode.Describe("basic"));
        Assert.Equal("copy the settings and memories · copy persona, operata and vocalia if present", NewProfileMode.Describe("advanced"));
        Assert.Equal("", NewProfileMode.Describe("full"));
    }

    [Fact]
    public void FilesFor_IsEveryCompanionFileAdvanced_AndTheMemoriesAloneBasic()
    {
        Assert.Same(Profiles.CompanionFiles, NewProfileMode.FilesFor(true));
        Assert.Same(Profiles.BasicCompanionFiles, NewProfileMode.FilesFor(false));
        Assert.Equal(new[] { "memory.json" }, Profiles.BasicCompanionFiles);
        Assert.All(Profiles.BasicCompanionFiles, f => Assert.Contains(f, Profiles.CompanionFiles));
    }

    [Fact]
    public void Resolve_MapsTheSavedMode()
    {
        Assert.True(NewProfileMode.Resolve(new AppSettingsData { NewProfileMode = "advanced" }));
        Assert.False(NewProfileMode.Resolve(new AppSettingsData { NewProfileMode = "basic" }));
        Assert.False(NewProfileMode.Resolve(new AppSettingsData()));   // the default is basic
    }

    [Fact]
    public void Resolve_UnknownSavedValue_WarnsAndUsesTheDefault()
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Settings" && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.False(NewProfileMode.Resolve(new AppSettingsData { NewProfileMode = "full" }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("NewProfileMode='full' is not one of basic, advanced. Using basic.", warning.Message);
    }
}
