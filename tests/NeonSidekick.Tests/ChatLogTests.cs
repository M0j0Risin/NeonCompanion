using NeonSidekick.App;

namespace NeonSidekick.Tests;

public class ChatLogTests
{
    [Fact]
    public void Add_TrimsBoth_AndSkipsABlankReply()
    {
        var log = new ChatLog();
        log.Add("  hi  ", "\n\nHello.\n");
        log.Add("errored", "   ");
        log.Add("", "a reply to nothing");

        Assert.Equal(2, log.Count);
        Assert.Equal("a reply to nothing", log.Markdown(1, includeUser: false));
        Assert.Equal("Hello.\n\n---\n\na reply to nothing", log.Markdown(2, includeUser: false));
        Assert.Equal("> hi\n\nHello.\n\n---\n\n>\n\na reply to nothing", log.Markdown(2, includeUser: true));
    }

    [Fact]
    public void Markdown_TakesTheLastN_OldestFirst_WithTheRuleBetween()
    {
        var log = new ChatLog();
        log.Add("q1", "r1");
        log.Add("q2", "r2\nline 2");
        log.Add("q3", "r3");

        Assert.Equal("\n\n---\n\n", ChatLog.Separator);
        Assert.Equal("r3", log.Markdown(1, false));
        Assert.Equal("r2\nline 2\n\n---\n\nr3", log.Markdown(2, false));
        Assert.Equal("r1\n\n---\n\nr2\nline 2\n\n---\n\nr3", log.Markdown(3, false));
        Assert.Equal("r1\n\n---\n\nr2\nline 2\n\n---\n\nr3", log.Markdown(99, false));
        Assert.Equal("r3", log.Markdown(0, false));
        Assert.Equal("r3", log.Markdown(-5, false));
        Assert.Equal("> q2\n\nr2\nline 2\n\n---\n\n> q3\n\nr3", log.Markdown(2, true));
    }

    [Fact]
    public void Take_ClampsToAtLeastOneAndAtMostEverything_ZeroWhenEmpty()
    {
        var log = new ChatLog();
        Assert.Equal(0, log.Take(1));
        Assert.Equal(0, log.Take(int.MaxValue));
        Assert.Equal("", log.Markdown(1, true));

        log.Add("q", "r");
        log.Add("q", "r");
        Assert.Equal(1, log.Take(0));
        Assert.Equal(1, log.Take(-1));
        Assert.Equal(1, log.Take(1));
        Assert.Equal(2, log.Take(2));
        Assert.Equal(2, log.Take(int.MaxValue));

        log.Clear();
        Assert.Equal(0, log.Count);
        Assert.Equal(0, log.Take(1));
    }

    [Fact]
    public void Quote_PrefixesEveryLine_AnEmptyLineIsABareMarker()
    {
        Assert.Equal("> one", ChatLog.Quote("one"));
        Assert.Equal("> a\n> b", ChatLog.Quote("a\nb"));
        Assert.Equal("> a\n> b", ChatLog.Quote("a\r\nb"));
        Assert.Equal("> a\n>\n> b", ChatLog.Quote("a\n\nb"));
        Assert.Equal(">", ChatLog.Quote(""));
    }
}
