using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonCompanion.UI.Markdown;

/// <summary>
/// The reply in the pane's live slot: the text so far as a <see cref="MarkdownView"/>, the
/// assistant glyph ahead of its first line and <see cref="ContinuationIndent"/> under it when the
/// block opens the reply (<paramref name="glyph"/>); flush-left when it continues one after a tool
/// line, the plain path's shape. Empty text with the glyph is the bare glyph line. The parse
/// happens once, on the first render — the pane lays the block out on its tick, not per token.
/// <paramref name="codeKeep"/> is the <c>Code collapse count</c> the reply opened with (2026-09-22):
/// a top-level code block of more lines folds once the pane commits it (<see cref="CodeSpans"/>).
/// </summary>
public sealed class ReplyBlock : IRenderable
{
    /// <summary>Under the glyph: a bullet or a code label at column 0 would read as a second glyph column.</summary>
    public const string ContinuationIndent = "  ";

    private readonly IMarkdownParser _parser;
    private MarkdownView? _view;
    private IRenderable? _content;

    public ReplyBlock(string text, bool glyph, IMarkdownParser? parser = null, int codeKeep = 0)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Glyph = glyph;
        CodeKeep = Math.Max(0, codeKeep);
        _parser = parser ?? MarkdownView.DefaultParser;
    }

    public string Text { get; }

    public bool Glyph { get; }

    /// <summary>How many source lines a top-level code block may have before it folds in the transcript; 0 = never.</summary>
    public int CodeKeep { get; }

    public Measurement Measure(RenderOptions options, int maxWidth) => Content.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => Content.Render(options, maxWidth);

    /// <summary>
    /// The top-level code blocks' rows in this block rendered at <paramref name="maxWidth"/>
    /// (<see cref="MarkdownView.CodeSpans"/>): the glyph's indent takes columns, never rows.
    /// </summary>
    public IReadOnlyList<CodeSpan> CodeSpans(RenderOptions options, int maxWidth)
    {
        _ = Content;
        return _view!.CodeSpans(options, Glyph ? Math.Max(1, maxWidth - TextCells.Width(TranscriptRenderer.AssistantGlyph)) : maxWidth);
    }

    private IRenderable Content => _content ??= Build();

    private IRenderable Build()
    {
        _view = new MarkdownView(_parser.Parse(Text));
        return Glyph ? new HangingIndent(TranscriptRenderer.AssistantGlyph, ContinuationIndent, Theme.Accent, _view) : _view;
    }
}
