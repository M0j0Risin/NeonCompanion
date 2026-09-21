using NeonCompanion.Diagnostics;
using NeonCompanion.Sessions;
using NeonCompanion.Settings;

namespace NeonCompanion.Tests;

public class SessionNamingModeTests
{
    [Theory]
    [InlineData("first-line", SessionNaming.FirstLine, true)]
    [InlineData("FIRST-LINE", SessionNaming.FirstLine, true)]
    [InlineData(" model-written ", SessionNaming.ModelWritten, true)]
    [InlineData("model", SessionNaming.FirstLine, false)]
    [InlineData("", SessionNaming.FirstLine, false)]
    [InlineData(null, SessionNaming.FirstLine, false)]
    public void TryParse_AcceptsTheTwoWords(string? text, SessionNaming expected, bool ok)
    {
        Assert.Equal(ok, SessionNamingMode.TryParse(text, out var mode));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void Names_Default_Name_AndDescribe_ArePinned()
    {
        Assert.Equal(new[] { "first-line", "model-written" }, SessionNamingMode.Names);
        Assert.Equal("model-written", SessionNamingMode.Default);   // the user's call, 2026-09-18 (first-line that morning)
        Assert.Equal("first-line", SessionNamingMode.Name(SessionNaming.FirstLine));
        Assert.Equal("model-written", SessionNamingMode.Name(SessionNaming.ModelWritten));
        Assert.Equal("the session is named after its first sent line", SessionNamingMode.Describe("first-line"));
        Assert.Equal("the model writes a short title after the first turn", SessionNamingMode.Describe("model-written"));
        Assert.Equal("", SessionNamingMode.Describe("other"));
        Assert.Equal(SessionNamingMode.Default, new AppSettingsData().SessionNamingMode);
    }

    [Fact]
    public void Resolve_WarnsOnAHandEditedValue_AndUsesTheDefault()
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Sessions") { warnings.Add(e); } };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(SessionNaming.FirstLine, SessionNamingMode.Resolve(new AppSettingsData { SessionNamingMode = "first-line" }));
            Assert.Empty(warnings);
            Assert.Equal(SessionNaming.ModelWritten, SessionNamingMode.Resolve(new AppSettingsData { SessionNamingMode = "gpt" }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal(DiagnosticLevel.Warning, warning.Level);
        Assert.Equal("SessionNamingMode='gpt' is not one of first-line, model-written. Using model-written.", warning.Message);
    }
}
