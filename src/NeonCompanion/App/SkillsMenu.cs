using NeonCompanion.Sessions;
using NeonCompanion.Settings;
using NeonCompanion.Skills;
using NeonCompanion.UI;
using Spectre.Console;

namespace NeonCompanion.App;

/// <summary>
/// The <c>/skills</c> screen (2026-09-18, the user's ask; the plural since 2026-09-19): the four tabs in <see cref="SkillsText"/>'s
/// words — Offered, Options, Reflection, Project (the Roots tab after them until later on 2026-09-19, the user's call; the
/// scope page names each root's folder) — as one tabbed <see cref="MenuPane"/> page, so a long catalog
/// scrolls behind the menu's viewport and every skill row is a cursor stop. The Options tab (2026-09-19,
/// the user's ask) and the Reflection tab (later that day) are the settings rows that were <c>/settings</c>' Skills tab
/// (<see cref="SettingsMenu.SkillsTabFields"/>), edited through <see cref="SettingsMenu"/>'s own seams
/// (<see cref="SettingsMenu.FieldsTab"/>, <see cref="SettingsMenu.EditAsync"/>) under this pane's strip, its
/// pickers titled <c>Skills › …</c> (<see cref="SettingsMenu.Root"/>) like the scope page — the <see cref="ToolsMenu"/>
/// shape; none of its rows reconnects or is <see cref="SettingsMenu.RefusedMidTurn"/>, so they edit mid-turn too,
/// and the facts are read again after an edit (the skills switch empties the Offered tab). The Project tab's one row
/// (later on 2026-09-19, the user's ask) is the <c>Project file</c> toggle in the <see cref="ToolsMenu"/> Offered tab's
/// shape: Enter or Space flips it in place (<c>AppSettingsData.ProjectFile</c>, <see cref="SkillsText.ProjectFlippedNotice"/>
/// on the status line, the facts read again, the cursor kept), mid-turn too — the next turn reads it. Enter or a double-click on
/// a skill's row (a loaded one, or a shadowed one — the duplicate is the thing to clean up) opens the
/// scope page under the list: <c>profile</c>, <c>global</c> and, while <c>Allow skill delete</c> is
/// on, <c>delete</c>, the cursor on the scope it is in. Picking the other root moves the folder
/// (<see cref="SkillEditor.Move"/>) after a yes/no confirmation kept under the list; picking
/// <c>delete</c> removes it (<see cref="SkillEditor.Delete"/>) after one; the scope it is in already
/// is <see cref="SettingsMenu.UnchangedNotice"/>. A destination that already holds the folder's name
/// is refused ahead of the confirmation (<see cref="ExistsError"/>), an external skill is read-only
/// (<see cref="ExternalReadOnlyNotice"/>), and mid-turn — the pane opens on the watcher task while a
/// reply runs — the pick is refused (<see cref="SettingsMenu.NotWhileReplyRunsNotice"/>: a move under
/// a running turn could race the model's <c>load_skill</c>). After a change the facts are read again
/// (a rescan) and the list re-shown with the notice on the status line. Enter on any other row — a
/// heading, a warning, a skipped folder — does nothing, and so does Space on the Offered tab. The
/// <see cref="QueueMenu"/> shape: no prompt fallback, a console without the pane gets
/// <see cref="Lines"/> in the transcript — the four tabs as headed sections.
/// </summary>
internal sealed class SkillsMenu
{
    // The key hints. Pinned.
    public const string LoadedKeys = "Enter = move or delete · ←/→ tabs · ESC = close";
    public const string OtherKeys = "←/→ tabs · ESC = close";
    public const string ScopeKeys = SettingsMenu.PickKeys;

    /// <summary>The scope page's last row while <c>Allow skill delete</c> is on. Pinned.</summary>
    public const string DeleteWord = "delete";

    /// <summary>What a declined confirmation says on the status line: the transcript's word.</summary>
    public const string KeptNotice = ChatScreen.KeptNotice;

    public const string ExternalReadOnlyNotice = "(external skills are read only here; move the folder by hand)";

    /// <summary>The scope page's two root rows, in the roots' precedence order; <see cref="DeleteWord"/> after them when offered.</summary>
    public static readonly IReadOnlyList<SkillScope> ScopeRows = [SkillScope.Profile, SkillScope.Global];

    private readonly Func<SkillsFacts> _facts;
    private readonly Func<bool> _allowDelete;
    private readonly AppSettings _settings;
    private readonly SettingsMenu _menu;
    private readonly INoticeSink _transcript;
    private readonly MenuPane _pane;
    private readonly Func<string, string?> _usage;

