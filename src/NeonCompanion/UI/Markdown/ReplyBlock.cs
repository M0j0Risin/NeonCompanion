using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonCompanion.UI.Markdown;

/// <summary>
/// The reply in the pane's live slot: the text so far as a <see cref="MarkdownView"/>, the
/// assistant glyph ahead of its first line and <see cref="ContinuationIndent"/> under it when the
/// block opens the reply (<paramref name="glyph"/>); flush-left when it continues one after a tool
/// line, the plain path's shape. Empty text with the glyph is the bare glyph line. The parse
/// happens once, on the first render — the pane lays the block out on its tick, not per token.
/// </summary>
public sealed class ReplyBlock : IRenderable
{
    /// <summary>Under the glyph: a bullet or a code label at column 0 would read as a second glyph column.</summary>
    public const string ContinuationIndent = "  ";

    private readonly IMarkdownParser _parser;
    private IRenderable? _content;

    public ReplyBlock(string text, bool glyph, IMarkdownParser? parser = null)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Glyph = glyph;
        _parser = parser ?? MarkdownView.DefaultParser;
    }

    public string Text { get; }

    public bool Glyph { get; }

    public Measurement Measure(RenderOptions options, int maxWidth) => Content.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => Content.Render(options, maxWidth);

    private IRenderable Content => _content ??= Build();

    private IRenderable Build()
    {
        var view = new MarkdownView(_parser.Parse(Text));
        return Glyph ? new HangingIndent(TranscriptRenderer.AssistantGlyph, ContinuationIndent, Theme.Accent, view) : view;
    }
}
