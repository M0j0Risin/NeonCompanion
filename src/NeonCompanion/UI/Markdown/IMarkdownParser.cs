namespace NeonCompanion.UI.Markdown;

/// <summary>
/// Reads a reply's text into a <see cref="MarkdownDocument"/>. The text is a document in progress
/// while the reply streams, so a parser never throws: an unclosed emphasis is literal text, an
/// unclosed fence runs to the end and closes on the next render that has its closing line.
/// </summary>
public interface IMarkdownParser
{
    MarkdownDocument Parse(string text);
}
