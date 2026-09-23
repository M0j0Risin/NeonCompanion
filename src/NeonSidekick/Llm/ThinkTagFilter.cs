using System.Text;

namespace NeonSidekick.Llm;

/// <summary>
/// Drops <c>&lt;think&gt;</c> … <c>&lt;/think&gt;</c> spans, and any orphan <c>&lt;/think&gt;</c>, from
/// a streamed reply. Response shaping, so it lives with <see cref="Assistant"/> rather than the
/// client. One instance per model call; pure, no console, no log — the caller reads the flags.
///
/// <para>Why it exists: a server that separates thinking into <c>reasoning_content</c> only does
/// so when it sees the opening tag (SGLang's <c>qwen3</c> parser), and Qwen3 sometimes skips the
/// opener after a tool result, so the thinking and a literal <c>&lt;/think&gt;</c> arrive as
/// content (seen in the field 2026-09-11). A server with no reasoning parser at all streams the
/// whole block as content. Either way the tags are noise, and a spoken reply must never read them.</para>
///
/// <para>Rules: a suffix that is a proper prefix of either tag is held back until the next delta
/// completes or disproves it (<see cref="Flush"/> releases it at the end of the stream); inside a
/// block nothing is emitted; a closing tag, orphan or not, is dropped together with the whitespace
/// that follows it (Qwen's <c>\n\n</c>), so no blank line lands mid-reply. Everything else passes
/// through in order: the concatenated output is the input minus the tagged spans.</para>
///
/// <para>What it cannot do: text streamed before an orphan closing tag has already been emitted.
/// The caller sees <see cref="SawOrphanClose"/> and can at least keep the tag out of the history.</para>
/// </summary>
public sealed class ThinkTagFilter
{
    public const string OpenTag = "<think>";
    public const string CloseTag = "</think>";

    private string _pending = string.Empty;
    private bool _inBlock;
    private bool _skipWhitespace;

    /// <summary>A <c>&lt;/think&gt;</c> arrived with no <c>&lt;think&gt;</c> before it: the text emitted before it was thinking.</summary>
    public bool SawOrphanClose { get; private set; }

    /// <summary>A whole <c>&lt;think&gt;</c> … <c>&lt;/think&gt;</c> block was dropped.</summary>
    public bool SawBlock { get; private set; }

    /// <summary>Feeds one delta and returns the text that is safe to emit now, possibly empty.</summary>
    public string Push(string delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (delta.Length == 0)
        {
            return string.Empty;
        }

        string s = _pending.Length == 0 ? delta : _pending + delta;
        _pending = string.Empty;
        StringBuilder? output = null;
        int pos = 0;

        while (pos < s.Length)
        {
            if (_skipWhitespace)
            {
                while (pos < s.Length && IsSkippable(s[pos]))
                {
                    pos++;
                }

                if (pos == s.Length)
                {
                    break;
                }

                _skipWhitespace = false;
            }

            if (_inBlock)
            {
                int close = s.IndexOf(CloseTag, pos, StringComparison.Ordinal);
                if (close < 0)
                {
                    // Thinking is dropped; only a possible start of the closing tag is kept.
                    _pending = s.Substring(s.Length - PartialSuffix(s, pos, CloseTag));
                    break;
                }

                pos = close + CloseTag.Length;
                _inBlock = false;
                SawBlock = true;
                _skipWhitespace = true;
                continue;
            }

            int open = s.IndexOf(OpenTag, pos, StringComparison.Ordinal);
            int orphan = s.IndexOf(CloseTag, pos, StringComparison.Ordinal);
            int next = open < 0 ? orphan : orphan < 0 ? open : Math.Min(open, orphan);
            if (next < 0)
            {
                int hold = Math.Max(PartialSuffix(s, pos, OpenTag), PartialSuffix(s, pos, CloseTag));
                Emit(ref output, s, pos, s.Length - hold - pos);
                _pending = s.Substring(s.Length - hold);
                break;
            }

            Emit(ref output, s, pos, next - pos);
            if (next == open)
            {
                _inBlock = true;
                pos = next + OpenTag.Length;
            }
            else
            {
                SawOrphanClose = true;
                _skipWhitespace = true;
                pos = next + CloseTag.Length;
            }
        }

        return output?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Releases whatever was held back as a possible tag start. Inside an unfinished block it is
    /// thinking and is dropped.
    /// </summary>
    public string Flush()
    {
        string held = _inBlock ? string.Empty : _pending;
        _pending = string.Empty;
        return held;
    }

    private static bool IsSkippable(char c) => c is ' ' or '\n' or '\r' or '\t';

    /// <summary>The length of the longest suffix of <paramref name="s"/> (at or after <paramref name="from"/>) that is a proper prefix of <paramref name="tag"/>.</summary>
    private static int PartialSuffix(string s, int from, string tag)
    {
        int max = Math.Min(tag.Length - 1, s.Length - from);
        for (int k = max; k >= 1; k--)
        {
            if (string.CompareOrdinal(s, s.Length - k, tag, 0, k) == 0)
            {
                return k;
            }
        }

        return 0;
    }

    private static void Emit(ref StringBuilder? output, string s, int start, int count)
    {
        if (count <= 0)
        {
            return;
        }

        (output ??= new StringBuilder()).Append(s, start, count);
    }
}
