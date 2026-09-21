using NeonCompanion.Audio;
using NeonCompanion.Speech;

namespace NeonCompanion.Tests;

/// <summary>
/// The pure half of wake-word detection.
/// </summary>
public class WakeWordMatchTests
{
    private static readonly PcmFormat Format = PcmFormat.Whisper;   // 32 000 bytes per second

    private static WakeWordTiming Word(string word, double startSeconds, double endSeconds) =>
        new(word, TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds));

    // ── NormalizePhrase / Contains ──────────────────────────────────────────

    [Theory]
    [InlineData("neon", "neon")]
    [InlineData("  Hey   NEON ", "hey neon")]
    [InlineData("hey\tneon\n", "hey neon")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizePhrase_LowercasesAndCollapsesWhitespace(string? phrase, string expected)
    {
        Assert.Equal(expected, WakeWordMatch.NormalizePhrase(phrase));
    }

    [Theory]
    [InlineData("neon", "neon", true)]
    [InlineData("hey NEON what time is it", "neon", true)]
    [InlineData("a neon sign", "neon", true)]
    [InlineData("neonatal", "neon", true)]              // a substring test, as in the reference
    [InlineData("hey   neon", "hey neon", true)]        // runs of whitespace on either side
    [InlineData("hello there", "neon", false)]
    [InlineData("", "neon", false)]
    [InlineData(null, "neon", false)]
    [InlineData("neon", "", false)]
    [InlineData("neon", null, false)]
    public void Contains_IsACaseInsensitiveSubstringTest(string? text, string? phrase, bool expected)
    {
        Assert.Equal(expected, WakeWordMatch.Contains(text, phrase));
    }

    // ── HasRequest (ported) ─────────────────────────────────────────────────

    [Theory]
    [InlineData("neon what's the weather like")]
    [InlineData("a neon what's the weather like")]
    [InlineData("neon tell me a joke")]
    [InlineData("NEON What Is The Time")]           // casing must not matter
    [InlineData("neon   what    time   is   it")]   // runs of whitespace
    public void RequestSpokenWithTheWakeWord_IsDetected(string recognized)
    {
        Assert.True(WakeWordMatch.HasRequest(recognized, "neon"));
    }

    [Theory]
    [InlineData("neon")]
    [InlineData("a neon")]
    [InlineData("hey neon")]
    [InlineData("neon uh")]        // one stray token is not a request
    [InlineData("  neon  ")]
    public void WakeWordAlone_IsNotTreatedAsARequest(string recognized)
    {
        // Starting early on a single trailing token, which the small Vosk model appends
        // routinely, would transcribe a second of silence and answer it.
        Assert.False(WakeWordMatch.HasRequest(recognized, "neon"));
    }

    [Fact]
    public void TextBeforeTheWakeWordDoesNotCount()
    {
        Assert.False(WakeWordMatch.HasRequest("what's the weather like neon", "neon"));
    }

    [Fact]
    public void LastOccurrenceOfTheWakeWordWins()
    {
        Assert.True(WakeWordMatch.HasRequest("neon neon what's the weather", "neon"));
        Assert.False(WakeWordMatch.HasRequest("neon what neon", "neon"));
    }

    [Theory]
    [InlineData(null, "neon")]
    [InlineData("", "neon")]
    [InlineData("   ", "neon")]
    [InlineData("neon what's the weather", null)]
    [InlineData("neon what's the weather", "")]
    [InlineData("nothing matching here", "neon")]
    public void MissingOrUnmatchedInput_IsNotARequest(string? recognized, string? wakeWord)
    {
        Assert.False(WakeWordMatch.HasRequest(recognized, wakeWord));
        Assert.Equal(0, WakeWordMatch.TrailingWordCount(recognized, wakeWord));
    }

    [Fact]
    public void Constants_ArePinned()
    {
        Assert.Equal(2, WakeWordMatch.MinTrailingWordsForRequest);
        Assert.Equal(300, WakeWordMatch.DefaultLeadMs);
    }

    // ── WakeStart ───────────────────────────────────────────────────────────

    [Fact]
    public void WakeStart_IsTheLastOccurrenceOfThePhraseAsWords()
    {
        var words = new[] { Word("neon", 0.5, 0.9), Word("um", 1.0, 1.1), Word("Neon", 2.0, 2.4), Word("what", 2.5, 2.7) };

        Assert.Equal(TimeSpan.FromSeconds(2.0), WakeWordMatch.WakeStart(words, "neon"));
    }

    [Fact]
    public void WakeStart_MultiWordPhrase_MatchesTheRun()
    {
        var words = new[] { Word("hey", 0.5, 0.7), Word("neon", 0.8, 1.1), Word("hey", 1.5, 1.7), Word("there", 1.8, 2.0) };

        Assert.Equal(TimeSpan.FromSeconds(0.5), WakeWordMatch.WakeStart(words, "hey neon"));
        Assert.Null(WakeWordMatch.WakeStart(words, "hey neon there"));
    }

    [Fact]
    public void WakeStart_NoTimingsOrNoWholeWord_IsNull()
    {
        Assert.Null(WakeWordMatch.WakeStart(Array.Empty<WakeWordTiming>(), "neon"));
        Assert.Null(WakeWordMatch.WakeStart(null, "neon"));
        Assert.Null(WakeWordMatch.WakeStart(new[] { Word("neonatal", 0.5, 1.0) }, "neon"));   // Contains is true, the timing is not usable
        Assert.Null(WakeWordMatch.WakeStart(new[] { Word("neon", 0.5, 1.0) }, ""));
    }

    // ── SeedDiscard ─────────────────────────────────────────────────────────

    [Fact]
    public void SeedDiscard_KeepsTheLeadBeforeThePhrase()
    {
        // 10 s fed; the pre-roll holds the last 5 s (160 000 bytes), i.e. from 5.0 s. The phrase
        // starts at 8.0 s; keep from 7.7 s: drop 2.7 s = 86 400 bytes.
        int discard = WakeWordMatch.SeedDiscard(TimeSpan.FromSeconds(8.0), bytesFed: 320_000, preRollBytes: 160_000, Format);

        Assert.Equal(86_400, discard);
        Assert.Equal(0, discard % Format.BlockAlign);
    }

    [Fact]
    public void SeedDiscard_LeadIsConfigurable_AndAFullPreRollIsNeverExceeded()
    {
        Assert.Equal(96_000, WakeWordMatch.SeedDiscard(TimeSpan.FromSeconds(8.0), 320_000, 160_000, Format, leadMs: 0));
        Assert.Equal(160_000, WakeWordMatch.SeedDiscard(TimeSpan.FromSeconds(30), 320_000, 160_000, Format));   // a timing past the end: keep nothing older than the end
    }

    [Fact]
    public void SeedDiscard_PhraseOlderThanThePreRoll_KeepsEverything()
    {
        Assert.Equal(0, WakeWordMatch.SeedDiscard(TimeSpan.FromSeconds(2.0), 320_000, 160_000, Format));
        Assert.Equal(0, WakeWordMatch.SeedDiscard(TimeSpan.FromSeconds(5.1), 320_000, 160_000, Format));   // the lead reaches the pre-roll's start
    }

    [Fact]
    public void SeedDiscard_NoTiming_KeepsEverything()
    {
        Assert.Equal(0, WakeWordMatch.SeedDiscard(null, 320_000, 160_000, Format));
        Assert.Equal(0, WakeWordMatch.SeedDiscard(TimeSpan.FromSeconds(8), 320_000, 0, Format));
    }

    [Fact]
    public void SeedDiscard_PreRollNotYetFull_MeasuresFromTheStreamStart()
    {
        // 2 s fed and the pre-roll holds all of it: from 0 s. Phrase at 1.0 s, keep from 0.7 s.
        Assert.Equal(22_400, WakeWordMatch.SeedDiscard(TimeSpan.FromSeconds(1.0), 64_000, 64_000, Format));
    }

    // ── SoundsLike (the interrupt's echo guard) ─────────────────────────────

    [Theory]
    [InlineData("The veil or a mask!", "theveiloramask")]
    [InlineData("hey neon", "heyneon")]
    [InlineData("I'm Neon, v2.", "imneonv")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("42 — …", "")]
    public void Letters_KeepsLowercaseLettersOnly(string? text, string expected) =>
        Assert.Equal(expected, WakeWordMatch.Letters(text));

    [Theory]
    [InlineData("neon", 100, 0)]
    [InlineData("neon", 65, 1)]      // 4 × 0.35 = 1.4
    [InlineData("neon", 50, 2)]
    [InlineData("velora", 65, 2)]    // 6 × 0.35 = 2.1
    [InlineData("velora", 100, 0)]
    [InlineData("hey neon", 65, 2)]  // 7 letters × 0.35 = 2.45
    [InlineData("a", 0, 0)]          // never as many edits as letters
    [InlineData("", 65, 0)]
    public void MaxEdits_ScalesWithTheLetters(string phrase, int percent, int expected) =>
        Assert.Equal(expected, WakeWordMatch.MaxEdits(phrase, percent));

    [Fact]
    public void SoundsLike_TheFieldCase_TheVeilOrAMaskIsVelora()
    {
        Assert.True(WakeWordMatch.SoundsLike("Somewhere the veil or a mask hides the sea.", "velora", 65, out var near));
        Assert.Contains("veil", near);   // the first window that fits: "eveilora" (2 edits), the scan starting a letter early
        Assert.False(WakeWordMatch.SoundsLike("Somewhere the veil or a mask hides the sea.", "velora", 100, out near));   // exact only
        Assert.Equal("", near);
    }

    [Theory]
    [InlineData("I'm Neon.", "neon", 100, true)]            // the exact phrase, as before
    [InlineData("an eon passed", "neon", 100, true)]        // across a word boundary, as the decoder hears it
    [InlineData("Leon called.", "neon", 65, true)]          // one edit
    [InlineData("Carry on.", "neon", 65, false)]            // "on" is two edits away: not for a 4-letter phrase
    [InlineData("Carry on.", "neon", 50, true)]             // at 50 it is
    [InlineData("Nothing here.", "velora", 65, false)]
    [InlineData("", "neon", 65, false)]
    [InlineData("neon", "", 65, false)]
    [InlineData("neon", "  ", 65, false)]
    public void SoundsLike_Cases(string text, string phrase, int percent, bool expected) =>
        Assert.Equal(expected, WakeWordMatch.SoundsLike(text, phrase, percent, out _));
}
