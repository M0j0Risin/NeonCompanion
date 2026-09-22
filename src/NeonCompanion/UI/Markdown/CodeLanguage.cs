using System.Collections.Frozen;

namespace NeonCompanion.UI.Markdown;

/// <summary>The scanner a <see cref="CodeLanguage"/> runs through.</summary>
public enum CodeLexMode
{
    /// <summary>The general scanner: comments, strings, numbers, identifiers, punctuation, driven by the language's switches.</summary>
    Code,
    /// <summary>The general scanner with CSS's shapes: selectors outside braces, <c>property:</c> keys and <c>#hex</c> colours inside.</summary>
    Css,
    /// <summary>XML/HTML: tags, attributes, comments, entities.</summary>
    Markup,
    /// <summary>A unified diff, classified line by line from its first character.</summary>
    Diff,
}

/// <summary>
/// One fenced-code language as data for <see cref="CodeLexer"/>: which comment and string shapes it
/// has, its keyword and builtin sets, and a handful of switches for the shapes one family has and
/// the others don't. A hand-rolled table rather than TextMate grammars or ColorCode (the user's
/// call, 2026-09-22): no native Oniguruma, nothing AOT has to be talked into, and every rule is
/// pinned by a test. It colours, it does not parse — a near miss is a wrong colour, never an error.
/// </summary>
public sealed class CodeLanguage
{
    private readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _keywords;
    private readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _builtins;
    private readonly IReadOnlyList<string> _stringPrefixes = [];

    public CodeLanguage(string name, string keywords = "", string builtins = "", bool ignoreCase = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IgnoreCase = ignoreCase;
        var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _keywords = Words(keywords, comparer).GetAlternateLookup<ReadOnlySpan<char>>();
        _builtins = Words(builtins, comparer).GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>The canonical name (the table's key; aliases map onto it).</summary>
    public string Name { get; }

    /// <summary>Keywords and builtins match case-insensitively (SQL, PowerShell, YAML's <c>True</c>).</summary>
    public bool IgnoreCase { get; }

    public CodeLexMode Mode { get; init; } = CodeLexMode.Code;

    /// <summary>Prefixes that comment out the rest of the line (<c>//</c>, <c>#</c>, <c>--</c>).</summary>
    public IReadOnlyList<string> LineComments { get; init; } = [];

    /// <summary>Open/close pairs of a comment that may span lines (<c>/* */</c>, <c>&lt;# #&gt;</c>).</summary>
    public IReadOnlyList<(string Open, string Close)> BlockComments { get; init; } = [];

    /// <summary>A line comment only opens at a word boundary: shell's <c>${#arr}</c> and YAML's <c>a#b</c> are not comments.</summary>
    public bool CommentNeedsBoundary { get; init; }

    /// <summary>The characters that open a string.</summary>
    public string Quotes { get; init; } = "\"'";

    /// <summary>The quotes whose strings may run past a line end (JS/Go backticks, shell strings); the rest stop at it.</summary>
    public string MultilineQuotes { get; init; } = "";

    /// <summary><c>"""</c> (and <c>'''</c> where <c>'</c> is a quote) opens a string that runs to the matching triple.</summary>
    public bool TripleQuotes { get; init; }

    /// <summary>The escape inside a string; null when the language has none (SQL doubles its quotes instead).</summary>
    public char? Escape { get; init; } = '\\';

    /// <summary>A doubled quote inside a string is the quote itself (<c>'it''s'</c>).</summary>
    public bool DoubledQuoteEscapes { get; init; }

    /// <summary>
    /// Prefixes that make the quote after them a string opener (<c>f"</c>, <c>$@"</c>, <c>r"</c>),
    /// matched case-insensitively, longest first. One containing <c>@</c> is C#'s verbatim string:
    /// no escape, doubled quotes.
    /// </summary>
    public IReadOnlyList<string> StringPrefixes
    {
        get => _stringPrefixes;
        init => _stringPrefixes = [.. value.OrderByDescending(p => p.Length)];
    }

    /// <summary>The sigil of a variable (<c>$</c>): <c>$name</c>, <c>${…}</c>, <c>$env:PATH</c>, <c>$?</c>.</summary>
    public char? VariablePrefix { get; init; }

    /// <summary>The sigil of a decorator or annotation (<c>@</c>): <c>@Override</c>, <c>@app.route</c>.</summary>
    public char? AttributePrefix { get; init; }

    /// <summary>Rust's <c>#[derive(…)]</c> and <c>#![…]</c>.</summary>
    public bool HashAttributes { get; init; }

    /// <summary>A <c>#word</c> opening a line is a directive (<c>#include</c>, <c>#region</c>).</summary>
    public bool Preprocessor { get; init; }

    /// <summary>A <c>-flag</c> or <c>--flag</c> after whitespace is an attribute (shell, PowerShell's <c>-Path</c> and <c>-eq</c>).</summary>
    public bool DashFlags { get; init; }

    /// <summary>A dash between letters stays in the name, and a dashed name is a command (PowerShell's <c>Get-ChildItem</c>).</summary>
    public bool DashedNames { get; init; }

    /// <summary>An identifier starting with a capital (and not all capitals) is a type (C#, Java, TS, Rust…).</summary>
    public bool CapitalizedTypes { get; init; }

    /// <summary>A name followed by <c>!</c> is a macro call (<c>println!</c>).</summary>
    public bool Macros { get; init; }

    /// <summary><c>'a</c> not closed by a quote is a lifetime, not an unterminated char literal.</summary>
    public bool Lifetimes { get; init; }

    /// <summary>A string followed by <c>:</c> is a key (JSON, YAML).</summary>
    public bool StringKeys { get; init; }

    /// <summary>The separator of a bare <c>key: value</c> (YAML) or <c>key = value</c> (INI/TOML) at the head of a line.</summary>
    public char? LineKeySeparator { get; init; }

    /// <summary>A line opening with <c>[</c> is a section header (INI, TOML).</summary>
    public bool Sections { get; init; }

    public bool IsKeyword(ReadOnlySpan<char> word) => _keywords.Contains(word);

    public bool IsBuiltin(ReadOnlySpan<char> word) => _builtins.Contains(word);

    private static FrozenSet<string> Words(string words, StringComparer comparer) =>
        words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(comparer);
}
