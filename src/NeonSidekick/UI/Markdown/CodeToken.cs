namespace NeonSidekick.UI.Markdown;

/// <summary>
/// What a stretch of a fenced code block is, as far as <see cref="CodeLexer"/> can tell without
/// parsing: the colour classes the theme paints (<see cref="Theme.CodeStyle"/>), not a grammar.
/// </summary>
public enum CodeTokenKind
{
    /// <summary>Identifiers, whitespace, anything unclassified: the block's body colour.</summary>
    Plain,
    Keyword,
    /// <summary>A built-in or capitalised type name, a Rust lifetime, a shell builtin.</summary>
    Type,
    String,
    Number,
    Comment,
    /// <summary>Operators, brackets, separators.</summary>
    Punctuation,
    /// <summary>A name followed by <c>(</c>, a Rust macro, a PowerShell <c>Verb-Noun</c>.</summary>
    Function,
    /// <summary>A shell or PowerShell <c>$variable</c>.</summary>
    Variable,
    /// <summary>A JSON/YAML/INI key, a markup attribute, a decorator, a command-line flag.</summary>
    Attribute,
    /// <summary>A markup element name, a CSS selector.</summary>
    Tag,
    /// <summary>An INI/TOML <c>[section]</c>, a diff's file header or hunk line.</summary>
    Heading,
    /// <summary>A diff's added line.</summary>
    Inserted,
    /// <summary>A diff's removed line.</summary>
    Deleted,
}

/// <summary>One classified run of the lexed text, <paramref name="Length"/> characters from <paramref name="Start"/>.</summary>
public readonly record struct CodeToken(CodeTokenKind Kind, int Start, int Length)
{
    public int End => Start + Length;
}