    /// <param name="facts">The catalog as of a fresh scan and the rest the tabs show; read when the list opens and again after every change.</param>
    /// <param name="allowDelete">The <c>Allow skill delete</c> setting, read when the scope page opens.</param>
    /// <param name="settings">The store the Options tab's rows show and save to.</param>
    /// <param name="menu">The settings menu whose rows the Options tab is.</param>
    /// <param name="transcript">Where the lines outside the pane go: the screen's deferring sink, since the list may open while a reply runs.</param>
    /// <param name="pane">The menu host in the bottom pane.</param>
    /// <param name="usage">The scope page's caption for a skill by name (<see cref="UsageCaption"/>; the session store's usage line, 2026-09-19), null for none — read when the page opens; tests pass nothing.</param>
    public SkillsMenu(Func<SkillsFacts> facts, Func<bool> allowDelete, AppSettings settings, SettingsMenu menu, INoticeSink transcript, MenuPane pane, Func<string, string?>? usage = null)
    {
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _allowDelete = allowDelete ?? throw new ArgumentNullException(nameof(allowDelete));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _menu = menu ?? throw new ArgumentNullException(nameof(menu));
        _transcript = transcript ?? throw new ArgumentNullException(nameof(transcript));
        _pane = pane ?? throw new ArgumentNullException(nameof(pane));
        _usage = usage ?? (_ => null);
    }

    /// <summary>
    /// The scope page's caption (2026-09-19): how the stored sessions used the skill, in the store's
    /// words (<see cref="SkillText.UsageLine"/>); null while <c>Session logging</c> is off, so the
    /// page shows none. Read on open, three light queries.
    /// </summary>
    public static string? UsageCaption(SessionStore store, string name, bool logging, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(zone);
        if (!logging)
        {
            return null;
        }

        var usage = store.SkillUsageOf(name);
        var mark = store.LastReflectionOf(name);
        int writes = mark is null ? 0 : store.ReflectionWrites(name);
        return SkillText.UsageLine(usage, mark, writes, zone);
    }

    /// <summary>Where a notice goes: the pane's status line while the list is open there, else the transcript.</summary>
    private INoticeSink Sink => _pane.IsOpen ? _pane : _transcript;

    // ── Pinned statics ──────────────────────────────────────────────────────

    /// <summary>The scope page's title: <c>Skills › haiku</c>.</summary>
    public static string ScopeTitle(string name) => SkillsText.Label + " › " + name;

    /// <summary>A scope row: the name padded to nine, the root's folder dim after it.</summary>
    public static string ScopeRow(SkillScope scope, SkillRoots roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return Markup.Escape(SkillScopes.Name(scope).PadRight(9)) + Theme.DimMarkup(roots.Of(scope));
    }

    /// <summary>The delete row: the word padded to nine, what it does dim after it.</summary>
    public static string DeleteRow => Markup.Escape(DeleteWord.PadRight(9)) + Theme.DimMarkup("remove the folder and everything in it");

    public static string MovePrompt(string name, SkillScope from, SkillScope to) => $"Move skill '{name}' from the {SkillScopes.Name(from)} skills to the {SkillScopes.Name(to)} skills?";

    public static string MovedNotice(string name, SkillScope to) => $"(moved: {name} → {SkillScopes.Name(to)} skills)";

    /// <summary>The destination root already holds the folder — by the folder's name, which a skill's name may differ from.</summary>
    public static string ExistsError(string name, string folder, SkillScope to) => $"Could not move skill '{name}': the {SkillScopes.Name(to)} skills already hold '{folder}'";

    public static string MoveFailedError(string detail) => $"Could not move the skill: {detail}";

    public static string DeletePrompt(string name, SkillScope scope) => $"Delete skill '{name}' from the {SkillScopes.Name(scope)} skills, folder and all?";

    public static string DeletedNotice(string name, SkillScope scope) => $"(deleted: {name} from the {SkillScopes.Name(scope)} skills)";

    public static string DeleteFailedError(string detail) => $"Could not delete the skill: {detail}";

    /// <summary>The folder went between the scan and the act (or a hand-built record points elsewhere): the list is read again.</summary>
    public static string MissingError(string name) => $"Could not find skill '{name}' on disk any more; the list was read again";

    /// <summary>The Options tab's index in the strip: the skill settings, second after Offered (2026-09-19).</summary>
    public const int OptionsTab = 1;

    /// <summary>The Reflection tab's index in the strip: the reflection's rows, third, between Options and Project (later on 2026-09-19, the user's ask).</summary>
    public const int ReflectionTab = 2;

    /// <summary>The Project tab's index in the strip: the <c>Project file</c> toggle, fourth and last (the Roots tab after it until later on 2026-09-19).</summary>
    public const int ProjectTab = 3;

