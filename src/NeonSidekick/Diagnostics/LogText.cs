using System.Text;

namespace NeonSidekick.Diagnostics;

/// <summary>
/// The one cutter behind every "text in the log" line: a message, a transcription, a tool's
/// result, a command's argument. The first line alone, its whitespace runs collapsed to one
/// space, cut at <see cref="DefaultChars"/> with an ellipsis. References nothing but
/// <c>System</c>, like <see cref="DiagnosticLog"/>.
/// </summary>
public static class LogText
{
    /// <summary>The cut most lines use; a command's argument takes a shorter one.</summary>
    public const int DefaultChars = 200;

    /// <summary>The character that closes a cut excerpt.</summary>
    public const char Ellipsis = '…';

    /// <summary>
    /// The first non-blank line of <paramref name="text"/>, whitespace collapsed, at most
    /// <paramref name="max"/> characters with the ellipsis as the last one when cut. Empty for
    /// null, empty or all-blank text.
    /// </summary>
    public static string Excerpt(string? text, int max = DefaultChars)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 2);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        // The first line with anything on it: a reply or a file that opens with a blank line
        // still has a first line worth quoting.
        ReadOnlySpan<char> line = ReadOnlySpan<char>.Empty;
        foreach (var candidate in text.AsSpan().EnumerateLines())
        {
            if (!candidate.IsWhiteSpace())
            {
                line = candidate.Trim();
                break;
            }
        }

        var sb = new StringBuilder(Math.Min(line.Length, max));
        bool space = false;
        foreach (char c in line)
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c);
            if (sb.Length > max)
            {
                break;
            }
        }

        if (sb.Length > max)
        {
            sb.Length = max - 1;
            while (sb.Length > 0 && sb[^1] == ' ')
            {
                sb.Length--;
            }

            sb.Append(Ellipsis);
        }

        return sb.ToString();
    }

    /// <summary>The excerpt in double quotes: <c>"first line…"</c>; <c>""</c> for nothing.</summary>
    public static string Quoted(string? text, int max = DefaultChars) => "\"" + Excerpt(text, max) + "\"";
}
