using System.Text;

namespace NeonCompanion.Speech;

/// <summary>
/// Incremental sentence splitter for streaming text-to-speech.
///
/// <para>Fed arbitrary chunks of text as they arrive from the model, it emits complete sentences
/// as soon as a boundary is confirmed, so synthesis of sentence N can overlap generation of
/// sentence N+1.</para>
///
/// <para>A terminator is only treated as a boundary when something follows it. A terminator
/// sitting at the very end of the buffer is held back until the next chunk arrives — that single
/// rule is what keeps "3." + "14 is pi." from being split mid-number. Whatever is still pending
/// when the stream ends is returned by <see cref="Flush"/>.</para>
///
/// <para>Deliberately a hand-rolled character scanner rather than a regex: the abbreviation and
/// decimal rules below are easier to get right (and to test) in straight code, and nothing here
/// needs the regex engine under NativeAOT.</para>
///
/// <para>Not thread-safe — owned by a single producer loop.</para>
/// </summary>
public sealed class SentenceChunker
{
    /// <summary>Characters buffered before a run-on sentence is broken up anyway.</summary>
    public const int DefaultMaxChunkChars = 240;

    private static readonly char[] Terminators = { '.', '!', '?' };

    /// <summary>Closing punctuation that belongs to the sentence it follows.</summary>
    private static readonly char[] Closers = { '"', '\'', ')', ']', '}', '”', '’' };

    /// <summary>
    /// Tokens that end in a period without ending a sentence. Compared case-insensitively against
    /// the whitespace-delimited word immediately preceding the period.
    /// </summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc",
        "e.g", "i.e", "inc", "ltd", "corp", "fig", "no", "approx", "al", "ca",
        "dept", "est",
    };

    private readonly StringBuilder _buffer = new();

    public SentenceChunker(int maxChunkChars = DefaultMaxChunkChars)
    {
        if (maxChunkChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkChars), "Max chunk size must be positive.");
        }

        MaxChunkChars = maxChunkChars;
    }

    /// <summary>Characters buffered before a run-on sentence is broken up anyway.</summary>
    public int MaxChunkChars { get; }

    /// <summary>Whether any text is currently buffered and unemitted.</summary>
    public bool HasPending => _buffer.Length > 0;

    /// <summary>
    /// Appends streamed text and returns every sentence that became complete. Never null;
    /// usually empty, since most chunks land mid-sentence.
    /// </summary>
    public IReadOnlyList<string> Append(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        _buffer.Append(text);

        List<string>? sentences = null;

        while (true)
        {
            var candidate = TakeNextSentence();
            if (candidate is null)
            {
                break;
            }

            if (candidate.Length == 0)
            {
                continue;
            }

            (sentences ??= new List<string>()).Add(candidate);
        }

        return (IReadOnlyList<string>?)sentences ?? Array.Empty<string>();
    }

    /// <summary>
    /// Returns the trailing partial buffer and clears it. Call once the stream has ended — a
    /// final sentence whose terminator arrived last is still pending until now. Returns an empty
    /// string when nothing is left worth speaking.
    /// </summary>
    public string Flush()
    {
        if (_buffer.Length == 0)
        {
            return string.Empty;
        }

        var remainder = _buffer.ToString().Trim();
        _buffer.Clear();

        return IsSpeakable(remainder) ? remainder : string.Empty;
    }

    /// <summary>Discards any buffered text. Used when a turn is abandoned.</summary>
    public void Reset() => _buffer.Clear();

    /// <summary>
    /// Removes and returns the next complete sentence, or null when the buffer holds no confirmed
    /// boundary. Returns an empty string for a chunk that is not worth speaking (pure
    /// punctuation), which the caller skips while continuing to scan.
    /// </summary>
    private string? TakeNextSentence()
    {
        if (_buffer.Length == 0)
        {
            return null;
        }

        var buffer = _buffer.ToString();
        var end = FindBoundary(buffer);

        if (end < 0)
        {
            end = FindRunOnBreak(buffer);
            if (end < 0)
            {
                return null;
            }
        }

        var sentence = buffer.Substring(0, end).Trim();
        _buffer.Remove(0, end);

        // Leading whitespace belongs to the separator, not the next sentence.
        while (_buffer.Length > 0 && char.IsWhiteSpace(_buffer[0]))
        {
            _buffer.Remove(0, 1);
        }

        return IsSpeakable(sentence) ? sentence : string.Empty;
    }

    /// <summary>
    /// Returns the exclusive end index of the first complete sentence, or -1 if the buffer does
    /// not yet contain a confirmed boundary.
    /// </summary>
    private static int FindBoundary(string buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var c = buffer[i];

            // A newline is a hard prosodic break. Model output is markdown-ish, so this is what
            // stops a bullet list from being glued into one long utterance.
            if (c == '\n')
            {
                return i + 1;
            }

            if (Array.IndexOf(Terminators, c) < 0)
            {
                continue;
            }

            if (c == '.' && IsSuppressedPeriod(buffer, i))
            {
                continue;
            }

            // Collapse runs like "?!" or "..." into a single boundary.
            var last = i;
            while (last + 1 < buffer.Length && Array.IndexOf(Terminators, buffer[last + 1]) >= 0)
            {
                last++;
            }

            // Quotes and brackets close the sentence they belong to.
            var end = last + 1;
            while (end < buffer.Length && Array.IndexOf(Closers, buffer[end]) >= 0)
            {
                end++;
            }

            // Confirmed only by what follows. Nothing following yet means we wait — that is both
            // the decimal guard ("3." + "14") and the end-of-stream case, which Flush picks up.
            if (end >= buffer.Length)
            {
                return -1;
            }

            if (char.IsWhiteSpace(buffer[end]))
            {
                return end;
            }

            i = last;
        }

        return -1;
    }

    /// <summary>
    /// Breaks an over-long buffer that contains no boundary, so a run-on sentence cannot stall
    /// audio indefinitely. Prefers the last whitespace within the limit.
    /// </summary>
    private int FindRunOnBreak(string buffer)
    {
        if (buffer.Length < MaxChunkChars)
        {
            return -1;
        }

        for (var i = MaxChunkChars - 1; i > 0; i--)
        {
            if (char.IsWhiteSpace(buffer[i]))
            {
                return i;
            }
        }

        return MaxChunkChars;
    }

    /// <summary>
    /// Whether the period at <paramref name="index"/> is part of an abbreviation or an initial
    /// rather than a sentence ending.
    /// </summary>
    private static bool IsSuppressedPeriod(string buffer, int index)
    {
        var start = index;
        while (start > 0 && !char.IsWhiteSpace(buffer[start - 1]))
        {
            start--;
        }

        var length = index - start;
        if (length == 0)
        {
            return false;
        }

        // A lone letter before a period is an initial: "J. R. R. Tolkien".
        if (length == 1 && char.IsLetter(buffer[start]))
        {
            return true;
        }

        return Abbreviations.Contains(buffer.Substring(start, length));
    }

    /// <summary>
    /// Whether a chunk is worth handing to TTS. Pure punctuation — markdown rules, stray emphasis
    /// markers — would otherwise cost a synthesis round trip to say nothing.
    /// </summary>
    private static bool IsSpeakable(string text)
    {
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                return true;
            }
        }

        return false;
    }
}
