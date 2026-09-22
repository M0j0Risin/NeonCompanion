using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonCompanion.UI;

/// <summary>One tab of the info pane: a title in the strip and the content it shows, built on every show (live state).</summary>
public sealed record InfoTab(string Title, Func<IRenderable> Content);

/// <summary>
/// The info pane: a box that rises from the bottom of the window over the input row — a tab strip,
/// the active tab's content, a hint row — and closes on ESC. The Claude Code <c>/help</c> shape,
/// kept out of the transcript. It is an overlay of the <see cref="ScreenPane"/>, which draws it and
/// keeps it in place under flow writes and a resize; this class owns the tabs, the viewport and the keys.
///
/// <para>Keys: ESC closes; → and Tab go to the next tab, ← and Shift+Tab to the previous one,
/// wrapping; ↑/↓ scroll the content by a line, PageUp/PageDown by a page, Home/End to either end;
/// every other key is swallowed, so nothing typed by accident lands on the input line.
/// The keys come through the <see cref="KeySource"/> like the input line's, never from the console
/// directly. Without the pane on the screen (no geometry) there is nothing to show and nothing is
/// read: the caller prints instead.</para>
///
/// <para>Mouse: the pane takes the console's mouse for the visit (the injected hook, the one the
/// menu pane uses) and hands it back
/// when it closes; a left click on a tab's title (<see cref="TabAt"/>) switches to that tab exactly
/// as the keys would; one on the × at the corner is ESC; two left clicks off the pane — the
/// transcript, a rule, the hint row — within <see cref="DoubleClick.Interval"/> close it too (later
/// on 2026-09-18, the menu's rule); a single click anywhere else, a right click and a drag do
/// nothing. The terminal's own selection needs Shift while the pane is up, as it does over a draft.</para>
///
/// <para>The viewport is the <see cref="MenuPane"/>'s: the content is laid out at the window's width
/// once per draw, and when it outgrows the rows the window leaves over one transcript row, the strip
/// and the spacer, one fewer row is shown and a dim <see cref="MenuPane.MoreHint"/> row takes the last
/// slot (<see cref="Viewport"/>, pure). Switching tabs starts at the top again.</para>
/// </summary>
public sealed class InfoPane
{
    /// <summary>The strip's label for <c>/help</c>.</summary>
    public const string Title = "Help";

    /// <summary>The hint row under the pane. Pinned.</summary>
    public const string HintText = "ESC closes · ←/→ tabs · ↑/↓ scroll";

    /// <summary>The rows above the content: the strip and the spacer.</summary>
    public const int HeaderRows = 2;

    /// <summary>Content lines one wheel notch scrolls (Windows' own lines-per-notch default); a notch lands anywhere, the pane is modal.</summary>
    public const int WheelLines = 3;

    private const int DefaultHeight = 24;

    private readonly ScreenPane _pane;
    private readonly KeySource _keys;
    private readonly Action<bool>? _mouse;
    private readonly DoubleClick _clicks;

    private int _first;
    private int _shown;
    private int _count;

    /// <param name="mouse">Takes (true) or hands back (false) the console's mouse; null when the screen has none to take.</param>
    public InfoPane(ScreenPane pane, KeySource keys, Action<bool>? mouse = null)
    {
        _pane = pane ?? throw new ArgumentNullException(nameof(pane));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _mouse = mouse;
        _clicks = new DoubleClick(pane.Time);
    }

    /// <summary>
    /// The strip: the label, then every title with a space either side (so the strip does not shift
    /// when the highlight moves), the active one highlighted, the others dim. Pinned.
    /// </summary>
    public static string TabStripMarkup(string label, IReadOnlyList<string> titles, int active)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(titles);
        var parts = new List<string>(titles.Count + 1) { $"[{Theme.Label.ToMarkup()}]{Markup.Escape(label)}[/]" };
        for (int i = 0; i < titles.Count; i++)
        {
            var style = i == active ? Theme.MenuHighlight : Theme.DimText;
            parts.Add($"[{style.ToMarkup()}] {Markup.Escape(titles[i])} [/]");
        }

