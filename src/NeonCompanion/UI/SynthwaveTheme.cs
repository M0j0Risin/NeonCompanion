using System.Globalization;
using System.Text;
using Spectre.Console;

namespace NeonCompanion.UI;

/// <summary>
/// The synthwave visual identity: a neon palette (hot magenta, cyan, violet, sunset amber), a
/// gradient title and styled panels shared by every screen. This is the <em>only</em> file that names a colour; every other
/// file composes markup through the helpers here so the whole look can be tuned in one place.
/// </summary>
public static class Theme
{
    // ── Palette ─────────────────────────────────────────────────────────────
    /// <summary>Hot magenta — the primary synthwave accent.</summary>
    public static readonly Color Magenta = new(0xFF, 0x2E, 0x97);
    /// <summary>Neon cyan — the cool counter-accent.</summary>
    public static readonly Color Cyan = new(0x33, 0xE0, 0xFF);
    /// <summary>Violet — mid-tone of the sunset gradient.</summary>
    public static readonly Color Purple = new(0xB1, 0x5B, 0xFF);
    /// <summary>Deep violet — receding / structural colour.</summary>
    public static readonly Color DeepPurple = new(0x7B, 0x2F, 0xF7);
    /// <summary>Sunset amber — warm gold highlight.</summary>
    public static readonly Color Amber = new(0xFF, 0xC8, 0x32);
    /// <summary>Orange — between amber and magenta in the sunset.</summary>
    public static readonly Color Orange = new(0xFF, 0x8A, 0x3D);
    /// <summary>Sunset red-pink — lower disc of the sun.</summary>
    public static readonly Color SunsetRed = new(0xF4, 0x5B, 0x9B);

    /// <summary>Near-white lavender — body text (readable on the dark bg).</summary>
    public static readonly Color Ink = new(0xEF, 0xE6, 0xFF);
    /// <summary>Muted lavender — secondary / dim text.</summary>
    public static readonly Color Dim = new(0x9A, 0x8B, 0xB8);
    /// <summary>Muted lavender a step darker than <see cref="Dim"/> — the ghost text on the empty input row, quieter than the hint row.</summary>
    public static readonly Color Dimmer = new(0x44, 0x3C, 0x56);

    /// <summary>Deep space background (page + panel fill).</summary>
    public static readonly Color Bg = new(0x0B, 0x04, 0x16);
    /// <summary>Panel fill, a touch lifted from the page background.</summary>
    public static readonly Color PanelBg = new(0x16, 0x0A, 0x28);

    /// <summary>Neon green — success, enabled, connected.</summary>
    public static readonly Color Good = new(0x3D, 0xF2, 0x7A);
    /// <summary>Hot red — failure, error.</summary>
    public static readonly Color Bad = new(0xFF, 0x4D, 0x6D);
    /// <summary>Amber — a warning (reuses the sunset gold).</summary>
    public static readonly Color Warn = Amber;

    /// <summary>The sunset gradient, cyan → violet → magenta → orange → amber.</summary>
    public static readonly Color[] GradientStops = { Cyan, Purple, Magenta, Orange, Amber };

    // ── Styles ──────────────────────────────────────────────────────────────
    public static readonly Style Body = new(foreground: Ink);
    public static readonly Style DimText = new(foreground: Dim);
    public static readonly Style Accent = new(foreground: Magenta, decoration: Decoration.Bold);
    public static readonly Style AccentCyan = new(foreground: Cyan, decoration: Decoration.Bold);
    public static readonly Style AccentPurple = new(foreground: Purple, decoration: Decoration.Bold);
    public static readonly Style Label = new(foreground: Dim, decoration: Decoration.Bold);

    /// <summary>The user's own lines in the transcript.</summary>
    public static readonly Style User = AccentCyan;
    /// <summary>The assistant's streamed reply text.</summary>
    public static readonly Style Assistant = Body;
    /// <summary>Notices, hints, status — anything neither party said.</summary>
    public static readonly Style SystemText = DimText;
    /// <summary>An error line.</summary>
    public static readonly Style ErrorText = new(foreground: Bad, decoration: Decoration.Bold);
    /// <summary>A success line (connected, enabled, passed).</summary>
    public static readonly Style GoodText = new(foreground: Good, decoration: Decoration.Bold);
    /// <summary>A warning line.</summary>
    public static readonly Style WarnText = new(foreground: Warn, decoration: Decoration.Bold);
    /// <summary>A section heading over cyan row labels in an info pane (<c>/usage</c>'s Context / Last reply, <c>/sys</c>'s Persona / Clock (3) …): violet bold, so the heading and its rows read as two tiers — the user's call, 2026-09-16.</summary>
    public static readonly Style SectionHeading = AccentPurple;

    /// <summary>Sunset border for panels.</summary>
    public static readonly Style BorderStyle = new(foreground: Magenta);
    /// <summary>Table header text.</summary>
    public static readonly Style TableHeader = new(foreground: Cyan, decoration: Decoration.Bold);

