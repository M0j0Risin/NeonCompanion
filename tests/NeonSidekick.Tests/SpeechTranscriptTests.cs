using NeonSidekick.Speech;

namespace NeonSidekick.Tests;

public class SpeechTranscriptTests
{
    [Theory]
    [InlineData("[music] Neon are you there?", "Neon are you there?")]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData("[BLANK_AUDIO].", "")]
    [InlineData("*laughs* ok", "ok")]
    [InlineData("(sighs) fine (pause) then", "fine then")]
    [InlineData("  hello   world \n there ", "hello world there")]
    [InlineData("the [main] branch", "the branch")]
    [InlineData("nested [a [b] c] words", "nested words")]
    [InlineData("unbalanced ] bracket", "unbalanced bracket")]
    [InlineData("...", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Clean_StripsAnnotationsAndNormalisesWhitespace(string? input, string expected)
    {
        Assert.Equal(expected, SpeechTranscript.Clean(input));
    }

    // ─── stripping the wake word ─────────────────────────────────────────────

    /// <summary>
    /// Verbatim from a real run. Left in, the assistant replied "I'm Leon, your assistant": it
    /// took a mis-transcription of its own trigger as its name.
    /// </summary>
    [Theory]
    [InlineData("Leon, how's it going?", "how's it going?")]
    [InlineData("Neon, hello. How are you?", "hello. How are you?")]
    [InlineData("neon what changed in the repo", "what changed in the repo")]
    [InlineData("Neon — are you there?", "are you there?")]
    [InlineData("  Neon,   what time is it?  ", "what time is it?")]
    public void AMisheardWakeWord_IsStrippedAlongWithItsPunctuation(string input, string expected)
    {
        Assert.Equal(expected, SpeechTranscript.StripLeadingWakeWord(input, "neon"));
    }

    /// <summary>Exact matching would only strip the word on the occasions the transcriber already got it right, which is when it matters least.</summary>
    [Theory]
    [InlineData("Leon")]
    [InlineData("neon")]
    [InlineData("Neon")]
    [InlineData("Neen")]
    public void WithinOneEdit_CountsAsTheWakeWord(string firstWord)
    {
        Assert.Equal("", SpeechTranscript.StripLeadingWakeWord(firstWord + ".", "neon"));
    }

    /// <summary>An ordinary sentence must never lose its first word; at four letters only one edit is allowed.</summary>
    [Theory]
    [InlineData("hello there")]
    [InlineData("need a hand with this")]
    [InlineData("when did that change")]
    [InlineData("what changed in the repo")]
    [InlineData("...")]
    public void AnOrdinaryOpening_IsLeftAlone(string input)
    {
        Assert.Equal(input, SpeechTranscript.StripLeadingWakeWord(input, "neon"));
    }

    [Fact]
    public void TheWakeWordAlone_LeavesNoRequest()
    {
        Assert.Equal("", SpeechTranscript.StripLeadingWakeWord("Neon.", "neon"));
    }

    [Fact]
    public void AMultiWordWakePhrase_IsMatchedExactly()
    {
        // The fuzzy allowance is for single tokens, where the failure mode is known.
        Assert.Equal("hey there, what is up", SpeechTranscript.StripLeadingWakeWord("hey there, what is up", "hey now"));
        Assert.Equal("Hey now, what is up", SpeechTranscript.StripLeadingWakeWord("Hey now, what is up", "hey now"));
    }

    [Fact]
    public void ALongerWakeWord_AllowsTwoEdits()
    {
        Assert.Equal("open the door", SpeechTranscript.StripLeadingWakeWord("Jarvus, open the door", "jarvis"));
        Assert.Equal("open the door", SpeechTranscript.StripLeadingWakeWord("Jorvus open the door", "jarvis"));
        Assert.Equal("Garbage open the door", SpeechTranscript.StripLeadingWakeWord("Garbage open the door", "jarvis"));
    }

    [Theory]
    [InlineData("what changed", "", "what changed")]
    [InlineData("what changed", null, "what changed")]
    [InlineData("", "neon", "")]
    [InlineData(null, "neon", "")]
    public void NoConfiguredWakeWordOrNoText_ChangesNothing(string? input, string? wakeWord, string expected)
    {
        Assert.Equal(expected, SpeechTranscript.StripLeadingWakeWord(input, wakeWord));
    }

    [Theory]
    [InlineData("neon", "neon", 0)]
    [InlineData("leon", "neon", 1)]
    [InlineData("nian", "neon", 2)]
    [InlineData("", "neon", 4)]
    [InlineData("neon", "", 4)]
    public void EditDistance_IsLevenshtein(string a, string b, int expected)
    {
        Assert.Equal(expected, SpeechTranscript.EditDistance(a, b));
    }
}
