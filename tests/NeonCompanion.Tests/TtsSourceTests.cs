using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;
using NeonCompanion.Speech;

namespace NeonCompanion.Tests;

public class TtsSourceTests
{
    [Fact]
    public void Names_ArePinned_InMenuOrder()
    {
        Assert.Equal(new[] { "http", "in-process" }, TtsSource.Names);
        Assert.Equal("in-process", TtsSource.Default);
        Assert.Equal(TtsSource.Default, new AppSettingsData().TtsSource);
        Assert.Equal("in-process Kokoro", TtsSource.InProcessSource);
    }

    [Theory]
    [InlineData("http", TtsEngine.Http)]
    [InlineData("in-process", TtsEngine.InProcess)]
    [InlineData("  In-Process ", TtsEngine.InProcess)]
    [InlineData("HTTP", TtsEngine.Http)]
    public void TryParse_TrimsAndIgnoresCase(string text, TtsEngine expected)
    {
        Assert.True(TtsSource.TryParse(text, out var engine));
        Assert.Equal(expected, engine);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("inprocess")]
    [InlineData("local")]
    [InlineData("server")]
    public void TryParse_RejectsAnythingElse_AsInProcess(string? text)
    {
        Assert.False(TtsSource.TryParse(text, out var engine));
        Assert.Equal(TtsEngine.InProcess, engine);
    }

    [Fact]
    public void Name_RoundTripsEverySource_AndEverySourceHasAHint()
    {
        foreach (var name in TtsSource.Names)
        {
            Assert.True(TtsSource.TryParse(name, out var engine));
            Assert.Equal(name, TtsSource.Name(engine));
            Assert.NotEqual("", TtsSource.Describe(name));
        }

        Assert.Equal("a Kokoro-FastAPI server at TTS HTTP URL", TtsSource.Describe("http"));
        Assert.Equal("KokoroSharp in this process; kokoro.onnx (326 MB) downloads on first use", TtsSource.Describe("in-process"));
        Assert.Equal("", TtsSource.Describe("local"));
    }

    [Fact]
    public void Resolve_MapsTheSavedSource()
    {
        Assert.Equal(TtsEngine.InProcess, TtsSource.Resolve(new AppSettingsData { TtsSource = "in-process" }));
        Assert.Equal(TtsEngine.Http, TtsSource.Resolve(new AppSettingsData { TtsSource = "Http" }));
        Assert.Equal(TtsEngine.InProcess, TtsSource.Resolve(new AppSettingsData()));
    }

    [Fact]
    public void Resolve_UnknownSavedValue_WarnsAndUsesTheDefault()
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Speech" && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(TtsEngine.InProcess, TtsSource.Resolve(new AppSettingsData { TtsSource = "local" }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("TtsSource='local' is not one of http, in-process. Using in-process.", warning.Message);
    }
}
