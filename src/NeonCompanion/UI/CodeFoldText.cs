using System.Globalization;

namespace NeonCompanion.UI;

/// <summary>
/// The wording of a folded code block's label line (2026-09-22, the user's ask: the tool runs' fold
/// for code): <c>▸ 📜 csharp · 57 lines</c> — the scroll (<see cref="Markdown.MarkdownView.CodeGlyph"/>)
/// after the triangle as the tools' glyph is in theirs, later that day — the triangle down (<see cref="ToolGroupText.ExpandedGlyph"/>)
/// while the block shows every line. It stands where the plain label stood, in its colour. Pinned.
/// </summary>
public static class CodeFoldText
{
    /// <summary>The label line of a block of <paramref name="lines"/> lines labelled <paramref name="label"/>, folded or not.</summary>
    public static string Summary(string label, int lines, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(label);
        return (expanded ? ToolGroupText.ExpandedGlyph : ToolGroupText.CollapsedGlyph) + " " + Markdown.MarkdownView.CodeGlyph + label + " · "
            + lines.ToString(CultureInfo.InvariantCulture) + (lines == 1 ? " line" : " lines");
    }
}
