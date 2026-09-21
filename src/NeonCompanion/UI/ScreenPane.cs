using NeonCompanion.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NeonCompanion.UI;

/// <summary>
/// The screen's bottom rows: a rule, the input rows, a second rule and a hint line, kept on the last
/// rows of the window while the transcript flows above them — the Claude Code shape. An <see cref="IAnsiConsole"/>
/// the chat screen writes through: every write is a <em>flow</em> write (the transcript), and the
/// pane lifts itself out of the way first, writes, and redraws itself below.
///
/// <para>The screen runs in the terminal's <em>alternate buffer</em> (<see cref="Open"/> enters it,
/// <see cref="Close"/> leaves it: the shell's screen and scrollback come back untouched). Spectre
/// has no layout manager, no fixed region and no resize event, so the drawing is done the way Ink
/// does it: the transcript is ordinary scrolling output; the pane is drawn under it with
/// <em>padding rows</em> in between while the transcript is shorter than the window; before a flow
/// write the cursor moves back up over the pane and the padding and erases to the end of the
/// screen; after it the pane is drawn again. Once the transcript reaches the pane the padding is
/// zero and the buffer scrolls at its last row exactly as the main one did. Every compound step is
/// wrapped in synchronized output (DEC 2026, honoured by Windows Terminal, ignored elsewhere) with
/// the cursor hidden, so a redraw per streamed token does not flicker.</para>
///
/// <para>The alternate buffer keeps no scrollback, so the pane keeps its own: every flow write is
/// retained in a <see cref="Scrollback"/> (the one place is <c>Track</c>), and <see cref="ScrollPage"/>
/// (PgUp/PgDn on the input line, and the key watcher during a turn) paints a window of it in the
/// transcript region while the pane stays on the last rows — <see cref="Scrolled"/>, the rows-below
/// count in the hint (<see cref="ScrolledHint"/>), the anchor a store row so rows arriving below never
/// move what is read. Flow writes meanwhile go to the store alone. Paging back to the bottom,
/// Ctrl+End (<see cref="ScrollToEnd"/>, 2026-09-17), a resize (the buffer is not reflowed by the
/// terminal) and a sent line write the store's tail back as the flow and draw the pane as before.
/// The wheel scrolls it too, <see cref="WheelRows"/> a notch (<see cref="ScrollWheel"/>).</para>
///
/// <para>Where the transcript ends is <em>counted</em> from the segments Spectre emits (line breaks
/// and cell widths with the terminal's deferred-wrap rule: a row is left only when the next character
/// does not fit — the store wraps by the same rule) and <em>corrected</em> from <see cref="ScreenGeometry"/>
/// on every redraw at the bottom when the console can say where its cursor is. Relative cursor moves
/// only: padding rows are empty and no input row ever fills the width.</para>
///
/// <para>The input area holds the whole draft, word-wrapped (<see cref="InputLayout"/>) over as many
/// rows as it needs up to <see cref="MaxInputRows"/>, the pane growing upward (and beyond the cap a
/// vertical viewport keeps the cursor's row on the screen); a key that keeps the row count rewrites
/// the rows in place, one that changes it lifts and draws the pane again. The pane remembers the
/// draft and lays it out afresh on every draw, so a resize re-wraps it.</para>
///
/// <para>An <em>overlay</em> (<see cref="ShowOverlay"/>, the info pane, the menus) takes the input rows' place:
/// the rules stay above and below it (the upper one keeps its title), its own hint under the lower rule, the pane grows upward to
/// fit and the cursor is hidden until <see cref="CloseOverlay"/>. The same lift and draw, with more rows.
/// An overlay <em>with an input slot</em> (a typed settings edit) keeps the input rows under its
/// content, the cursor on them, so the input line works there unchanged; what it submits stays
/// out of the flow.</para>
///
/// <para>Disabled (no geometry, or a console without menus) the pane is a pass-through: every
/// call degenerates to the plain write the screen made before the pane existed, which is what
/// the tests over <c>TestConsole</c> see unless they build a geometry. One lock serialises the
/// screen thread (flow writes, the input line), the key watcher's preview and the pane's own
/// tick (spinner frames, a size change, a changed hint).</para>
/// </summary>
public sealed class ScreenPane : IAnsiConsole, IDisposable
{
    /// <summary>Rule (the session's name at its right edge, <see cref="RuleTitle"/>), one input row, rule, hint: the pane at its smallest (a longer draft or an overlay adds rows).</summary>
    public const int PaneRows = 4;
    public const char RuleGlyph = '─';

    /// <summary>
    /// The close glyph at the right edge of an overlay's first row (2026-09-18): the ESC key under
    /// the mouse — a menu backs out one level, an info pane closes. Drawn for an overlay shown with
    /// <c>close</c> while <see cref="CloseGlyphShown"/> says so (the screen ties it to <c>Mouse in menus</c>:
    /// a button nobody can press is not drawn), in column <c>Width − 2</c> — the last column left
    /// empty as the hint row leaves it. Pinned.
    /// </summary>
    public const string CloseGlyph = "×";
    public const string Category = "Screen";

    /// <summary>The tick that advances the spinner and polls the window size.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private static readonly ControlCode SyncBegin = new("\e[?2026h");
    private static readonly ControlCode SyncEnd = new("\e[?2026l");
    private static readonly ControlCode EraseDown = new("\e[J");
    private static readonly ControlCode EraseLineEnd = new("\e[K");
    // The alternate screen buffer (the shell's screen comes back on leaving it), with Windows
    // Terminal's alternate scroll mode off while it is up: on, a wheel notch there arrives as ↑/↓
    // key records — history on the line — and the wheel is the terminal's anyway.
    private static readonly ControlCode EnterAlternate = new("\e[?1049h\e[?1007l");
    private static readonly ControlCode LeaveAlternate = new("\e[?1007h\e[?1049l");

    private readonly IAnsiConsole _inner;
    private readonly ScreenGeometry? _geometry;
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly ITimer? _timer;
    private readonly Scrollback _store = new();

    // The flow cursor: where the transcript's next character goes (screen rows, 0 = top).
    private int _row;
    private int _col;
    private bool _lineFull;
    private bool _drawn;
    private int _pad;
    private int _lastWidth;
    private int _lastHeight;
    private bool _inAlternate;

    // The scroll: the store row at the top of the transcript region while the user has paged up,
    // −1 at the bottom (the live state, the flow on the screen). _drawnScrolled is the shape the
    // last Draw put on the screen (Lift steps up from it, like the drawn overlay). _blank: the
    // flow is not on the screen — a scrolled draw or a resize erased it — and the next draw at
    // the bottom writes the store's tail back first (RestoreFlow). _liveCount: the live block's
    // rows not yet committed, for the rows-below count while scrolled.
    private int _top = -1;
    private bool _drawnScrolled;
    private bool _blank = true;
    private int _liveCount;

    // The live slot: the reply in progress (a ReplyBlock), laid out afresh on every draw between the
    // flow and the padding, its rows lifted with the pane's; repainted on the tick when dirty, never
    // per token. A block taller than the screen leaves over the pane commits its top rows into the
    // flow (written and tracked like any flow write) and _liveCommitted skips that many lines of the
    // next layout — the content is the whole document every time, the pane shows the tail.
    private IRenderable? _live;
    private int _liveRows;
    private int _liveCommitted;
    private bool _liveDirty;
    private int _batch;
    private int _modal;
    private bool _disposed;

    // The input area (enabled): the draft, and the rows drawn from it by the last draw. The drawn
    // fields (_shownRows, _inputRows, _cursorRow, _cursorCell) say where the terminal's cursor is
    // and are set only by Draw and the in-place rewrite; a lift steps up from them.
    private string _text = "";
    private int _cursor;
    private int _anchor = -1;
    private IReadOnlyList<(int Start, int Length)> _labels = Array.Empty<(int, int)>();
    private int _firstRow;
    private List<string> _shownRows = new() { "" };
    private List<int> _shownStarts = new() { 0 };
    private List<int> _shownNext = new() { 0 };
    private int _inputRows = 1;
    private int _cursorRow;
    private int _cursorCell;
    // The cells the last draw put on the first row for the placeholder (0 = none drawn): the
    // in-place rewrite blanks them like a draft's, so the first key takes the ghost text away.
    private int _shownGhostCells;
    private string _placeholder = "";

    // The single row of the disabled pane (drawn where the cursor is): the cells it used.
    private int _renderedCells;

    // The upper rule's title (the session's name, 2026-09-18) and the one last drawn, for the tick.
    private Func<string> _ruleTitle = static () => "";
    private string _drawnRuleTitle = "";

    // The hint row.
    private Func<string> _hint = static () => "";
    private Func<string> _strip = static () => "";
    private Func<string> _trailer = static () => "";
    private Func<string> _trailerMark = static () => "";
    private string? _busyLabel;
    // The labels of the busy scopes still open, outermost first (a menu's spinner inside the turn's):
    // the last one is _busyLabel; a scope's end shows the one under it again. The count is the outermost's.
    private readonly List<string> _busyLabels = new();
    private long _busySince;
    private int _frame;
    private string _shownHint = "";
    private Func<string> _queued = () => "";
    private Func<string> _usage = () => "";

    // The overlay (the info pane, the menus): drawn where the input row is, the cursor hidden
    // meanwhile — or, with an input slot, above the input rows, the cursor on them.
    private Overlay? _overlay;
    private int _paneRows = PaneRows;
    private int _overlayRows;

    // The overlay the last draw put on the screen (Draw is the only writer): the cursor's depth
    // is a fact about what is drawn, and a shape change lifts from the OLD shape before it draws
    // the new one — ShowOverlay and CloseOverlay set _overlay before the lift [scar 2026-09-13].
    private bool _drawnOverlay;
    private bool _drawnInput;

    // Whether the drawn overlay was shown with close (a pane — never the line's completion list):
    // TryHitOutside answers only then, so a double-click on the transcript never closes the list.
    private bool _drawnClose;

    // The column the last draw put the overlay's close glyph in; −1 when none was drawn.
    private int _closeColumn = -1;

    // The standing hint row as last drawn (TryHitHint's zones): the strip at column 0 and the
    // column the trailer starts in, −1 without one or under the busy row.
    private string _hintStrip = "";
    private int _trailerColumn = -1;

    // The queued part (Queued) as last drawn, in either row: its first column and its width in
    // cells, −1 / 0 when none was drawn (nothing queued, cut by a narrow window, or under an
    // overlay's or the scroll's hint). TryHitQueued reads them; TryHitHint the standing row's alone.
    private int _queuedColumn = -1;
    private int _queuedCells;

    // The usage zone (HintZone.Usage, 2026-09-21) as last drawn, in either row: the token tally
    // (Usage) on the standing row, the spinner and its label on the busy row — its first column
    // and its width in cells, −1 / 0 when none was drawn (nothing counted, the timers or the exit
    // hint in the tally's place, cut by a narrow window, or under an overlay's or the scroll's hint).
    private int _usageColumn = -1;
    private int _usageCells;

    // Whether the hint row as last drawn (either row) carried the scroll's hint (ScrolledHint):
    // then every hit that is neither a strip glyph nor the trailer is Scrolled, not the row.
    private bool _hintScrolled;

    private sealed record Overlay(IRenderable Content, string Hint, bool Input, bool Close);

    /// <summary>The part of the standing hint row a click landed on (<see cref="TryHitHint(int, int, out HintHit)"/>).</summary>
    public enum HintZone
    {
        /// <summary>Anywhere that is neither a strip glyph nor the trailer: the hint's own text, a separator, a blank.</summary>
        Row,

        /// <summary>One of the speech strip's glyphs at the row's start (<see cref="HintHit.Glyph"/> says which).</summary>
        Strip,

        /// <summary>The model name and its reasoning mark at the right edge.</summary>
        Trailer,

        /// <summary>The queued-messages count after the strip (<see cref="Queued"/>, 2026-09-18).</summary>
        Queued,

        /// <summary>
        /// The scroll's hint (<see cref="ScrolledHint"/>, later on 2026-09-18): what <see cref="Row"/>
        /// is while the transcript is scrolled — the text, a separator, a blank, the whole busy row —
        /// so a double-click there is the bottom again, as Ctrl+End is.
        /// </summary>
        Scrolled,

        /// <summary>
        /// The token tally on the standing row (<see cref="Usage"/>), or the spinner and its label
        /// on the busy row (2026-09-21, the user's ask): a double-click on either opens <c>/usage</c>.
        /// Last so <c>InputLine.HintPairKey</c>'s values stand.
        /// </summary>
        Usage,
    }

