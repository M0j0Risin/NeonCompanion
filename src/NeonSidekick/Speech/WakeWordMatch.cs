using NeonSidekick.Audio;

namespace NeonSidekick.Speech;

/// <summary>
/// The pure half of wake-word detection: does a recogniser's text contain the phrase, did the
/// user already say their request in the same breath, and where in the pre-roll does the phrase
/// start. Static so every rule is pinned without a model or a microphone.
///
/// <para>Matching is a lowercase substring test: the small Vosk model is
/// open-vocabulary and hears "neon" reliably, and anything fuzzier starts firing on the room.
/// The phrase is normalised the same way the settings menu stores it (<see cref="NormalizePhrase"/>).</para>
///
/// <para><b>The request shortcut.</b> Vosk reports a final result when its recogniser settles,
/// which for a phrase spoken in one breath ("neon, what's the weather like") is <em>after</em> the
/// whole request: the user has stopped talking, no speech will follow, and waiting for it would
/// sit out the whole no-speech timeout. When at least <see cref="MinTrailingWordsForRequest"/>
/// words follow the last occurrence of the phrase, the request has been said and the seed is
/// transcribed straight away. Two words, not one: the small model routinely tacks a stray token
/// onto the phrase, and a turn started on that would transcribe a second of silence.</para>
/// </summary>
public static class WakeWordMatch
{
    public const int MinTrailingWordsForRequest = 2;

    /// <summary>How much audio before the phrase the seed keeps, so its first syllable is not clipped.</summary>
    public const int DefaultLeadMs = 300;

    private static readonly char[] Separators = { ' ', '\t', '\r', '\n' };

    /// <summary>Lowercase, single-spaced, trimmed; empty for null or blank.</summary>
    public static string NormalizePhrase(string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return "";
        }

        return string.Join(' ', phrase.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Whether <paramref name="text"/> contains the phrase, ignoring case and runs of whitespace. False for an empty phrase.</summary>
    public static bool Contains(string? text, string? phrase)
    {
        string needle = NormalizePhrase(phrase);
        return needle.Length > 0 && NormalizePhrase(text).Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>
    /// The interrupt's echo guard: whether <paramref name="text"/> (what the speakers just
    /// played) contains a stretch of letters within <see cref="MaxEdits"/> of the phrase. The
    /// keyword grammar forces anything close enough in the assistant's own voice into the
    /// phrase — "the veil or a mask" came back as "velora" in the field — and the assistant's text is
    /// known, so a hit that lands next to a near-match of it is its own echo. Letters only, no
    /// spaces: the decoder crossed the word boundary and so must this. <paramref name="matched"/>
    /// is the stretch that matched (for the log), empty when nothing did or the phrase is empty.
    /// </summary>
    /// <param name="minSimilarityPercent">100 ignores the exact phrase only; see <see cref="MaxEdits"/>.</param>
    public static bool SoundsLike(string? text, string? phrase, int minSimilarityPercent, out string matched)
    {
        matched = "";
        string needle = Letters(phrase);
        string haystack = Letters(text);
        if (needle.Length == 0 || haystack.Length == 0)
        {
            return false;
        }

        int edits = MaxEdits(needle, minSimilarityPercent);
        int shortest = Math.Max(1, needle.Length - edits);
        int longest = needle.Length + edits;
        for (int start = 0; start < haystack.Length; start++)
        {
            for (int length = shortest; length <= longest && start + length <= haystack.Length; length++)
            {
                string window = haystack.Substring(start, length);
                if (SpeechTranscript.EditDistance(window, needle) <= edits)
                {
                    matched = window;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// How many edits <see cref="SoundsLike"/> allows: the letters of the phrase times
    /// (100 − <paramref name="minSimilarityPercent"/>) %, rounded down, never as many as the
    /// letters themselves. 100 ⇒ 0 (exact); 65 ⇒ 1 for "neon", 2 for "velora" — the allowance
    /// <c>SpeechTranscript.IsCloseEnough</c> uses on the wake path.
    /// </summary>
    public static int MaxEdits(string? phrase, int minSimilarityPercent)
    {
        int letters = Letters(phrase).Length;
        if (letters == 0)
        {
            return 0;
        }

        int percent = Math.Clamp(minSimilarityPercent, 0, 100);
        int edits = letters * (100 - percent) / 100;
        return Math.Min(edits, letters - 1);
    }

    /// <summary>Lowercase letters only: spaces, digits and punctuation dropped. "The veil or a mask!" ⇒ "theveiloramask".</summary>
    public static string Letters(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var letters = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                letters.Append(char.ToLowerInvariant(c));
            }
        }

        return letters.ToString();
    }

    /// <summary>How many words follow the last occurrence of the phrase; 0 when it does not occur.</summary>
    public static int TrailingWordCount(string? text, string? phrase)
    {
        string needle = NormalizePhrase(phrase);
        string haystack = NormalizePhrase(text);
        if (needle.Length == 0 || haystack.Length == 0)
        {
            return 0;
        }

        int index = haystack.LastIndexOf(needle, StringComparison.Ordinal);
        if (index < 0)
        {
            return 0;
        }

        return haystack[(index + needle.Length)..].Split(Separators, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>Whether the request was spoken with the phrase (see the class remarks).</summary>
    public static bool HasRequest(string? text, string? phrase) => TrailingWordCount(text, phrase) >= MinTrailingWordsForRequest;

    /// <summary>
    /// Where the last occurrence of the phrase starts, from the recogniser's word timings: the
    /// start of the first word of the last run of words equal to the phrase's words. Null when
    /// there are no timings or the phrase's words do not appear as words (a substring hit inside
    /// a longer word, say), in which case the caller keeps the whole pre-roll.
    /// </summary>
    public static TimeSpan? WakeStart(IReadOnlyList<WakeWordTiming>? words, string? phrase)
    {
        if (words is null || words.Count == 0)
        {
            return null;
        }

        var wanted = NormalizePhrase(phrase).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wanted.Length == 0 || wanted.Length > words.Count)
        {
            return null;
        }

        for (int start = words.Count - wanted.Length; start >= 0; start--)
        {
            bool match = true;
            for (int i = 0; i < wanted.Length; i++)
            {
                if (!string.Equals(NormalizePhrase(words[start + i].Word), wanted[i], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return words[start].Start;
            }
        }

        return null;
    }

    /// <summary>
    /// How many of the pre-roll's oldest bytes to drop so the seed starts <paramref name="leadMs"/>
    /// before the phrase. The pre-roll's last byte is at stream position <paramref name="bytesFed"/>,
    /// so it starts at <c>bytesFed - preRollBytes</c>; the phrase starts at <paramref name="wakeStart"/>
    /// in the same stream. 0 when there is no timing, when the phrase is older than the pre-roll,
    /// or when the lead already reaches the pre-roll's start; never more than the pre-roll holds.
    /// The result is a whole number of frames.
    /// </summary>
    public static int SeedDiscard(TimeSpan? wakeStart, long bytesFed, int preRollBytes, PcmFormat format, int leadMs = DefaultLeadMs)
    {
        if (wakeStart is not { } start || preRollBytes <= 0 || format.BlockAlign <= 0)
        {
            return 0;
        }

        long wakeByte = (long)(start.TotalSeconds * format.BytesPerSecond);
        long keepFrom = wakeByte - format.BytesFor(Math.Max(0, leadMs));
        long preRollStart = bytesFed - preRollBytes;
        long discard = keepFrom - preRollStart;
        if (discard <= 0)
        {
            return 0;
        }

        discard = Math.Min(discard, preRollBytes);
        return (int)(discard - discard % format.BlockAlign);
    }
}
