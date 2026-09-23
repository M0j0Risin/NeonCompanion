using NeonCompanion.Obsidian;

namespace NeonCompanion.Tests;

/// <summary>The Daily notes layout (2026-09-22): moment.js formats, the settings' defaults, the template's substitutions and the date argument.</summary>
public sealed class DailyNotesTests
{
    private static readonly DateTime Moment = new(2026, 9, 3, 14, 7, 9);

    [Theory]
    [InlineData("YYYY-MM-DD", "2026-09-03")]
    [InlineData("DD.MM.YY", "03.09.26")]
    [InlineData("dddd, MMMM Do YYYY", "Thursday, September 3rd 2026")]
    [InlineData("ddd D MMM", "Thu 3 Sep")]
    [InlineData("YYYY/[W]WW", "2026/W36")]
    [InlineData("gggg-[W]ww", "2026-W36")]
    [InlineData("[Daily] YYYY-MM-DD", "Daily 2026-09-03")]
    [InlineData("YYYY-[Q]Q", "2026-Q3")]
    [InlineData("HH:mm:ss h A", "14:07:09 2 PM")]
    [InlineData("DDDD", "246")]
    public void Format_SpeaksMomentJs(string pattern, string expected) =>
        Assert.Equal(expected, DailyNotes.Format(Moment, pattern));

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(111, "111th")]
    public void Ordinal_IsEnglish(int n, string expected) => Assert.Equal(expected, DailyNotes.Ordinal(n));

    [Fact]
    public void Settings_FallBackToTheDefaults_AndCleanTheirPaths()
    {
        Assert.Equal(DailyNoteSettings.Default, DailyNotes.Settings(null));
        var settings = DailyNotes.Settings(new DailyNotesConfigFile { Folder = "/Journal\\Daily/", Format = " ", Template = "Templates/Daily" });
        Assert.Equal(new DailyNoteSettings("Journal/Daily", "YYYY-MM-DD", "Templates/Daily"), settings);
        Assert.Equal("Journal/Daily/2026-09-03.md", DailyNotes.PathFor(settings, new DateOnly(2026, 9, 3)));
        Assert.Equal("2026-09-03.md", DailyNotes.PathFor(DailyNoteSettings.Default, new DateOnly(2026, 9, 3)));
    }

    [Fact]
    public void ApplyTemplate_FillsTitleDateAndTime_AndLeavesTheUnknown()
    {
        string text = DailyNotes.ApplyTemplate("# {{title}}\n{{date}} {{time}} · {{date:dddd}} · {{ Time:HH }} · {{weather}}", "2026-09-03", Moment);
        Assert.Equal("# 2026-09-03\n2026-09-03 14:07 · Thursday · 14 · {{weather}}", text);
    }

    [Theory]
    [InlineData("", 2026, 9, 11)]
    [InlineData("today", 2026, 9, 11)]
    [InlineData("Yesterday", 2026, 9, 10)]
    [InlineData("tomorrow", 2026, 9, 12)]
    [InlineData("+3", 2026, 9, 14)]
    [InlineData("-11", 2026, 8, 31)]
    [InlineData("2025-01-02", 2025, 1, 2)]
    public void TryParseDay_TakesTheWordsTheToolNames(string text, int y, int m, int d)
    {
        Assert.True(DailyNotes.TryParseDay(text, new DateOnly(2026, 9, 11), out var day));
        Assert.Equal(new DateOnly(y, m, d), day);
    }

    [Theory]
    [InlineData("next week")]
    [InlineData("09/11/2026")]
    [InlineData("+9999999")]
    public void TryParseDay_RefusesTheRest(string text) =>
        Assert.False(DailyNotes.TryParseDay(text, new DateOnly(2026, 9, 11), out _));
}