    /// <summary>Braille spinner frames, 80 ms cadence.</summary>
    public static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠸", "⠴", "⠧", "⠇", "⠏" };

    /// <summary>The <c>Status</c> spinner while the model thinks or a server is probed.</summary>
    public static readonly Style SpinnerStyle = AccentCyan;
    /// <summary>The rule above the input row.</summary>
    public static readonly Style PaneRule = new(foreground: DeepPurple);
    /// <summary>The hint row under the input row.</summary>
    public static readonly Style Hint = DimText;
    /// <summary>The mark after the trailer on the hint row (the reasoning glyph beside the model): violet, plain — the user's call, 2026-09-15.</summary>
    public static readonly Style TrailerMark = new(foreground: Purple);
    /// <summary>The highlighted row of a selection menu.</summary>
    public static readonly Style MenuHighlight = new(foreground: Ink, background: PanelBg);
    /// <summary>The dim part of a highlighted row (the note beside a command or skill name on the input line's list, 2026-09-16).</summary>
    public static readonly Style MenuHighlightDim = new(foreground: Dim, background: PanelBg);
    /// <summary>A disabled menu row.</summary>
    public static readonly Style MenuDisabled = new(foreground: Dim, background: Bg);
    /// <summary>The selected stretch of the input row (a drag or Shift+arrows): the user's colour inverted.</summary>
    public static readonly Style SelectedText = new(foreground: Bg, background: Cyan);
    /// <summary>A pasted block's placeholder on the input row (<c>[Pasted text #1 +49 lines]</c>): violet, so it reads as a thing and not as typed text.</summary>
    public static readonly Style PasteLabel = new(foreground: Purple, decoration: Decoration.Bold);
    /// <summary>The ghost text on the empty input row (<c>Type a message or /help for more info</c>): a step dimmer than the hint row (the user's call, 2026-09-14), never the user's colour.</summary>
    public static readonly Style Placeholder = new(foreground: Dimmer);

    // ── The styled transcript (UI/Markdown, `Transcript markdown`) ──────────
    /// <summary>Bold text in a reply (<c>**bold**</c>): the body colour, bold — emphasis, not a colour change.</summary>
    public static readonly Style MarkdownBold = new(foreground: Ink, decoration: Decoration.Bold);
    /// <summary>Italic text in a reply (<c>*italic*</c>).</summary>
    public static readonly Style MarkdownItalic = new(foreground: Ink, decoration: Decoration.Italic);
    /// <summary>Inline code (<c>`code`</c>): cyan, so a name stands out of the prose.</summary>
    public static readonly Style MarkdownCode = new(foreground: Cyan);
    /// <summary>The lines of a fenced code block: body text on the lifted panel fill.</summary>
    public static readonly Style MarkdownCodeBlock = new(foreground: Ink, background: PanelBg);
    /// <summary>The language label above a fenced code block.</summary>
    public static readonly Style MarkdownCodeLabel = DimText;
    /// <summary>A first-level heading.</summary>
    public static readonly Style MarkdownHeading1 = Accent;
    /// <summary>Every other heading level.</summary>
    public static readonly Style MarkdownHeading = AccentCyan;
    /// <summary>The bullet or number ahead of a list item.</summary>
    public static readonly Style MarkdownBullet = new(foreground: Purple);
    /// <summary>The gutter bar of a blockquote.</summary>
    public static readonly Style MarkdownQuoteBar = new(foreground: Purple);
    /// <summary>The text of a blockquote.</summary>
    public static readonly Style MarkdownQuote = DimText;
    /// <summary>The URL shown after a link's text.</summary>
    public static readonly Style MarkdownLinkUrl = DimText;
    /// <summary>A thematic break (<c>---</c>) in a reply.</summary>
    public static readonly Style MarkdownRule = PaneRule;

    /// <summary>A table in a reply: the pane's rule colour, fitted to its content — never expanded to the window.</summary>
    public static Table MarkdownTable()
    {
        return new Table
        {
            Border = TableBorder.Rounded,
            BorderStyle = PaneRule,
            Expand = false,
        };
    }

    /// <summary>Menus show at most this many rows before paging.</summary>
    public const int MenuPageSize = 15;

    // ── Markup helpers ──────────────────────────────────────────────────────
    /// <summary>Renders a string as a per-character sunset gradient. Used for the title.</summary>
    public static string GradientMarkup(string text)
    {
        var sb = new StringBuilder();
        int n = Math.Max(text.Length, 1);
        int idx = 0;
        foreach (var ch in text)
        {
            Color c = SampleGradient((double)idx / Math.Max(1, n - 1), GradientStops);
            sb.Append('[').Append(ToHex(c)).Append(']');
            sb.Append(Markup.Escape(ch.ToString()));
            sb.Append("[/]");
            idx++;
        }

        return sb.ToString();
    }

