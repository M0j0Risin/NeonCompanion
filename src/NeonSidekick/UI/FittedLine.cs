using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonSidekick.UI;

/// <summary>
/// One line cut to the width it is rendered at — an ellipsis when cut (<see cref="ScreenPane.Fit"/>),
/// never wrapped: the <c>/skill</c> catalog rows, which stay a table however long a description
/// runs. Spectre's own <c>Overflow.Ellipsis</c> does not do this: a <c>Paragraph</c> splits the text
/// into words and cuts only a single word wider than the line, so a sentence still wraps. An empty
/// line renders one space, so a <see cref="Rows"/> child keeps its row (Rows adds a line break only
/// after a child that rendered something). The first <c>leadCells</c> cells may carry a style of
/// their own (the skill's name in the label colour, 2026-09-16); the cut applies to the whole line
/// first, so a lead wider than the room is cut like the rest.
/// </summary>
internal sealed class FittedLine : IRenderable
{
    private readonly string _text;
    private readonly Style _style;
    private readonly int _leadCells;
    private readonly Style? _leadStyle;

    public FittedLine(string text, Style style, int leadCells = 0, Style? leadStyle = null)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _style = style;
        _leadCells = Math.Max(0, leadCells);
        _leadStyle = leadStyle;
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        new(Math.Min(1, maxWidth), Math.Min(maxWidth, TextCells.Width(_text)));

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        if (_text.Length == 0)
        {
            return [new Segment(" ", _style)];
        }

        string fitted = ScreenPane.Fit(_text, maxWidth);
        if (_leadStyle is not { } leadStyle || _leadCells == 0)
        {
            return [new Segment(fitted, _style)];
        }

        // The lead is a prefix of the text's own characters: a name padded to its column, single cells throughout.
        int lead = Math.Min(_leadCells, fitted.Length);
        return lead == fitted.Length
            ? [new Segment(fitted, leadStyle)]
            : [new Segment(fitted[..lead], leadStyle), new Segment(fitted[lead..], _style)];
    }
}
