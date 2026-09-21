using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonCompanion.UI;

/// <summary>
/// One pre-rendered line handed back as a renderable: what <see cref="InfoPane"/> shows for the lines
/// in its viewport after laying the tab out itself. An empty line renders one space, so a
/// <see cref="Rows"/> child keeps its row (Rows adds a line break only after a child that rendered
/// something). <see cref="ScreenPane"/> keeps its own verbatim writer: its cursor count must see
/// exactly what was rendered, a blank included.
/// </summary>
internal sealed class SegmentLines : IRenderable
{
    private static readonly Segment Blank = new(" ");

    private readonly IReadOnlyList<Segment> _segments;

    public SegmentLines(IReadOnlyList<Segment> segments)
    {
        _segments = segments ?? throw new ArgumentNullException(nameof(segments));
    }

    public Measurement Measure(RenderOptions options, int maxWidth) => new(0, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        IsBlank ? [Blank] : _segments;

    // Segment.SplitLines leaves an empty-text segment on a blank line, not an empty line.
    private bool IsBlank => _segments.All(s => s.Text.Length == 0);
}
