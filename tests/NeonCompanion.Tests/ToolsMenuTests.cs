using Microsoft.Extensions.AI;
using NeonCompanion.App;
using NeonCompanion.Llm.Tools;
using NeonCompanion.Memory;
using NeonCompanion.Settings;
using NeonCompanion.Speech;
using NeonCompanion.Tests.Fakes;
using NeonCompanion.Timers;
using NeonCompanion.UI;
using NeonCompanion.Web;
using Spectre.Console;
using Spectre.Console.Testing;

namespace NeonCompanion.Tests;

/// <summary>
/// The <c>/tools</c> pane (2026-09-19): the Offered tab's flips and the three settings tabs that moved
/// off <c>/settings</c> that day — the tab-position and row tests came from <c>SettingsMenuTests</c> with them.
/// </summary>
public class ToolsMenuTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NeonCompanion.Tests", Guid.NewGuid().ToString("N"));
    private readonly TestConsole _console = new TestConsole().Interactive();
    private readonly AppSettings _settings;
    private readonly ManualTimeProvider _time = new();
    private readonly FakeSynthesizer _synth = new();
    private readonly SpeechSession _speech;
    private readonly IReadOnlyList<AIFunction> _clock;
    private readonly IReadOnlyList<AIFunction> _timers;
    private readonly IReadOnlyList<AIFunction> _files;
    private readonly IReadOnlyList<AIFunction> _web;
    private readonly IReadOnlyList<AIFunction> _memory;
    private readonly IReadOnlyList<AIFunction> _questions;
    private bool _paneOn = true;

    public ToolsMenuTests()
    {
        _console.Profile.Width = 100;
        _settings = new AppSettings(_dir);
        _settings.Update(d => { d.TtsOutput = true; d.TtsSource = "http"; d.ToolsDisabled = []; d.GitNativeTools = true; });   // delete off by default (2026-09-20), Git native tools off by default (2026-09-21): the Offered-tab scripts start from every tool on
        _speech = new SpeechSession(_ => _synth, _ => new FakeAudioPlayback(), new ModelStore(Path.Combine(_dir, "models"), new HttpClient(new StubHttpMessageHandler())));
        var root = Path.Combine(_dir, "files");
        Directory.CreateDirectory(root);
        var files = new NeonCompanion.Files.WorkingDirectory(() => root, _time);
        _clock = ChatScreen.ClockTools(_time);
        _timers = ChatScreen.TimerTools(new TimerBoard(_time, () => { }));
        _files = ChatScreen.FileTools(files, () => true, _ => { }, () => _settings.Current);
        _web = ChatScreen.WebTools(new WebAccess(new HttpClient(new StubHttpMessageHandler()), new FakeHeadlessBrowser(), _time), files, () => _settings.Current);
        _memory = ChatScreen.MemoryTools(new MemoryStore(Path.Combine(_dir, "memory"), _time));
        _questions = ChatScreen.AskTools((_, _) => Task.FromResult<IReadOnlyList<AskAnswer>?>(null), () => _settings.Current);
    }

    /// <summary>What the fixture's browser auto-detection "finds", so the empty <c>Browser path</c> row reads the same on every machine.</summary>
    private const string FakeBrowserPath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

    public void Dispose()
    {
        _speech.Dispose();
        _settings.Dispose();
        _console.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>The facts as the screen reads them: the six groups over the live settings (no skills or sessions here — the screen's own list is pinned by ToolsTextTests).</summary>
    private ToolsFacts Facts()
    {
        var e = _settings.Current;
        var disabled = ToolsText.DisabledSet(e.ToolsDisabled);
        return new ToolsFacts(SystemPromptSummary.ToolGroups(_clock, _timers, _files, _memory, e.Memory, e.LlmOfferTools, _web, e.WebTools, e.FileTools, _questions, e.AskUser, _paneOn, disabled: disabled), e.LlmOfferTools, disabled);
    }

    private void Push(params ConsoleKeyInfo[] keys)
    {
        foreach (var key in keys)
        {
            _console.Input.PushKey(key);
        }
    }

    private void Down(int times)
    {
        for (int i = 0; i < times; i++)
        {
            Push(Keys.Down);
        }
    }

    /// <summary>The menu over a pane with geometry: every list is a level of the pane, the notices its status line.</summary>
    private (ToolsMenu Menu, ScreenPane Pane, SettingsMenu Settings) PaneMenu(Func<string, CancellationToken, Task<string?>>? browseVault = null)
    {
        _console.Profile.Height = 40;
        var pane = new ScreenPane(_console, new ScreenGeometry(() => null), new ManualTimeProvider()) { Hint = () => "idle" };
        var keys = new KeySource(_console.Input, TimeSpan.FromMilliseconds(1));
        var menuPane = new MenuPane(pane, keys);
        var settings = new SettingsMenu(new ConsoleWithInput(pane, keys), _settings, _ => null, new InputLine(pane, keys), new TranscriptRenderer(pane), _speech, menuPane, _ => FakeBrowserPath, browseVault: browseVault);
        var menu = new ToolsMenu(Facts, _settings, settings, new TranscriptRenderer(pane), menuPane);
        pane.Show();
        return (menu, pane, settings);
    }

    /// <summary><see cref="PaneMenu"/> over a scripted source that carries clicks, the overlay's first row at buffer row <paramref name="cursorTop"/> (the strip; the spacer under it, the rows from +2).</summary>
    private (ToolsMenu Menu, ScreenPane Pane, ScriptedInput Input) ClickablePaneMenu(int cursorTop)
    {
        _console.Profile.Height = 40;
        var pane = new ScreenPane(_console, new ScreenGeometry(() => null, () => cursorTop), new ManualTimeProvider()) { Hint = () => "idle" };
        var input = new ScriptedInput();
        var keys = new KeySource(input, TimeSpan.FromMilliseconds(1));
        var menuPane = new MenuPane(pane, keys);
        var settings = new SettingsMenu(new ConsoleWithInput(pane, keys), _settings, _ => null, new InputLine(pane, keys), new TranscriptRenderer(pane), _speech, menuPane, _ => FakeBrowserPath);
        var menu = new ToolsMenu(Facts, _settings, settings, new TranscriptRenderer(pane), menuPane);
        pane.Show();
        return (menu, pane, input);
    }

    private static string Rule(int width) => new(ScreenPane.RuleGlyph, width);

    /// <summary>A title or strip row as the pane prints it: the text, then the × close glyph in column width − 2.</summary>
    private string Titled(string row) => row + new string(' ', _console.Profile.Width - 2 - TextCells.Width(row)) + ScreenPane.CloseGlyph;

    /// <summary>The strip as the pane prints it: the label, then every tab title with a space either side, two spaces between. Pinned.</summary>
    private const string Strip = ToolsText.Label + "   Offered    Web    Files    Shell    Ask    Git (native)    Obsidian    Options ";   // Obsidian since 2026-09-22, Options last since later that day (second from later on 2026-09-19); the user's order (Web, Files, Shell, Ask, Git (native)) since later on 2026-09-21, alphabetical before

    /// <summary>A tool row as the pane prints it at width 100 (the markup rendered): the name padded to 22, the state to 5, then the description, cut to 99 cells and an ellipsis (FittedMarkup; every description is longer).</summary>
    private string Row(string name, bool on, string mark = "  ") => Fitted(mark + name.PadRight(22) + (on ? "on" : "off").PadRight(5) + Description(name));

    private static string Fitted(string row) => row.Length <= 100 ? row : row[..99] + "…";

    private string Description(string name) => Facts().Groups.SelectMany(g => g.Tools).Single(t => t.Name == name).Description;

    [Fact]
    public void Labels_ArePinned()
    {
        // The three tabs that left /settings (2026-09-19): their rows unchanged, every field on exactly one tab of the three panes (Skills left for /skills later that day);
        // the Options tab ahead of them (later on 2026-09-19): the pane's own $-mention switch.
        Assert.Equal(5, SettingsMenu.TabFields.Count);
        Assert.Equal(7, SettingsMenu.ToolsTabFields.Count);   // Obsidian since 2026-09-22   // Git since 2026-09-20, Shell since 2026-09-21; the user's order (Web, Files, Shell, Ask, Git (native)) since later on 2026-09-21, alphabetical before
        Assert.Equal(["Offered", "Web", "Files", "Shell", "Ask", "Git (native)", "Obsidian", "Options"], ToolsText.TabTitles);
        Assert.Equal([SettingsField.ToolsDollarMention, SettingsField.ToolCollapseCount, SettingsField.CodeCollapseCount], SettingsMenu.ToolsTabFields[6]);   // the fold's count under the switch (2026-09-22, the user's place), the code fold's under it
        Assert.Equal([SettingsField.WebTools, SettingsField.WebBrowserMode, SettingsField.WebBrowserPath, SettingsField.WebBrowserNetworkMode, SettingsField.WebSearchMethod, SettingsField.WebSearxngUrl, SettingsField.WebSearchMaxResults], SettingsMenu.ToolsTabFields[0]);
        Assert.Equal([SettingsField.FileTools, SettingsField.FileSafeEdits, SettingsField.FileTreeMaxLength, SettingsField.FileTreeShowSizes, SettingsField.FileMentionFolderMode, SettingsField.FileBrowserMode, SettingsField.FileViewImageMaxPerCall], SettingsMenu.ToolsTabFields[1]);   // the view_image cap last, 2026-09-19; the browser mode under the folder mode, 2026-09-21
        Assert.Equal([SettingsField.ShellCommandPolicy, SettingsField.ShellCommandAllowed, SettingsField.ShellPoliceOutsidePaths, SettingsField.ShellDefault, SettingsField.ShellTimeoutSeconds, SettingsField.ShellForegroundCapSeconds, SettingsField.ShellOutputMaxChars, SettingsField.ShellCodeLanguages, SettingsField.ShellCodeTimeoutSeconds, SettingsField.ShellToolBridge, SettingsField.ShellCodeMaxToolCalls], SettingsMenu.ToolsTabFields[2]);   // the policy (the switch) first, then the list, the shell, the caps, then execute_code's four (2026-09-21; the bridge switch later that day; the police toggle third, 2026-09-22)
        Assert.Equal([SettingsField.AskUser, SettingsField.AskMaxQuestions, SettingsField.AskMaxChoices], SettingsMenu.ToolsTabFields[3]);
        Assert.Equal([SettingsField.GitNativeTools, SettingsField.GitNativeDiffMaxLines, SettingsField.GitNativeLogMaxCommits, SettingsField.GitNativeEmail, SettingsField.GitNativeName], SettingsMenu.ToolsTabFields[4]);   // the switch first, then the limits, then the identity pair (2026-09-21); the Git native labels later that day
        Assert.Equal([SettingsField.ObsidianTools, SettingsField.ObsidianVault], SettingsMenu.ToolsTabFields[5]);   // the switch, then the vault (2026-09-22)
        Assert.Equal(Enum.GetValues<SettingsField>().Order(), SettingsMenu.TabFields.Concat(SettingsMenu.SkillsTabFields).Concat(SettingsMenu.ToolsTabFields).Concat(SettingsMenu.McpTabFields).SelectMany(t => t).Order());
        Assert.Equal(21, SettingsMenu.LabelWidthOf(SettingsMenu.ToolsTabFields[6]));   // "Tool collapse count" (2026-09-22; "$-mention enabled", 19, before)
        Assert.Equal(26, SettingsMenu.LabelWidthOf(SettingsMenu.ToolsTabFields[0]));   // "Web browser network mode" (the Web-prefixed labels, later still on 2026-09-19; "Web search max results", 24, before)
        Assert.Equal(32, SettingsMenu.LabelWidthOf(SettingsMenu.ToolsTabFields[1]));   // "File view image max (per call)" (later still on 2026-09-19; "Stale line number guard", 25, that morning; "Always return line numbers", 28, before)
        Assert.Equal(29, SettingsMenu.LabelWidthOf(SettingsMenu.ToolsTabFields[2]));   // "Shell tool bridge max calls" (the Shell tab, 2026-09-21; the row was "Shell code max tool calls", 27, until later that day)
        Assert.Equal(30, SettingsMenu.LabelWidthOf(SettingsMenu.ToolsTabFields[3]));   // "Ask max choices per question"
        Assert.Equal(28, SettingsMenu.LabelWidthOf(SettingsMenu.ToolsTabFields[4]));   // "Git native log max commits" (later on 2026-09-21; "Git log max commits", 21, from 2026-09-20)
        Assert.Equal(16, SettingsMenu.LabelWidthOf(SettingsMenu.ToolsTabFields[5]));   // "Obsidian tools" (2026-09-22)
        Assert.All(SettingsMenu.ToolsTabFields.SelectMany(t => t), f => Assert.False(SettingsMenu.RefusedMidTurn(f)));
        Assert.Equal("⚙️ Settings", SettingsMenu.Title);
        Assert.Equal(SettingsMenu.Title + " › Web browser mode", SettingsMenu.Breadcrumb("Web browser mode"));
    }

    [Fact]
    public async Task OnThePane_OpensOnTheOfferedTab_OnTheFirstTool_AndEscClosesIt()
    {
        var (menu, pane, _) = PaneMenu();
        _console.Profile.Height = 30;   // 39 rows since 2026-09-19 (43 with the nineteen file tools) no longer overflow the fixture's 40: shorter, so the scroll hint is still exercised
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        // The strip, the Clock heading, the cursor on get_current_time (the first tool row, past its heading), the hint with the flip keys; nothing reached the transcript.
        Assert.Contains("\n" + Titled(Strip) + "\n \n  Clock (3)\n" + Row(GetCurrentTimeTool.ToolName, true, "▸ ") + "\n" + Row(ShiftDateTool.ToolName, true) + "\n" + Row(DaysBetweenTool.ToolName, true) + "\n  Timers (3)\n", _console.Output);
        Assert.Contains("\n" + ToolsText.OfferedKeys + "\n", _console.Output);
        Assert.Contains("  Files (15)\n" + Row(GetWorkingDirectoryTool.ToolName, true) + "\n", _console.Output);
        Assert.Contains(MenuPane.MoreHint, _console.Output);   // 39 rows over 30: the list scrolls
        Assert.False(pane.OverlayOpen);
        Assert.Equal(0, pane.FlowRow);
        Assert.Empty(_settings.Current.ToolsDisabled);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_EnterOnATool_FlipsItInPlace_SavesAndShowsTheNotice_CursorKept_SpaceFlipsItBack()
    {
        var (menu, pane, _) = PaneMenu();
        Down(1);                                  // shift_date
        Push(Keys.Enter);                         // off
        Push(Keys.Char(' '));                     // on again
        Push(Keys.Enter);                         // off again
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(["shift_date"], _settings.Current.ToolsDisabled);
        // The row reads off where it stands, the cursor on it, the notice on the status line under the strip.
        Assert.Contains("\n" + Titled(Strip) + "\n  · shift_date: off\n  Clock (2 of 3)\n" + Row(GetCurrentTimeTool.ToolName, true) + "\n" + Row(ShiftDateTool.ToolName, false, "▸ ") + "\n", _console.Output);
        Assert.Contains("\n" + Titled(Strip) + "\n  · shift_date: on\n  Clock (3)\n" + Row(GetCurrentTimeTool.ToolName, true) + "\n" + Row(ShiftDateTool.ToolName, true, "▸ ") + "\n", _console.Output);
        Assert.Equal(0, pane.FlowRow);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_EnterOnAHeading_DoesNothing()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Up);                            // the Clock heading
        Push(Keys.Enter, Keys.Char(' '));
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Empty(_settings.Current.ToolsDisabled);
        Assert.DoesNotContain("  · ", _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_AGroupOff_ShowsDimWithTheSwitchNamed_AndStillFlips()
    {
        _settings.Update(d => d.FileTools = false);
        var (menu, pane, _) = PaneMenu();
        Down(8);                                  // past the two other clock rows, the Timers heading and its three rows, the Files heading: get_working_directory
        Push(Keys.Enter);
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal([GetWorkingDirectoryTool.ToolName], _settings.Current.ToolsDisabled);
        Assert.Contains("  Files (15) (off: File tools is off)\n", _console.Output);
        Assert.Contains("  · get_working_directory: off\n", _console.Output);
        Assert.Contains("  Files (14 of 15) (off: File tools is off)\n" + Row(GetWorkingDirectoryTool.ToolName, false, "▸ ") + "\n", _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_LlmToolsOff_OpensWithTheOffLine_AndStillFlips()
    {
        _settings.Update(d => d.LlmOfferTools = false);
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Enter);                         // the cursor opens on the first tool row, past the off line and the heading
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal([GetCurrentTimeTool.ToolName], _settings.Current.ToolsDisabled);
        Assert.Contains("\n" + Titled(Strip) + "\n \n" + Fitted("  " + ToolsText.OffLine) + "\n  Clock (3)\n" + Row(GetCurrentTimeTool.ToolName, true, "▸ ") + "\n", _console.Output);
        Assert.Contains("\n" + Titled(Strip) + "\n  · get_current_time: off\n" + Fitted("  " + ToolsText.OffLine) + "\n  Clock (2 of 3)\n" + Row(GetCurrentTimeTool.ToolName, false, "▸ ") + "\n", _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_TheOptionsTab_IsTheLastTab_ItsToggleSaves()
    {
        // The $-mention switch (later on 2026-09-19): the pane's own row, last on the strip in the /skills shape (second until later on 2026-09-22).
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Left);                                        // the strip wraps: Offered → Options
        Push(Keys.Enter, Keys.Down, Keys.Enter);                // $-mention enabled: the page, off picked
        Push(Keys.Right);                                       // back round to Offered
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.False(_settings.Current.ToolsDollarMention);
        Assert.Contains("\n" + Titled(Strip) + "\n \n▸ $-mention enabled    on\n  Tool collapse count  2 lines\n  Code collapse count  20 lines\n" + Rule(100), _console.Output);
        Assert.Contains(ToolsText.Label + " › $-mention enabled", _console.Output);
        Assert.Contains("$ is ordinary text", _console.Output);
        Assert.Contains("\n" + Titled(Strip) + "\n  · $-mention enabled: off\n▸ $-mention enabled    off\n", _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_TheAskTab_SitsBetweenShellAndGit_ItsToggleSaves()
    {
        // The user's order (later on 2026-09-21): Offered, Web, Files, Shell, Ask, Git (native) — Obsidian and Options after it since 2026-09-22.
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right, Keys.Right, Keys.Right);   // Web, Files, Shell, Ask
        Push(Keys.Enter, Keys.Down, Keys.Enter);                // Ask user: the page, off picked
        Push(Keys.Left, Keys.Left);                             // Shell, Files
        Push(Keys.Left, Keys.Left);                             // Web, Offered
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.False(_settings.Current.AskUser);
        // The three rows padded to the tab's own column (30), the toggle's notice on the status line under the strip.
        Assert.Contains("\n" + Titled(Strip) + "\n \n▸ Ask user                      on\n  Ask max questions             10 questions\n  Ask max choices per question  10 choices\n" + Rule(100), _console.Output);
        Assert.Contains("\n" + Titled(Strip) + "\n  · Ask user: off\n▸ Ask user                      off\n", _console.Output);
        Assert.Contains("\n▸ File tools                      on\n", _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_TheFilesTab_SitsBetweenWebAndShell()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right);                           // Web, Files
        Push(Keys.Enter, Keys.Down, Keys.Enter);                // File tools: the page, off picked
        Push(Keys.Right, Keys.Right, Keys.Right);               // Shell, Ask, Git (native)
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.False(_settings.Current.FileTools);
        // The seven rows (the view_image cap last, 2026-09-19; the @-mention folder mode before it, 2026-09-17, and the browser mode under that, 2026-09-21; Safe edits off by default since 2026-09-19, folder-remain the default since then, Always return line numbers gone later that day and the stale line number guard later still, with edit_lines) padded to the tab's own column (32), the toggle's notice on the status line under the strip.
        Assert.Contains("\n" + Titled(Strip) + "\n \n▸ File tools                      on\n  File safe edits                 off\n  File /tree max length           500 entries\n  File /tree show sizes           on\n  File @-mention folder mode      folder-remain\n  File browser mode               default\n  File view image max (per call)  10 pictures\n" + Rule(100), _console.Output);
        Assert.Contains("\n" + Titled(Strip) + "\n  · File tools: off\n▸ File tools                      off\n", _console.Output);
        Assert.Contains("\n▸ Git native tools            on\n", _console.Output);
        pane.Dispose();
    }

    /// <summary>The Obsidian tab (2026-09-22): before Options, the last but one; the vault is typed here (the fixture has no folder picker), a folder without <c>.obsidian</c> refused, a vault saved.</summary>
    [Fact]
    public async Task OnThePane_TheObsidianTab_SitsBeforeOptions_AndAVaultMustHoldDotObsidian()
    {
        string plain = Path.Combine(_dir, "plain");
        string vault = Path.Combine(_dir, "vault");
        Directory.CreateDirectory(plain);
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        var (menu, _, _) = PaneMenu();
        Push(Keys.Left, Keys.Left, Keys.Down, Keys.Enter);      // Offered → Options → Obsidian, the vault row's typed slot
        Push([.. plain.Select(Keys.Char), Keys.Enter]);         // no .obsidian: refused, kept
        Push(Keys.Enter);
        Push([.. vault.Select(Keys.Char), Keys.Enter]);
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(vault, _settings.Current.ObsidianVault);
        Assert.Contains("Obsidian vault " + SettingsMenu.ObsidianVaultError[..40], _console.Output);   // the status line, cut at the pane's width
        Assert.Contains("\n" + Titled(Strip) + "\n \n▸ Obsidian tools  on\n  Obsidian vault  (not set)\n", _console.Output);
    }

    /// <summary>The vault row's folder picker opens on the vault saved (later on 2026-09-22, the user's report: it opened on the working directory every time): empty the first time, the picked vault the next; nothing picked keeps it.</summary>
    [Fact]
    public async Task OnThePane_TheVaultPicker_OpensOnTheSavedVault()
    {
        string vault = Path.Combine(_dir, "vault");
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        var opened = new List<string>();
        var answers = new Queue<string?>([vault, null]);
        var (menu, pane, _) = PaneMenu((openOn, _) => { opened.Add(openOn); return Task.FromResult(answers.Dequeue()); });
        Push(Keys.Left, Keys.Left, Keys.Down, Keys.Enter);      // Offered → Options → Obsidian, the vault row: the picker, the vault picked
        Push(Keys.Enter);                                       // again: nothing picked
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(["", vault], opened);
        Assert.Equal(vault, _settings.Current.ObsidianVault);
        pane.Dispose();
    }

    /// <summary>The Git (native) tab (2026-09-20; its name since later on 2026-09-21, the last but one since 2026-09-22): the switch first, the two caps typed.</summary>
    [Fact]
    public async Task OnThePane_TheGitTab_SitsBeforeObsidian_ItsCapsAreTyped()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Left, Keys.Left, Keys.Left);                  // the strip wraps: Offered → Options → Obsidian → Git (native), its first row
        Push(Keys.Down, Keys.Enter);                            // Git diff max lines: the typed slot, pre-filled with 500
        Push(Keys.Backspace, Keys.Backspace, Keys.Backspace, Keys.Char('1'), Keys.Char('0'), Keys.Char('0'), Keys.Char('0'), Keys.Enter);
        Push(Keys.Down, Keys.Enter);                            // Git log max commits: the slot, pre-filled with 20; 500 is out of range, kept
        Push(Keys.Backspace, Keys.Backspace, Keys.Char('5'), Keys.Char('0'), Keys.Char('0'), Keys.Enter);
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(1000, _settings.Current.GitNativeDiffMaxLines);
        Assert.Equal(20, _settings.Current.GitNativeLogMaxCommits);
        // The five rows padded to the tab's own column (28), the diff cap's notice then the log cap's refusal on the status line.
        Assert.Contains("\n" + Titled(Strip) + "\n \n▸ Git native tools            on\n  Git native diff max lines   500 lines\n  Git native log max commits  20 commits\n  Git native email            (not set)\n  Git native name             (not set)\n" + Rule(100), _console.Output);
        Assert.Contains("\n  Git native diff max lines   1000 lines\n", _console.Output);
        Assert.Contains("Git native log max commits must be 1 to 200 commits; keeping 20.", _console.Output);
        pane.Dispose();
    }

    /// <summary>The Shell tab (2026-09-21; between Files and Ask since later that day): the policy picker first (its switch), the allowed list, the shell picker, then the three typed rows.</summary>
    [Fact]
    public async Task OnThePane_TheShellTab_SitsBetweenFilesAndAsk_PolicyAndShellArePickers_TheListRemoves()
    {
        _settings.Update(d => d.ShellCommandAllowed = ["git push", "dotnet build"]);
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right, Keys.Right);   // Web, Files, Shell
        Push(Keys.Enter, Keys.Down, Keys.Enter);                            // Shell command policy: the picker opens on ask, yolo picked
        Push(Keys.Down, Keys.Enter, Keys.Enter, Keys.Escape);               // Shell allowed commands: the list, dotnet build removed, back
        Push(Keys.Down, Keys.Down, Keys.Enter, Keys.Down, Keys.Enter);      // past Shell police outside paths (2026-09-22); Shell default: the picker, cmd picked
        Push(Keys.Down, Keys.Enter);                                        // Shell timeout (s): the typed slot, pre-filled with 180; 0 is out of range, kept
        Push(Keys.Backspace, Keys.Backspace, Keys.Backspace, Keys.Char('0'), Keys.Enter);
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal("yolo", _settings.Current.ShellCommandPolicy);
        Assert.Equal(["git push"], _settings.Current.ShellCommandAllowed);
        Assert.Equal("cmd", _settings.Current.ShellDefault);
        Assert.Equal(180, _settings.Current.ShellTimeoutSeconds);
        // The ten rows padded to the tab's own column (27), then the picker's rows, the list's, and the notices on the status line.
        Assert.Contains("\n" + Titled(Strip) + "\n \n▸ Shell command policy         ask\n  Shell allowed commands       2 prefixes\n  Shell police outside paths   on\n  Shell default                powershell\n  Shell timeout (s)            180\n  Shell foreground cap (s)     600\n  Shell output max chars       30,000 chars\n  Shell code languages         powershell, python, node\n  Shell code timeout (s)       300\n  Shell tool bridge            off\n  Shell tool bridge max calls  50 tool calls\n" + Rule(100), _console.Output);
        Assert.Contains("\n" + Titled(ToolsText.Label + " › Shell command policy") + "\n \n  off  no shell or script tool is offered\n▸ ask  you approve each command not on the allow list\n  yolo every command runs, nothing is asked\n", _console.Output);
        Assert.Contains("  · Shell command policy: yolo\n", _console.Output);
        Assert.Contains("\n" + Titled(ToolsText.Label + " › Shell allowed commands") + "\n \n▸ dotnet build\n  git push\n", _console.Output);
        Assert.Contains("  · Shell allowed commands: dotnet build removed\n▸ git push\n", _console.Output);
        Assert.Contains("\n" + Titled(ToolsText.Label + " › Shell default") + "\n \n▸ powershell pwsh when installed, else Windows PowerShell 5.1\n  cmd        cmd.exe: batch syntax\n  bash       Git Bash, when bash.exe is found\n", _console.Output);
        Assert.Contains("  · Shell default: cmd\n", _console.Output);
        Assert.Contains("Shell timeout (s) must be 1 to 3600 seconds; keeping 180.", _console.Output);
        pane.Dispose();
    }

    /// <summary>The tool bridge row (later on 2026-09-21): the Shell tab's one toggle, a picker that opens on the saved off; no reconnect.</summary>
    [Fact]
    public async Task OnThePane_TheToolBridgeRow_IsAPicker_NoReconnect()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right, Keys.Right);   // Shell
        Push(Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Enter);   // Shell tool bridge: the picker opens on off (one more Down since the police row, 2026-09-22)
        Push(Keys.Up, Keys.Enter);                                          // on is the row above
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.True(_settings.Current.ShellToolBridge);
        Assert.Contains("\n" + Titled(ToolsText.Label + " › Shell tool bridge") + "\n \n  on  a script may call this app's other tools through its neon_tools module\n▸ off a script does everything itself: no neon_tools module, no tool calls\n", _console.Output);
        Assert.Contains("  · Shell tool bridge: on", _console.Output);
        Assert.Contains("\n▸ Shell tool bridge            on\n  Shell tool bridge max calls  50 tool calls\n", _console.Output);
        pane.Dispose();
    }

    /// <summary>The outside-paths police row (2026-09-22): the Shell tab's third row, a toggle that opens on the saved on; off is the row below; no reconnect.</summary>
    [Fact]
    public async Task OnThePane_ThePoliceRow_IsAPicker_NoReconnect()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right, Keys.Right);   // Shell
        Push(Keys.Down, Keys.Down, Keys.Enter);                 // Shell police outside paths: the picker opens on on
        Push(Keys.Down, Keys.Enter);                            // off is the row below
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.False(_settings.Current.ShellPoliceOutsidePaths);
        Assert.Contains("\n" + Titled(ToolsText.Label + " › Shell police outside paths") + "\n \n▸ on  a command or script may only name paths under the working directory\n  off paths anywhere on the computer are allowed\n", _console.Output);
        Assert.Contains("  · Shell police outside paths: off", _console.Output);
        Assert.Contains("\n▸ Shell police outside paths   off\n  Shell default                powershell\n", _console.Output);
        pane.Dispose();
    }

    /// <summary>The code-languages list (2026-09-21): Enter or Space flips and saves at once, the last one on refuses to go.</summary>
    [Fact]
    public async Task OnThePane_TheCodeLanguagesRow_IsACheckboxList_TheLastOneStays()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right, Keys.Right);   // Shell
        Push(Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Enter);   // Shell code languages: the list (one more Down since the police row, 2026-09-22)
        Push(Keys.Char(' '));                                               // powershell off
        Push(Keys.Down, Keys.Enter);                                        // python off
        Push(Keys.Down, Keys.Enter);                                        // node: the last one, refused
        Push(Keys.Escape, Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(["node"], _settings.Current.ShellCodeLanguages);
        Assert.Contains("\n" + Titled(ToolsText.Label + " › Shell code languages") + "\n \n▸ [x] powershell a .ps1 through pwsh or Windows PowerShell; Invoke-NeonTool calls a tool\n  [x] python     a .py through python.exe; from neon_tools import …\n  [x] node       a .js through node.exe; require('neon_tools')\n", _console.Output);
        Assert.Contains("  · Shell code languages: python, node\n", _console.Output);
        Assert.Contains("  · Shell code languages: node\n", _console.Output);
        Assert.Contains("At least one language stays on.", _console.Output);
        Assert.Contains("\n▸ Shell code languages         node\n  Shell code timeout (s)       300\n", _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_TheWebTab_SitsAfterOptions()
    {
        // The last tab until later on 2026-09-21 (Left wrapped to it); third since, the user's order.
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right);                          // Web
        Push(Keys.Enter, Keys.Down, Keys.Enter);   // Web tools: the page, off picked
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.False(_settings.Current.WebTools);
        // The seven rows padded to the tab's own column (26), the toggle's notice on the status line under the strip.
        Assert.Contains("\n" + Titled(Strip) + "\n \n▸ Web tools                 on\n  Web browser mode          default\n  Web browser path          (auto: msedge.exe)\n  Web browser network mode  internet\n  Web search method         duckduckgo\n  Web SearXNG URL           (not set)\n  Web search max results    20 results\n" + Rule(100), _console.Output);
        Assert.Contains("\n" + Titled(Strip) + "\n  · Web tools: off\n▸ Web tools                 off\n", _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_APickerFromTheWebTab_IsTitledUnderTools_AndTheRootComesBack()
    {
        var (menu, pane, settings) = PaneMenu();
        Push(Keys.Right);                          // Web
        Push(Keys.Down, Keys.Enter);               // Browser mode: the picker
        Push(Keys.Down, Keys.Enter);               // httpclient (the second name)
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal("httpclient", _settings.Current.WebBrowserMode);
        Assert.Contains("\n" + Titled(ToolsText.Label + " › Web browser mode") + "\n", _console.Output);
        Assert.DoesNotContain(SettingsMenu.Title + " › Web browser mode", _console.Output);
        Assert.Contains("  · Web browser mode: httpclient\n", _console.Output);
        Assert.Equal(SettingsMenu.Title, settings.Root);   // restored for /settings
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_TheSafeEditsRow_IsAPicker_NoReconnect()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right);                                       // Files
        Push(Keys.Down, Keys.Enter, Keys.Up, Keys.Enter);                   // Safe edits: on (the page opens on the saved off; on is the row above)
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.True(_settings.Current.FileSafeEdits);
        Assert.Contains("  · File safe edits: on", _console.Output);
        Assert.Contains(SettingsMenu.ToggleDescribe(SettingsField.FileSafeEdits, true), _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_ATypedRow_EditsUnderTheList_AndEscKeepsTheSavedValue()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right, Keys.Right, Keys.Right);   // Ask
        Push(Keys.Down, Keys.Enter);                // Ask max questions: the typed slot
        Push(Keys.Backspace, Keys.Backspace, Keys.Char('3'), Keys.Enter);
        Push(Keys.Down, Keys.Enter, Keys.Escape);   // Ask max choices: the slot, ESC
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(3, _settings.Current.AskMaxQuestions);
        Assert.Equal(10, _settings.Current.AskMaxChoices);
        Assert.Contains("  · Ask max questions: 3 questions\n", _console.Output);
        Assert.Contains("  · " + SettingsMenu.UnchangedNotice + "\n", _console.Output);
        Assert.Contains(Rule(100) + "\n" + SettingsMenu.EditKeys, _console.Output);   // the typed slot under the list (pre-filled with 10: two Backspaces and a 3 made 3), the edit keys in the hint row
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_ToolCollapseCount_IsTheOptionsTabsSecondRow_Typed_ZeroIsOff_OutOfRangeRefused()
    {
        // 2026-09-22, the user's place: under $-mention enabled; 0 to 100, 2 by default, 0 = off.
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Left);                                          // Options, the strip wrapped
        Push(Keys.Down, Keys.Enter);                              // Tool collapse count: the typed slot with "2"
        Push(Keys.Backspace, Keys.Char('1'), Keys.Char('0'), Keys.Char('1'), Keys.Enter);   // refused: 101
        Push(Keys.Enter, Keys.Backspace, Keys.Char('0'), Keys.Enter);                        // 0: off
        Push(Keys.Enter, Keys.Backspace, Keys.Char('5'), Keys.Enter);                        // 5
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(5, _settings.Current.ToolCollapseCount);
        Assert.Contains("Tool collapse count " + SettingsMenu.ToolCollapseCountRangeError + "; keeping 2.", _console.Output);
        Assert.Contains("  · Tool collapse count: off\n", _console.Output);
        Assert.Contains("  · Tool collapse count: 5 lines\n", _console.Output);
        Assert.Equal("must be 0 to 100 lines (0 = off)", SettingsMenu.ToolCollapseCountRangeError);
        Assert.Equal("Tool collapse count", SettingsMenu.FieldName(SettingsField.ToolCollapseCount));
        Assert.False(SettingsMenu.IsToggle(SettingsField.ToolCollapseCount));
        Assert.False(SettingsMenu.RefusedMidTurn(SettingsField.ToolCollapseCount));
        Assert.Equal(2, new AppSettingsData().ToolCollapseCount);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_CodeCollapseCount_IsTheOptionsTabsThirdRow_Typed_ZeroIsOff_OutOfRangeRefused()
    {
        // Later on 2026-09-22, the user's place: under Tool collapse count; 0 to 100, 20 by default, 0 = off.
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Left);                                          // Options, the strip wrapped
        Push(Keys.Down, Keys.Down, Keys.Enter);                   // Code collapse count: the typed slot with "20"
        Push(Keys.Backspace, Keys.Backspace, Keys.Char('1'), Keys.Char('0'), Keys.Char('1'), Keys.Enter);   // refused: 101
        Push(Keys.Enter, Keys.Backspace, Keys.Backspace, Keys.Char('0'), Keys.Enter);                      // 0: off
        Push(Keys.Enter, Keys.Backspace, Keys.Char('3'), Keys.Char('0'), Keys.Enter);                      // 30
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(30, _settings.Current.CodeCollapseCount);
        Assert.Contains("Code collapse count " + SettingsMenu.CodeCollapseCountRangeError + "; keeping 20.", _console.Output);
        Assert.Contains("  · Code collapse count: off\n", _console.Output);
        Assert.Contains("  · Code collapse count: 30 lines\n", _console.Output);
        Assert.Equal("must be 0 to 100 lines (0 = off)", SettingsMenu.CodeCollapseCountRangeError);
        Assert.Equal("Code collapse count", SettingsMenu.FieldName(SettingsField.CodeCollapseCount));
        Assert.False(SettingsMenu.IsToggle(SettingsField.CodeCollapseCount));
        Assert.False(SettingsMenu.RefusedMidTurn(SettingsField.CodeCollapseCount));
        Assert.Equal(20, new AppSettingsData().CodeCollapseCount);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_ViewImageMaxPerCall_IsTheFilesTabsLastRow_Typed_OutOfRangeRefused()
    {
        // 2026-09-19, the user's ask: 1 to 100, 10 by default; the value the tool reads at its next call.
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right);   // Files
        Push(Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Enter);   // View image max per call (the seventh row since 2026-09-21): the typed slot with "10"
        Push(Keys.Backspace, Keys.Backspace, Keys.Char('0'), Keys.Enter);        // refused: 0
        Push(Keys.Enter, Keys.Backspace, Keys.Backspace, Keys.Char('1'), Keys.Char('0'), Keys.Char('1'), Keys.Enter);   // refused: 101
        Push(Keys.Enter, Keys.Backspace, Keys.Backspace, Keys.Char('2'), Keys.Char('5'), Keys.Enter);   // 25
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal(25, _settings.Current.FileViewImageMaxPerCall);
        Assert.Contains("File view image max (per call) " + SettingsMenu.ViewImageMaxPerCallRangeError + "; keeping 10.", _console.Output);
        Assert.Contains("  · File view image max (per call): 25 pictures\n", _console.Output);
        Assert.Contains("\n▸ File view image max (per call)  25 pictures\n", _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task OnThePane_SpaceOnASettingsRow_IsNothing()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Right, Keys.Right, Keys.Right, Keys.Right);   // Ask
        Push(Keys.Char(' '));
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None);

        Assert.True(_settings.Current.AskUser);
        Assert.Empty(_settings.Current.ToolsDisabled);
        Assert.DoesNotContain("  · ", _console.Output);
        Assert.DoesNotContain(SettingsMenu.PickKeys, _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task MidTurn_AFlipSaves_AndTheRowsEdit()
    {
        var (menu, pane, _) = PaneMenu();
        Push(Keys.Enter);                           // get_current_time off
        Push(Keys.Right, Keys.Right, Keys.Right, Keys.Right, Keys.Enter, Keys.Down, Keys.Enter);   // Ask user off
        Push(Keys.Escape);

        await menu.ShowAsync(CancellationToken.None, midTurn: true);

        Assert.Equal([GetCurrentTimeTool.ToolName], _settings.Current.ToolsDisabled);
        Assert.False(_settings.Current.AskUser);
        Assert.DoesNotContain(SettingsMenu.NotWhileReplyRunsNotice, _console.Output);
        pane.Dispose();
    }

    [Fact]
    public async Task ADoubleClickOnAToolRow_FlipsIt_AndTwoClicksOffThePane_CloseIt()
    {
        var (menu, pane, input) = ClickablePaneMenu(100);
        input.PushClick(4, 104);                    // the strip at 100, the spacer at 101, Clock (3) at 102, get_current_time at 103, shift_date at 104
        input.PushClick(4, 104);
        input.PushClick(4, 50);                     // off the pane, twice: close
        input.PushClick(4, 50);
        input.Push(Keys.Escape);                    // never read

        await menu.ShowAsync(CancellationToken.None);

        Assert.Equal([ShiftDateTool.ToolName], _settings.Current.ToolsDisabled);
        Assert.Contains("  · shift_date: off\n", _console.Output);
        Assert.False(pane.OverlayOpen);
        Assert.False(pane.Dismissed);
        Assert.True(input.IsAvailable);
        pane.Dispose();
    }

    [Fact]
    public async Task WithoutThePane_PrintsTheFiveTabsAsLines_ToTheTranscript()
    {
        _settings.Update(d => d.ToolsDisabled = ["read_file"]);
        _paneOn = false;
        var pane = new ScreenPane(_console, geometry: null, _time);
        var keys = new KeySource(_console.Input, TimeSpan.FromMilliseconds(1));
        var menuPane = new MenuPane(pane, keys);
        var settings = new SettingsMenu(_console, _settings, _ => null, new InputLine(_console, keys), new TranscriptRenderer(_console), _speech, menuPane, _ => FakeBrowserPath);
        var menu = new ToolsMenu(Facts, _settings, settings, new TranscriptRenderer(_console), menuPane);

        await menu.ShowAsync(CancellationToken.None);

        Assert.Contains("  · Offered\n  ·   Clock (3)\n  ·     get_current_time      on   ", _console.Output);
        Assert.Contains("  ·   Files (14 of 15)\n", _console.Output);
        Assert.Contains("  ·     read_file             off  Reads a text file", _console.Output);   // the console wraps the long line
        Assert.Contains("switched off in /tools", _console.Output);
        Assert.Contains("  ·   Questions (1) (off: no pane)\n", _console.Output);
        // The tabs in strip order: Web right after Offered, then Files, Shell, Ask, Git (native) (the user's order, later on 2026-09-21), Obsidian and Options last (2026-09-22).
        Assert.Contains("  · Web\n  ·   Web tools: on\n  ·   Web browser mode: default\n  ·   Web browser path: (auto: msedge.exe)\n", _console.Output);
        Assert.Contains("  ·   Web search max results: 20 results\n  · Files\n  ·   File tools: on\n  ·   File safe edits: off\n", _console.Output);
        Assert.Contains("  · Shell\n  ·   Shell command policy: ask\n", _console.Output);
        Assert.Contains("  · Ask\n  ·   Ask user: on\n  ·   Ask max questions: 10 questions\n  ·   Ask max choices per question: 10 choices\n  · Git (native)\n  ·   Git native tools: on\n  ·   Git native diff max lines: 500 lines\n  ·   Git native log max commits: 20 commits\n  ·   Git native email: (not set)\n  ·   Git native name: (not set)\n  · Obsidian\n  ·   Obsidian tools: on\n  ·   Obsidian vault: (not set)\n  · Options\n  ·   $-mention enabled: on\n  ·   Tool collapse count: 2 lines\n  ·   Code collapse count: 20 lines\n", _console.Output);
        Assert.False(pane.OverlayOpen);
        pane.Dispose();
    }

    /// <summary>
    /// The allowed-commands list straight (later still on 2026-09-21, /cmdlist and the toolbar's lock):
    /// the same list under the same crumb as the Shell tab's row, Enter removing, ESC closing the
    /// pane with the crumb's root put back — the Tools tabs never drawn.
    /// </summary>
    [Fact]
    public async Task ShowAllowedCommands_OpensTheListUnderTheToolsCrumb_EnterRemoves_EscClosesThePane()
    {
        _settings.Update(d => d.ShellCommandAllowed = ["git push", "dotnet build"]);
        var (menu, pane, settings) = PaneMenu();
        Push(Keys.Enter, Keys.Escape);   // dotnet build removed, then the pane closed

        await menu.ShowAllowedCommandsAsync(CancellationToken.None);

        Assert.Equal(["git push"], _settings.Current.ShellCommandAllowed);
        Assert.Contains("\n" + Titled(ToolsText.Label + " › Shell allowed commands") + "\n \n▸ dotnet build\n  git push\n", _console.Output);
        Assert.Contains("  · Shell allowed commands: dotnet build removed\n▸ git push\n", _console.Output);
        Assert.DoesNotContain(Strip, _console.Output);
        Assert.Equal(SettingsMenu.Title, settings.Root);
        Assert.False(pane.OverlayOpen);
        pane.Dispose();
    }

    [Fact]
    public async Task ShowAllowedCommands_WithoutThePane_PrintsTheListAsLines()
    {
        _settings.Update(d => d.ShellCommandAllowed = ["git push", "dotnet build"]);
        _paneOn = false;
        var pane = new ScreenPane(_console, geometry: null, _time);
        var keys = new KeySource(_console.Input, TimeSpan.FromMilliseconds(1));
        var menuPane = new MenuPane(pane, keys);
        var settings = new SettingsMenu(_console, _settings, _ => null, new InputLine(_console, keys), new TranscriptRenderer(_console), _speech, menuPane, _ => FakeBrowserPath);
        var menu = new ToolsMenu(Facts, _settings, settings, new TranscriptRenderer(_console), menuPane);

        await menu.ShowAllowedCommandsAsync(CancellationToken.None);
        _settings.Update(d => d.ShellCommandAllowed = []);
        await menu.ShowAllowedCommandsAsync(CancellationToken.None);

        Assert.Contains("  · Shell allowed commands\n  ·   dotnet build\n  ·   git push\n", _console.Output);
        Assert.Contains("  · Shell allowed commands\n  ·   " + SettingsMenu.NoAllowedCommandsRow + "\n", _console.Output);
        Assert.Equal(["Shell allowed commands", "  dotnet build", "  git push"], ToolsMenu.AllowedCommandLines(new AppSettingsData { ShellCommandAllowed = ["git push", "dotnet build"] }));
        Assert.Equal(["Shell allowed commands", "  " + SettingsMenu.NoAllowedCommandsRow], ToolsMenu.AllowedCommandLines(new AppSettingsData()));
        Assert.False(pane.OverlayOpen);
        pane.Dispose();
    }
}
