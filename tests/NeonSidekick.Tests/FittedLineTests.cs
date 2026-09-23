using NeonSidekick.UI;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace NeonSidekick.Tests;

public class FittedLineTests
{
    private static string[] Render(int width, params string[] lines)
    {
        using var console = new TestConsole();
        console.Profile.Width = width;
        console.Write(new Rows(lines.Select(IRenderable (l) => new FittedLine(l, Theme.Body))));
        return console.Output.TrimEnd('\n').Split('\n');
    }

    [Fact]
    public void ALongLine_IsCutToTheWidth_WithAnEllipsis_OnOneRow()
    {
        Assert.Equal(["haiku  profile  Writes …"], Render(24, "haiku  profile  Writes haiku with seventeen syllables."));
    }

    [Fact]
    public void AShortLine_IsWhole()
    {
        Assert.Equal(["haiku  profile  Writes haiku."], Render(40, "haiku  profile  Writes haiku."));
    }

    [Fact]
    public void ABlankLine_KeepsItsRow()
    {
        Assert.Equal(["one", " ", "three"], Render(40, "one", "", "three"));
    }

    [Fact]
    public void ALead_CarriesItsOwnStyle_AndIsCutWithTheRest()
    {
        var options = RenderOptions.Create(new TestConsole(), new TestConsole().Profile.Capabilities);

        // The first five cells in the lead style, the rest in the line's: the skill's name over its row.
        var segments = new FittedLine("haiku  profile  Writes haiku.", Theme.Body, 5, Theme.AccentCyan).Render(options, 40).ToArray();
        Assert.Equal([("haiku", Theme.AccentCyan), ("  profile  Writes haiku.", Theme.Body)], segments.Select(s => (s.Text, s.Style)));

        // The whole line is cut first: a lead wider than the room ends in the ellipsis, nothing after it.
        segments = new FittedLine("haiku  profile  Writes haiku.", Theme.Body, 16, Theme.AccentCyan).Render(options, 10).ToArray();
        Assert.Equal([("haiku  pr…", Theme.AccentCyan)], segments.Select(s => (s.Text, s.Style)));

        // No lead style, or a zero lead: one segment as before.
        Assert.Single(new FittedLine("haiku  profile", Theme.Body, 5).Render(options, 40));
        Assert.Single(new FittedLine("haiku  profile", Theme.Body, 0, Theme.AccentCyan).Render(options, 40));
        Assert.Equal(["haiku  profile  Writes …"], Render(24, "haiku  profile  Writes haiku with seventeen syllables."));
    }

    [Fact]
    public void Measure_IsAtMostTheWidth()
    {
        var options = RenderOptions.Create(new TestConsole(), new TestConsole().Profile.Capabilities);
        var line = new FittedLine("twelve cells", Theme.Body);

        Assert.Equal(new Measurement(1, 12), line.Measure(options, 40));
        Assert.Equal(new Measurement(1, 8), line.Measure(options, 8));
    }
}
