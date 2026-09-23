using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonSidekick.UI.Markdown;

/// <summary>
/// A renderable laid out beside a prefix: <paramref name="first"/> ahead of its first line,
/// <paramref name="rest"/> ahead of every other — the assistant glyph and the indent under it, a
/// list marker and its hanging indent, a blockquote's gutter bar on every line, a code block's
/// indent. The inner renderable is laid out in what the prefix leaves of the width, so nothing
/// here can wrap wider than the window. Nothing inside renders no lines, and then only a
/// non-empty <paramref name="first"/> is shown (the bare glyph of an empty reply).
/// </summary>
internal sealed class HangingIndent : IRenderable
{
    private readonly string _first;
    private readonly string _rest;
    private readonly Style _style;
    private readonly IRenderable _inner;
    private readonly int _width;

    public HangingIndent(string first, string rest, Style style, IRenderable inner)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
        _style = style;
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _width = Math.Max(TextCells.Width(first), TextCells.Width(rest));
    }

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        var inner = _inner.Measure(options, Math.Max(1, maxWidth - _width));
        return new Measurement(Math.Min(inner.Min + _width, maxWidth), Math.Min(inner.Max + _width, maxWidth));
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var lines = Segment.SplitLines(_inner.Render(options, Math.Max(1, maxWidth - _width)));
        if (lines.Count == 0)
        {
            if (_first.Length > 0)
            {
                yield return new Segment(_first, _style);
            }

            yield break;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                yield return Segment.LineBreak;
            }

            string prefix = i == 0 ? _first : _rest;
            if (prefix.Length > 0)
            {
                yield return new Segment(prefix, _style);
            }

            foreach (var segment in lines[i])
            {
                yield return segment;
            }
        }
    }
}