    /// <summary>The settings tabs' titles in strip order, indexed like <see cref="SettingsMenu.SkillsTabFields"/> (the tab one down).</summary>
    public static readonly IReadOnlyList<string> SettingsTabTitles = [SkillsText.OptionsTabTitle, SkillsText.ReflectionTabTitle];

    /// <summary>The tabbed page: the Offered rows first, the Options and Reflection rows (<see cref="SettingsMenu.FieldsTab"/> under <see cref="SettingsMenu.TabKeys"/>) second and third, the Project tab last with its cursor on the toggle (past the off line), Space a flip there (<see cref="MenuPage.SpaceToggles"/> — page-wide, so the host ignores it elsewhere).</summary>
    public static MenuPage Page(SkillsFacts facts, IReadOnlyList<(string Markup, Skill? Skill)> loaded, AppSettingsData saved, SettingsMenu menu, int tab)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(menu);
        var tabs = new MenuTab[]
        {
            new(SkillsText.OfferedTabTitle, loaded.Select(r => r.Markup).ToList()) { Hint = LoadedKeys },
            menu.FieldsTab(SkillsText.OptionsTabTitle, SettingsMenu.SkillsTabFields[OptionsTab - 1], saved) with { Hint = SettingsMenu.TabKeys },
            menu.FieldsTab(SkillsText.ReflectionTabTitle, SettingsMenu.SkillsTabFields[ReflectionTab - 1], saved) with { Hint = SettingsMenu.TabKeys },
            new(SkillsText.ProjectTabTitle, SkillsText.ProjectRowsMarkup(facts)) { Hint = SkillsText.ProjectKeys },
        };
        return MenuPage.Tabbed(SkillsText.Label, tabs, tab, OtherKeys) with { SpaceToggles = true, TabCursors = [0, 0, 0, SkillsText.ProjectRowIndex(facts)] };
    }

    /// <summary>The four tabs as plain lines, for a console without the pane: <see cref="SkillsText.Lines"/> with the Options and Reflection rows (<see cref="SettingsMenu.PlainRow"/>) headed and indented after the Offered section.</summary>
    public static IEnumerable<string> Lines(SkillsFacts facts, AppSettingsData saved, SettingsMenu menu)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(menu);
        foreach (var line in SkillsText.Lines(facts))
        {
            if (line == SkillsText.ProjectTabTitle)
            {
                for (int t = 0; t < SettingsTabTitles.Count; t++)
                {
                    yield return SettingsTabTitles[t];
                    foreach (var field in SettingsMenu.SkillsTabFields[t])
                    {
                        yield return "  " + menu.PlainRow(field, saved);
                    }
                }
            }

            yield return line;
        }
    }

    // ── Screen ──────────────────────────────────────────────────────────────

    /// <param name="midTurn">The pane opened while a reply runs: the list shows, a scope pick is refused; the Options rows edit as on <c>/settings</c> (none is refused there) and the Project toggle flips (read at the next turn).</param>
    public async Task ShowAsync(CancellationToken cancellationToken, bool midTurn = false)
    {
        var facts = _facts();
        if (!_pane.Enabled)
        {
            foreach (var line in Lines(facts, _settings.Current, _menu))
            {
                _transcript.Notice(line);
            }

            return;
        }

        int tab = 0;
        int cursor = 0;
        _menu.Root = SkillsText.Label;
        try
        {
            while (true)
            {
                var loaded = SkillsText.LoadedRows(facts);
                var saved = _settings.Current;
                var page = Page(facts, loaded, saved, _menu, tab);
                var picked = await _pane.PickAsync(page, cursor, cancellationToken).ConfigureAwait(false);
                if (picked is not { } pick)
                {
                    return;
                }

                tab = pick.Tab;
                cursor = pick.Row;
                if (tab == ProjectTab)
                {
                    // The Project file toggle (later on 2026-09-19): flipped in place, the /tools Offered tab's shape.
                    if (cursor == SkillsText.ProjectRowIndex(facts))
                    {
                        bool on = !facts.ProjectFile;
                        _settings.Update(d => d.ProjectFile = on);
                        Sink.Notice(SkillsText.ProjectFlippedNotice(on));
                        facts = _facts();
                    }

                    continue;
                }

                if (pick.Toggle)
                {
                    continue;   // Space elsewhere: nothing, as on /settings and /tools
                }

                if (tab is OptionsTab or ReflectionTab)
                {
                    var fields = SettingsMenu.SkillsTabFields[tab - 1];
                    if (cursor >= fields.Count)
                    {
                        continue;
                    }

                    var field = fields[cursor];
                    if (midTurn && SettingsMenu.RefusedMidTurn(field))
                    {
                        Sink.Notice(SettingsMenu.NotWhileReplyRunsNotice);
                        continue;
                    }

                    // The page on the tab the pane ended on, so a typed edit under it keeps that tab's rows (ToolsMenu's shape).
                    var shown = pick.Tab == page.Tab ? page : MenuPage.Tabbed(SkillsText.Label, page.Tabs!, pick.Tab, OtherKeys);
                    if (await _menu.EditAsync(field, saved, shown, cursor, cancellationToken).ConfigureAwait(false))
                    {
                        facts = _facts();   // the skills switch empties the Offered tab
                    }

                    continue;
                }

                if (tab != 0 || cursor >= loaded.Count || loaded[cursor].Skill is not { } skill)
                {
                    continue;
                }

                if (midTurn)
                {
                    Sink.Notice(SettingsMenu.NotWhileReplyRunsNotice);
                    continue;
                }

                if (skill.Scope == SkillScope.External)
                {
                    Sink.Notice(ExternalReadOnlyNotice);
                    continue;
                }

                if (await PickScopeAsync(skill, facts.Roots, cancellationToken).ConfigureAwait(false))
                {
                    facts = _facts();
                    cursor = Math.Max(0, Math.Min(cursor, SkillsText.LoadedRows(facts).Count - 1));
                }
            }
        }
        finally
        {
            _menu.Root = SettingsMenu.Title;
            _pane.Close();
        }
    }

    /// <summary>The scope page under the list, the confirmation under that, then the act; true when the folder changed (the facts are stale).</summary>
    private async Task<bool> PickScopeAsync(Skill skill, SkillRoots roots, CancellationToken cancellationToken)
    {
        bool delete = _allowDelete();
        var rows = ScopeRows.Select(scope => ScopeRow(scope, roots)).ToList();
        if (delete)
        {
            rows.Add(DeleteRow);
        }

        var page = new MenuPage(ScopeTitle(skill.Name), rows, ScopeKeys) { Caption = _usage(skill.Name) };
        var picked = await _pane.PickAsync(page, IndexOf(ScopeRows, skill.Scope), cancellationToken).ConfigureAwait(false);
        if (picked is not { Row: var row })
        {
            return false;
        }

        if (row >= ScopeRows.Count)
        {
            if (!await ConfirmAsync(DeletePrompt(skill.Name, skill.Scope), cancellationToken).ConfigureAwait(false))
            {
                Sink.Notice(KeptNotice);
                return false;
            }

            var deleted = SkillEditor.Delete(roots, skill);
            switch (deleted.Outcome)
            {
                case SkillEditOutcome.Deleted:
                    Sink.Notice(DeletedNotice(skill.Name, skill.Scope));
                    return true;
                case SkillEditOutcome.Missing:
                    Sink.Error(MissingError(skill.Name));
                    return true;
                default:
                    Sink.Error(DeleteFailedError(deleted.Detail));
                    return false;
            }
        }

        var to = ScopeRows[row];
        if (to == skill.Scope)
        {
            Sink.Notice(SettingsMenu.UnchangedNotice);
            return false;
        }

        // Refused ahead of the question: nothing to confirm when the destination holds the name already.
        string destination = Path.Combine(roots.Of(to), skill.FolderName);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            Sink.Error(ExistsError(skill.Name, skill.FolderName, to));
            return false;
        }

        if (!await ConfirmAsync(MovePrompt(skill.Name, skill.Scope, to), cancellationToken).ConfigureAwait(false))
        {
            Sink.Notice(KeptNotice);
            return false;
        }

        var moved = SkillEditor.Move(roots, skill, to);
        switch (moved.Outcome)
        {
            case SkillEditOutcome.Moved:
                Sink.Notice(MovedNotice(skill.Name, to));
                return true;
            case SkillEditOutcome.Exists:
                Sink.Error(ExistsError(skill.Name, skill.FolderName, to));
                return true;
            case SkillEditOutcome.Missing:
                Sink.Error(MissingError(skill.Name));
                return true;
            default:
                Sink.Error(MoveFailedError(moved.Detail));
                return false;
        }
    }

    private static int IndexOf(IReadOnlyList<SkillScope> scopes, SkillScope scope)
    {
        for (int i = 0; i < scopes.Count; i++)
        {
            if (scopes[i] == scope)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>The yes/no page kept under the list (<see cref="SettingsMenu.ConfirmAsync"/>'s rows, keys and hotkeys, the cursor on No): true for Enter on Yes alone.</summary>
    private async Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken)
    {
        var page = new MenuPage(question, SettingsMenu.ConfirmRows, SettingsMenu.ConfirmKeys) { Hotkeys = SettingsMenu.ConfirmHotkeys };
        var picked = await _pane.PickAsync(page, 0, cancellationToken).ConfigureAwait(false);
        return picked is { Row: 1 };
    }
}