    /// <summary>A short neon label (magenta, bold). The text is escaped.</summary>
    public static string AccentMarkup(string text) =>
        string.Concat("[", ToHex(Magenta), " bold]", Markup.Escape(text), "[/]");

    /// <summary>Dim secondary text. The text is escaped.</summary>
    public static string DimMarkup(string text) =>
        string.Concat("[", ToHex(Dim), "]", Markup.Escape(text), "[/]");

    /// <summary>Text in an arbitrary palette colour. The text is escaped.</summary>
    public static string ColorMarkup(Color color, string text) =>
        string.Concat("[", ToHex(color), "]", Markup.Escape(text), "[/]");

    /// <summary>
    /// A horizontal gradient rule of <paramref name="width"/> cells, split into five sunset
    /// segments. Used as the divider under the header.
    /// </summary>
    public static string Rule(int width, char glyph = '─')
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        const int segments = 5;
        int per = Math.Max(1, width / segments);
        for (int s = 0; s < segments; s++)
        {
            int count = (s == segments - 1) ? width - s * per : per;
            if (count <= 0)
            {
                continue;
            }

            Color c = SampleGradient(s / (double)(segments - 1), GradientStops);
            sb.Append('[').Append(ToHex(c)).Append(']');
            sb.Append(new string(glyph, count));
            sb.Append("[/]");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The retro "sunset" logo: a small disc of full blocks with horizontal slits in its lower
    /// half, tinted amber → orange → magenta → violet from the top down. Multi-line markup.
    /// </summary>
    public static string Sun()
    {
        var rows = new[]
        {
            "..█▄▄▄█..",
            ".███████.",
            "█████████",
            "█████████",
            "█████████",
            "█.████.█",
            "█████████",
            "█..██..█",
            "█...█...█",
        };

        var colors = new[]
        {
            Amber, Amber, Orange, Orange, SunsetRed, SunsetRed, Magenta, Magenta, DeepPurple,
        };

        var sb = new StringBuilder();
        for (int r = 0; r < rows.Length; r++)
        {
            string row = rows[r];
            sb.Append('[').Append(ToHex(colors[r])).Append(']');
            foreach (char ch in row)
            {
                sb.Append(ch == '.' ? ' ' : ch);
            }

            sb.Append("[/]\n");
        }

        sb.Length--; // drop the trailing newline
        return sb.ToString();
    }

    // ── Widget factories ────────────────────────────────────────────────────
    /// <summary>
    /// Applies the synthwave look to a selection menu: highlight, disabled style, page size and
    /// wrap-around. Spectre's default highlight is plain blue. Search stays off, so J/K move and
    /// a stray key is not treated as a filter.
    /// </summary>
    public static SelectionPrompt<T> Selection<T>(SelectionPrompt<T> prompt) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(prompt);
        prompt.HighlightStyle = MenuHighlight;
        prompt.DisabledStyle = MenuDisabled;
        prompt.PageSize = MenuPageSize;
        prompt.WrapAround = true;
        return prompt;
    }

    /// <summary>A table styled with the sunset rule and cool header.</summary>
    public static Table SunsetTable(string? title = null, Style? borderStyle = null)
    {
        var t = new Table
        {
            Border = TableBorder.Rounded,
            BorderStyle = borderStyle ?? BorderStyle,
            Expand = true,
        };
        if (!string.IsNullOrEmpty(title))
        {
            t.Title = new TableTitle(title, AccentCyan);
        }

        return t;
    }

    /// <summary>A <see cref="TableColumn"/> with the cool header style.</summary>
    public static TableColumn HeaderColumn(string name, Justify? alignment = null)
    {
        var c = new TableColumn(new Markup(Markup.Escape(name), TableHeader));
        if (alignment is { } a)
        {
            c.Alignment = a;
        }

        return c;
    }

    // ── Internals ───────────────────────────────────────────────────────────
    /// <summary>Linearly samples a gradient stop array at t ∈ [0,1].</summary>
    internal static Color SampleGradient(double t, Color[] stops)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double scaled = t * (stops.Length - 1);
        int idx = (int)Math.Floor(scaled);
        if (idx >= stops.Length - 1)
        {
            return stops[^1];
        }

        double frac = scaled - idx;
        return Lerp(stops[idx], stops[idx + 1], frac);
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        int r = (int)Math.Round(a.R + (b.R - a.R) * t);
        int g = (int)Math.Round(a.G + (b.G - a.G) * t);
        int bl = (int)Math.Round(a.B + (b.B - a.B) * t);
        return new Color((byte)r, (byte)g, (byte)bl);
    }

    /// <summary>Hex markup colour (e.g. <c>#FF2E97</c>) for a Spectre <see cref="Color"/>.</summary>
    public static string ToHex(Color c) => string.Concat("#", ToHex2(c.R), ToHex2(c.G), ToHex2(c.B));

    private static string ToHex2(byte v) => v.ToString("X2", CultureInfo.InvariantCulture);
}
