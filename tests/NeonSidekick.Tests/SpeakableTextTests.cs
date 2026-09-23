using NeonSidekick.Speech;

namespace NeonSidekick.Tests;

/// <summary>
/// The sanitiser between a model's prose and the synthesizer. The "leaves prose alone" cases
/// matter as much as the stripping ones, because over-stripping mangles ordinary sentences.
/// </summary>
public class SpeakableTextTests
{
    /// <summary>A response shape with an emoji in it.</summary>
    private const string CrashingResponse = "Hey! \U0001F44B What can I help you with today?";

    // ─── Typographic punctuation ─────────────────────────────────────────────

    /// <summary>
    /// Written with escapes rather than literal glyphs deliberately: every character involved is
    /// visually near-identical to the ASCII it maps to, so a literal would be invisible in a diff.
    /// </summary>
    [Theory]
    [InlineData("don’t", "don't")]
    [InlineData("user’s", "user's")]
    [InlineData("I’m ready to assist", "I'm ready to assist")]
    [InlineData("‘quoted’", "'quoted'")]
    [InlineData("“double”", "\"double\"")]
    public void TypographicPunctuationBecomesAsciiBeforeSynthesis(string input, string expected)
    {
        Assert.Equal(expected, SpeakableText.MakeSpeakable(input));
    }

    [Theory]
    [InlineData('\u00A0', ' ')] // non-breaking space
    [InlineData('′', '\'')]     // prime
    [InlineData('—', '—')] // em dash: deliberately NOT folded, and pinned so
    [InlineData('a', 'a')]
    [InlineData('\'', '\'')]
    public void TypographicToAscii_MapsOnlyWhatItShould(char input, char expected)
    {
        Assert.Equal(expected, SpeakableText.TypographicToAscii(input));
    }

    // ─── Unspeakable characters ──────────────────────────────────────────────

    [Fact]
    public void TheResponseThatCrashedATurn_IsMadeSpeakable()
    {
        var speakable = SpeakableText.MakeSpeakable(CrashingResponse);

        Assert.Equal("Hey! What can I help you with today?", speakable);
        AssertNoSurrogates(speakable);
    }

    [Theory]
    [InlineData("\U0001F44B")]                       // waving hand
    [InlineData("\U0001F600 \U0001F601")]            // two emoji
    [InlineData("\U0001F1EC\U0001F1E7")]             // flag (two regional indicators)
    [InlineData("\U0001F468‍\U0001F4BB")]       // ZWJ sequence
    [InlineData("\U0001D11E")]                       // musical symbol, non-BMP but not emoji
    public void NonBmpCharactersAreRemovedEntirely(string input)
    {
        var speakable = SpeakableText.MakeSpeakable($"before {input} after");

        AssertNoSurrogates(speakable);
        Assert.Contains("before", speakable);
        Assert.Contains("after", speakable);
    }

    [Fact]
    public void AChunkThatIsNothingButAnEmoji_BecomesEmpty()
    {
        Assert.Equal(string.Empty, SpeakableText.MakeSpeakable("\U0001F44B"));
        Assert.Equal(string.Empty, SpeakableText.MakeSpeakable("  \U0001F600  "));
    }

    [Theory]
    [InlineData("Café", "Café")]
    [InlineData("naïve résumé", "naïve résumé")]
    [InlineData("It costs £5 — or €6.", "It costs £5 — or €6.")]
    [InlineData("Ω is omega", "Ω is omega")]
    [InlineData("\"quoted\" and 'single'", "\"quoted\" and 'single'")]
    public void SpeakableCharactersSurvive(string input, string expected)
    {
        Assert.Equal(expected, SpeakableText.MakeSpeakable(input));
    }

    [Fact]
    public void RemovingACharacterDoesNotLeaveDoubledSpaces()
    {
        var speakable = SpeakableText.MakeSpeakable("Hello \U0001F44B world");

        Assert.Equal("Hello world", speakable);
        Assert.DoesNotContain("  ", speakable);
    }

    [Fact]
    public void LoneSurrogatesAreRemoved()
    {
        var speakable = SpeakableText.MakeSpeakable("bad \uD83D end");

        AssertNoSurrogates(speakable);
        Assert.Contains("bad", speakable);
        Assert.Contains("end", speakable);
    }

