namespace NeonSidekick.Files;

/// <summary>
/// A path glob for <c>search_files</c> (2026-09-17; <c>find_files</c> shared it until it was folded in on 2026-09-18): <c>**</c> matches any
/// run of segments (none too), <c>*</c> and <c>?</c> match within one segment, <c>/</c> and
/// <c>\</c> are the same separator, case is ignored. A pattern with no separator and no <c>**</c>
/// is a plain name pattern the walks hand to <see cref="System.IO.Enumeration.FileSystemName"/>
/// as before — <see cref="IsPathPattern"/> is that test.
/// </summary>
public static class PathGlob
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>Whether <paramref name="pattern"/> speaks of folders (a separator or <c>**</c>) and must be matched by <see cref="IsMatch"/> over the whole relative path.</summary>
    public static bool IsPathPattern(string pattern) =>
        pattern.IndexOfAny(Separators) >= 0 || pattern.Contains("**", StringComparison.Ordinal);

    /// <summary>Whether <paramref name="relativePath"/> (any separator, no leading one) matches <paramref name="pattern"/> whole.</summary>
    public static bool IsMatch(string pattern, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(relativePath);
        string[] parts = pattern.Trim().Trim(Separators).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        string[] segments = relativePath.Trim(Separators).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return MatchSegments(parts, 0, segments, 0);
    }

    private static bool MatchSegments(string[] parts, int p, string[] segments, int s)
    {
        while (p < parts.Length)
        {
            if (parts[p] == "**")
            {
                // Collapse a run of ** and try every split of the rest.
                while (p < parts.Length && parts[p] == "**")
                {
                    p++;
                }

                if (p == parts.Length)
                {
                    return true;
                }

                for (int i = s; i <= segments.Length; i++)
                {
                    if (MatchSegments(parts, p, segments, i))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (s >= segments.Length || !MatchSegment(parts[p], segments[s]))
            {
                return false;
            }

            p++;
            s++;
        }

        return s == segments.Length;
    }

    /// <summary>One segment against one glob segment: <c>*</c> any run, <c>?</c> one character, case ignored.</summary>
    public static bool MatchSegment(string pattern, string text)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(text);
        int p = 0, t = 0, starP = -1, starT = -1;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starT = t;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                t = ++starT;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