    /// <summary>Where on the hint row a click landed: the zone, the strip glyph under it (<c>""</c> elsewhere) and the zone's first column (−1 for the row).</summary>
    public readonly record struct HintHit(HintZone Zone, string Glyph, int Column);

    /// <param name="inner">The console the pane draws on.</param>
    /// <param name="geometry">Where the cursor is; null disables the pane (a plain transcript).</param>
    /// <param name="time">The clock for the tick; tests pass a manual one.</param>
    public ScreenPane(IAnsiConsole inner, ScreenGeometry? geometry, TimeProvider time)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _geometry = geometry;
        Enabled = geometry is not null && inner.Profile.Capabilities.Interactive && inner.Profile.Capabilities.Ansi;
        if (Enabled)
        {
            // Before Open the flow is whatever the console holds, from its cursor on.
            _row = Math.Clamp(geometry!.CursorRow() ?? 0, 0, Height - 1);
            _blank = false;
            _lastWidth = Width;
            _lastHeight = Height;
            _timer = time.CreateTimer(_ => OnTick(), null, Tick, Tick);
        }
    }

    /// <summary>The pane is on the screen: the geometry is known and the console draws menus.</summary>
    public bool Enabled { get; }

    /// <summary>Whether an overlay's <see cref="CloseGlyph"/> is drawn, read at every draw; the screen answers with <c>Mouse in menus</c>.</summary>
    public Func<bool> CloseGlyphShown { get; set; } = () => true;

    /// <summary>
    /// The close-everything signal (2026-09-18): set by <see cref="Dismiss"/> while an overlay is
    /// drawn — a double-click off the pane, read by the pane readers — and cleared by
    /// <see cref="CloseOverlay"/> (and <see cref="Close"/>). While it stands
    /// <see cref="MenuPane.PickAsync"/> returns null at entry, so every nested host on the way up
    /// backs out without a draw until the top one's close clears it; the × glyph stays one level.
    /// </summary>
    public bool Dismissed { get; private set; }

    /// <summary>Raises <see cref="Dismissed"/>; nothing without an overlay to dismiss.</summary>
    public void Dismiss()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_overlay is not null)
            {
                Dismissed = true;
            }
        }
    }

    /// <summary>The clock the tick and the busy row run on; the overlays share it (a menu's double-click).</summary>
    public TimeProvider Time => _time;

    /// <summary>The standing hint (the state line under the input row), read on every redraw and on the tick.</summary>
    public Func<string> Hint
    {
        get => _hint;
        set => _hint = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The status strip at column 0 of the hint row in every state — the standing hint, an overlay's
    /// hint, the spinner — with <see cref="HintSeparator"/> after it, read like <see cref="Hint"/>;
    /// empty = nothing drawn. The screen puts the speech glyphs there: status, not a hint, so a
    /// reply's spinner or a menu never takes it away (the user's call, 2026-09-15). The profile's
    /// name sat ahead of it until 2026-09-15; the window title names it now.
    /// </summary>
    public Func<string> Strip
    {
        get => _strip;
        set => _strip = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The title at the right edge of the upper rule — the one above the input row — in every
    /// state (2026-09-18, the user's ask), read like <see cref="Strip"/> on every draw and on the
    /// tick, which repaints the pane when it changes; empty = the bare rule. The screen puts the
    /// session's name there: <see cref="RuleWithTitle"/> is the row.
    /// </summary>
    public Func<string> RuleTitle
    {
        get => _ruleTitle;
        set => _ruleTitle = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The label at the right edge of the hint row in every state, read like <see cref="Strip"/>;
    /// empty = the row as it is. The screen puts the model's name there.
    /// </summary>
    public Func<string> Trailer
    {
        get => _trailer;
        set => _trailer = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The queued-messages part after the row's lead in both states (2026-09-18): behind the strip
    /// on the standing row, behind the spinner's label on the busy row, <see cref="HintSeparator"/>
    /// between, read like <see cref="Strip"/>; empty = nothing drawn. Hidden under an overlay's
    /// hint and the scroll's, like the screen's own hint; ahead of that hint so a long usage part
    /// is what a narrow row cuts. The screen puts <c>📨 2 queued</c> there; the pane records where
    /// (<see cref="TryHitQueued"/>, <see cref="HintZone.Queued"/>) for the double-click that opens <c>/queue</c>.
    /// </summary>
    public Func<string> Queued
    {
        get => _queued;
        set => _queued = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The token tally as it stands in the screen's hint (2026-09-21): not drawn by the pane —
    /// <see cref="Hint"/> carries it — but looked for in the drawn standing row so its cells are
    /// <see cref="HintZone.Usage"/> for the double-click that opens <c>/usage</c>; empty, or absent
    /// from the row (the timers or the exit hint in its place), = no zone. Read like <see cref="Strip"/>.
    /// </summary>
    public Func<string> Usage
    {
        get => _usage;
        set => _usage = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// A short glyph after the trailer, <see cref="MarkSeparator"/> between, drawn in
    /// <see cref="Theme.TrailerMark"/> and never cut — the trailer's text is cut ahead of it; read
    /// like <see cref="Trailer"/>; empty = none. The screen puts the reasoning glyph there.
    /// </summary>
    public Func<string> TrailerMark
    {
        get => _trailerMark;
        set => _trailerMark = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The ghost text on the empty input row: drawn dim after the glyph while the draft is empty,
    /// the pane idle (no spinner) and no overlay open — never in an overlay's input slot — and
    /// gone with the first key; empty = none (the default). Cut to the row (<see cref="PlaceholderRow"/>).
    /// The screen puts <c>Type a message or /help for more info</c> there. Disabled: nothing.
    /// </summary>
    public string Placeholder
    {
        get => _placeholder;
        set => _placeholder = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Whether the draft the pane draws is empty (2026-09-20): the text of the last
    /// <see cref="ShowInput"/>, emptied by a commit or a clear. What the screen's splash hint reads
    /// from its <see cref="Hint"/> func — under the pane's lock on the tick, which is reentrant —
    /// so the row says <c>← → slideshow</c> only while an arrow would walk the pictures.
    /// A disabled pane has no draft of its own: true.
    /// </summary>
    public bool DraftEmpty
    {
        get { lock (_gate) { return _text.Length == 0; } }
    }

    /// <summary>Between the strip and the rest of the hint row (and the spinner's text and an overlay's hint, <see cref="BusyRow"/>).</summary>
    public const string HintSeparator = " · ";

    /// <summary>The least blanks between the hint row's text and the trailer.</summary>
    public const int TrailerGap = 2;

    /// <summary>Between the trailer's text and its mark.</summary>
    public const string MarkSeparator = " ";

    /// <summary>
    /// The hint row with <paramref name="trailer"/> on its last cell: the trailer cut to half of
    /// <paramref name="cells"/> at most, <paramref name="left"/> cut ahead of the gap, blanks
    /// between; with no trailer, <paramref name="left"/> fitted as before. Pinned.
    /// </summary>
    public static string PinRight(string left, string trailer, int cells) => PinRight(left, trailer, "", cells);

    /// <summary>
    /// <see cref="PinRight(string, string, int)"/> with <paramref name="mark"/> after the trailer's
    /// text (<see cref="Trail"/>): the mark is never cut, the text is cut ahead of it. Pinned.
    /// </summary>
    public static string PinRight(string left, string trailer, string mark, int cells)
    {
        ArgumentNullException.ThrowIfNull(left);
        string right = Trail(trailer, mark, cells);
        if (right.Length == 0)
        {
            return Fit(left, cells);
        }

        int rightCells = TextCells.Width(right);
        string fitted = Fit(left, cells - rightCells - TrailerGap);
        return fitted + new string(' ', cells - TextCells.Width(fitted) - rightCells) + right;
    }

    /// <summary>
    /// The right part of a hint row of <paramref name="cells"/>: <paramref name="trailer"/> cut to
    /// half the row at most; with a <paramref name="mark"/>, the text cut to what the mark and its
    /// separator leave of that half and the mark whole behind it — at an absurd width the text
    /// goes to an ellipsis, then to nothing, and the mark stands alone. Pinned.
    /// </summary>
    public static string Trail(string trailer, string mark, int cells)
    {
        ArgumentNullException.ThrowIfNull(trailer);
        ArgumentNullException.ThrowIfNull(mark);
        if (mark.Length == 0)
        {
            return Fit(trailer, cells / 2);
        }

        return TrailerText(Fit(trailer, cells / 2 - TextCells.Width(mark) - MarkSeparator.Length), mark);
    }

    /// <summary>
    /// The trailer's text and its mark with <see cref="MarkSeparator"/> between; either alone when
    /// the other is empty. Pinned.
    /// </summary>
    public static string TrailerText(string trailer, string mark)
    {
        ArgumentNullException.ThrowIfNull(trailer);
        ArgumentNullException.ThrowIfNull(mark);
        return trailer.Length == 0 ? mark : mark.Length == 0 ? trailer : trailer + MarkSeparator + mark;
    }

    /// <summary>
    /// The hint row's text: <paramref name="rest"/> behind the <paramref name="lead"/> (the strip)
    /// when there is one; either alone when the other is empty. Pinned.
    /// </summary>
    public static string HintRow(string lead, string rest)
    {
        ArgumentNullException.ThrowIfNull(lead);
        ArgumentNullException.ThrowIfNull(rest);
        return lead.Length == 0 ? rest : rest.Length == 0 ? lead : lead + HintSeparator + rest;
    }

    /// <summary>The strip with its separator, ahead of the spinner; empty without one. Pinned.</summary>
    public static string StripPrefix(string strip)
    {
        ArgumentNullException.ThrowIfNull(strip);
        return strip.Length == 0 ? "" : strip + HintSeparator;
    }

    /// <summary>The counted flow row (tests).</summary>
    public int FlowRow
    {
        get
        {
            lock (_gate)
            {
                return _row;
            }
        }
    }

    /// <summary>The counted flow column (tests).</summary>
    public int FlowColumn
    {
        get
        {
            lock (_gate)
            {
                return _col;
            }
        }
    }

    /// <summary>The padding rows drawn above the pane by the last redraw (tests).</summary>
    public int Padding
    {
        get
        {
            lock (_gate)
            {
                return _pad;
            }
        }
    }

    /// <summary>The overlay's content rows drawn by the last redraw, 0 without one (tests).</summary>
    public int OverlayRows
    {
        get
        {
            lock (_gate)
            {
                return _overlayRows;
            }
        }
    }

    /// <summary>An overlay is on the pane in place of the input row.</summary>
    public bool OverlayOpen
    {
        get
        {
            lock (_gate)
            {
                return _overlay is not null;
            }
        }
    }

    /// <summary>The open overlay keeps the input rows under its content (tests).</summary>
    public bool OverlayHasInput
    {
        get
        {
            lock (_gate)
            {
                return _overlay is { Input: true };
            }
        }
    }

    // ── The live slot ───────────────────────────────────────────────────────

    /// <summary>A reply is open in the live slot (set and not yet committed or discarded).</summary>
    public bool LiveOpen
    {
        get
        {
            lock (_gate)
            {
                return _live is not null;
            }
        }
    }

    /// <summary>The live rows drawn above the padding by the last redraw (tests).</summary>
    public int LiveRows
    {
        get
        {
            lock (_gate)
            {
                return _liveRows;
            }
        }
    }

    /// <summary>The lines of the live content already committed into the flow because the block outgrew the screen (tests).</summary>
    public int LiveCommitted
    {
        get
        {
            lock (_gate)
            {
                return _liveCommitted;
            }
        }
    }

    // ── The alternate buffer and the scroll ─────────────────────────────────

    /// <summary>
    /// Enters the alternate screen buffer (the shell's screen is kept for <see cref="Close"/>) with
    /// the flow at its top: the screen's start, before the banner. Disabled, or already in it: nothing.
    /// </summary>
    public void Open()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_inAlternate)
            {
                return;
            }

            _inner.Write(EnterAlternate);
            _inAlternate = true;
            _row = 0;
            _col = 0;
            _lineFull = false;
            _blank = true;
            DiagnosticLog.Debug(Category, AlternateEnteredLogLine(Width, Height));
        }
    }

    /// <summary><c>Alternate buffer entered (240×60)</c>. Pinned.</summary>
    public static string AlternateEnteredLogLine(int width, int height) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Alternate buffer entered ({width}×{height})");

    public const string AlternateLeftLogLine = "Alternate buffer left";

    /// <summary><c>Resized 240×60 → 200×50</c>: the screen rebuilt from the store. Pinned.</summary>
    public static string ResizedLogLine(int fromWidth, int fromHeight, int width, int height) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Resized {fromWidth}×{fromHeight} → {width}×{height}");

    /// <summary>The transcript region shows an earlier window of the store, the pane pinned under it.</summary>
    public bool Scrolled
    {
        get
        {
            lock (_gate)
            {
                return _top >= 0;
            }
        }
    }

    /// <summary>The store row on the region's first row while scrolled, −1 at the bottom (tests).</summary>
    public int ScrollTop
    {
        get
        {
            lock (_gate)
            {
                return _top;
            }
        }
    }

    /// <summary>The rows the pane keeps of the transcript, at the window's width (tests).</summary>
    public int StoredRows
    {
        get
        {
            lock (_gate)
            {
                return _store.Rows(Width).Count;
            }
        }
    }

    /// <summary>
    /// The rows under the region's last row while scrolled — the store's, and the live block's not
    /// yet committed (a reply streams on below while the user reads) — 0 at the bottom.
    /// </summary>
    public int RowsBelow
    {
        get
        {
            lock (_gate)
            {
                return RowsBelowLocked();
            }
        }
    }

    /// <summary>Scrolls the region by <paramref name="pages"/> pages (a page = the region's rows less one; negative = up, towards the start).</summary>
    public void ScrollPage(int pages) => ScrollBy(pages * Math.Max(1, RegionRows(_paneRows) - 1));

    /// <summary>The rows a wheel notch scrolls: Windows' own lines-per-notch default (<c>InfoPane.WheelLines</c> is the pane's own).</summary>
    public const int WheelRows = 3;

    /// <summary>Scrolls the region by <paramref name="notches"/> wheel notches (positive = away from the user = up, towards the start), <see cref="WheelRows"/> rows each.</summary>
    public void ScrollWheel(int notches) => ScrollBy(-notches * WheelRows);

    /// <summary>
    /// Scrolls the region by <paramref name="rows"/> (negative = up, towards the start), clamped to
    /// the store: a store no taller than the region never leaves the bottom, and a scroll down
    /// that reaches the last row is the bottom again (the flow written back, the live block shown).
    /// While the pane is lifted (a batch, a modal) only the anchor moves; the next draw paints it.
    /// Disabled: nothing.
    /// </summary>
    public void ScrollBy(int rows)
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            int max = Math.Max(0, _store.Rows(Width).Count - RegionRows(_paneRows));
            int current = _top >= 0 ? _top : max;
            int top = Math.Clamp(current + rows, 0, max);
            int next = top >= max ? -1 : top;
            if (next == _top)
            {
                return;
            }

            _top = next;
            Redraw();
        }
    }

    /// <summary>The bottom again (Ctrl+End on the line or under a turn): the flow written back where the window was; nothing while not scrolled.</summary>
    public void ScrollToEnd()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_top < 0)
            {
                return;
            }

            _top = -1;
            Redraw();
        }
    }

    /// <summary>
    /// The transcript's first rows in the region — Ctrl+Home (2026-09-18), <see cref="ScrollToEnd"/>'s
    /// mirror: nothing on a transcript that fits, nothing at the top already; the hint row keeps
    /// its <see cref="ScrolledHint"/> wording (the user's call: no Ctrl+Home in it).
    /// </summary>
    public void ScrollToTop()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            int max = Math.Max(0, _store.Rows(Width).Count - RegionRows(_paneRows));
            if (max == 0 || _top == 0)
            {
                return;
            }

            _top = 0;
            Redraw();
        }
    }

    /// <summary>The hint row's text while scrolled: <c>⇡ 12 rows below · PgUp/PgDn scroll · Ctrl+End bottom</c>, <c>1 row</c> singular. Pinned.</summary>
    public static string ScrolledHint(int below) =>
        "⇡ " + below.ToString(System.Globalization.CultureInfo.InvariantCulture) + (below == 1 ? " row below" : " rows below") + HintSeparator + "PgUp/PgDn scroll" + HintSeparator + "Ctrl+End bottom";

    /// <summary>The transcript region's rows over a pane of <paramref name="paneRows"/>: the window less the pane, one at least. Pinned.</summary>
    public static int RegionRows(int height, int paneRows) => Math.Max(1, height - paneRows);

    private int RegionRows(int paneRows) => RegionRows(Height, paneRows);

    private int RowsBelowLocked()
    {
        if (_top < 0)
        {
            return 0;
        }

        return Math.Max(0, _store.Rows(Width).Count - (_top + RegionRows(_paneRows))) + _liveCount;
    }

    /// <summary>
    /// Shows <paramref name="content"/> in the live slot — the reply so far, the whole of it every
    /// time — in place of what was there. Nothing is drawn here: the tick lays it out and repaints,
    /// so a stream of tokens costs one layout per <see cref="Tick"/>. Disabled, nothing (the
    /// transcript keeps its plain path).
    /// </summary>
    public void SetLive(IRenderable content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            _live = content;
            _liveDirty = true;
        }
    }

    /// <summary>
    /// The slot's content becomes transcript: its lines not yet committed are written into the flow
    /// (counted like any flow write), the slot is empty, the pane drawn under them. The reply's end,
    /// and what any flow write does first while a slot is open, so the order on the screen is the
    /// order of the calls.
    /// </summary>
    public void CommitLive()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_live is null)
            {
                return;
            }

            if (_modal > 0)
            {
                var lines = RenderLines(_live, _inner, Width);
                for (int i = _liveCommitted; i < lines.Count; i++)
                {
                    _inner.Write(new SegmentList(lines[i]));
                    _inner.WriteLine();
                }

                ForgetLive();
                return;
            }

            if (_top >= 0)
            {
                // Scrolled: into the store, the count below changed.
                FlushLive();
                RedrawHint();
                return;
            }

            bool sync = _batch == 0 && _drawn;
            if (sync)
            {
                BeginSync();
            }

            Lift();
            RestoreFlow();
            FlushLive();
            if (_batch == 0)
            {
                Draw();
            }

            if (sync)
            {
                EndSync();
            }
        }
    }

    /// <summary>The slot is emptied without writing anything (a reply that said nothing); its drawn rows are erased.</summary>
    public void DiscardLive()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_live is null)
            {
                return;
            }

            ForgetLive();
            if (_top >= 0)
            {
                // Scrolled: nothing of it was drawn; the count below changed.
                RedrawHint();
                return;
            }

            if (_drawn && _batch == 0 && _modal == 0)
            {
                BeginSync();
                Lift();
                Draw();
                EndSync();
            }
        }
    }

    /// <summary>The input rows drawn by the last redraw (tests).</summary>
    public int InputRows
    {
        get
        {
            lock (_gate)
            {
                return _inputRows;
            }
        }
    }

    /// <summary>The input row the terminal's cursor is on, counted from the area's first drawn row (tests: a geometry that follows the cursor).</summary>
    public int CursorInputRow
    {
        get
        {
            lock (_gate)
            {
                return _cursorRow;
            }
        }
    }

    /// <summary>The input area's most rows on a window of <paramref name="height"/>: half of it, and never past the overlay's one-transcript-row rule. Pinned.</summary>
    public static int MaxInputRows(int height) => Math.Clamp(height / 2, 1, Math.Max(1, height - 4));

    /// <summary>
    /// The content rows an overlay may take on a window of <paramref name="height"/> over
    /// <paramref name="inputRows"/> input rows (0 without a slot): everything but one transcript
    /// row, the two rules and the hint. Pinned; the menus size their viewport by it.
    /// </summary>
    public static int MaxOverlayRows(int height, int inputRows) => Math.Max(0, height - 4 - inputRows);

    private int Width => Math.Max(1, _inner.Profile.Width);

    private int Height
    {
        get
        {
            int h = _inner.Profile.Height;
            return h > 0 ? h : 24;
        }
    }

    // ── IAnsiConsole ────────────────────────────────────────────────────────

    public Profile Profile => _inner.Profile;

    public IAnsiConsoleCursor Cursor => _inner.Cursor;

    /// <summary>
    /// Keys are read through <see cref="KeySource"/>, never here: on the screen the real reader is
    /// <see cref="WindowsConsoleInput"/>, and Spectre's own input would eat the mouse records while
    /// peeking. A prompt on the bare pane fails loudly instead.
    /// </summary>
    public IAnsiConsoleInput Input => Enabled ? throw new InvalidOperationException("Read keys through KeySource (a ConsoleWithInput over the pane), not the pane.") : _inner.Input;

    public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;

    public RenderPipeline Pipeline => _inner.Pipeline;

    /// <summary>A flow write: lift the pane, write, count the rows, draw the pane again.</summary>
    public void Write(IRenderable renderable)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        if (!Enabled)
        {
            _inner.Write(renderable);
            return;
        }

        lock (_gate)
        {
            if (_modal > 0)
            {
                // A menu is drawing itself at the flow end; it erases its own region when it closes.
                _inner.Write(renderable);
                return;
            }

            var segments = renderable.GetSegments(_inner).ToList();
            if (_top >= 0)
            {
                // Scrolled: the store takes it, the screen shows the window; the count below changed.
                FlushLive();
                Emit(segments);
                RedrawHint();
                return;
            }

            bool sync = _batch == 0 && _drawn;
            if (sync)
            {
                BeginSync();
            }

            Lift();
            RestoreFlow();
            FlushLive();
            Emit(segments);
            if (_batch == 0)
            {
                Draw();
            }

            if (sync)
            {
                EndSync();
            }
        }
    }

    /// <summary>
    /// The one way flow segments leave the pane: at the bottom written to the console and counted
    /// (<see cref="Track"/>, which stores them too); scrolled, stored alone.
    /// </summary>
    private void Emit(List<Segment> segments)
    {
        if (_top >= 0)
        {
            Store(segments);
            return;
        }

        _inner.Write(new SegmentList(segments));
        Track(segments);
    }

    /// <summary>The segments into the store; a scrolled anchor follows the rows the cap dropped.</summary>
    private void Store(List<Segment> segments)
    {
        int dropped = _store.Append(segments, Width);
        if (_top >= 0 && dropped > 0)
        {
            _top = Math.Max(0, _top - dropped);
        }
    }

    /// <summary>
    /// The flow back on a blank screen (a scrolled draw or a resize erased it): the store's last
    /// rows, as many as the region holds, written from the top row and counted like any flow
    /// write — the open last line without its break, so the flow cursor sits at its end. At the
    /// bottom only; nothing when the flow is already there.
    /// </summary>
    private void RestoreFlow()
    {
        if (!_blank || _top >= 0)
        {
            return;
        }

        _blank = false;
        var rows = _store.Rows(Width);
        int region = RegionRows(_paneRows);
        int first = Math.Max(0, rows.Count - region);
        _row = 0;
        _col = 0;
        _lineFull = false;
        for (int i = first; i < rows.Count; i++)
        {
            var segments = new List<Segment>(rows[i].Count + 1);
            segments.AddRange(rows[i]);
            bool last = i == rows.Count - 1;
            if (!last || !_store.LastLineOpen)
            {
                segments.Add(Segment.LineBreak);
            }

            _inner.Write(new SegmentList(segments));
            Count(segments);
        }
    }

    public void WriteAnsi(Action<AnsiWriter> write) => _inner.WriteAnsi(write);

    /// <summary>The screen is wiped: the flow starts at the top and the pane is off the screen until the next write.</summary>
    public void Clear(bool home)
    {
        _inner.Clear(home);
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            _row = 0;
            _col = 0;
            _lineFull = false;
            _drawn = false;
            _drawnScrolled = false;
            _pad = 0;
            _liveRows = 0;
            _store.Clear();
            _top = -1;
            _blank = false;
            ForgetLive();
        }
    }

    // ── The pane ────────────────────────────────────────────────────────────

    /// <summary>Puts the pane on the screen if it is not there (the screen's start).</summary>
    public void Show()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (!_drawn && _batch == 0 && _modal == 0)
            {
                Draw();
            }
        }
    }

    /// <summary>
    /// Several flow writes as one: the pane is lifted once, the writes are counted, and the pane is
    /// drawn once when the scope ends (the screen clear + banner of <c>/clear</c>).
    /// </summary>
    public IDisposable Batch()
    {
        if (!Enabled)
        {
            return Scope.None;
        }

        lock (_gate)
        {
            if (_batch++ == 0 && _drawn)
            {
                Lift();
            }
        }

        return new Scope(() =>
        {
            lock (_gate)
            {
                if (--_batch == 0 && _modal == 0)
                {
                    Draw();
                }
            }
        });
    }

    /// <summary>
    /// A menu at the flow end: the pane is lifted, <paramref name="work"/> draws and reads keys on
    /// its own (a Spectre prompt erases its region when it closes, so the flow cursor is where it
    /// was), then the pane is drawn again. Writes during the work are not counted.
    /// </summary>
    public async Task<T> ModalAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!Enabled)
        {
            return await work().ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (_modal++ == 0 && _drawn)
            {
                Lift();
            }
        }

        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (--_modal == 0 && _batch == 0)
                {
                    Draw();
                }
            }
        }
    }

    /// <summary>
    /// <see cref="ModalAsync{T}"/> on the pane <paramref name="console"/> renders to — itself, or
    /// the inner console of a <see cref="ConsoleWithInput"/> — and plainly the work when there is
    /// none. The menus call this around every Spectre prompt.
    /// </summary>
    public static Task<T> ModalAsync<T>(IAnsiConsole console, Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(work);
        return console switch
        {
            ScreenPane pane => pane.ModalAsync(work),
            ConsoleWithInput wrapped => ModalAsync(wrapped.Inner, work),
            _ => work(),
        };
    }

    /// <summary>
    /// The overlay: <paramref name="content"/> drawn in place of the input row (the rule stays above
    /// it, <paramref name="hint"/> below it in the hint row, the cursor hidden), the pane growing
    /// upward to fit — the info pane. Content beyond the window is cut at the bottom, one transcript
    /// row always kept. Calling it again replaces the content (a tab switch); the renderable is
    /// kept and laid out again on every redraw, so a resize follows. With <paramref name="input"/>
    /// the input rows stay under the content and the cursor on them (a typed settings edit): the
    /// draft is drawn there as on the plain pane, and what it submits is not written into the flow.
    /// With <paramref name="close"/> the first row ends in the <see cref="CloseGlyph"/> (a menu, an
    /// info pane — never the input line's completion list, which has no title row) and
    /// <see cref="TryHitClose"/> answers a click on it. Disabled: nothing.
    /// </summary>
    public void ShowOverlay(IRenderable content, string hint, bool input = false, bool close = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(hint);
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            _overlay = new Overlay(content, hint, input, close);
            Redraw();
        }
    }

    /// <summary>The input row and the standing hint again, the cursor shown.</summary>
    public void CloseOverlay()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            Dismissed = false;
            if (_overlay is null)
            {
                return;
            }

            // The drawn rows stay until the redraw: the lift steps up over them (Draw resets them).
            _overlay = null;
            Redraw();
        }
    }

    /// <summary>The pane again from the flow cursor, unless a batch or a modal will draw it when it ends.</summary>
    private void Redraw()
    {
        if (_batch > 0 || _modal > 0)
        {
            return;
        }

        bool sync = _drawn;
        if (sync)
        {
            BeginSync();
        }

        Lift();
        Draw();
        if (sync)
        {
            EndSync();
        }
    }

    /// <summary>
    /// A spinner in the hint row with <paramref name="label"/> and the time since this call
    /// (<see cref="BusyText"/>) until the scope is disposed; <see cref="BusyScope.SetLabel"/> changes
    /// the label meanwhile and the count runs on. The transcript may be written under it: every
    /// write draws the pane, busy row included. Disabled: a scope that does nothing.
    /// </summary>
    public BusyScope BeginBusy(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (!Enabled)
        {
            return BusyScope.None;
        }

        int slot;
        lock (_gate)
        {
            if (_busyLabels.Count == 0)
            {
                _busySince = _time.GetTimestamp();
                _frame = 0;
            }

            // Nested (a menu's spinner under a running turn): the inner label shows, the count runs
            // on from the outer's start, and the scope's end brings the outer label back. The scope
            // owns its slot, not its label: SetLabel renames the slot, shown only while it is the top.
            _busyLabels.Add(label);
            slot = _busyLabels.Count - 1;
            _busyLabel = label;
            RedrawHint();
            RefreshGhost();
        }

        return new BusyScope(this, slot);
    }

    /// <summary>The scope's label, from any thread; nothing once the scope ended. Shown when the scope is the top one; the count is not restarted.</summary>
    private void SetBusyLabel(int slot, string label)
    {
        lock (_gate)
        {
            if (slot >= _busyLabels.Count)
            {
                return;
            }

            _busyLabels[slot] = label;
            if (slot == _busyLabels.Count - 1)
            {
                _busyLabel = label;
                RedrawHint();
            }
        }
    }

    /// <summary>The scope's end: its slot removed, the label under it back, once.</summary>
    private void EndBusy(int slot)
    {
        lock (_gate)
        {
            if (slot < _busyLabels.Count)
            {
                _busyLabels.RemoveAt(slot);
            }

            _busyLabel = _busyLabels.Count > 0 ? _busyLabels[^1] : null;
            RedrawHint();
            RefreshGhost();
        }
    }

    /// <summary>
    /// A live spinner (<see cref="BeginBusy"/>): <see cref="SetLabel"/> renames it while it runs — a
    /// turn's stage, a download's progress, "transcribing…" — from any thread, and <see cref="Dispose"/>
    /// ends it. The scope renames its own slot, so a stage change under a nested menu spinner never
    /// touches the menu's label and shows once the menu's scope ends. Ended or disabled: nothing.
    /// </summary>
    public sealed class BusyScope : IDisposable
    {
        /// <summary>The scope a disabled pane hands out: every call a no-op.</summary>
        public static readonly BusyScope None = new(null, -1);

        private ScreenPane? _pane;
        private readonly int _slot;

        internal BusyScope(ScreenPane? pane, int slot)
        {
            _pane = pane;
            _slot = slot;
        }

        /// <summary>The spinner's label from now on; the count runs on.</summary>
        public void SetLabel(string label)
        {
            ArgumentNullException.ThrowIfNull(label);
            _pane?.SetBusyLabel(_slot, label);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _pane, null)?.EndBusy(_slot);
        }
    }

    /// <summary>
    /// The placeholder's row again when the spinner's start or end changed whether it applies
    /// (the empty row under a spinner is bare; the idle row shows the ghost text): the one-row
    /// in-place rewrite blanks or restores it and puts the cursor back. Gated like <see cref="RedrawHint"/>.
    /// </summary>
    private void RefreshGhost()
    {
        if (!_drawn || _batch > 0 || _modal > 0 || _overlay is not null)
        {
            return;
        }

        if (GhostApplies() == _shownGhostCells > 0)
        {
            return;
        }

        var shown = LayoutInput(Width, Height);
        if (shown.Rows.Count == _inputRows)
        {
            RewriteInputRows(shown);
        }
        else
        {
            Redraw();
        }
    }

    /// <summary>Whether the empty row shows the placeholder now: a sentence set, an empty draft, no overlay, no spinner.</summary>
    private bool GhostApplies() => _placeholder.Length > 0 && _text.Length == 0 && _overlay is null && _busyLabel is null;

    /// <summary>The placeholder as the row shows it: cut to the row's cells at <paramref name="width"/> (<see cref="Fit"/>), so no row ever fills the width. Pinned.</summary>
    public static string PlaceholderRow(string placeholder, int width)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        return Fit(placeholder, InputLine.AvailableCells(width));
    }

    /// <summary>The spinner's label beside its elapsed time: <c>thinking 00:12</c>, <c>01:02:03</c> past an hour (<see cref="ElapsedText.Countdown"/>, the timer line's shape). Pinned.</summary>
    public static string BusyText(string label, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(label);
        return label + " " + ElapsedText.Countdown(elapsed);
    }

    /// <summary>
    /// The busy row's text past the frame: <see cref="BusyText"/>, and behind <see cref="HintSeparator"/>
    /// the overlay's own hint when one is open under the spinner (<c>thinking 00:12 · ESC closes · ←/→ tabs · ↑/↓ scroll</c>:
    /// a pane opened mid-turn needs its keys named, ESC closing it rather than the turn). An empty
    /// <paramref name="overlayHint"/> is the bare <see cref="BusyText"/>. Pinned.
    /// </summary>
    public static string BusyRow(string label, TimeSpan elapsed, string overlayHint) => BusyRow(label, elapsed, overlayHint, "");

    /// <summary>
    /// <see cref="BusyRow(string, TimeSpan, string)"/> with the <paramref name="queued"/> part between the
    /// label and the overlay's hint (<c>thinking 00:12 · 📨 2 queued</c>, 2026-09-18); empty = the row as before. Pinned.
    /// </summary>
    public static string BusyRow(string label, TimeSpan elapsed, string overlayHint, string queued)
    {
        ArgumentNullException.ThrowIfNull(overlayHint);
        ArgumentNullException.ThrowIfNull(queued);
        return HintRow(HintRow(BusyText(label, elapsed), queued), overlayHint);
    }

    /// <summary>Redraws the hint row if the standing hint changed (a state change with no transcript line).</summary>
    public void RefreshHint()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_busyLabel is null && HintChanged())
            {
                RedrawHint();
            }
        }
    }

    /// <summary>
    /// The screen's end: the cursor under the pane, and the alternate buffer left (the shell's
    /// screen comes back as it was, the transcript gone with the buffer), for whatever follows the app.
    /// </summary>
    public void Close()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_drawn)
            {
                // From the cursor's row to the hint row: over the input rows below it and the lower rule, or the overlay's rows and it.
                _inner.Cursor.Move(CursorDirection.Down, _paneRows - 2 - CursorDepth);
                _inner.WriteLine();
                _drawn = false;
                _drawnScrolled = false;
                if (_overlay is not null)
                {
                    if (!_drawnInput)
                    {
                        _inner.Cursor.Show(true);
                    }

                    _overlay = null;
                    _overlayRows = 0;
                    _drawnOverlay = false;
                    _drawnInput = false;
                    _drawnClose = false;
                    Dismissed = false;
                }
            }

            LeaveAlternateLocked();
        }
    }

    /// <summary>The alternate buffer left, once (<see cref="Close"/>, or a dispose that never saw a close — the crash path).</summary>
    private void LeaveAlternateLocked()
    {
        if (!_inAlternate)
        {
            return;
        }

        _inner.Write(LeaveAlternate);
        _inAlternate = false;
        DiagnosticLog.Debug(Category, AlternateLeftLogLine);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            LeaveAlternateLocked();
        }

        _timer?.Dispose();
    }

    // ── The input row ───────────────────────────────────────────────────────

    /// <summary>
    /// The start of a read: the prompt glyph is written where the cursor is (disabled), or the
    /// row is already on the screen (enabled). Either way the row's bookkeeping starts over.
    /// </summary>
    public void BeginInput()
    {
        if (!Enabled)
        {
            _inner.Write(new RawText(InputLine.PromptGlyph, Theme.User));
            _renderedCells = 0;
            _cursorCell = 0;
            return;
        }

        lock (_gate)
        {
            if (!_drawn && _batch == 0 && _modal == 0)
            {
                Draw();
            }
        }
    }

    /// <summary>
    /// The input area shows the draft <paramref name="text"/> with the cursor at the UTF-16 index
    /// <paramref name="cursor"/>. Enabled, the draft is word-wrapped over the pane's rows
    /// (<see cref="InputLayout.Wrap"/>): the same row count is rewritten in place, a different one
    /// lifts and draws the pane again; under an overlay without an input slot, or while the pane is
    /// lifted, the draft is only remembered. Disabled, the single row shows the slice <see cref="InputLine.Layout"/>
    /// chooses, cells the previous slice used beyond it blanked, the cursor on the row on entry.
    /// <paramref name="anchor"/> is the other end of the line's selection (−1, or the cursor itself,
    /// = none): the stretch between it and the cursor is drawn in <see cref="Theme.SelectedText"/>
    /// on the enabled pane's rows; the disabled row never shows it (its writes stay as they were).
    /// <paramref name="labels"/> are the stretches of <paramref name="text"/> that stand for pasted
    /// blocks (<see cref="PasteBlocks.LabelRanges"/>), drawn in <see cref="Theme.PasteLabel"/> where
    /// the selection does not cover them; null = none.
    /// </summary>
    public void ShowInput(string text, int cursor, int anchor = -1, IReadOnlyList<(int Start, int Length)>? labels = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            ShowInputLocked(text, cursor, anchor, labels);
        }
    }

    /// <summary>The keys typed ahead during a turn, shown on the input rows as they will read when the turn ends (enabled only); <paramref name="labels"/> as in <see cref="ShowInput"/>.</summary>
    public void PreviewInput(string text, IReadOnlyList<(int Start, int Length)>? labels = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (!_drawn || _modal > 0 || _batch > 0 || _overlay is not null)
            {
                return;
            }

            ShowInputLocked(text, text.Length, -1, labels);
        }
    }

    /// <summary>
    /// Enter: the row becomes the transcript's <c>› text</c> line. Disabled, the row is rewritten
    /// from its first cell as a normal markup line (it may wrap now) and ended; enabled, the line
    /// is written into the flow (the lift steps up from the cursor's row over the whole area) and
    /// the pane comes back with one empty row. Under an overlay with an input slot the submission
    /// is the overlay's (a settings value): nothing is written, the slot is emptied.
    /// <paramref name="preview"/>, when not empty, is the start of the line's collapsed pastes
    /// (<see cref="InputLine.PreviewText"/>), written under the line in the same write, dim
    /// (<see cref="InputLine.PreviewMarkup"/>).
    /// </summary>
    public void CommitInput(string submitted, string preview = "")
    {
        ArgumentNullException.ThrowIfNull(submitted);
        ArgumentNullException.ThrowIfNull(preview);
        string under = preview.Length == 0 ? "" : InputLine.PreviewMarkup(preview) + Environment.NewLine;
        if (!Enabled)
        {
            _inner.Cursor.Move(CursorDirection.Left, TextCells.Width(InputLine.PromptGlyph) + _cursorCell);
            _inner.Write(new Markup(InputLine.SubmittedMarkup(submitted)));
            int stale = _renderedCells - TextCells.Width(submitted);
            if (stale > 0)
            {
                _inner.Write(new RawText(new string(' ', stale)));
            }

            _inner.WriteLine();
            if (under.Length > 0)
            {
                _inner.Write(new Markup(under));
            }

            ResetInputRow();
            return;
        }

        lock (_gate)
        {
            if (_overlay is { Input: true })
            {
                ShowInputLocked("", 0);
                return;
            }

            ResetInputRow();
            // A sent line is the bottom: the flow comes back under the window before the line joins it.
            if (_top >= 0)
            {
                _top = -1;
                Redraw();
            }
        }

        Write(new Markup(InputLine.SubmittedMarkup(submitted) + Environment.NewLine + under));
    }

    /// <summary>
    /// The read ended without a submission (ESC, the wake word, an alert, no keyboard). Disabled,
    /// the cursor steps past the row and the row stays in the transcript; enabled, the area is
    /// simply emptied (back to one row).
    /// </summary>
    public void ClearInput()
    {
        if (!Enabled)
        {
            _inner.Cursor.Move(CursorDirection.Right, _renderedCells - _cursorCell);
            _inner.WriteLine();
            ResetInputRow();
            return;
        }

        lock (_gate)
        {
            ShowInputLocked("", 0);
        }
    }

    // ── Measuring ───────────────────────────────────────────────────────────

    /// <summary>The rows <paramref name="renderable"/> takes at <paramref name="width"/> on <paramref name="console"/>.</summary>
    public static int MeasureRows(IRenderable renderable, IAnsiConsole console, int width) =>
        RenderLines(renderable, console, width).Count;

    /// <summary>
    /// <paramref name="renderable"/> laid out at <paramref name="width"/> on <paramref name="console"/>, one
    /// <see cref="SegmentLine"/> per row: the layout the overlay draw does, for a caller that shows a
    /// window of it (the info pane's viewport).
    /// </summary>
    public static List<SegmentLine> RenderLines(IRenderable renderable, IAnsiConsole console, int width)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        ArgumentNullException.ThrowIfNull(console);
        var options = RenderOptions.Create(console, console.Profile.Capabilities);
        return Segment.SplitLines(renderable.Render(options, width));
    }

    // ── Internals (all under _gate) ─────────────────────────────────────────

    private void ShowInputLocked(string text, int cursor, int anchor = -1, IReadOnlyList<(int Start, int Length)>? labels = null)
    {
        if (!Enabled)
        {
            ShowSingleRow(text, cursor);
            return;
        }

        _text = text;
        _cursor = Math.Clamp(cursor, 0, text.Length);
        _anchor = anchor < 0 || anchor == _cursor ? -1 : Math.Min(anchor, text.Length);
        _labels = labels ?? Array.Empty<(int, int)>();
        if (!_drawn || _batch > 0 || _modal > 0 || _overlay is { Input: false })
        {
            // Under an overlay without a slot, or lifted for a menu or a batch: remembered, drawn when the pane comes back.
            return;
        }

        int firstRow = _firstRow;
        var shown = LayoutInput(Width, Height);
        if (shown.Rows.Count == _inputRows && firstRow == _firstRow)
        {
            RewriteInputRows(shown);
            // A hint that reads the draft (the splash hint, 2026-09-20) follows it on the same
            // key, not at the next tick; under an overlay's slot the standing hint is the overlay's.
            if (_busyLabel is null && HintChanged())
            {
                RedrawHint();
            }
        }
        else
        {
            // The area grows or shrinks: the padding and the flow row are Draw's business.
            Redraw();
        }
    }

    /// <summary>The disabled pane's row, where the cursor is: the slice around the cursor, the stale cells blanked.</summary>
    private void ShowSingleRow(string text, int cursor)
    {
        var (visible, cursorCell) = InputLine.Layout(text, cursor, InputLine.AvailableCells(Width));
        int visibleCells = TextCells.Width(visible);
        int available = InputLine.AvailableCells(Width);
        int pad = Math.Clamp(_renderedCells - visibleCells, 0, Math.Max(0, available - visibleCells));

        _inner.Cursor.Move(CursorDirection.Left, _cursorCell);
        _inner.Write(new RawText(pad > 0 ? visible + new string(' ', pad) : visible, Theme.User));
        _inner.Cursor.Move(CursorDirection.Left, visibleCells + pad - cursorCell);

        _renderedCells = visibleCells;
        _cursorCell = cursorCell;
    }

    /// <summary>
    /// The same number of rows again, in place: from the cursor's row up to the area's first row,
    /// each row's text rewritten after its prefix with the cells the previous text used beyond it
    /// blanked, then the cursor to its new row and cell. Nothing above the area moves.
    /// </summary>
    private void RewriteInputRows(ShownInput shown)
    {
        var (rows, cursorRow, cursorCol) = (shown.Rows, shown.CursorRow, shown.CursorCol);
        int available = InputLine.AvailableCells(Width);
        int prefix = TextCells.Width(InputLine.PromptGlyph);
        bool sync = rows.Count > 1;
        if (sync)
        {
            BeginSync();
            _inner.Cursor.Show(false);
        }

        _inner.Cursor.Move(CursorDirection.Up, _cursorRow);
        _inner.Cursor.Move(CursorDirection.Left, _cursorCell);
        int endCol = 0;
        int ghostCells = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                _inner.WriteLine();
                _inner.Write(new RawText(InputLine.ContinuationIndent, Theme.User));
            }

            // The first row of an empty draft carries the placeholder instead of the draft's
            // text; the cells the last draw gave the ghost count as the row's previous cells,
            // so the first key blanks them and a Backspace to nothing writes the ghost again.
            string row = rows[i];
            string? ghost = i == 0 && row.Length == 0 && GhostApplies() ? PlaceholderRow(_placeholder, Width) : null;
            int cells = ghost is null ? TextCells.Width(row) : TextCells.Width(ghost);
            int previous = (i < _shownRows.Count ? TextCells.Width(_shownRows[i]) : 0) + (i == 0 ? _shownGhostCells : 0);
            int pad = Math.Clamp(previous - cells, 0, Math.Max(0, available - cells));
            if (ghost is null)
            {
                WriteRowText(row, shown.Starts[i], pad);
            }
            else
            {
                _inner.Write(new RawText(pad > 0 ? ghost + new string(' ', pad) : ghost, Theme.Placeholder));
                ghostCells = cells;
            }

            endCol = cells + pad;
        }

        int up = rows.Count - 1 - cursorRow;
        if (up > 0)
        {
            _inner.Cursor.Move(CursorDirection.Up, up);
            ColumnZero();
            _inner.Cursor.Move(CursorDirection.Right, prefix + cursorCol);
        }
        else
        {
            _inner.Cursor.Move(CursorDirection.Left, endCol - cursorCol);
        }

        if (sync)
        {
            _inner.Cursor.Show(true);
            EndSync();
        }

        SetShown(shown, ghostCells);
    }

    /// <summary>The drawn state: what the rows show, where each starts in the draft, where the cursor is, and the cells the placeholder took on the first row (0 = none).</summary>
    private void SetShown(ShownInput shown, int ghostCells)
    {
        _shownRows = shown.Rows;
        _shownStarts = shown.Starts;
        _shownNext = shown.Next;
        _inputRows = shown.Rows.Count;
        _cursorRow = shown.CursorRow;
        _cursorCell = shown.CursorCol;
        _shownGhostCells = ghostCells;
    }

    /// <summary>The rows the area shows, each row's first index in the draft and the next row's start (−1 for the draft's last row), and the cursor among them.</summary>
    private sealed record ShownInput(List<string> Rows, List<int> Starts, List<int> Next, int CursorRow, int CursorCol);

    /// <summary>
    /// The draft laid out at <paramref name="width"/>, cut to the rows the window allows with the
    /// viewport moved just far enough to keep the cursor's row in view: the rows to draw, and the
    /// cursor's row and cell among them.
    /// </summary>
    private ShownInput LayoutInput(int width, int height)
    {
        var layout = InputLayout.Wrap(_text, _cursor, InputLine.AvailableCells(width));
        int shown = Math.Min(layout.Rows.Count, MaxInputRows(height));
        if (layout.CursorRow < _firstRow)
        {
            _firstRow = layout.CursorRow;
        }
        else if (layout.CursorRow >= _firstRow + shown)
        {
            _firstRow = layout.CursorRow - shown + 1;
        }

        _firstRow = Math.Clamp(_firstRow, 0, layout.Rows.Count - shown);
        var rows = new List<string>(shown);
        var starts = new List<int>(shown);
        var next = new List<int>(shown);
        for (int i = 0; i < shown; i++)
        {
            int r = _firstRow + i;
            rows.Add(layout.Rows[r]);
            starts.Add(layout.Starts[r]);
            next.Add(r + 1 < layout.Starts.Count ? layout.Starts[r + 1] : -1);
        }

        return new ShownInput(rows, starts, next, layout.CursorRow - _firstRow, layout.CursorCol);
    }

    /// <summary>
    /// Where in the draft a click at buffer cell (<paramref name="x"/>, <paramref name="y"/>)
    /// lands: the UTF-16 index of the element under it, the row's end past its last character.
    /// False when the click is not on a drawn input row (the transcript, a rule, the hint), when
    /// the pane is lifted or under an overlay without an input slot (an overlay's own rows answer
    /// to <see cref="TryHitOverlay"/>), or when the console cannot say where the cursor is.
    /// The click's row is measured from <see cref="ScreenGeometry.CursorTop"/>: the terminal's
    /// cursor is on the area's row <c>_cursorRow</c>, so the area starts <c>_cursorRow</c> rows above it.
    /// </summary>
    public bool TryHitInput(int x, int y, out int index)
    {
        index = 0;
        if (!Enabled)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_drawn || _batch > 0 || _modal > 0 || _overlay is { Input: false } || _geometry?.CursorTop() is not int top)
            {
                return false;
            }

            int r = y - (top - _cursorRow);
            if (r < 0 || r >= _inputRows)
            {
                return false;
            }

            string row = _shownRows[r];
            int col = Math.Max(0, x - TextCells.Width(InputLine.PromptGlyph));
            // The walk is InputLayout's (shared with the Up/Down row moves since 2026-09-21): past a
            // row broken by cells, the last character of this row is what the click meant.
            index = _shownStarts[r] + InputLayout.IndexInRow(row, col, _shownNext[r] >= 0 && _shownStarts[r] + row.Length == _shownNext[r]);
            return true;
        }
    }

    /// <summary>
    /// The draft's caret one row up or down (2026-09-21, <paramref name="delta"/> −1 / +1) at cell
    /// column <paramref name="preferredCol"/> (−1 = the caret's own column): the display index it lands
    /// on (<see cref="InputLayout.IndexAt"/>) and the column used, which the line keeps as its goal
    /// column across a run of arrows. False — the key is history — when the pane is off, the draft
    /// is one row, or the target row is outside it. Laid out from the held text at the current width
    /// under the gate, as the draw does; nothing drawn.
    /// </summary>
    public bool TryStepInputRow(int delta, int preferredCol, out int index, out int col)
    {
        index = 0;
        col = 0;
        if (!Enabled)
        {
            return false;
        }

        lock (_gate)
        {
            var layout = InputLayout.Wrap(_text, _cursor, InputLine.AvailableCells(Width));
            int target = layout.CursorRow + delta;
            if (layout.Rows.Count < 2 || target < 0 || target >= layout.Rows.Count)
            {
                return false;
            }

            col = preferredCol < 0 ? layout.CursorCol : preferredCol;
            index = layout.IndexAt(target, col);
            return true;
        }
    }

    /// <summary>
    /// Which drawn overlay content row a click at buffer cell (<paramref name="x"/>, <paramref name="y"/>)
    /// lands on (0 = the overlay's first line; the column does not matter). False when no overlay
    /// is drawn, the click is off its rows (the transcript, a rule, an input slot, the hint), the
    /// pane is lifted, or the console cannot say where the cursor is. The overlay's first row is
    /// <see cref="CursorDepth"/> rows above the terminal's cursor: 0 without a slot (the hidden
    /// cursor rests on that very row), the overlay's rows plus the cursor's input row with one.
    /// </summary>
    public bool TryHitOverlay(int x, int y, out int row)
    {
        row = 0;
        if (!Enabled)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_drawn || !_drawnOverlay || _batch > 0 || _modal > 0 || _geometry?.CursorTop() is not int top)
            {
                return false;
            }

            int r = y - (top - CursorDepth);
            if (r < 0 || r >= _overlayRows)
            {
                return false;
            }

            row = r;
            return true;
        }
    }

    /// <summary>
    /// Whether a click at buffer cell (<paramref name="x"/>, <paramref name="y"/>) lands on the
    /// drawn overlay's <see cref="CloseGlyph"/> (2026-09-18): its first row, from the cell before
    /// the glyph to the screen's last column — three cells, a target for a mouse. False when no
    /// glyph was drawn (no overlay, one shown without <c>close</c>, <see cref="CloseGlyphShown"/>
    /// false, no room), when the pane is lifted, or when the console cannot say where the cursor is.
    /// The readers treat a hit as the ESC key.
    /// </summary>
    public bool TryHitClose(int x, int y)
    {
        if (!Enabled)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_drawn || !_drawnOverlay || _closeColumn < 0 || _batch > 0 || _modal > 0 || _geometry?.CursorTop() is not int top)
            {
                return false;
            }

            return y == top - CursorDepth && x >= _closeColumn - 1 && x <= Math.Max(_closeColumn, _lastWidth - 1);
        }
    }

    /// <summary>
    /// Whether a click at buffer cell (<paramref name="x"/>, <paramref name="y"/>) lands off the
    /// drawn pane — the transcript above it, either rule, the hint row — while an overlay shown
    /// with <c>close</c> is drawn (2026-09-18): a menu or an info pane, never the input line's
    /// completion list. False on the overlay's rows, on its input slot, when the pane is lifted,
    /// or when the console cannot say where the cursor is. Two such clicks within
    /// <see cref="DoubleClick.Interval"/> are the readers' <see cref="Dismiss"/>.
    /// </summary>
    public bool TryHitOutside(int x, int y)
    {
        if (!Enabled)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_drawn || !_drawnOverlay || !_drawnClose || _batch > 0 || _modal > 0 || _geometry?.CursorTop() is not int top)
            {
                return false;
            }

            int first = top - CursorDepth;
            int last = first + _overlayRows + (_drawnInput ? _inputRows : 0) - 1;
            return y < first || y > last;
        }
    }

    /// <summary>
    /// Whether a click at buffer cell (<paramref name="x"/>, <paramref name="y"/>) lands on the
    /// drawn hint row — the pane's last row, any column (2026-09-18: the input line's double-click
    /// there opens the settings). False under an overlay (that hint row is the overlay's, and the
    /// overlay reads the click), when the pane is lifted, or when the console cannot say where the
    /// cursor is. The hint row is <c>_paneRows - 2 - CursorDepth</c> rows under the terminal's
    /// cursor, the offset <see cref="RedrawHint"/> writes it at.
    /// </summary>
    public bool TryHitHint(int x, int y) => TryHitHint(x, y, out _);

    /// <summary>
    /// <see cref="TryHitHint(int, int)"/> naming the part of the row under the click
    /// (2026-09-18): <see cref="HintZone.Strip"/> with the speech glyph and its first column,
    /// <see cref="HintZone.Trailer"/> over the model name and its reasoning mark at the right
    /// edge, <see cref="HintZone.Usage"/> over the token tally (2026-09-21), <see cref="HintZone.Row"/>
    /// anywhere else — the separators between the glyphs included.
    /// The zones are those of the standing row as last drawn; under the busy row the spinner and
    /// its label are <see cref="HintZone.Usage"/> and every other hit is the row.
    /// While the transcript is scrolled (either row) the row is <see cref="HintZone.Scrolled"/>
    /// instead — the strip and the trailer keep their zones — so a double-click on the scroll's
    /// hint is the bottom again; <c>/settings</c> from the row waits for the bottom.
    /// </summary>
    public bool TryHitHint(int x, int y, out HintHit hit)
    {
        hit = default;
        if (!Enabled)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_drawn || _drawnOverlay || _batch > 0 || _modal > 0 || _geometry?.CursorTop() is not int top)
            {
                return false;
            }

            if (y != top + _paneRows - 2 - CursorDepth)
            {
                return false;
            }

            hit = HintHitAt(_hintStrip, _trailerColumn, _busyLabel is null ? _queuedColumn : -1, _queuedCells, _usageColumn, _usageCells, x, _hintScrolled);
            return true;
        }
    }

    /// <summary>
    /// Whether a click at buffer cell (<paramref name="x"/>, <paramref name="y"/>) lands on the
    /// queued part of the drawn hint row (<see cref="Queued"/>, 2026-09-18) — the standing row's or
    /// the busy row's, the one hit test that answers under a turn, so the watcher can open
    /// <c>/queue</c> from a double-click there. <see cref="TryHitHint(int, int)"/>'s guards otherwise.
    /// </summary>
    public bool TryHitQueued(int x, int y)
    {
        if (!Enabled)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_drawn || _drawnOverlay || _batch > 0 || _modal > 0 || _geometry?.CursorTop() is not int top)
            {
                return false;
            }

            return y == top + _paneRows - 2 - CursorDepth && _queuedColumn >= 0 && x >= _queuedColumn && x < _queuedColumn + _queuedCells;
        }
    }

    /// <summary>
    /// The zone of column <paramref name="x"/> on a standing row whose strip is <paramref name="strip"/>
    /// (from column 0) and whose trailer starts at <paramref name="trailerColumn"/> (−1 for none). Pinned.
    /// </summary>
    public static HintHit HintHitAt(string strip, int trailerColumn, int x) => HintHitAt(strip, trailerColumn, -1, 0, x);

    /// <summary>
    /// <see cref="HintHitAt(string, int, int)"/> with the queued part's place (2026-09-18): the
    /// <paramref name="queuedCells"/> from <paramref name="queuedColumn"/> (−1 for none) are
    /// <see cref="HintZone.Queued"/>, its first column the hit's; with <paramref name="scrolled"/>
    /// (the row drawn with <see cref="ScrolledHint"/>) what would be the row is
    /// <see cref="HintZone.Scrolled"/>. Pinned.
    /// </summary>
    public static HintHit HintHitAt(string strip, int trailerColumn, int queuedColumn, int queuedCells, int x, bool scrolled = false) =>
        HintHitAt(strip, trailerColumn, queuedColumn, queuedCells, -1, 0, x, scrolled);

    /// <summary>
    /// <see cref="HintHitAt(string, int, int, int, int, bool)"/> with the usage zone's place
    /// (2026-09-21): the <paramref name="usageCells"/> from <paramref name="usageColumn"/> (−1 for
    /// none) are <see cref="HintZone.Usage"/>, its first column the hit's — the token tally on the
    /// standing row, the spinner and its label on the busy row; behind the trailer and the queued
    /// part, ahead of the strip. Pinned.
    /// </summary>
    public static HintHit HintHitAt(string strip, int trailerColumn, int queuedColumn, int queuedCells, int usageColumn, int usageCells, int x, bool scrolled = false)
    {
        ArgumentNullException.ThrowIfNull(strip);
        if (trailerColumn >= 0 && x >= trailerColumn)
        {
            return new HintHit(HintZone.Trailer, "", trailerColumn);
        }

        if (queuedColumn >= 0 && x >= queuedColumn && x < queuedColumn + queuedCells)
        {
            return new HintHit(HintZone.Queued, "", queuedColumn);
        }

        if (usageColumn >= 0 && x >= usageColumn && x < usageColumn + usageCells)
        {
            return new HintHit(HintZone.Usage, "", usageColumn);
        }

        int column = 0;
        int i = 0;
        while (i < strip.Length && column <= x)
        {
            int width = TextCells.ElementWidth(strip, i, out int length);
            length = Math.Max(1, length);
            if (x < column + width && !string.IsNullOrWhiteSpace(strip.AsSpan(i, length).ToString()))
            {
                return new HintHit(HintZone.Strip, strip.Substring(i, length), column);
            }

            column += width;
            i += length;
        }

        return new HintHit(scrolled ? HintZone.Scrolled : HintZone.Row, "", -1);
    }

    /// <summary>
    /// A console write that changes nothing on the screen, for the moment the console's input
    /// mode was changed: ConPTY applies a mode change with the next write, so without one the
    /// mouse would not be taken or handed back until the next keystroke drew something. The hint
    /// row rewritten in place when the pane is drawn, else the two synchronized-output codes.
    /// </summary>
    public void Touch()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_drawn && _batch == 0 && _modal == 0)
            {
                RedrawHint();
            }
            else
            {
                BeginSync();
                EndSync();
            }
        }
    }

    /// <summary>What a drawn row starts with: the glyph on the draft's first row, the indent on every other.</summary>
    private static string Prefix(int textRow) => textRow == 0 ? InputLine.PromptGlyph : InputLine.ContinuationIndent;

    /// <summary>
    /// One input row's text after its prefix, <paramref name="pad"/> blanks after it: one write in
    /// <see cref="Theme.User"/> when neither the selection nor a paste label touches the row (byte
    /// for byte what it always was), else the runs <see cref="RowRuns"/> gives, the blanks on the last.
    /// <paramref name="rowStart"/> is the row's first index in the draft.
    /// </summary>
    private void WriteRowText(string row, int rowStart, int pad)
    {
        string blanks = pad > 0 ? new string(' ', pad) : "";
        var runs = RowRuns(row, rowStart, _anchor < 0 ? -1 : Math.Min(_anchor, _cursor), _anchor < 0 ? -1 : Math.Max(_anchor, _cursor), _labels);
        for (int i = 0; i < runs.Count; i++)
        {
            var (text, style) = runs[i];
            if (i == runs.Count - 1)
            {
                text += blanks;
            }

            if (text.Length > 0)
            {
                _inner.Write(new RawText(text, style));
            }
        }
    }

    /// <summary>
    /// A drawn row as styled runs: the stretch of the draft's selection <c>[start, end)</c>
    /// (−1 = none) in <see cref="Theme.SelectedText"/>, the paste <paramref name="labels"/> (draft
    /// ranges) outside it in <see cref="Theme.PasteLabel"/>, everything else in <see cref="Theme.User"/>;
    /// a row nothing touches is one run. <paramref name="rowStart"/> is the row's first index in
    /// the draft. Empty runs are left out except that an empty row is one empty run. Pure, pinned.
    /// </summary>
    public static IReadOnlyList<(string Text, Style Style)> RowRuns(string row, int rowStart, int start, int end, IReadOnlyList<(int Start, int Length)> labels)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(labels);
        var runs = new List<(string, Style)>();
        if (row.Length == 0)
        {
            runs.Add(("", Theme.User));
            return runs;
        }

        var styles = new Style[row.Length];
        Array.Fill(styles, Theme.User);
        foreach (var (labelStart, length) in labels)
        {
            for (int i = Math.Max(0, labelStart - rowStart); i < Math.Min(row.Length, labelStart + length - rowStart); i++)
            {
                styles[i] = Theme.PasteLabel;
            }
        }

        if (start >= 0 && end > start)
        {
            for (int i = Math.Max(0, start - rowStart); i < Math.Min(row.Length, end - rowStart); i++)
            {
                styles[i] = Theme.SelectedText;
            }
        }

        int from = 0;
        for (int i = 1; i <= row.Length; i++)
        {
            if (i == row.Length || !styles[i].Equals(styles[from]))
            {
                runs.Add((row[from..i], styles[from]));
                from = i;
            }
        }

        return runs;
    }

    /// <summary>The draft forgotten. The drawn fields stay: the next lift steps up from where the cursor is.</summary>
    private void ResetInputRow()
    {
        _text = "";
        _cursor = 0;
        _anchor = -1;
        _labels = Array.Empty<(int, int)>();
        _firstRow = 0;
        _renderedCells = 0;
        if (!Enabled)
        {
            _cursorCell = 0;
        }
    }

    /// <summary>Segments written to the console at the flow cursor: counted, and kept in the store.</summary>
    private void Track(List<Segment> segments)
    {
        Count(segments);
        Store(segments);
    }

    /// <summary>Counts the rows and cells the segments took, with the terminal's deferred wrap (the store wraps by the same rule).</summary>
    private void Count(List<Segment> segments)
    {
        int w = Width;
        foreach (var segment in segments)
        {
            if (segment.IsControlCode)
            {
                continue;
            }

            if (segment.IsLineBreak)
            {
                _row++;
                _col = 0;
                _lineFull = false;
                continue;
            }

            string text = segment.Text;
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] is '\r' or '\n')
                {
                    if (text[i] == '\n')
                    {
                        _row++;
                        _col = 0;
                        _lineFull = false;
                    }

                    i++;
                    continue;
                }

                int cells = TextCells.ElementWidth(text, i, out int length);
                if (_lineFull || _col + cells > w)
                {
                    _row++;
                    _col = cells;
                }
                else
                {
                    _col += cells;
                }

                _lineFull = _col >= w;
                i += Math.Max(1, length);
            }
        }

        _row = Math.Min(_row, Height - 1);
    }

    /// <summary>
    /// From the flow cursor: the padding, the rule, the overlay's rows if any, the input rows unless
    /// the overlay has no slot, the lower rule, the hint, then the cursor back into the area — on the
    /// cursor's input row, shown; or the overlay's first row, hidden. Either way it is
    /// <see cref="CursorDepth"/> rows under the upper rule, so <see cref="Lift"/> is one computation
    /// — over the drawn fields this draw sets last, so the next lift steps up from THIS shape even
    /// when a different one has already been requested.
    /// </summary>
    private void Draw()
    {
        int w = Width;
        int h = Height;

        // The overlay, and the draft, are laid out again on every draw, at the window's width; the
        // draft to MaxInputRows around the cursor, the overlay cut to what the window leaves over
        // one transcript row and the input rows it keeps.
        List<SegmentLine>? overlayLines = null;
        int overlayRows = 0;
        ShownInput? shown = null;
        if (_overlay is null || _overlay.Input)
        {
            shown = LayoutInput(w, h);
        }

        int closeColumn = -1;
        if (_overlay is { } overlay)
        {
            overlayLines = Segment.SplitLines(overlay.Content.GetSegments(_inner), w);
            overlayRows = Math.Clamp(overlayLines.Count, 0, MaxOverlayRows(h, shown?.Rows.Count ?? 0));
            if (overlay.Close && overlayRows > 0 && CloseGlyphShown())
            {
                // The close glyph in column w − 2 of the first row, TrailerGap cells clear of the
                // title or the strip; a first row that leaves no room (a strip wider than the
                // window) goes without.
                int cells = overlayLines[0].CellCount();
                int glyph = TextCells.Width(CloseGlyph);
                if (cells + TrailerGap + glyph <= w - 1)
                {
                    closeColumn = w - 1 - glyph;
                    overlayLines[0].Add(new Segment(new string(' ', closeColumn - cells)));
                    overlayLines[0].Add(new Segment(CloseGlyph, Theme.DimText));
                }
            }
        }

        _paneRows = 3 + overlayRows + (shown?.Rows.Count ?? 0);

        // Scrolled: the anchor against the region this pane leaves; at or past the last window it
        // is the bottom after all (a taller pane, a store that shrank).
        IReadOnlyList<SegmentLine>? window = null;
        int region = RegionRows(_paneRows);
        if (_top >= 0)
        {
            window = _store.Rows(w);
            int max = Math.Max(0, window.Count - region);
            if (_top >= max)
            {
                _top = -1;
                window = null;
            }
        }

        _inner.Cursor.Show(false);

        int liveRows = 0;
        if (window is not null)
        {
            // The region from the top row: the flow the screen held is erased (a lift from the
            // bottom shape left the cursor on it), the window's rows written in its place, the
            // live block left in its slot — its rows only counted, for the hint.
            if (!_blank)
            {
                _inner.Cursor.Move(CursorDirection.Up, _row);
                ColumnZero();
                _inner.Write(EraseDown);
                _blank = true;
            }

            for (int i = 0; i < region; i++)
            {
                _inner.Write(new SegmentList(window[_top + i]));
                _inner.WriteLine();
            }

            _liveCount = _live is null ? 0 : Math.Max(0, RenderLines(_live, _inner, w).Count - _liveCommitted);
            _pad = 0;
        }
        else
        {
            RestoreFlow();
            if (_geometry?.CursorRow() is int actual && actual >= 0 && actual < h && actual != _row)
            {
                DiagnosticLog.Debug(Category, $"Flow row corrected: counted {_row}, the console says {actual}.");
                _row = actual;
            }

            // The live block, laid out at the window's width: what does not fit above the pane is
            // committed into the flow first (the terminal scrolls it away like any transcript), the tail
            // is drawn under the flow and lifted with the pane next time.
            List<SegmentLine>? live = null;
            if (_live is not null)
            {
                var lines = RenderLines(_live, _inner, w);
                int skip = Math.Min(_liveCommitted, lines.Count);
                int excess = lines.Count - skip - region;
                if (excess > 0)
                {
                    EndFlowRow();
                    for (int i = 0; i < excess; i++)
                    {
                        WriteFlowLine(lines[skip + i]);
                    }

                    _liveCommitted += excess;
                    skip += excess;
                }

                live = lines.GetRange(skip, lines.Count - skip);
            }

            liveRows = live?.Count ?? 0;
            _liveCount = liveRows;
            int start = _col > 0 ? _row + 1 : _row;
            _pad = Math.Max(0, h - _paneRows - start - liveRows);

            if (_col > 0)
            {
                _inner.WriteLine();
            }

            if (live is not null)
            {
                foreach (var line in live)
                {
                    _inner.Write(new SegmentList(line));
                    _inner.WriteLine();
                }
            }

            for (int i = 0; i < _pad; i++)
            {
                _inner.WriteLine();
            }

            // Drawing past the last row scrolls the screen: the flow moves up with it.
            int overflow = start + liveRows + _paneRows - h;
            if (overflow > 0)
            {
                _row = Math.Max(0, _row - overflow);
            }
        }

        WriteUpperRule(w);
        if (overlayLines is not null)
        {
            for (int i = 0; i < overlayRows; i++)
            {
                _inner.Write(new SegmentList(overlayLines[i]));
                _inner.WriteLine();
            }
        }

        int ghostCells = 0;
        if (shown is not null)
        {
            for (int i = 0; i < shown.Rows.Count; i++)
            {
                _inner.Write(new RawText(Prefix(_firstRow + i), Theme.User));
                if (i == 0 && shown.Rows[i].Length == 0 && GhostApplies())
                {
                    // The empty idle row: the placeholder after the glyph, in place of the draft.
                    string ghost = PlaceholderRow(_placeholder, w);
                    _inner.Write(new RawText(ghost, Theme.Placeholder));
                    ghostCells = TextCells.Width(ghost);
                }
                else
                {
                    WriteRowText(shown.Rows[i], shown.Starts[i], 0);
                }

                _inner.WriteLine();
            }
        }

        WriteRule(w);
        WriteHintRow();

        _liveRows = liveRows;
        _liveDirty = false;

        // From the hint row back up over the lower rule into the area: the overlay's first row
        // when it has no slot, else the cursor's input row.
        _overlayRows = overlayRows;
        if (shown is null)
        {
            _cursorRow = 0;
            _inner.Cursor.Move(CursorDirection.Up, overlayRows + 1);
            ColumnZero();
        }
        else
        {
            SetShown(shown, ghostCells);
            _inner.Cursor.Move(CursorDirection.Up, _inputRows - _cursorRow + 1);
            ColumnZero();
            _inner.Cursor.Move(CursorDirection.Right, TextCells.Width(InputLine.PromptGlyph) + _cursorCell);
            _inner.Cursor.Show(true);
        }

        _lastWidth = w;
        _lastHeight = h;
        _drawnOverlay = _overlay is not null;
        _drawnInput = _overlay?.Input ?? false;
        _drawnClose = _overlay?.Close ?? false;
        _drawnScrolled = window is not null;
        _closeColumn = closeColumn;
        _drawn = true;
    }

    /// <summary>
    /// From the cursor's input row: back to the flow cursor, and everything from there to the end
    /// of the screen erased — or, drawn scrolled, back to the top row and the whole screen erased
    /// (the flow is not on it; the next draw at the bottom writes it back).
    /// </summary>
    private void Lift()
    {
        if (!_drawn)
        {
            return;
        }

        if (_drawnScrolled)
        {
            LiftToTop();
            return;
        }

        // The area's first row is at start + pad + 1 and the cursor _cursorRow rows under it. A
        // full flow row (wrap pending) continues on the next row, at its first cell: the terminal
        // would have wrapped the next character there.
        int up;
        if (_lineFull)
        {
            up = _pad + 1;
            _row++;
            _col = 0;
            _lineFull = false;
        }
        else
        {
            up = _pad + 1 + (_col > 0 ? 1 : 0);
        }

        // The live rows sit between the flow and the padding: over them too, and they are gone
        // with the erase — the next draw lays the block out again.
        up += _liveRows;
        _liveRows = 0;

        _inner.Cursor.Show(false);
        _inner.Cursor.Move(CursorDirection.Up, up + CursorDepth);
        ColumnZero();
        if (_col > 0)
        {
            _inner.Cursor.Move(CursorDirection.Right, _col);
        }

        _inner.Write(EraseDown);
        _drawn = false;
    }

    /// <summary>
    /// Drawn scrolled (the hint row on the screen's last row, the cursor <c>_paneRows − 2 − CursorDepth</c>
    /// rows above it): the cursor to the top row and the whole screen erased. Also the resize: the
    /// buffer is not reflowed, so the screen is rebuilt from the store.
    /// </summary>
    private void LiftToTop()
    {
        int up = Math.Max(0, _lastHeight - 1 - (_paneRows - 2 - CursorDepth));
        _inner.Cursor.Show(false);
        _inner.Cursor.Move(CursorDirection.Up, up);
        ColumnZero();
        _inner.Write(EraseDown);
        _liveRows = 0;
        _blank = true;
        _drawn = false;
    }

    /// <summary>
    /// Lifted (or scrolled): the slot's lines not yet in the flow are written there (counted, or
    /// stored alone while scrolled), and the slot is empty. Nothing to write for an empty document.
    /// </summary>
    private void FlushLive()
    {
        if (_live is null)
        {
            return;
        }

        var lines = RenderLines(_live, _inner, Width);
        if (lines.Count > _liveCommitted)
        {
            EndFlowRow();
            for (int i = _liveCommitted; i < lines.Count; i++)
            {
                WriteFlowLine(lines[i]);
            }
        }

        ForgetLive();
    }

    private void ForgetLive()
    {
        _live = null;
        _liveCommitted = 0;
        _liveDirty = false;
        _liveCount = 0;
    }

    /// <summary>A partial flow row is ended (the block starts at column 0), the flow cursor with it — the store's open line too.</summary>
    private void EndFlowRow()
    {
        if (_top >= 0 ? _store.LastLineOpen : _col > 0)
        {
            Emit(new List<Segment> { Segment.LineBreak });
        }
    }

    /// <summary>One laid-out line into the flow, at the flow cursor (column 0), ended and counted.</summary>
    private void WriteFlowLine(SegmentLine line)
    {
        var segments = new List<Segment>(line.Count + 1);
        segments.AddRange(line);
        segments.Add(Segment.LineBreak);
        Emit(segments);
    }

    /// <summary>
    /// How many rows under the upper rule the terminal's cursor sits, as the LAST draw left it (the
    /// drawn overlay, never the requested one): the overlay's first row (0) without a slot, else the
    /// cursor's input row under the overlay's rows. What <see cref="Lift"/>, <see cref="Close"/> and
    /// <see cref="RedrawHint"/> count from.
    /// </summary>
    private int CursorDepth => !_drawnOverlay ? _cursorRow : _drawnInput ? _overlayRows + _cursorRow : 0;

    private void WriteRule(int width)
    {
        _inner.Write(new RawText(new string(RuleGlyph, width), Theme.PaneRule));
        _inner.WriteLine();
    }

    /// <summary>The upper rule with <see cref="RuleTitle"/> at its right edge, remembered for the tick's comparison.</summary>
    private void WriteUpperRule(int width)
    {
        _drawnRuleTitle = _ruleTitle();
        _inner.Write(new RawText(RuleWithTitle(_drawnRuleTitle, width), Theme.PaneRule));
        _inner.WriteLine();
    }

    /// <summary>The least rule glyphs kept at the left of a titled upper rule; a title that would leave fewer is cut, a width that cannot hold even a cut title gets the bare rule.</summary>
    public const int RuleTitleMinRule = 8;

    /// <summary>
    /// The upper rule of <paramref name="width"/> cells: bare for an empty <paramref name="title"/>,
    /// else the rule, a space, the title, a space and one last rule glyph at the edge —
    /// <c>──────── settings-layout-reorder ─</c> (2026-09-18, the user's picture). The title is cut
    /// with <see cref="Fit"/> so <see cref="RuleTitleMinRule"/> glyphs stay on the left; a width
    /// with no room for a one-cell title after them is the bare rule. Pinned.
    /// </summary>
    public static string RuleWithTitle(string title, int width)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (title.Length == 0 || width <= 0)
        {
            return new string(RuleGlyph, Math.Max(0, width));
        }

        int room = width - RuleTitleMinRule - 3;   // the space either side and the last glyph
        if (room < 1)
        {
            return new string(RuleGlyph, width);
        }

        string fitted = Fit(title, room);
        int left = width - TextCells.Width(fitted) - 3;
        return new string(RuleGlyph, left) + " " + fitted + " " + RuleGlyph;
    }

    /// <summary>The title <see cref="RuleTitle"/> answers now is not the one on the drawn upper rule.</summary>
    private bool RuleTitleChanged() => !string.Equals(_ruleTitle(), _drawnRuleTitle, StringComparison.Ordinal);

    private void WriteHintRow()
    {
        int max = Math.Max(1, Width - 1);
        string mark = _trailerMark();
        if (_busyLabel is { } label)
        {
            // The strip, then the frame in its own style, then the label and its count in what is
            // left ahead of the trailer — the same cut PinRight makes, in four styles.
            string right = Trail(_trailer(), mark, max);
            int leftMax = right.Length == 0 ? max : max - TextCells.Width(right) - TrailerGap;
            string frame = Theme.SpinnerFrames[_frame % Theme.SpinnerFrames.Length];
            string prefix = Fit(StripPrefix(_strip()), leftMax - TextCells.Width(frame));
            string queued = StandingQueued();
            var elapsed = _time.GetElapsedTime(_busySince);
            string unfitted = " " + BusyRow(label, elapsed, _overlay?.Hint ?? (_top >= 0 ? ScrolledHint(RowsBelowLocked()) : ""), queued);
            int restMax = leftMax - TextCells.Width(prefix) - TextCells.Width(frame);
            string rest = Fit(unfitted, restMax);
            string left = prefix + frame + rest;
            string tail = right.Length == 0 ? "" : new string(' ', max - TextCells.Width(left) - TextCells.Width(right)) + right;
            _inner.Write(new RawText(prefix, Theme.Hint));
            _inner.Write(new RawText(frame, Theme.SpinnerStyle));
            WriteTrailed(rest + tail, mark);
            _shownHint = left + tail;
            // The queued part's place: after the prefix, the frame, the blank, the label and a separator — when the fit left it whole.
            RecordQueued(queued, TextCells.Width(prefix) + TextCells.Width(frame), TextCells.Width(" " + BusyText(label, elapsed) + HintSeparator), unfitted, restMax);
            // The usage zone: the frame and the label after the prefix — when the fit kept the label
            // whole, and not while scrolled (the row is the scroll's then, like the standing one).
            RecordUsage(_top < 0 ? frame + " " + BusyText(label, elapsed) : "", TextCells.Width(prefix), 0, frame + unfitted, restMax + TextCells.Width(frame));
        }
        else
        {
            string right = Trail(_trailer(), mark, max);
            string row = StandingRow();
            string hint = PinRight(row, _trailer(), mark, max);
            WriteTrailed(hint, mark);
            _shownHint = hint;
            _hintStrip = _strip();
            _trailerColumn = right.Length == 0 ? -1 : max - TextCells.Width(right);
            // The queued part's place: after the strip and its separator — when the fit left it whole.
            int cells = right.Length == 0 ? max : max - TextCells.Width(right) - TrailerGap;
            RecordQueued(StandingQueued(), 0, _hintStrip.Length == 0 ? 0 : TextCells.Width(_hintStrip) + HintSeparator.Length, row, cells);
            // The usage zone: the tally where the screen's hint put it — nowhere under an overlay's
            // or the scroll's hint, or when the timers or the exit hint stand in its place.
            string usage = _overlay is null && _top < 0 ? _usage() : "";
            int at = usage.Length == 0 ? -1 : row.IndexOf(usage, StringComparison.Ordinal);
            RecordUsage(at < 0 ? "" : usage, 0, at < 0 ? 0 : TextCells.Width(row[..at]), row, cells);
        }

        if (_busyLabel is not null)
        {
            _hintStrip = "";
            _trailerColumn = -1;
        }

        // Either row carries the scroll's hint while scrolled (an overlay's hint wins on both).
        _hintScrolled = _overlay is null && _top >= 0;
        _inner.Write(EraseLineEnd);
    }

    /// <summary>
    /// Remembers where the queued part landed for the hit tests: <paramref name="ahead"/> cells into
    /// the row after <paramref name="offset"/> (what was written before the fitted text), or nowhere
    /// when there is none or <see cref="Fit"/> cut <paramref name="text"/> (of which it is a part) at
    /// <paramref name="cells"/> before its end — a cut part is no button.
    /// </summary>
    private void RecordQueued(string queued, int offset, int ahead, string text, int cells)
    {
        int width = TextCells.Width(queued);
        bool whole = width > 0 && (TextCells.Width(text) <= cells || ahead + width < cells);
        _queuedColumn = whole ? offset + ahead : -1;
        _queuedCells = whole ? width : 0;
    }

    /// <summary><see cref="RecordQueued"/> for the usage zone (2026-09-21): <paramref name="usage"/> is the tally or the spinner with its label.</summary>
    private void RecordUsage(string usage, int offset, int ahead, string text, int cells)
    {
        int width = TextCells.Width(usage);
        bool whole = width > 0 && (TextCells.Width(text) <= cells || ahead + width < cells);
        _usageColumn = whole ? offset + ahead : -1;
        _usageCells = whole ? width : 0;
    }

    /// <summary>
    /// <paramref name="text"/> in the hint style, its trailing <paramref name="mark"/> — the row
    /// ends with the mark whenever there is one (<see cref="Trail"/>) — in the mark's own.
    /// </summary>
    private void WriteTrailed(string text, string mark)
    {
        if (mark.Length == 0 || !text.EndsWith(mark, StringComparison.Ordinal))
        {
            _inner.Write(new RawText(text, Theme.Hint));
            return;
        }

        _inner.Write(new RawText(text[..^mark.Length], Theme.Hint));
        _inner.Write(new RawText(mark, Theme.TrailerMark));
    }

    /// <summary>The overlay's hint while one is open, else the scroll's while scrolled (<see cref="ScrolledHint"/>), else the screen's.</summary>
    private string StandingHint() => _overlay is { } overlay ? overlay.Hint : _top >= 0 ? ScrolledHint(RowsBelowLocked()) : _hint();

    /// <summary>The queued part while the row is the screen's own — nothing under an overlay's hint or the scroll's (2026-09-18).</summary>
    private string StandingQueued() => _overlay is null && _top < 0 ? _queued() : "";

    /// <summary>The standing hint behind the strip and the queued part (<see cref="HintRow"/>: each alone when the others are empty).</summary>
    private string StandingRow() => HintRow(HintRow(_strip(), StandingQueued()), StandingHint());

    /// <summary>
    /// The hint row again, in place: down from the cursor's row over the rows under it and the
    /// lower rule, and back — to the cell on the input row when one is shown, else to the overlay's
    /// first row with the cursor left hidden (a menu lists voices under its own spinner).
    /// </summary>
    private void RedrawHint()
    {
        if (!_drawn || _batch > 0 || _modal > 0)
        {
            return;
        }

        int down = _paneRows - 2 - CursorDepth;
        _inner.Cursor.Show(false);
        _inner.Cursor.Move(CursorDirection.Down, down);
        ColumnZero();
        WriteHintRow();
        _inner.Cursor.Move(CursorDirection.Up, down);
        ColumnZero();
        if (_drawnOverlay && !_drawnInput)
        {
            return;
        }

        _inner.Cursor.Move(CursorDirection.Right, TextCells.Width(InputLine.PromptGlyph) + _cursorCell);
        _inner.Cursor.Show(true);
    }

    private void OnTick()
    {
        lock (_gate)
        {
            if (_disposed || !_drawn || _batch > 0 || _modal > 0)
            {
                return;
            }

            int w = Width;
            int h = Height;
            if (w != _lastWidth || h != _lastHeight)
            {
                // The alternate buffer is not reflowed: the screen is rebuilt from the store — the
                // window again, or the flow's tail — from the top row (a move past it stops there).
                DiagnosticLog.Debug(Category, ResizedLogLine(_lastWidth, _lastHeight, w, h));
                BeginSync();
                Lift();
                _inner.Cursor.Move(CursorDirection.Up, Math.Max(h, _lastHeight));
                ColumnZero();
                _inner.Write(EraseDown);
                _blank = true;
                _row = 0;
                _col = 0;
                _lineFull = false;
                Draw();
                EndSync();
                return;
            }

            if (_liveDirty)
            {
                if (_busyLabel is not null)
                {
                    _frame++;
                }

                if (_drawnScrolled)
                {
                    // Scrolled: the block is not drawn; its rows count in the hint.
                    _liveCount = _live is null ? 0 : Math.Max(0, RenderLines(_live, _inner, w).Count - _liveCommitted);
                    _liveDirty = false;
                    RedrawHint();
                    return;
                }

                // The reply grew since the last layout: the whole pane again, the spinner's frame with it.
                BeginSync();
                Lift();
                Draw();
                EndSync();
                return;
            }

            if (RuleTitleChanged())
            {
                // The session's name landed or went (the model's title arrives off-thread): the
                // whole pane again, as a grown reply gets — the rule is drawn in every state.
                if (_busyLabel is not null)
                {
                    _frame++;
                }

                BeginSync();
                Lift();
                Draw();
                EndSync();
                return;
            }

            if (_busyLabel is not null)
            {
                _frame++;
                RedrawHint();
                return;
            }

            if (HintChanged())
            {
                RedrawHint();
            }
        }
    }

    /// <summary>The standing hint (the overlay's while one is open) with the trailer is not what the hint row shows.</summary>
    private bool HintChanged() => !string.Equals(PinRight(StandingRow(), _trailer(), _trailerMark(), Math.Max(1, Width - 1)), _shownHint, StringComparison.Ordinal);

    private void ColumnZero() => _inner.Cursor.Move(CursorDirection.Left, Width);

    private void BeginSync() => _inner.Write(SyncBegin);

    private void EndSync() => _inner.Write(SyncEnd);

    /// <summary>At most <paramref name="cells"/> cells of <paramref name="text"/>, an ellipsis when cut. Pinned.</summary>
    public static string Fit(string text, int cells)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cells <= 0)
        {
            return "";
        }

        if (TextCells.Width(text) <= cells)
        {
            return text;
        }

        if (cells == 1)
        {
            return "…";
        }

        int end = 0;
        int used = 0;
        while (end < text.Length)
        {
            int w = TextCells.ElementWidth(text, end, out int length);
            if (used + w > cells - 1)
            {
                break;
            }

            used += w;
            end += Math.Max(1, length);
        }

        return text[..end] + "…";
    }

    /// <summary>
    /// <see cref="Fit"/>'s mirror: at most <paramref name="cells"/> cells of the END of
    /// <paramref name="text"/>, an ellipsis ahead of it when cut — a path keeps its innermost
    /// folders (the banner's working directory, 2026-09-18). Pinned.
    /// </summary>
    public static string FitTail(string text, int cells)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cells <= 0)
        {
            return "";
        }

        if (TextCells.Width(text) <= cells)
        {
            return text;
        }

        if (cells == 1)
        {
            return "…";
        }

        int start = text.Length;
        int used = 0;
        while (start > 0)
        {
            int length = TextCells.ElementLengthBefore(text, start);
            int w = TextCells.ElementWidth(text, start - length, out _);
            if (used + w > cells - 1)
            {
                break;
            }

            used += w;
            start -= length;
        }

        return "…" + text[start..];
    }

    /// <summary>Pre-rendered segments written as they are.</summary>
    private sealed class SegmentList : IRenderable
    {
        private readonly List<Segment> _segments;

        public SegmentList(List<Segment> segments)
        {
            _segments = segments;
        }

        public Measurement Measure(RenderOptions options, int maxWidth) => new(0, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => _segments;
    }

    /// <summary>A batch's end (<see cref="Batch"/>), once.</summary>
    private sealed class Scope : IDisposable
    {
        public static readonly Scope None = new(null);

        private Action? _onDispose;

        public Scope(Action? onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }
}
