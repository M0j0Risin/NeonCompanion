namespace NeonCompanion.UI.Markdown;

/// <summary>
/// Where a top-level code block sits in a rendered reply (2026-09-22, the code fold): its label from
/// row <see cref="LabelRow"/> for <see cref="LabelRows"/> rows, then its <see cref="BodyRows"/> body
/// rows; <see cref="Label"/> is the label's text (the language, or <see cref="MarkdownView.CodeLabel"/>)
/// and <see cref="SourceLines"/> the block's own lines, which the fold counts (a wrapped line once).
/// </summary>
public sealed record CodeSpan(int LabelRow, int LabelRows, int BodyRows, string Label, int SourceLines)
{
    /// <summary>The first row after the block.</summary>
    public int End => LabelRow + LabelRows + BodyRows;
}
