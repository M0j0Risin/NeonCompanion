using System.Globalization;
using NeonCompanion.UI;
using Spectre.Console;

namespace NeonCompanion.App;

/// <summary>
/// The <c>/queue</c> screen (2026-09-18): one row per message waiting in the <see cref="MessageQueue"/>
/// (its number, then its label — a paste as <c>[Pasted text +N lines]</c>), Enter or a double-click removes the highlighted one and shows the
/// list again, the <see cref="ClearAllButton"/> on the title row (a click, or <see cref="ClearAllKey"/>;
/// 2026-09-21, the user's ask, the folder pane's collapse-all shape) drops every one and closes
/// with the transcript's dropped notice — what <c>/queue clear</c> prints —, ESC / Ctrl+C / the <c>×</c> back out. A list in the bottom pane (<see cref="MenuPane"/>,
/// the removal notice on its status line above the re-shown rows), the <see cref="MemoryMenu"/>
/// shape — with no prompt fallback: a message is queued only from the pane's mid-turn line hook, so
/// a console without the pane never holds one and <c>/queue</c> there prints <see cref="EmptyNotice"/>.
/// Opens mid-turn on the watcher task (a <c>Pane</c>-class command) and at the idle line over a held
/// queue; the rows are a snapshot and the removal checks the text, so a row the loop sent meanwhile
/// is not taken for another.
/// </summary>
internal sealed class QueueMenu
{
    // The label and the key hints: the pane shows the label as its title and the keys in its hint row. Pinned.
    public const string Title = "⏳ Queue";
    public const string Keys = "Enter = remove · c = clear all · ESC = back";
    public const string EmptyNotice = "(nothing queued)";

    /// <summary>The one button on the title row (2026-09-21), drawn as a dim tab: every queued message dropped.</summary>
    public const string ClearAllButton = "⊠ clear all";

    /// <summary>The key that is the button.</summary>
    public const char ClearAllKey = 'c';

    /// <summary>The page's buttons: the one.</summary>
    public static readonly IReadOnlyList<MenuButton> Buttons = [new(ClearAllButton, ClearAllKey)];

    private readonly MessageQueue _queue;
    private readonly INoticeSink _transcript;
    private readonly MenuPane _pane;

    /// <param name="transcript">Where the lines outside the pane go: the screen's deferring sink, since the list may open while a reply runs.</param>
    /// <param name="pane">The menu host in the bottom pane.</param>
    public QueueMenu(MessageQueue queue, INoticeSink transcript, MenuPane pane)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _transcript = transcript ?? throw new ArgumentNullException(nameof(transcript));
        _pane = pane ?? throw new ArgumentNullException(nameof(pane));
    }

    /// <summary>Where a notice goes: the pane's status line while the list is open there, else the transcript.</summary>
    private INoticeSink Sink => _pane.IsOpen ? _pane : _transcript;

    // ── Pinned statics ──────────────────────────────────────────────────────

    public static string RemovedNotice(string text) => $"(removed: {text})";

    /// <summary>One menu row as markup: the position (1-based) dimmed, two spaces, the text escaped; the pane cuts it at the edge.</summary>
    public static string RowMarkup(int index, string text) =>
        Theme.DimMarkup((index + 1).ToString(CultureInfo.InvariantCulture)) + "  " + Markup.Escape(text);

    // ── Screen ──────────────────────────────────────────────────────────────

    public async Task ShowAsync(CancellationToken cancellationToken)
    {
        var entries = _queue.Snapshot();
        if (entries.Count == 0 || !_pane.Enabled)
        {
            _transcript.Notice(EmptyNotice);
            return;
        }

        // What ends the visit is said after the pane has closed, so it lands in the transcript
        // rather than on a status line the close forgets.
        string? closingNotice = null;
        int cursor = 0;
        try
        {
            while (true)
            {
                var page = new MenuPage(Title, entries.Select((entry, i) => RowMarkup(i, entry.Label)).ToList(), Keys) { Buttons = Buttons };
                var picked = await _pane.PickAsync(page, cursor, cancellationToken).ConfigureAwait(false);
                if (picked is not { Row: var row })
                {
                    return;
                }

                if (picked.Value.Button >= 0)
                {
                    // The button: every message goes, the loop's own drop notice says how many
                    // (nothing queued by now — the loop took the last — is the empty notice).
                    int dropped = _queue.Clear();
                    closingNotice = dropped > 0 ? ChatScreen.QueueDroppedNotice(dropped) : EmptyNotice;
                    return;
                }

                string label = entries[row].Label;
                if (_queue.Remove(row, label))
                {
                    Sink.Notice(RemovedNotice(label));
                }

                entries = _queue.Snapshot();
                if (entries.Count == 0)
                {
                    closingNotice = EmptyNotice;
                    return;
                }

                cursor = Math.Min(row, entries.Count - 1);
            }
        }
        finally
        {
            _pane.Close();
            if (closingNotice is not null)
            {
                _transcript.Notice(closingNotice);
            }
        }
    }
}