        return string.Join("  ", parts);
    }

    /// <summary>
    /// Which tab a click at column <paramref name="x"/> of the strip lands on: the title's
    /// highlighted cells (the title and its one space either side), laid out as
    /// <see cref="TabStripMarkup"/> draws them from column 0; null on the label, a gap, past the
    /// end or before the start. Shared with the <see cref="MenuPane"/>. Pure.
    /// </summary>
    public static int? TabAt(string label, IReadOnlyList<string> titles, int x)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(titles);
        int start = TextCells.Width(label) + 2;
        for (int i = 0; i < titles.Count; i++)
        {
            int cells = TextCells.Width(titles[i]) + 2;
            if (x >= start && x < start + cells)
            {
                return i;
            }

            start += cells + 2;
        }

        return null;
    }

    /// <summary>
    /// The tab keys, shared with the <see cref="MenuPane"/>: +1 for → or Tab, -1 for ← or
    /// Shift+Tab, null for any other key. Pure.
    /// </summary>
    public static int? TabStep(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.RightArrow || (key.Key == ConsoleKey.Tab && (key.Modifiers & ConsoleModifiers.Shift) == 0))
        {
            return 1;
        }

        return key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.Tab ? -1 : null;
    }

    /// <summary>
    /// Which content lines to show: all of them when <paramref name="count"/> fits <paramref name="capacity"/>;
    /// otherwise one fewer than the capacity (the more row takes the last slot), starting at
    /// <paramref name="first"/> clamped so the last page is full. Pure.
    /// </summary>
    public static (int First, int Shown) Viewport(int count, int capacity, int first)
    {
        if (count <= 0 || capacity <= 0)
        {
            return (0, 0);
        }

        if (count <= capacity)
        {
            return (0, count);
        }

        int shown = Math.Max(1, capacity - 1);
        return (Math.Clamp(first, 0, count - shown), shown);
    }

    /// <summary>
    /// Shows the pane under <paramref name="label"/> on <paramref name="tabs"/>[<paramref name="initial"/>]
    /// and reads keys until ESC, the token, or the end of input; the pane is closed on every path.
    /// </summary>
    public async Task ShowAsync(string label, IReadOnlyList<InfoTab> tabs, int initial, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(tabs);
        if (tabs.Count == 0)
        {
            throw new ArgumentException("At least one tab.", nameof(tabs));
        }

        if (!_pane.Enabled)
        {
            return;
        }

        int active = Math.Clamp(initial, 0, tabs.Count - 1);
        _first = 0;
        _clicks.Reset();
        _mouse?.Invoke(true);
        try
        {
            Show(label, tabs, active);
            while (true)
            {
                var input = await _keys.ReadInputAsync(cancellationToken).ConfigureAwait(false);
                if (input is null)
                {
                    return;
                }

                int next = active;
                int first = _first;
                if (input is InputEvent.Click click)
                {
                    // A left click on the × at the corner is ESC (2026-09-18), one on a tab's title
                    // that tab, a second off the pane within the interval the close too; the label,
                    // a gap, the content, a right click and a drag are nothing.
                    if (click.Button == MouseButton.Left && _pane.TryHitClose(click.X, click.Y))
                    {
                        return;
                    }

                    if (click.Button == MouseButton.Left && _pane.TryHitOutside(click.X, click.Y))
                    {
                        if (_clicks.Second(MenuPane.OutsideRow))
                        {
                            return;
                        }

                        continue;
                    }

                    _clicks.Reset();
                    if (click.Button == MouseButton.Left && _pane.TryHitOverlay(click.X, click.Y, out int at) && at == 0
                        && TabAt(label, Titles(tabs), click.X) is int hit)
                    {
                        next = hit;
                    }
                }
                else if (input is InputEvent.Drag)
                {
                    // A jiggle between the two presses of a double-click keeps the pair.
                    continue;
                }
                else if (input is InputEvent.Wheel wheel)
                {
                    // A notch away from the user scrolls up, like ↑ three times; clamped like the keys.
                    _clicks.Reset();
                    first = Math.Clamp(_first - wheel.Notches * WheelLines, 0, Math.Max(0, _count - _shown));
                }
                else if (input is not InputEvent.Key { Info: var k })
                {
                    _clicks.Reset();
                    continue;
                }
                else if (Keys.IsCancel(k) || Keys.IsInterrupt(k))
                {
                    // ESC, and Ctrl+C the same (2026-09-17): the pane backs out.
                    return;
                }
                else if (TabStep(k) is int step)
                {
                    _clicks.Reset();
                    next = (active + step + tabs.Count) % tabs.Count;
                }
                else
                {
                    _clicks.Reset();
                    first = k.Key switch
                    {
                        ConsoleKey.DownArrow => _first + 1,
                        ConsoleKey.UpArrow => _first - 1,
                        ConsoleKey.PageDown => _first + Math.Max(1, _shown),
                        ConsoleKey.PageUp => _first - Math.Max(1, _shown),
                        ConsoleKey.End => int.MaxValue,
                        ConsoleKey.Home => 0,
                        _ => _first,
                    };
                    first = Math.Clamp(first, 0, Math.Max(0, _count - _shown));
                }

                if (next != active)
                {
                    active = next;
                    _first = 0;
                }
                else if (first != _first)
                {
                    _first = first;
                }
                else
                {
                    continue;
                }

                Show(label, tabs, active);
            }
        }
        catch (InvalidOperationException)
        {
            // No keyboard: the read ran dry, the pane closes.
        }
        finally
        {
            _pane.CloseOverlay();
            _mouse?.Invoke(false);
        }
    }

    private static string[] Titles(IReadOnlyList<InfoTab> tabs)
    {
        var titles = new string[tabs.Count];
        for (int i = 0; i < tabs.Count; i++)
        {
            titles[i] = tabs[i].Title;
        }

        return titles;
    }

    private void Show(string label, IReadOnlyList<InfoTab> tabs, int active)
    {
        var titles = Titles(tabs);
        int width = Math.Max(1, _pane.Profile.Width);
        int height = _pane.Profile.Height > 0 ? _pane.LayoutHeight : DefaultHeight;   // less the toolbar's row (2026-09-21)
        int capacity = ScreenPane.MaxOverlayRows(height, 0) - HeaderRows;
        var content = ScreenPane.RenderLines(tabs[active].Content(), _pane, width);
        _count = content.Count;
        (_first, _shown) = Viewport(_count, capacity, _first);

        // The spacer is a space, not an empty Text: Rows adds a line break only after a child that
        // rendered something, so an empty one would collapse the blank line.
        var lines = new List<IRenderable>(HeaderRows + _shown + 1) { new Markup(TabStripMarkup(label, titles, active)), new Text(" ") };
        for (int i = 0; i < _shown; i++)
        {
            lines.Add(new SegmentLines(content[_first + i]));
        }

        if (_shown < _count)
        {
            lines.Add(new Markup(Theme.DimMarkup(MenuPane.MoreHint)));
        }

        _pane.ShowOverlay(new Rows(lines), HintText, close: true);
    }
}
