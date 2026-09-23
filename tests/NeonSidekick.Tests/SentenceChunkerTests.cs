using NeonSidekick.Speech;

namespace NeonSidekick.Tests;

/// <summary>Pure logic — no device, no server, no ordering hazards.</summary>
public class SentenceChunkerTests
{
    private static List<string> FeedCharByChar(SentenceChunker chunker, string text)
    {
        var emitted = new List<string>();
        foreach (var c in text)
        {
            emitted.AddRange(chunker.Append(c.ToString()));
        }

        return emitted;
    }

    [Fact]
    public void Append_WithNoTerminator_EmitsNothing()
    {
        var chunker = new SentenceChunker();

        Assert.Empty(chunker.Append("This sentence has not finished"));
    }

    [Fact]
    public void Append_TerminatorAtEndOfBuffer_IsHeldBackUntilConfirmed()
    {
        var chunker = new SentenceChunker();

        // Nothing follows the period yet, so it is not yet a boundary.
        Assert.Empty(chunker.Append("Hello world."));

        // The following space confirms it.
        Assert.Equal(new[] { "Hello world." }, chunker.Append(" Next"));
    }

    [Fact]
    public void Append_DecimalNumber_IsNotSplit()
    {
        var chunker = new SentenceChunker();

        Assert.Empty(chunker.Append("Pi is 3."));
        Assert.Empty(chunker.Append("14 roughly"));

        Assert.Equal("Pi is 3.14 roughly", chunker.Flush());
    }

    [Fact]
    public void Append_TokenByToken_EmitsWholeSentences()
    {
        var chunker = new SentenceChunker();

        var emitted = new List<string>();
        emitted.AddRange(chunker.Append("Hel"));
        emitted.AddRange(chunker.Append("lo wor"));
        emitted.AddRange(chunker.Append("ld. Nex"));
        emitted.AddRange(chunker.Append("t one."));

        Assert.Equal(new[] { "Hello world." }, emitted);
        Assert.Equal("Next one.", chunker.Flush());
    }

    [Theory]
    [InlineData("Really? Yes.", "Really?")]
    [InlineData("Stop! Now.", "Stop!")]
    [InlineData("What?! Really.", "What?!")]
    [InlineData("Wait... Then go.", "Wait...")]
    public void Append_TerminatorVariants_ProduceOneBoundary(string input, string expectedFirst)
    {
        var chunker = new SentenceChunker();
        var emitted = chunker.Append(input);

        Assert.Equal(expectedFirst, emitted[0]);
    }

    [Fact]
    public void Append_Newline_IsAHardBoundary()
    {
        var chunker = new SentenceChunker();

        // No terminator at all, but a list item still ends here.
        var emitted = chunker.Append("- first item\n- second item");

        Assert.Equal(new[] { "- first item" }, emitted);
    }

    [Theory]
    [InlineData("Dr. Smith arrived.")]
    [InlineData("Meet Mr. Jones today.")]
    [InlineData("Use tabs vs. spaces here.")]
    [InlineData("J. R. R. Tolkien wrote it.")]
    public void Append_AbbreviationsAndInitials_DoNotSplit(string sentence)
    {
        var chunker = new SentenceChunker();

        // No trailing space, so the final period is never confirmed — everything stays pending.
        Assert.Empty(chunker.Append(sentence));
        Assert.Equal(sentence, chunker.Flush());
    }

    [Fact]
    public void Append_TrailingCloser_StaysWithItsSentence()
    {
        var chunker = new SentenceChunker();
        var emitted = chunker.Append("He said \"go.\" Then left.");

        Assert.Equal("He said \"go.\"", emitted[0]);
    }

    [Fact]
    public void Append_RunOnBeyondMaxChunk_BreaksAtWhitespace()
    {
        var chunker = new SentenceChunker(maxChunkChars: 20);

        var emitted = chunker.Append("aaa bbb ccc ddd eee fff ggg hhh");

        Assert.NotEmpty(emitted);
        Assert.True(emitted[0].Length <= 20, $"Expected a break at or before 20 chars, got '{emitted[0]}'.");
        Assert.DoesNotContain("  ", emitted[0]);
    }

    [Fact]
    public void Append_RunOnWithoutWhitespace_BreaksAtLimit()
    {
        var chunker = new SentenceChunker(maxChunkChars: 10);
        var emitted = chunker.Append(new string('x', 25));

        Assert.NotEmpty(emitted);
        Assert.Equal(10, emitted[0].Length);
    }

    [Fact]
    public void Append_PunctuationOnly_IsNotEmitted()
    {
        var chunker = new SentenceChunker();

        // A markdown rule has nothing to say out loud.
        Assert.Empty(chunker.Append("---\n"));
        Assert.Equal(string.Empty, chunker.Flush());
    }

    [Fact]
    public void Flush_ReturnsPendingOnceThenEmpty()
    {
        var chunker = new SentenceChunker();
        chunker.Append("Trailing text with no terminator");

        Assert.Equal("Trailing text with no terminator", chunker.Flush());
        Assert.Equal(string.Empty, chunker.Flush());
        Assert.False(chunker.HasPending);
    }

    [Fact]
    public void Reset_DiscardsPendingText()
    {
        var chunker = new SentenceChunker();
        chunker.Append("Abandoned turn");

        chunker.Reset();

        Assert.False(chunker.HasPending);
        Assert.Equal(string.Empty, chunker.Flush());
    }

    [Fact]
    public void Ctor_WithNonPositiveMax_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SentenceChunker(0));
    }

    [Fact]
    public void Append_NoContentIsLost_RegardlessOfChunkBoundaries()
    {
        const string paragraph =
            "The build succeeded. Two tests failed on the first run! Did you see the log? " +
            "It mentions Dr. Smith and version 3.14 of the runtime.";

        var chunker = new SentenceChunker();
        var emitted = FeedCharByChar(chunker, paragraph);

        var tail = chunker.Flush();
        if (tail.Length > 0)
        {
            emitted.Add(tail);
        }

        // Whitespace differs (separators are consumed), so compare on non-whitespace content.
        var expected = new string(paragraph.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var actual = new string(string.Concat(emitted).Where(c => !char.IsWhiteSpace(c)).ToArray());

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Append_MultiSentenceParagraph_SplitsIntoSpeakableUnits()
    {
        var chunker = new SentenceChunker();
        var emitted = chunker.Append("First one. Second one! Third one? ");

        Assert.Equal(new[] { "First one.", "Second one!", "Third one?" }, emitted);
    }
}
