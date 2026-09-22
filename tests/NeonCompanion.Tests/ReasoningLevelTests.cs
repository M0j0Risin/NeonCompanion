using Microsoft.Extensions.AI;
using NeonCompanion.Diagnostics;
using NeonCompanion.Llm;
using NeonCompanion.Settings;
using NeonCompanion.UI;

namespace NeonCompanion.Tests;

public class ReasoningLevelTests
{
    [Fact]
    public void Levels_ArePinned_InMenuOrder()
    {
        Assert.Equal(new[] { "none", "low", "medium", "high", "xhigh" }, ReasoningLevel.Levels);
        Assert.Equal("none", ReasoningLevel.Default);
        Assert.Equal(ReasoningLevel.Default, new AppSettingsData().LlmReasoning);
    }

    [Theory]
    [InlineData("none", ReasoningEffort.None)]
    [InlineData("low", ReasoningEffort.Low)]
    [InlineData("medium", ReasoningEffort.Medium)]
    [InlineData("high", ReasoningEffort.High)]
    [InlineData("xhigh", ReasoningEffort.ExtraHigh)]
    [InlineData("  XHigh ", ReasoningEffort.ExtraHigh)]
    public void TryParse_TrimsAndIgnoresCase(string text, ReasoningEffort expected)
    {
        Assert.True(ReasoningLevel.TryParse(text, out var effort));
        Assert.Equal(expected, effort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("turbo")]
    [InlineData("extra high")]
    [InlineData("ExtraHigh")]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        Assert.False(ReasoningLevel.TryParse(text, out _));
    }

    [Fact]
    public void Name_RoundTripsEveryLevel()
    {
        foreach (var level in ReasoningLevel.Levels)
        {
            Assert.True(ReasoningLevel.TryParse(level, out var effort));
            Assert.Equal(level, ReasoningLevel.Name(effort));
            Assert.NotEqual("", ReasoningLevel.Describe(level));
        }

        Assert.Equal("", ReasoningLevel.Describe("turbo"));
    }

    [Theory]
    [InlineData("none", "○")]       // U+25CB: the empty circle, always shown (2026-09-21; nothing until then)
    [InlineData("low", "◔")]        // U+25D4
    [InlineData("medium", "◑")]     // U+25D1
    [InlineData("high", "◕")]       // U+25D5
    [InlineData("xhigh", "●")]      // U+25CF
    [InlineData("turbo", "○")]      // a hand-edited level: none's circle (Resolve treats it as none), never a throw
    public void Glyph_IsPinned(string level, string expected)
    {
        Assert.Equal(expected, ReasoningLevel.Glyph(level));
        Assert.True(TextCells.Width(expected) <= 1);
    }

    [Fact]
    public void Resolve_MapsTheSavedLevel()
    {
        Assert.Equal(ReasoningEffort.High, ReasoningLevel.Resolve(new AppSettingsData { LlmReasoning = "high" }));
        Assert.Equal(ReasoningEffort.None, ReasoningLevel.Resolve(new AppSettingsData()));
    }

    [Fact]
    public void Resolve_BadSavedValue_WarnsAndUsesTheDefault()
    {
        var warnings = new List<string>();
        Action<DiagnosticEvent> handler = e =>
        {
            if (e.Category == "Llm" && e.Level == DiagnosticLevel.Warning) warnings.Add(e.Message);
        };

        DiagnosticLog.Emitted += handler;
        try
        {
            Assert.Equal(ReasoningEffort.None, ReasoningLevel.Resolve(new AppSettingsData { LlmReasoning = "turbo" }));
        }
        finally
        {
            DiagnosticLog.Emitted -= handler;
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("turbo", warning);
        Assert.Contains("none, low, medium, high, xhigh", warning);
    }
}
