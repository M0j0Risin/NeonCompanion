using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonSidekick.UI;

/// <summary>
/// One markup row cut to the width it is rendered at — an ellipsis when cut, never wrapped: the
/// <see cref="MenuPane"/>'s rows since 2026-09-18 (a skill's description, a memory's text, a queued
/// line, a root's path), so a list stays one row per line and the viewport's count holds.
/// <see cref="FittedLine"/> does this for plain text; this is its markup twin, since Spectre's own
/// <c>Overflow.Ellipsis</c> cuts only a single word wider than the line and still wraps a sentence.
/// The markup is laid out at an unbounded width (one line), then cut to one cell short of the room
/// — whole segments while they fit, the first that does not cut inside — and the ellipsis appended
/// in the cut segment's style. An empty
/// row renders one space, so a <see cref="Rows"/> child keeps its row.
/// </summary>
internal sealed class FittedMarkup : IRenderable
{
    private const string Ellipsis = "…";

    private readonly Markup _markup;

    public FittedMarkup(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        _markup = new Markup(markup.Length == 0 ? " " : markup);
    }

    public Measurement Measure(RenderOptions options, int maxWidth) => new(Math.Min(1, maxWidth), Math.Min(maxWidth, _markup.Length));

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var segments = ((IRenderable)_markup).Render(options, int.MaxValue).Where(s => !s.IsLineBreak).ToList();
        if (maxWidth <= 0 || Segment.CellCount(segments) <= maxWidth)
        {
            return segments;
        }

        // Whole segments while they fit, the first that does not cut inside (Segment.Truncate over a
        // list drops whole segments, which would lose a long path after its short label).
        int room = Math.Max(0, maxWidth - 1);
        var cut = new List<Segment>(segments.Count + 1);
        var style = segments[^1].Style;
        foreach (var segment in segments)
        {
            int cells = segment.CellCount();
            if (cells <= room)
            {
                cut.Add(segment);
                room -= cells;
                continue;
            }

            style = segment.Style;
            if (room > 0 && Segment.Truncate(segment, room) is { } head)
            {
                cut.Add(head);
            }

            break;
        }

        cut.Add(new Segment(Ellipsis, style));
        return cut;
    }
}