    [Fact]
    public void ControlCharactersGoButWhitespaceStays()
    {
        var speakable = SpeakableText.MakeSpeakable("one\u0001two\nthree");

        Assert.DoesNotContain('\u0001', speakable);
        Assert.Contains("one", speakable);
        Assert.Contains("three", speakable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputIsHandled(string input)
    {
        Assert.Equal(string.Empty, SpeakableText.MakeSpeakable(input));
    }

    // ─── Markdown: strings taken verbatim from live runs ─────────────────────

    [Theory]
    [InlineData("**Neon Heider** - ready when you are.", "Neon Heider - ready when you are.")]
    [InlineData("## Modified (11 files, unstaged)", "Modified (11 files, unstaged)")]
    [InlineData("- **Docker** - managing containers", "Docker - managing containers")]
    [InlineData("- **Container management** - start, stop, restart, remove containers",
                "Container management - start, stop, restart, remove containers")]
    [InlineData("Here's where things stand in the repo:", "Here's where things stand in the repo:")]
    public void ThingsTheAssistantActuallySaidAloud_AreCleanedUp(string spoken, string expected)
    {
        Assert.Equal(expected, SpeakableText.MakeSpeakable(spoken));
    }

    [Fact]
    public void ATableRowIsReadAsAListOfCells_NotAsPipes()
    {
        var speakable = SpeakableText.MakeSpeakable("| `CLAUDE.md` | Project notes |");

        Assert.DoesNotContain("|", speakable);
        Assert.DoesNotContain("`", speakable);
        Assert.Contains("CLAUDE.md", speakable);
        Assert.Contains("Project notes", speakable);
    }

    [Fact]
    public void ATableSeparatorRowIsDroppedEntirely()
    {
        Assert.Equal(string.Empty, SpeakableText.MakeSpeakable("|---|---|"));
        Assert.Equal(string.Empty, SpeakableText.MakeSpeakable("| :--- | ---: |"));
    }

    [Theory]
    [InlineData("**bold**", "bold")]
    [InlineData("__also bold__", "also bold")]
    [InlineData("*italic*", "italic")]
    [InlineData("_underscore italic_", "underscore italic")]
    [InlineData("`inline code`", "inline code")]
    [InlineData("# Heading", "Heading")]
    [InlineData("###### Deep heading", "Deep heading")]
    [InlineData("> quoted text", "quoted text")]
    [InlineData("- bullet", "bullet")]
    [InlineData("* star bullet", "star bullet")]
    [InlineData("+ plus bullet", "plus bullet")]
    [InlineData("[the label](https://example.com/a/b)", "the label")]
    [InlineData("![alt text](image.png)", "alt text")]
    public void EachMarkdownConstructIsRemoved(string markdown, string expected)
    {
        Assert.Equal(expected, SpeakableText.MakeSpeakable(markdown));
    }

    [Fact]
    public void NumberedListsAreLeftAlone()
    {
        Assert.Equal("1. First item", SpeakableText.MakeSpeakable("1. First item"));
    }

    [Theory]
    [InlineData("The answer is 2 * 3 * 7.")]
    [InlineData("Use snake_case for the field name.")]
    [InlineData("It costs £5 — or €6.")]
    [InlineData("She said \"hello\" and left.")]
    [InlineData("Rates went from 5% to 7%.")]
    [InlineData("Call foo_bar_baz then _init.")]
    [InlineData("A hyphen - like this - is not a bullet.")]
    public void OrdinaryProseIsUntouched(string prose)
    {
        Assert.Equal(prose, SpeakableText.MakeSpeakable(prose));
    }

    [Fact]
    public void EmphasisInsideASentenceIsRemovedWithoutEatingTheSentence()
    {
        var speakable = SpeakableText.MakeSpeakable("You need the **git** tool, not the *docker* one.");

        Assert.Equal("You need the git tool, not the docker one.", speakable);
    }

    [Fact]
    public void ACodeFenceLineIsDropped()
    {
        Assert.Equal(string.Empty, SpeakableText.MakeSpeakable("```csharp"));
        Assert.Equal(string.Empty, SpeakableText.MakeSpeakable("```"));
    }

    [Fact]
    public void MarkdownAndEmojiAreBothHandledInOnePass()
    {
        var speakable = SpeakableText.MakeSpeakable("**Hello** \U0001F44B `world`");

        Assert.Equal("Hello world", speakable);
        Assert.DoesNotContain("*", speakable);
        Assert.DoesNotContain("`", speakable);
    }

    [Fact]
    public void AnUnmatchedMarkerIsLeftAsIs()
    {
        Assert.Equal("2 * 3 = 6", SpeakableText.MakeSpeakable("2 * 3 = 6"));
    }

    [Fact]
    public void StripMarkdown_AloneKeepsEmoji()
    {
        // The two layers are separable: StripMarkdown is formatting only.
        Assert.Equal("Hello \U0001F44B", SpeakableText.StripMarkdown("**Hello** \U0001F44B"));
    }

    private static void AssertNoSurrogates(string text)
    {
        Assert.False(text.Any(char.IsSurrogate), $"surrogate survived sanitising: <{text}>");
    }
}
