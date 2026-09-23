using NeonSidekick.UI;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace NeonSidekick.Tests;

public class FittedMarkupTests
{
    private static string[] Render(int width, params string[] rows)
    {
        using var console = new TestConsole();
        console.Profile.Width = width;
        console.Write(new Rows(rows.Select(IRenderable (r) => new FittedMarkup(r))));
        return console.Output.TrimEnd('\n').Split('\n');
    }

    /// <summary>A sentence with spaces is cut at the edge, never wrapped — what Spectre's Overflow.Ellipsis does not do.</summary>
    [Fact]
    public void ALongRow_IsCutToTheWidth_WithAnEllipsis_OnOneRow()
    {
        Assert.Equal(["▸ haiku  profile  Writes …"], Render(26, "▸ haiku  profile  Writes haiku with seventeen syllables."));
        Assert.Equal(["profile  C:\\Users\\x\\.agents…"], Render(28, "profile  [#9A8BB8]C:\\Users\\x\\.agents\\skills\\very\\long[/]"));
    }

    [Fact]
    public void AShortRow_IsWhole_AndABlankOneKeepsItsRow()
    {
        Assert.Equal(["haiku  profile  Writes haiku.", " ", "three"], Render(40, "haiku  profile  Writes haiku.", "", "three"));
        Assert.Equal(["a [b]"], Render(40, "a [[b]]"));
    }

    /// <summary>The cut lands inside the segment that overflows, and the ellipsis takes that segment's style; the segments before it are whole.</summary>
    [Fact]
    public void TheCut_IsInsideTheOverflowingSegment_TheEllipsisInItsStyle()
    {
        using var console = new TestConsole();
        var options = RenderOptions.Create(console, console.Profile.Capabilities);
        var segments = new FittedMarkup("[bold]profile  [/][#9A8BB8]C:\\Users\\x\\.agents\\skills[/]").Render(options, 20).ToList();

        Assert.Equal("profile  C:\\Users\\x…", string.Concat(segments.Select(s => s.Text)));
        Assert.Equal(Decoration.Bold, segments[0].Style.Decoration);
        Assert.Equal("…", segments[^1].Text);
        Assert.Equal(segments[^2].Style, segments[^1].Style);
        Assert.NotEqual(segments[0].Style, segments[^1].Style);
        Assert.Equal(20, Segment.CellCount(segments));

        // Room for the ellipsis alone, or none: the ellipsis, or nothing at all.
        Assert.Equal(["…"], new FittedMarkup("abc").Render(options, 1).Select(s => s.Text));
        Assert.Equal(["abc"], new FittedMarkup("abc").Render(options, 0).Select(s => s.Text));   // no room reported: as laid out, the pane clips
    }
}
