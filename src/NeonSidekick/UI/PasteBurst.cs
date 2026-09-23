using System.Text;

namespace NeonSidekick.UI;

/// <summary>
/// What a burst of key records is: the terminal's paste, or keys. Windows Terminal pastes by
/// injecting the clipboard into the console input buffer as ordinary key records — one per
/// character, <c>VK_RETURN</c> with <c>'\r'</c> for each line break — so nothing about a single
/// record says "pasted". What does is their arrival: a paste lands as one burst with more records
/// still queued behind each one, while a keystroke is always alone. The reader thread
/// (<see cref="WindowsConsoleInput"/>) gathers the records the buffer already holds into a run and
/// <see cref="Coalesce"/> decides, pure and pinned. Bracketed paste is not an option: VT input is
/// off (see <see cref="WindowsConsoleInput"/>).
///
/// <para>A <em>text key</em> is a printable character, Enter (a line break, whatever character it
/// carries: <c>'\r'</c> live, <c>'\0'</c> from a test) or Tab, each a single press; anything else —
/// arrows, function keys, ESC, a Ctrl chord, a record with a repeat count (a held key) — is a
/// <em>bare key</em>. A maximal run of two or more text keys is one <see cref="InputEvent.Paste"/>;
/// a run of one is the key it is; bare keys are keys, in order. So two characters typed within
/// the same scheduling slice read as a paste of two characters, which inserts exactly what two
/// keys would; the one case that differs, a lone pasted line break (indistinguishable from
/// Enter), is a documented limit.</para>
///
/// <para>ConPTY hands a long paste over in chunks a few milliseconds apart; a chunk that happens
/// to be exactly one <c>'\r'</c> would read as Enter. So a run that begins within
/// <see cref="Grace"/> of the previous paste (<paramref name="joinsPrevious"/>) is a paste
/// whatever its length, and the consumer sees the chunks as consecutive pastes, which insert
/// as one block.</para>
/// </summary>
public static class PasteBurst
{
    /// <summary>How long after a paste's last record the next run still belongs to it.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The events a run of translated key records (each with its repeat count) becomes; see the
    /// type. <paramref name="joinsPrevious"/>: the run started within <see cref="Grace"/> of a
    /// paste, so its first run of text keys is one too. <paramref name="endsAsPaste"/> is true when
    /// the last event out is a paste (the grace window starts). Pinned.
    /// </summary>
    public static IReadOnlyList<InputEvent> Coalesce(IReadOnlyList<(ConsoleKeyInfo Key, int Repeat)> run, bool joinsPrevious, out bool endsAsPaste)
    {
        ArgumentNullException.ThrowIfNull(run);
        var events = new List<InputEvent>();
        var text = new StringBuilder();
        int textKeys = 0;
        ConsoleKeyInfo lastTextKey = default;
        bool first = true;
        bool ended = false;

        foreach (var (key, repeat) in run)
        {
            if (repeat == 1 && TextOf(key) is char c)
            {
                text.Append(c);
                textKeys++;
                lastTextKey = key;
                continue;
            }

            Flush();
            for (int i = 0; i < Math.Max(1, repeat); i++)
            {
                events.Add(new InputEvent.Key(key));
            }

            ended = false;
        }

        Flush();
        endsAsPaste = ended;
        return events;

        void Flush()
        {
            if (textKeys == 0)
            {
                return;
            }

            if (textKeys >= 2 || (first && joinsPrevious))
            {
                events.Add(new InputEvent.Paste(text.ToString()));
                ended = true;
            }
            else
            {
                // One text key on its own is the key it was: the Enter, the letter.
                events.Add(new InputEvent.Key(lastTextKey));
                ended = false;
            }

            text.Clear();
            textKeys = 0;
            first = false;
        }
    }

    /// <summary>
    /// The character a key contributes to a paste: <c>'\n'</c> for Enter, <c>'\t'</c> for Tab, else
    /// the printable character it carries (the input line's own rule: a key event that carries a
    /// printable character IS that character, modifiers or not — AltGr letters arrive with Ctrl+Alt
    /// set); null for a bare key.
    /// </summary>
    public static char? TextOf(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            return '\n';
        }

        if (key.Key == ConsoleKey.Tab)
        {
            return '\t';
        }

        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
        {
            return key.KeyChar;
        }

        return null;
    }
}
