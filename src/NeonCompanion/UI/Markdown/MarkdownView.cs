using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonCompanion.UI.Markdown;

/// <summary>
/// A <see cref="MarkdownDocument"/> as Spectre renderables: paragraphs and headings as styled
/// <see cref="Paragraph"/>s, lists and quotes as <see cref="HangingIndent"/>s, code blocks as an
/// indented block under a dim language label, a rule, a table. The markers are consumed —
/// <c>**bold**</c> is bold text, <c>- item</c> is <c>• item</c> — and every colour is the theme's.
/// Model text never goes through <see cref="Markup"/>: a <c>[</c> in a reply is a <c>[</c>.
///
/// <para>Pure and lazy: the tree is composed on the first render and the class holds no console,
/// because the pane renders the reply block on its tick thread under its own lock.</para>
/// </summary>
public sealed class MarkdownView : IRenderable
{
    /// <summary>The parser the transcript reads replies with.</summary>
    public static readonly IMarkdownParser DefaultParser = new MarkdigParser();

    public const string BulletGlyph = "• ";
    public const string QuoteGlyph = "▎ ";
    public const string CodeIndent = "  ";
    /// <summary>The label above a fenced block with no language.</summary>
    public const string CodeLabel = "code";

    private static readonly IRenderable Spacer = new Text(" ");

    private readonly MarkdownDocument _document;
    private IRenderable? _content;

    public MarkdownView(MarkdownDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>The view of <paramref name="text"/> read by <see cref="DefaultParser"/>.</summary>
    public static MarkdownView Of(string text, IMarkdownParser? parser = null) =>
        new((parser ?? DefaultParser).Parse(text));

    public Measurement Measure(RenderOptions options, int maxWidth) => Content.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => Content.Render(options, maxWidth);

    private IRenderable Content => _content ??= Compose(_document.Blocks, quote: null, tight: false);

    /// <summary>The blocks stacked, a blank row between them unless <paramref name="tight"/> (a list item's).</summary>
    private static IRenderable Compose(IReadOnlyList<MarkdownBlock> blocks, Style? quote, bool tight)
    {
        var children = new List<IRenderable>(blocks.Count * 2);
        foreach (var block in blocks)
        {
            if (children.Count > 0 && !tight)
            {
                children.Add(Spacer);
            }

            children.Add(Block(block, quote));
        }

        return new Rows(children);
    }

    private static IRenderable Block(MarkdownBlock block, Style? quote) => block switch
    {
        ParagraphBlock paragraph => Paragraph(paragraph.Inlines, quote),
        HeadingBlock heading => Paragraph(heading.Inlines, heading.Level == 1 ? Theme.MarkdownHeading1 : Theme.MarkdownHeading, flat: true),
        CodeBlock code => Code(code),
        ListBlock list => List(list, quote),
        QuoteBlock inner => new HangingIndent(QuoteGlyph, QuoteGlyph, Theme.MarkdownQuoteBar, Compose(inner.Blocks, Theme.MarkdownQuote, tight: false)),
        RuleBlock => new Rule().RuleStyle(Theme.MarkdownRule),
        TableBlock table => Table(table, quote),
        _ => Spacer,
    };

    private static IRenderable List(ListBlock list, Style? quote)
    {
        var items = new List<IRenderable>(list.Items.Count);
        for (int i = 0; i < list.Items.Count; i++)
        {
            string marker = list.Ordered
                ? (list.Start + i).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". "
                : BulletGlyph;
            var content = Compose(list.Items[i].Blocks, quote, tight: true);
            items.Add(new HangingIndent(marker, new string(' ', TextCells.Width(marker)), Theme.MarkdownBullet, content));
        }

        return new Rows(items);
    }

    private static IRenderable Code(CodeBlock code)
    {
        var lines = new List<IRenderable>(code.Lines.Count);
        foreach (string line in code.Lines)
        {
            // A blank line must keep its row (Rows skips a child that renders nothing); a tab is four cells.
            string shown = line.Length == 0 ? " " : line.Replace("\t", "    ", StringComparison.Ordinal);
            lines.Add(new Text(shown, Theme.MarkdownCodeBlock));
        }

        var block = new HangingIndent(CodeIndent, CodeIndent, Theme.MarkdownCodeBlock, new Rows(lines));
        return new Rows(new Text(code.Language ?? CodeLabel, Theme.MarkdownCodeLabel), block);
    }

    private static IRenderable Table(TableBlock table, Style? quote)
    {
        var spectre = Theme.MarkdownTable();
        int columns = table.Header.Cells.Count;
        for (int c = 0; c < columns; c++)
        {
            var column = new TableColumn(Paragraph(table.Header.Cells[c].Inlines, Theme.TableHeader, flat: true));
            if (c < table.Alignments.Count)
            {
                column.Alignment = table.Alignments[c] switch
                {
                    TableAlignment.Left => Justify.Left,
                    TableAlignment.Center => Justify.Center,
                    TableAlignment.Right => Justify.Right,
                    _ => null,
                };
            }

            spectre.AddColumn(column);
        }

        foreach (var row in table.Rows)
        {
            var cells = new IRenderable[columns];
            for (int c = 0; c < columns; c++)
            {
                cells[c] = c < row.Cells.Count ? Paragraph(row.Cells[c].Inlines, quote) : new Text("");
            }

            spectre.AddRow(cells);
        }

        return spectre;
    }

    /// <summary>
    /// The inlines as one wrapped paragraph. <paramref name="baseStyle"/> is the quote's or the
    /// heading's colour; <paramref name="flat"/> keeps it for every run (a heading is one voice).
    /// </summary>
    private static Paragraph Paragraph(IReadOnlyList<MarkdownInline> inlines, Style? baseStyle, bool flat = false)
    {
        var paragraph = new Paragraph();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun run:
                    paragraph.Append(run.Text, flat ? baseStyle : RunStyle(run, baseStyle));
                    break;
                case LinkRun link:
                    paragraph.Append(link.Text.Length > 0 ? link.Text : link.Url, baseStyle ?? Theme.Assistant);
                    if (link.Url.Length > 0 && !string.Equals(link.Url, link.Text, StringComparison.Ordinal))
                    {
                        paragraph.Append(" (" + link.Url + ")", Theme.MarkdownLinkUrl);
                    }

                    break;
                case HardBreak:
                    paragraph.Append("\n", baseStyle);
                    break;
            }
        }

        return paragraph;
    }

    /// <summary>A run's style: code wins, then the emphasis over the base colour (the quote's) or the body's.</summary>
    public static Style RunStyle(TextRun run, Style? baseStyle = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Code)
        {
            return Theme.MarkdownCode;
        }

        var decoration = (run.Bold ? Decoration.Bold : Decoration.None) | (run.Italic ? Decoration.Italic : Decoration.None);
        if (baseStyle is not null)
        {
            return decoration == Decoration.None ? baseStyle.Value : baseStyle.Value.Combine(new Style(decoration: decoration));
        }

        return (run.Bold, run.Italic) switch
        {
            (true, true) => Theme.MarkdownBold.Combine(Theme.MarkdownItalic),
            (true, false) => Theme.MarkdownBold,
            (false, true) => Theme.MarkdownItalic,
            _ => Theme.Assistant,
        };
    }
}
