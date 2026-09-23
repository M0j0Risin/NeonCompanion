using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonSidekick.UI;

/// <summary>
/// Text that is written exactly as given: one styled segment per line, a line break between
/// lines, never measured against the console width and never wrapped.
///
/// <para>Spectre's <c>Text</c>/<c>Paragraph</c> fold every write at the console width counting
/// from column 0. That is right for a whole paragraph and wrong for a stream of tokens or for an
/// input line being redrawn mid-row, where the cursor is nowhere near column 0: a long token would
/// be folded twice (once by Spectre, once by the terminal) and a run of blanking spaces would grow
/// a newline. The terminal soft-wraps what does not fit; nothing here tries to help.</para>
/// </summary>
public sealed class RawText : Renderable
{
    private readonly string _text;
    private readonly Style _style;

    public RawText(string text, Style? style = null)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _style = style ?? Style.Plain;
    }

    protected override Measurement Measure(RenderOptions options, int maxWidth)
    {
        int widest = 0;
        foreach (var line in _text.Split('\n'))
        {
            widest = Math.Max(widest, TextCells.Width(line.TrimEnd('\r')));
        }

        int width = Math.Min(widest, maxWidth);
        return new Measurement(width, width);
    }

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var lines = _text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                yield return Segment.LineBreak;
            }

            string line = lines[i].TrimEnd('\r');
            if (line.Length > 0)
            {
                yield return new Segment(line, _style);
            }
        }
    }
}
