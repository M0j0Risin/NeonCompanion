using NeonCompanion.Diagnostics;

namespace NeonCompanion.Tests;

public class LogTextTests
{
    [Fact]
    public void Excerpt_TakesTheFirstNonBlankLine_AndCollapsesWhitespace()
    {
        Assert.Equal("why is  the build red".Replace("  ", " "), LogText.Excerpt("\n\n  why is  the build red  \nsecond line\n"));
        Assert.Equal("a b c", LogText.Excerpt("a\t\tb   c"));
        Assert.Equal("", LogText.Excerpt(null));
        Assert.Equal("", LogText.Excerpt("   \n\t\n"));
    }

    [Fact]
    public void Excerpt_CutsAtTheLimit_WithTheEllipsisAsTheLastCharacter()
    {
        string text = new string('x', 300);
        string cut = LogText.Excerpt(text);
        Assert.Equal(LogText.DefaultChars, cut.Length);
        Assert.Equal(LogText.Ellipsis, cut[^1]);
        Assert.Equal(new string('x', 199), cut[..^1]);

        // Exactly the limit: whole.
        Assert.Equal(new string('y', 200), LogText.Excerpt(new string('y', 200)));
        // A shorter limit, and no trailing space before the ellipsis.
        Assert.Equal("abc d…", LogText.Excerpt("abc d efgh", 6));
        Assert.Equal("abc…", LogText.Excerpt("abc  defgh", 5));
        Assert.Equal(200, LogText.DefaultChars);
    }

    [Fact]
    public void Quoted_WrapsTheExcerpt()
    {
        Assert.Equal("\"hello there\"", LogText.Quoted("hello there\nmore"));
        Assert.Equal("\"\"", LogText.Quoted(""));
    }

    [Fact]
    public void Excerpt_RefusesAnUnusableLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogText.Excerpt("abc", 1));
    }
}
