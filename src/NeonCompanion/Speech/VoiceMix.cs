using System.Globalization;

namespace NeonCompanion.Speech;

/// <summary>
/// The one place a Kokoro <c>voice</c> string is built from the primary voice, the optional
/// secondary voice and the mix. Kokoro-FastAPI blends voices when the field is a weighted
/// expression — <c>af_heart(70)+af_sky(30)</c>, weights normalised to 100 % — and the rest of the
/// speech path (<see cref="SpeechOutput"/>, <see cref="KokoroHttpSynthesizer"/>) carries the
/// string verbatim, so a mix costs nothing downstream. <see cref="TryParse"/> is the inverse for
/// the in-process engine (<see cref="KokoroInProcessSynthesizer"/>), which blends the voice
/// embeddings itself.
/// </summary>
public static class VoiceMix
{
    /// <summary>
    /// The parts of a wire voice: a bare name is one part at 100; <c>a(70)+b(30)</c> is two. False
    /// for a blank spec, a part without a closing parenthesis, a non-digit percent or an empty
    /// name; the percents are taken as written (Kokoro normalises them).
    /// </summary>
    public static bool TryParse(string? spec, out IReadOnlyList<(string Name, int Percent)> parts)
    {
        var result = new List<(string, int)>();
        parts = result;
        foreach (var raw in (spec ?? "").Split('+'))
        {
            string part = raw.Trim();
            if (part.Length == 0)
            {
                return false;
            }

            int open = part.IndexOf('(');
            if (open < 0)
            {
                result.Add((part, 100));
                continue;
            }

            if (open == 0 || !part.EndsWith(')') || !int.TryParse(part.AsSpan(open + 1, part.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int percent))
            {
                return false;
            }

            result.Add((part[..open].Trim(), percent));
        }

        return result.Count > 0;
    }

    /// <summary>
    /// The wire voice: <paramref name="primary"/> alone when <paramref name="secondary"/> is blank,
    /// names the same voice, or <paramref name="primaryPercent"/> is 100 or more;
    /// <paramref name="secondary"/> alone when the percent is 0 or less; otherwise
    /// <c>primary(p)+secondary(100-p)</c>. Names are trimmed; digits are invariant.
    /// </summary>
    public static string Spec(string primary, string? secondary, int primaryPercent)
    {
        string first = (primary ?? "").Trim();
        string second = (secondary ?? "").Trim();
        if (second.Length == 0 || string.Equals(first, second, StringComparison.Ordinal) || primaryPercent >= 100)
        {
            return first;
        }

        if (primaryPercent <= 0)
        {
            return second;
        }

        return first + "(" + primaryPercent.ToString(CultureInfo.InvariantCulture) + ")+"
            + second + "(" + (100 - primaryPercent).ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The distinct voice names <see cref="Spec"/> actually sends (one or two), for checks against the server's list.</summary>
    public static IReadOnlyList<string> Voices(string primary, string? secondary, int primaryPercent)
    {
        string first = (primary ?? "").Trim();
        string second = (secondary ?? "").Trim();
        if (second.Length == 0 || string.Equals(first, second, StringComparison.Ordinal) || primaryPercent >= 100)
        {
            return new[] { first };
        }

        if (primaryPercent <= 0)
        {
            return new[] { second };
        }

        return new[] { first, second };
    }
}
