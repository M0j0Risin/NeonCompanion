using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonCompanion.UI;

/// <summary>
/// The transcript the pane wrote, kept by the app: the alternate screen buffer the screen runs in
/// has no scrollback, so <see cref="ScreenPane"/> retains every flow write here and paints a window
/// of it while the user scrolls (PgUp/PgDn). The source is the <em>logical lines</em> — the segments
/// between line breaks, styles kept, an open last line while no break has ended it; the rows are
/// those lines wrapped at one width with the terminal's own rule, the one <c>ScreenPane.Track</c>
/// counts by (<see cref="TextCells"/> widths, a row left only when the next character does not
/// fit), so the count and the store agree cell for cell. Not Spectre's <c>SplitLines</c>: it measures
/// with its own cell table, keeps a wide character on the row the terminal wraps it from, and
/// drops a blank line inside one text segment. The rows are cached at the last width asked for,
/// extended on every append (the open line wrapped again with what joins it), rebuilt on a width
/// change; past <see cref="MaxRows"/> the oldest lines go. No lock of its own: the pane calls it
/// under its own.
/// </summary>
public sealed class Scrollback
{
    /// <summary>The most rows kept; whole lines are dropped from the front past it (a constant, not a setting).</summary>
    public const int MaxRows = 10_000;

    private readonly List<Line> _lines = new();
    private readonly List<SegmentLine> _rows = new();
    private int _width = -1;

    /// <summary>One logical line: its segments (no line break, no control code, no <c>\r</c>) and the rows it took at the cached width.</summary>
    private sealed class Line
    {
        public readonly List<Segment> Segments = new();
        public bool Closed;
        public int Rows;
    }

    /// <summary>The rows at the cached width (0 before the first <see cref="Rows"/> or <see cref="Append"/>).</summary>
    public int Count => _rows.Count;

    /// <summary>The logical lines kept (tests).</summary>
    public int LineCount => _lines.Count;

    /// <summary>The last line has no line break after it yet: the flow cursor sits at its end, not on the row under it.</summary>
    public bool LastLineOpen => _lines.Count > 0 && !_lines[^1].Closed;

    /// <summary>The rows at <paramref name="width"/>, laid out afresh when the width changed.</summary>
    public IReadOnlyList<SegmentLine> Rows(int width)
    {
        Layout(width);
        return _rows;
    }

    /// <summary>
    /// <paramref name="segments"/> — one flow write as Spectre emitted it — appended: control codes
    /// and <c>\r</c> skipped, a line break (a break segment, or a <c>\n</c> inside a text) closing the
    /// line, the text after it opening the next; the rows at <paramref name="width"/> extended.
    /// Returns the rows dropped from the front to stay under <see cref="MaxRows"/>, so a caller
    /// anchored on a row can move with it.
    /// </summary>
    public int Append(IReadOnlyList<Segment> segments, int width)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Layout(width);

        // The open line is wrapped again with what joins it: its rows leave the cache first.
        var touched = new List<Line>();
        Line? current = null;
        if (LastLineOpen)
        {
            current = _lines[^1];
            _rows.RemoveRange(_rows.Count - current.Rows, current.Rows);
            touched.Add(current);
        }

        foreach (var segment in segments)
        {
            if (segment.IsControlCode)
            {
                continue;
            }

            if (segment.IsLineBreak)
            {
                Close(ref current, touched);
                continue;
            }

            string text = segment.Text;
            int from = 0;
            while (from <= text.Length)
            {
                int end = text.IndexOf('\n', from);
                int stop = end < 0 ? text.Length : end;
                if (stop > from)
                {
                    string piece = text[from..stop];
                    if (piece.Contains('\r'))
                    {
                        piece = piece.Replace("\r", "", StringComparison.Ordinal);
                    }

                    if (piece.Length > 0)
                    {
                        current ??= Open(touched);
                        current.Segments.Add(new Segment(piece, segment.Style));
                    }
                }

                if (end < 0)
                {
                    break;
                }

                Close(ref current, touched);
                from = end + 1;
            }
        }

        foreach (var line in touched)
        {
            line.Rows = Wrap(line, width, _rows);
        }

        return Trim();
    }

    /// <summary>Everything forgotten (the screen was cleared).</summary>
    public void Clear()
    {
        _lines.Clear();
        _rows.Clear();
    }

    private Line Open(List<Line> touched)
    {
        var line = new Line();
        _lines.Add(line);
        touched.Add(line);
        return line;
    }

    /// <summary>A line break: the current line closed (an empty one opened and closed when none was open — a blank row).</summary>
    private void Close(ref Line? current, List<Line> touched)
    {
        current ??= Open(touched);
        current.Closed = true;
        current = null;
    }

    private void Layout(int width)
    {
        width = Math.Max(1, width);
        if (width == _width)
        {
            return;
        }

        _width = width;
        _rows.Clear();
        foreach (var line in _lines)
        {
            line.Rows = Wrap(line, width, _rows);
        }

        Trim();
    }

    /// <summary>Whole lines off the front while the rows exceed <see cref="MaxRows"/> (the last line always stays); the rows dropped.</summary>
    private int Trim()
    {
        int dropped = 0;
        while (_rows.Count > MaxRows && _lines.Count > 1)
        {
            var first = _lines[0];
            _rows.RemoveRange(0, first.Rows);
            _lines.RemoveAt(0);
            dropped += first.Rows;
        }

        return dropped;
    }

    /// <summary>
    /// <paramref name="line"/> wrapped at <paramref name="width"/> onto <paramref name="rows"/> with the
    /// terminal's rule: cells accumulate on a row, a row is left when the next character does not
    /// fit (or the row is full and another comes), each row's runs keep their styles; an empty
    /// line is one empty row. Returns the rows added.
    /// </summary>
    private static int Wrap(Line line, int width, List<SegmentLine> rows)
    {
        int added = 1;
        var row = new SegmentLine();
        int col = 0;
        bool full = false;
        var run = new StringBuilder();
        foreach (var segment in line.Segments)
        {
            string text = segment.Text;
            int i = 0;
            while (i < text.Length)
            {
                int cells = TextCells.ElementWidth(text, i, out int length);
                length = Math.Max(1, length);
                if (full || col + cells > width)
                {
                    if (run.Length > 0)
                    {
                        row.Add(new Segment(run.ToString(), segment.Style));
                        run.Clear();
                    }

                    rows.Add(row);
                    added++;
                    row = new SegmentLine();
                    col = 0;
                }

                run.Append(text, i, length);
                col += cells;
                full = col >= width;
                i += length;
            }

            if (run.Length > 0)
            {
                row.Add(new Segment(run.ToString(), segment.Style));
                run.Clear();
            }
        }

        rows.Add(row);
        return added;
    }
}
