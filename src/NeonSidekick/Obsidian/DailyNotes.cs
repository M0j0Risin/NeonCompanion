using System.Globalization;
using System.Text;

namespace NeonSidekick.Obsidian;

/// <summary>The daily-notes settings in force: the folder (vault-relative, <c>/</c>-separated, empty for the root), the moment.js date format, the template note (empty for none).</summary>
public sealed record DailyNoteSettings(string Folder, string Format, string Template)
{
    public static DailyNoteSettings Default { get; } = new("", DailyNotes.DefaultFormat, "");
}

/// <summary>
/// Obsidian's core Daily notes plugin, as a file layout (2026-09-22): the note for a day is
/// <c>&lt;folder&gt;/&lt;date in format&gt;.md</c>, created from the template note when missing. The format
/// is moment.js's (<see cref="Format"/>), since that is what <c>daily-notes.json</c> holds; the template
/// gets the core Templates plugin's <c>{{date}}</c>, <c>{{time}}</c>, <c>{{title}}</c> and
/// <c>{{date:FORMAT}}</c> / <c>{{time:FORMAT}}</c> substitutions. Invariant culture throughout: month and
/// day names are English, which is also what Obsidian writes under its default locale. Pure.
/// </summary>
public static class DailyNotes
{
    public const string DefaultFormat = "YYYY-MM-DD";
    public const string DefaultTimeFormat = "HH:mm";

    /// <summary>The settings from <c>daily-notes.json</c>'s text (null for no file): blanks fall back to the defaults, the folder and template trimmed of slashes.</summary>
    public static DailyNoteSettings Settings(DailyNotesConfigFile? file)
    {
        if (file is null)
        {
            return DailyNoteSettings.Default;
        }

        return new DailyNoteSettings(
            Clean(file.Folder),
            string.IsNullOrWhiteSpace(file.Format) ? DefaultFormat : file.Format.Trim(),
            Clean(file.Template));

        static string Clean(string? path) => (path ?? "").Replace('\\', '/').Trim().Trim('/');
    }

    /// <summary>The vault-relative path of the note for <paramref name="date"/>: <c>Daily/2026-09-22.md</c>.</summary>
    public static string PathFor(DailyNoteSettings settings, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string name = Format(date.ToDateTime(TimeOnly.MinValue), settings.Format) + ".md";
        return settings.Folder.Length == 0 ? name : settings.Folder + "/" + name;
    }

    /// <summary>
    /// A template's text for a note titled <paramref name="title"/> at <paramref name="now"/>:
    /// <c>{{title}}</c>, <c>{{date}}</c> (<see cref="DefaultFormat"/>), <c>{{time}}</c>
    /// (<see cref="DefaultTimeFormat"/>) and either with <c>:FORMAT</c>. An unknown <c>{{…}}</c> stays.
    /// </summary>
    public static string ApplyTemplate(string template, string title, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(title);
        var sb = new StringBuilder(template.Length);
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            sb.Append(template, i, open - i);
            string inner = template[(open + 2)..close].Trim();
            int colon = inner.IndexOf(':', StringComparison.Ordinal);
            string name = (colon < 0 ? inner : inner[..colon]).Trim().ToLowerInvariant();
            string? format = colon < 0 ? null : inner[(colon + 1)..];
            string? value = name switch
            {
                "title" => title,
                "date" => Format(now, format ?? DefaultFormat),
                "time" => Format(now, format ?? DefaultTimeFormat),
                _ => null,
            };
            sb.Append(value ?? template[open..(close + 2)]);
            i = close + 2;
        }

