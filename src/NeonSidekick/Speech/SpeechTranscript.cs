using System.Text;

namespace NeonSidekick.Speech;

/// <summary>
/// Cleans what Whisper returns before anything treats it as something the user said.
///
/// <para>Whisper annotates non-speech rather than ignoring it: a perfectly good transcription
/// arrives as <c>"[music] Neon are you there?"</c> and near-silence as <c>"[BLANK_AUDIO]"</c>.
/// Those are the model describing the audio, not words anyone spoke; passed on, the assistant
/// is asked to respond to <c>[music]</c>, and a turn that captured nothing still looks like a
/// request. Observed verbatim on a working setup.</para>
///
/// <para>Only bracketed, parenthesised and asterisk-delimited spans are removed: those are the
/// shapes Whisper emits for non-speech, and anything cleverer starts deleting words people
/// said. <see cref="StripLeadingWakeWord"/> runs on the wake-word path only (M5); the echo
/// detection of the reference waits for barge-in (M6).</para>
/// </summary>
internal static class SpeechTranscript
{
    private static readonly char[] LeadingPunctuation = { ' ', ',', '.', '!', '?', ':', ';', '-', '—' };

    /// <summary>
    /// Removes the wake word from the front of a transcript, with the punctuation after it.
    ///
    /// <para>Matching is fuzzy because the small models reliably mishear short proper nouns:
    /// "neon" comes back as "Leon" (one substitution) and "Nia" (two). The allowance scales with
    /// length so short wake words cannot swallow unrelated openings: at four letters only a single
    /// edit is permitted, so "hello", "need" and "when" are never mistaken for "neon". A phrase of
    /// several words is matched exactly on its first word. Returns the input (trimmed) when the
    /// first word does not match, so an ordinary sentence is never truncated; empty when nothing
    /// but the wake word was said.</para>
    /// </summary>
    internal static string StripLeadingWakeWord(string? text, string? wakeWord)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(wakeWord))
        {
            return text?.Trim() ?? "";
        }

        string trimmed = text.TrimStart();

        // The first run of letters and apostrophes; the punctuation after it goes with it, so
        // "Leon, how's it going?" leaves "how's it going?" rather than ", how's it going?".
        int end = 0;
        while (end < trimmed.Length && (char.IsLetter(trimmed[end]) || trimmed[end] == '\''))
        {
            end++;
        }

        if (end == 0)
        {
            return trimmed.Trim();
        }

        string firstWord = trimmed[..end];
        if (!IsCloseEnough(firstWord, wakeWord.Trim()))
        {
            return trimmed.Trim();
        }

        return trimmed[end..].TrimStart(LeadingPunctuation).Trim();
    }

    /// <summary>One edit for words up to five letters, two beyond; anything looser matches real words.</summary>
    internal static bool IsCloseEnough(string candidate, string wakeWord)
    {
        if (candidate.Equals(wakeWord, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (wakeWord.Contains(' '))
        {
            return false;
        }

        int allowed = wakeWord.Length <= 5 ? 1 : 2;
        return EditDistance(candidate.ToLowerInvariant(), wakeWord.ToLowerInvariant()) <= allowed;
    }

    /// <summary>Levenshtein distance, two rows rather than a full matrix.</summary>
    internal static int EditDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// Strips annotation spans and collapses whitespace. Empty when nothing but annotations (or
    /// stranded punctuation such as the <c>.</c> of <c>"[BLANK_AUDIO]."</c>) remains, which the
    /// caller must treat as "heard nothing", never as an empty question.
    /// </summary>
    internal static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var kept = new StringBuilder(text.Length);
        int squareDepth = 0;
        int roundDepth = 0;
        bool inAsterisks = false;

        foreach (char c in text)
        {
            switch (c)
            {
                case '[':
                    squareDepth++;
                    continue;
                case ']':
                    if (squareDepth > 0)
                    {
                        squareDepth--;
                    }

                    continue;
                case '(':
                    roundDepth++;
                    continue;
                case ')':
                    if (roundDepth > 0)
                    {
                        roundDepth--;
                    }

                    continue;
                case '*':
                    inAsterisks = !inAsterisks;
                    continue;
            }

            if (squareDepth == 0 && roundDepth == 0 && !inAsterisks)
            {
                kept.Append(c);
            }
        }

        // Collapse the runs of spaces a removed span leaves behind.
        var collapsed = new StringBuilder(kept.Length);
        bool lastWasSpace = false;
        foreach (char c in kept.ToString())
        {
            bool isSpace = char.IsWhiteSpace(c);
            if (isSpace && lastWasSpace)
            {
                continue;
            }

            collapsed.Append(isSpace ? ' ' : c);
            lastWasSpace = isSpace;
        }

        string cleaned = collapsed.ToString().Trim();
        return cleaned.Any(char.IsLetterOrDigit) ? cleaned : "";
    }
}
