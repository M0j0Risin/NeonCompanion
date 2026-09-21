using NeonCompanion.UI;
using Spectre.Console;
using Spectre.Console.Testing;

namespace NeonCompanion.Tests;

public class RawTextTests
{
    private static TestConsole Console20()
    {
        var console = new TestConsole();
        console.Profile.Width = 20;
        return console;
    }

    [Fact]
    public void LongerThanTheWidth_IsNotFolded()
    {
        using var console = Console20();
        string text = new('x', 45);

        console.Write(new RawText(text));

        Assert.Equal(text, console.Output);
    }

    [Fact]
    public void Newline_BecomesALineBreak_AndCarriageReturnIsDropped()
    {
        using var console = Console20();
        console.Write(new RawText("a\r\nb\nc"));
        Assert.Equal(new[] { "a", "b", "c" }, console.Lines);
    }

    [Fact]
    public void Spaces_ArePreservedExactly()
    {
        using var console = Console20();
        console.Write(new RawText("   "));
        Assert.Equal("   ", console.Output);
    }

    [Fact]
    public void MarkupCharacters_AreLiteral()
    {
        using var console = Console20();
        console.Write(new RawText("[bold]not markup[/]", Theme.Assistant));
        Assert.Equal("[bold]not markup[/]", console.Output);
    }

    [Fact]
    public void Empty_WritesNothing()
    {
        using var console = Console20();
        console.Write(new RawText(""));
        Assert.Equal("", console.Output);
    }
}