        return sb.ToString();
    }

    private static readonly string[] Tokens =
    [
        "YYYY", "YY", "Qo", "Q", "MMMM", "MMM", "MM", "Mo", "M", "DDDD", "DDD", "DD", "Do", "D", "dddd", "ddd", "dd", "d",
        "GGGG", "gggg", "WW", "W", "ww", "w", "HH", "H", "hh", "h", "mm", "m", "ss", "s", "A", "a", "X", "x",
    ];

    /// <summary>
    /// <paramref name="moment"/> formatted with a moment.js pattern: <c>YYYY YY Q MMMM MMM MM M Mo DDDD DDD DD D Do
    /// dddd ddd dd d GGGG/gggg WW/W ww/w HH H hh h mm m ss s A a X x</c>, <c>[literal]</c> text kept as written, every other
    /// character itself. Weeks are ISO weeks (moment's locale weeks are Sunday-first in <c>en</c>; ISO is what a
    /// weekly-notes format such as <c>gggg-[W]ww</c> means in practice). Invariant.
    /// </summary>
    public static string Format(DateTime moment, string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(pattern.Length + 8);
        int i = 0;
        while (i < pattern.Length)
        {
            if (pattern[i] == '[')
            {
                int close = pattern.IndexOf(']', i + 1);
                if (close > i)
                {
                    sb.Append(pattern, i + 1, close - i - 1);
                    i = close + 1;
                    continue;
                }
            }

            string? token = null;
            foreach (var t in Tokens)
            {
                if (string.CompareOrdinal(pattern, i, t, 0, t.Length) == 0)
                {
                    token = t;
                    break;
                }
            }

            if (token is null)
            {
                sb.Append(pattern[i]);
                i++;
                continue;
            }

            int week = ISOWeek.GetWeekOfYear(moment);
            int hour12 = moment.Hour % 12 == 0 ? 12 : moment.Hour % 12;
            int quarter = (moment.Month - 1) / 3 + 1;
            sb.Append(token switch
            {
                "YYYY" => moment.Year.ToString("0000", inv),
                "YY" => (moment.Year % 100).ToString("00", inv),
                "Q" => quarter.ToString(inv),
                "Qo" => Ordinal(quarter),
                "MMMM" => moment.ToString("MMMM", inv),
                "MMM" => moment.ToString("MMM", inv),
                "MM" => moment.Month.ToString("00", inv),
                "Mo" => Ordinal(moment.Month),
                "M" => moment.Month.ToString(inv),
                "DDDD" => moment.DayOfYear.ToString("000", inv),
                "DDD" => moment.DayOfYear.ToString(inv),
                "DD" => moment.Day.ToString("00", inv),
                "Do" => Ordinal(moment.Day),
                "D" => moment.Day.ToString(inv),
                "dddd" => moment.ToString("dddd", inv),
                "ddd" => moment.ToString("ddd", inv),
                "dd" => moment.ToString("ddd", inv)[..2],
                "d" => ((int)moment.DayOfWeek).ToString(inv),
                "GGGG" or "gggg" => ISOWeek.GetYear(moment).ToString("0000", inv),
                "WW" or "ww" => week.ToString("00", inv),
                "W" or "w" => week.ToString(inv),
                "HH" => moment.Hour.ToString("00", inv),
                "H" => moment.Hour.ToString(inv),
                "hh" => hour12.ToString("00", inv),
                "h" => hour12.ToString(inv),
                "mm" => moment.Minute.ToString("00", inv),
                "m" => moment.Minute.ToString(inv),
                "ss" => moment.Second.ToString("00", inv),
                "s" => moment.Second.ToString(inv),
                "A" => moment.Hour < 12 ? "AM" : "PM",
                "a" => moment.Hour < 12 ? "am" : "pm",
                "X" => new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(inv),
                "x" => new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)).ToUnixTimeMilliseconds().ToString(inv),
                _ => token,
            });
            i += token.Length;
        }

        return sb.ToString();
    }

    /// <summary><c>1st</c>, <c>2nd</c>, <c>3rd</c>, <c>11th</c>, <c>22nd</c>: moment's English ordinals.</summary>
    public static string Ordinal(int n)
    {
        int tens = n % 100;
        string suffix = tens is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>
    /// A <c>date</c> argument as a day: blank or <c>today</c> is <paramref name="today"/>, <c>yesterday</c> and
    /// <c>tomorrow</c> step one, <c>+3</c> / <c>-2</c> step that many days, else an ISO <c>YYYY-MM-DD</c>. False for anything else.
    /// </summary>
    public static bool TryParseDay(string text, DateOnly today, out DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(text);
        string t = text.Trim().ToLowerInvariant();
        day = today;
        switch (t)
        {
            case "" or "today":
                return true;
            case "yesterday":
                day = today.AddDays(-1);
                return true;
            case "tomorrow":
                day = today.AddDays(1);
                return true;
        }

        if ((t[0] == '+' || t[0] == '-') && int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int step) && Math.Abs(step) <= 36_500)
        {
            day = today.AddDays(step);
            return true;
        }

        return DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }
}
