using System.Text;

namespace NeonCompanion.App;

/// <summary>
/// What <c>/copy</c> copies: every exchange of the session as the transcript showed it — the
/// user's line and the reply's streamed text after the think-tag filter, one entry per turn
/// (a tool-using turn is still one reply to the user). Not <see cref="Llm.ConversationHistory"/>,
/// which trims to <see cref="Llm.ConversationHistory.MaxTurns"/> turns and holds several
/// assistant messages for one such turn. Cleared with the conversation (<c>/clear</c>, a
/// profile switch). Pure; the screen owns one.
/// </summary>
public sealed class ChatLog
{
    /// <summary>One turn: the user's text and the reply, both trimmed.</summary>
    public readonly record struct Exchange(string User, string Reply);

    /// <summary>Between two copied exchanges: a markdown rule with a blank line either side. Pinned.</summary>
    public const string Separator = "\n\n---\n\n";

    private readonly List<Exchange> _exchanges = new();

    public int Count => _exchanges.Count;

    /// <summary>
    /// Records a turn. Both texts are trimmed (the renderer skips a reply's leading whitespace
    /// too); a turn whose reply is blank — an error, tool lines only — records nothing.
    /// </summary>
    public void Add(string user, string reply)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(reply);
        string trimmedReply = reply.Trim();
        if (trimmedReply.Length == 0)
        {
            return;
        }

        _exchanges.Add(new Exchange(user.Trim(), trimmedReply));
    }

    public void Clear() => _exchanges.Clear();

    /// <summary>How many exchanges a request for <paramref name="count"/> yields: at least one, at most everything, none when empty.</summary>
    public int Take(int count) => Count == 0 ? 0 : Math.Clamp(count, 1, Count);

    /// <summary>
    /// The last <paramref name="count"/> exchanges (clamped by <see cref="Take"/>) as markdown,
    /// oldest first, <see cref="Separator"/> between them: the reply alone, or with
    /// <paramref name="includeUser"/> the user's text as a blockquote, a blank line, then the
    /// reply unindented so its own markdown still renders. Empty when nothing is logged.
    /// </summary>
    public string Markdown(int count, bool includeUser)
    {
        int take = Take(count);
        var text = new StringBuilder();
        for (int i = _exchanges.Count - take; i < _exchanges.Count; i++)
        {
            if (text.Length > 0)
            {
                text.Append(Separator);
            }

            var exchange = _exchanges[i];
            if (includeUser)
            {
                text.Append(Quote(exchange.User)).Append("\n\n");
            }

            text.Append(exchange.Reply);
        }

        return text.ToString();
    }

    /// <summary>Every line of <paramref name="text"/> as a markdown blockquote line (an empty line is a bare <c>&gt;</c>). Pinned.</summary>
    public static string Quote(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var quoted = new StringBuilder();
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (quoted.Length > 0)
            {
                quoted.Append('\n');
            }

            quoted.Append(line.Length == 0 ? ">" : "> " + line);
        }

        return quoted.ToString();
    }
}
