using NeonCompanion.UI;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonCompanion.Tests;

public class ScrollbackTests
{
    private static List<Segment> Segments(params string[] texts) => texts.Select(t => new Segment(t)).ToList();

    private static string Text(SegmentLine row) => string.Concat(row.Select(s => s.Text));

    private static string[] Texts(IReadOnlyList<SegmentLine> rows) => rows.Select(Text).ToArray();

    [Fact]
    public void Empty_HasNoRows_AndNoOpenLine()
    {
        var store = new Scrollback();
        Assert.Equal(0, store.Count);
        Assert.False(store.LastLineOpen);
        Assert.Empty(store.Rows(40));
    }

    [Fact]
    public void Lines_EndWithABreak_TheLastOneOpenUntilItComes()
    {
        var store = new Scrollback();
        store.Append(Segments("hello\n", "wor"), 40);

        Assert.Equal(new[] { "hello", "wor" }, Texts(store.Rows(40)));
        Assert.True(store.LastLineOpen);
        Assert.Equal(2, store.LineCount);

        // What joins the open line continues its row.
        store.Append(Segments("ld", "\n"), 40);
        Assert.Equal(new[] { "hello", "world" }, Texts(store.Rows(40)));
        Assert.False(store.LastLineOpen);
    }

    [Fact]
    public void ABreakSegment_ClosesTheLine_LikeANewline()
    {
        var store = new Scrollback();
        store.Append(new List<Segment> { new("a"), Segment.LineBreak, new("b"), Segment.LineBreak }, 40);

        Assert.Equal(new[] { "a", "b" }, Texts(store.Rows(40)));
        Assert.False(store.LastLineOpen);
    }

    [Fact]
    public void BlankLines_AreRows_AndAControlCodeOrACarriageReturn_IsNothing()
    {
        var store = new Scrollback();
        store.Append(new List<Segment> { Segment.Control("\e[J"), new("a\r\n\nb\n") }, 40);

        Assert.Equal(new[] { "a", "", "b" }, Texts(store.Rows(40)));
    }

    [Fact]
    public void Wraps_ByCells_AWideCharacterWhole_AndAFullRowWaitsForTheNextCharacter()
    {
        var store = new Scrollback();
        // 4 cells, then a 2-cell character: it starts the next row (the terminal's rule, ScreenPane.Track's).
        store.Append(Segments("abcd日\n"), 5);
        Assert.Equal(new[] { "abcd", "日" }, Texts(store.Rows(5)));

        // A row filled exactly and ended: one row, not an empty second one.
        store.Append(Segments("xxxxx\n"), 5);
        Assert.Equal(new[] { "abcd", "日", "xxxxx" }, Texts(store.Rows(5)));

        // Filled exactly and continued: the next character wraps.
        store.Append(Segments("yyyyy", "z\n"), 5);
        Assert.Equal(new[] { "abcd", "日", "xxxxx", "yyyyy", "z" }, Texts(store.Rows(5)));
    }

    [Fact]
    public void Styles_AreKept_PerRun_AcrossAWrap()
    {
        var store = new Scrollback();
        var red = new Style(Color.Red);
        store.Append(new List<Segment> { new("ab", red), new("cdef"), Segment.LineBreak }, 4);

        var rows = store.Rows(4);
        Assert.Equal(2, rows.Count);
        Assert.Equal(("ab", red), (rows[0][0].Text, rows[0][0].Style));
        Assert.Equal(("cd", Style.Plain), (rows[0][1].Text, rows[0][1].Style));
        Assert.Equal(("ef", Style.Plain), (rows[1][0].Text, rows[1][0].Style));
    }

    [Fact]
    public void AWidthChange_LaysTheRowsOutAgain()
    {
        var store = new Scrollback();
        store.Append(Segments("abcdefgh\n"), 40);
        Assert.Single(store.Rows(40));

        Assert.Equal(new[] { "abc", "def", "gh" }, Texts(store.Rows(3)));
        Assert.Equal(3, store.Count);
        Assert.Equal(new[] { "abcdefgh" }, Texts(store.Rows(40)));
    }

    [Fact]
    public void PastTheCap_TheOldestLinesGo_AndTheRowsDroppedAreReturned()
    {
        var store = new Scrollback();
        for (int i = 0; i < Scrollback.MaxRows; i++)
        {
            Assert.Equal(0, store.Append(Segments("x\n"), 40));
        }

        Assert.Equal(Scrollback.MaxRows, store.Count);
        // A two-row line: two rows over, the first two one-row lines go.
        Assert.Equal(2, store.Append(Segments("first\nsecond\n"), 40));
        Assert.Equal(Scrollback.MaxRows, store.Count);
        Assert.Equal("second", Text(store.Rows(40)[^1]));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var store = new Scrollback();
        store.Append(Segments("a\nb"), 40);
        store.Clear();

        Assert.Equal(0, store.Count);
        Assert.False(store.LastLineOpen);
        Assert.Equal(0, store.LineCount);
    }
}
