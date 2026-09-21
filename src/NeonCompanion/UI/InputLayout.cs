namespace NeonCompanion.UI;

/// <summary>
/// The input area's rows: <paramref name="Rows"/> is the draft word-wrapped to the row budget,
/// <paramref name="Starts"/> each row's first UTF-16 index in the draft (a row's end is the next
/// start, less one when a breaking space was dropped between them), <paramref name="CursorRow"/> /
/// <paramref name="CursorCol"/> where the cursor sits in them (the column in cells, after the glyph
/// or the indent). Pure; the pane applies the height cap and the vertical viewport on top.
/// </summary>
public sealed record InputLayout(IReadOnlyList<string> Rows, IReadOnlyList<int> Starts, int CursorRow, int CursorCol)
{
    /// <summary>
    /// Word-wraps <paramref name="text"/> into rows of at most <paramref name="availableCells"/>
    /// cells: a row breaks at its last space (the breaking space is dropped from the row; the next
    /// row starts right after it), a word longer than a row breaks by cells, never inside a wide
    /// character or a surrogate pair; an element wider than an empty row is placed anyway. The
    /// cursor (a UTF-16 index, clamped) lands on the last row that starts at or before it, so a
    /// cursor right after a breaking space is at the next row's first cell and a cursor on the space
    /// itself at its row's end. A <c>'\n'</c> (a pasted line break) is a hard break: the row ends
    /// before it and the next starts after it, the same as a dropped breaking space, so two of them
    /// make an empty row. Empty text is one empty row.
    /// </summary>
    public static InputLayout Wrap(string text, int cursor, int availableCells)
    {
        ArgumentNullException.ThrowIfNull(text);
        cursor = Math.Clamp(cursor, 0, text.Length);
        availableCells = Math.Max(1, availableCells);

        var rows = new List<string>();
        var starts = new List<int> { 0 };
        int rowStart = 0;
        int cells = 0;
        int lastSpace = -1;
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\n')
            {
                rows.Add(text[rowStart..i]);
                rowStart = i + 1;
                starts.Add(rowStart);
                cells = 0;
                lastSpace = -1;
                i = rowStart;
                continue;
            }

            int w = TextCells.ElementWidth(text, i, out int length);
            length = Math.Max(1, length);
            if (cells > 0 && cells + w > availableCells)
            {
                if (text[i] == ' ' || lastSpace >= 0)
                {
                    int breakAt = text[i] == ' ' ? i : lastSpace;
                    rows.Add(text[rowStart..breakAt]);
                    rowStart = breakAt + 1;
                }
                else
                {
                    rows.Add(text[rowStart..i]);
                    rowStart = i;
                }

                starts.Add(rowStart);
                cells = 0;
                lastSpace = -1;
                i = rowStart;
                continue;
            }

            cells += w;
            if (text[i] == ' ')
            {
                lastSpace = i;
            }

            i += length;
        }

        rows.Add(text[rowStart..]);

        int row = starts.Count - 1;
        while (row > 0 && starts[row] > cursor)
        {
            row--;
        }

        return new InputLayout(rows, starts, row, TextCells.Width(text[starts[row]..cursor]));
    }
}
