namespace NeonCompanion.App;

/// <summary>What a typed line turned out to be.</summary>
public enum SlashCommand
{
    /// <summary>Not a command: a message for the model.</summary>
    None,
    Help,

    /// <summary><c>/clear</c>: forget the conversation and wipe the screen back to the banner.</summary>
    Clear,

    /// <summary><c>/new</c>: forget the conversation and keep the screen — a rule and a notice mark the boundary in the transcript (2026-09-16).</summary>
    New,

    /// <summary><c>/splash</c>: forget the conversation, wipe the screen and show the welcome splash again — the startup view, whatever <c>Welcome splash</c> says (2026-09-19, the user's ask). No argument.</summary>
    Splash,

    /// <summary><c>/compact</c>: shrink the history the model re-reads (a summary and the recent turns, or pruned tool results), or <c>/compact &lt;focus&gt;</c>.</summary>
    Compact,

    /// <summary><c>/server</c>: pick an LLM server from the ones that answer, or <c>/server &lt;url&gt;</c>; then its model.</summary>
    Server,
    Model,

    /// <summary><c>/reasoning</c>: pick the LLM reasoning effort from a list, or <c>/reasoning &lt;level&gt;</c>.</summary>
    Reasoning,
    Settings,

    /// <summary><c>/tools</c>: the Tools pane (2026-09-19, the user's ask) — every tool the model can be offered, on or off one by one (the Offered tab), then its Options tab (the <c>$</c>-mention switch, later that day) and the Ask, Files and Web settings rows that sat on <c>/settings</c> until then. No argument: bare like <see cref="Skills"/>. <c>///</c> is its alias (2026-09-21, the user's ask, beside <c>//</c>).</summary>
    Tools,

    /// <summary><c>/mcp</c>: the MCP pane (2026-09-20, the user's ask) — the servers the two <c>mcp.json</c> files name, on or off one by one and connected or not (the Servers tab), their tools on or off by their prefixed names (the Tools tab), then its Options tab (the master switch and the connect timeout). No argument: bare like <see cref="Tools"/>.</summary>
    Mcp,
    Tts,
    Voice,
    Wake,
    Interrupt,

    /// <summary><c>/remember &lt;text&gt;</c>: keep one fact across sessions.</summary>
    Remember,

    /// <summary><c>/forget</c>: erase every memory, after a confirmation.</summary>
    Forget,

    /// <summary><c>/memory</c>: list what is remembered; Enter removes one.</summary>
    Memory,

    /// <summary><c>/memcopy &lt;profile&gt; [overwrite]</c>: copy this profile's memory into another's — appended, the duplicates skipped, or in place of it — after a confirmation (2026-09-17).</summary>
    MemCopy,

    /// <summary><c>/persona</c>: open <c>persona.md</c> in the editor Windows associates with it, or <c>/persona reset</c> to remove it (2026-09-16).</summary>
    Persona,

    /// <summary><c>/operata</c>: open <c>operata.md</c> (the operating rules) in the editor Windows associates with it, or <c>/operata reset</c> to remove it (2026-09-16).</summary>
    Operata,

    /// <summary><c>/vocalia</c>: open <c>vocalia.md</c> (the spoken-reply directive) in the editor Windows associates with it, or <c>/vocalia reset</c> to remove it (2026-09-16).</summary>
    Vocalia,

    /// <summary><c>/sysprompt</c>: the system prompt the next turn sends and the tools it offers, in the info pane.</summary>
    Sysprompt,

    /// <summary><c>/usage</c>: the tokens used and the speed — the last reply, this conversation, since launch.</summary>
    Usage,

    /// <summary><c>/profile</c>: pick, switch to, add, delete, rename or reset a profile; since 2026-09-21 <c>edit</c> opens its <c>profile.json</c> and <c>reload</c> reads it back.</summary>
    Profile,

    /// <summary><c>/timer</c>: list the timers, start one, stop one or all.</summary>
    Timer,

    /// <summary><c>/cwd</c>: show the working directory, or <c>/cwd &lt;path&gt;</c> | <c>~</c> (<c>default</c> went 2026-09-16).</summary>
    Cwd,

    /// <summary><c>/tree</c>: a tree of the working directory's folders and files, or <c>/tree &lt;path&gt;</c> for a folder under it.</summary>
    Tree,

    /// <summary><c>/explore</c>: open the working directory in the system's file browser (Explorer, Finder, …), or <c>/explore &lt;path&gt;</c> for a folder under it.</summary>
    Explore,

    /// <summary><c>/copy</c>: copy the last exchange to the clipboard as markdown, or <c>/copy &lt;n&gt;</c> | <c>all</c>.</summary>
    Copy,

    /// <summary><c>/draft</c>: a temporary text file in your editor; saved and closed, its text is the next message — sent through the input line as a paste, so a long one lands as a token — and the file goes; blank, or closed unsaved, nothing is sent (2026-09-19). No argument.</summary>
    Draft,

    /// <summary><c>/loop &lt;count&gt; &lt;message&gt;</c> | <c>/loop infinite &lt;message&gt;</c>: the message sent that many times, or until ESC or Ctrl+C, each reply waited for as if typed again (2026-09-21, the user's ask). Refused mid-turn like <see cref="Draft"/>: it sends what the running turn cannot take.</summary>
    Loop,

    /// <summary><c>/emptytrash</c>: delete everything in the working directory's <c>.trash</c> for good, after a confirmation.</summary>
    EmptyTrash,

    /// <summary><c>/git user [force]</c> (2026-09-21): the <c>Git email</c> and <c>Git name</c> settings written into the working directory's repository config as <c>user.email</c> / <c>user.name</c>; a <c>[user]</c> section already there is kept unless <c>force</c>. Nothing else yet.</summary>
    Git,

    /// <summary><c>/window</c> (<c>/windowsize</c> until later on 2026-09-19): the terminal window's width and height, for information.</summary>
    Window,

    /// <summary><c>/about</c>: the app's version, runtime, folders, servers, third-party components and licence, in the info pane.</summary>
    About,

    /// <summary><c>/skills</c>: the Skills pane listing the skills the model can load, the project notes file and the skill folders (<c>/skills</c> again since 2026-09-19, the plural beside <c>/tools</c>, the user's call — <c>/skill</c> is an unknown command now; a bare <c>/skill</c> from later on 2026-09-18, <c>/skill list</c> earlier that day, <c>/skills</c> from 2026-09-16 until then). No argument: <c>/skill &lt;name&gt; [message]</c> loaded a skill into the next reply from 2026-09-16 until later on 2026-09-18, when the <c>#</c>-mention made it redundant (the user's call); an argument was <see cref="Overloaded"/> until 2026-09-21, when <c>/skills edit &lt;name&gt;</c> came (the user's ask: the skill's <c>SKILL.md</c> in your editor). <c>////</c> is its alias (the same day).</summary>
    Skills,

    /// <summary><c>/learn [note]</c>: a skill-learning reflection over the last turn whatever its shape, the note steering it (2026-09-17, the explicit signal of <c>Skills auto learn</c>); <c>/learn sessions [N | text]</c> a pass over the stored sessions (2026-09-19).</summary>
    Learn,
    /// <summary><c>/speak &lt;file&gt; [n]</c>: a text file from the working directory printed as a reply and read aloud when speech is on, from sentence <em>n</em> when given — the model never sees it; <c>/speak</c> alone resumes a stopped reading, <c>/speak &lt;n&gt;</c> starts the last file at sentence <em>n</em> (2026-09-17).</summary>
    Speak,

    /// <summary><c>/view &lt;image&gt;</c>: one picture from the working directory drawn in the transcript as large as the window allows — the model never sees it (2026-09-17).</summary>
    View,

    /// <summary><c>/echo &lt;text&gt;</c>: the line printed as a reply and read aloud when speech is on — <c>/speak</c>'s block and voice over typed text, never resumed (2026-09-17).</summary>
    Echo,

    /// <summary><c>/queue</c>: the messages queued while a reply runs, on a pane where Enter removes one (2026-09-18, behind <c>Queue messages</c>).</summary>
    Queue,

    /// <summary><c>/session</c>: this profile's stored sessions on a pane (restore, rename, purge), or <c>/session &lt;id&gt; | purge &lt;id&gt; | purge older &lt;age&gt; | purge all | title &lt;text&gt;</c> typed (2026-09-18).</summary>
    Session,

    /// <summary><c>/exit</c>: leave the app (<c>/quit</c> until 2026-09-17, the user's call; the old word is unknown now, as the aliases are).</summary>
    Exit,
    /// <summary>A command we know, given an argument it does not take (<c>/about me</c>): <c>Args</c> carries it. Since 2026-09-17 its own case, so the error names the command rather than calling it unknown.</summary>
    Overloaded,
    /// <summary>Started with <c>/</c> but is not a command we know.</summary>
    Unknown,
}

/// <summary>
/// The slash-command classifier. A line is a command only when it starts with <c>/</c>, and the
/// first token must match exactly: <c>/exit the program please</c> is not <c>/exit</c>, and
/// <c>what does /clear do?</c> is a question for the model. <c>//</c> is <c>/settings</c>, and since 2026-09-21 (the user's ask) <c>///</c> is <c>/tools</c> and <c>////</c> is <c>/skills</c> — the three aliases; every other one (<c>/?</c>, <c>/cls</c>, <c>/exit</c>, <c>/srv</c>, <c>/prof</c> …) went on 2026-09-16 with the argument completion, the user's call, and reads as an unknown command now (<c>/config</c> had gone the same day). <c>/new</c> is its own command (a new conversation, the screen kept) since 2026-09-16; <c>/splash</c> (a new conversation, the screen wiped and the welcome splash shown) since 2026-09-19. Only <c>/server</c>, <c>/model</c>, <c>/reasoning</c>, <c>/tts</c>,
/// <c>/stt</c>, <c>/wake</c>, <c>/interrupt</c>, <c>/speak</c>, <c>/echo</c>, <c>/view</c>, <c>/learn</c>, <c>/remember</c>, <c>/memcopy</c>, <c>/profile</c>, <c>/timer</c>, <c>/cwd</c>, <c>/tree</c>, <c>/explore</c>, <c>/copy</c>, <c>/compact</c>, <c>/git</c>, <c>/loop</c>, <c>/skills</c> (2026-09-21) and (since 2026-09-16, <c>reset</c>) <c>/persona</c>, <c>/operata</c>, <c>/vocalia</c> take an argument (<see cref="TakesArgument"/>); any other command given one is <see cref="SlashCommand.Overloaded"/>, so <c>/about me</c> is told the command takes nothing rather than called unknown (2026-09-17). <c>/draft</c> takes nothing (2026-09-19: the editor is the argument). <c>/skills</c> opens the Skills pane, or <c>/skills edit &lt;name&gt;</c> the skill's file (2026-09-21; it took nothing before) (the plural since 2026-09-19, beside <c>/tools</c>; a bare <c>/skill</c> from later on 2026-09-18, in place of <c>/skill list</c>; <c>/skills</c> before that; <c>/skill &lt;name&gt; [message]</c> took a name until later that day; <c>/skill</c> is unknown now). The three tool switches <c>/web</c>, <c>/files</c>, <c>/ask</c> went later on 2026-09-18 (the user's call: the settings rows <c>Web tools</c>, <c>File tools</c>, <c>Ask user</c> are the one place now) and read as unknown commands.
/// </summary>
public static class SlashCommands
{
    /// <summary>
    /// One command and what it does: a row of the Commands tab of the info pane, and a line of <see cref="HelpText"/>.
    /// An alias goes in the first column with the command (<see cref="Label"/>), never in the summary — <c>//</c> was the one left (2026-09-16), <c>///</c> and <c>////</c> came on 2026-09-21.
    /// </summary>
    public sealed record HelpEntry(string Command, string Summary, params string[] Aliases)
    {
        /// <summary>The first column: the command and its aliases, comma-separated (<c>/settings, //</c>). Pinned by tests.</summary>
        public string Label => Aliases.Length == 0 ? Command : Command + ", " + string.Join(", ", Aliases);
    }

    /// <summary>
    /// Every command with its summary, grouped as <c>/help</c> lists them — a blank row between the groups
    /// (the user's order and wording, 2026-09-15; <c>/exit</c> (<c>/quit</c> until 2026-09-17) last
    /// beside <c>/about</c>, <c>/compact</c> under <c>/reasoning</c> since 2026-09-16; <c>/help</c> at the bottom above <c>/about</c>
    /// and <c>/memory</c> heading its group, the user's call later that day; <c>/speak</c> and <c>/view</c> a group of their own
    /// under <c>/windowsize</c>'s, 2026-09-17; the tool switches <c>/ask</c>, <c>/files</c>, <c>/web</c> — a group of their own
    /// from 2026-09-15 — gone later on 2026-09-18, the same day <c>/session</c> moved under <c>/profile</c> and <c>/copy</c>
    /// under <c>/queue</c>, leaving <c>/skills</c> + <c>/learn</c> and <c>/timer</c> + <c>/windowsize</c> as groups, the user's call;
    /// later still on 2026-09-19 (the user's call again) <c>/skills</c> + <c>/learn</c> went under <c>/session</c>, <c>/windowsize</c>
    /// became <c>/window</c> under <c>/view</c> and <c>/timer</c> went under <c>/help</c> — nine groups; <c>/draft</c> under <c>/copy</c>, 2026-09-19; later still that day <c>/splash</c> under <c>/new</c> and <c>/help</c> under <c>/timer</c>, the user's ask). Pinned by tests.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<HelpEntry>> HelpGroups =
    [
        [
            new("/settings", "edit and save settings", "//"),
            new("/tools", "switch the model's tools on or off and edit the Options, Ask, Files and Web settings on a pane", "///"),
            new("/mcp", "connect external MCP servers and switch their tools on or off on a pane"),
            new("/profile", "switch profiles, or /profile <name> | add <name> | delete <name> | rename <name> <new-name> | reset [name] | edit | reload"),
            new("/session", "list, restore and purge sessions: /session [<id> | purge <id> | purge older <age> | purge all | title <text>]"),
            new("/skills", "list the skills, edit the skill settings and the project file on a pane, or /skills edit <name> to open its SKILL.md", "////"),
            new("/learn", "write or improve a skill from the last turn or the stored sessions, in the background: /learn [what to keep] | sessions [N | what to search]"),
        ],
        [
            new("/server", "pick an LLM server found on the usual ports, or /server <url>"),
            new("/model", "pick a model from the LLM server, or /model <id>"),
            new("/reasoning", "pick the LLM reasoning effort, or /reasoning <level>"),
            new("/compact", "shrink the current context, or /compact <focus> to steer the summary"),
            new("/sysprompt", "show the system prompt and tools sent to the model"),
            new("/usage", "show token usage and performance statistics"),
        ],
        [
            new("/clear", "start a new conversation and clear the screen"),
            new("/new", "start a new conversation but do not clear the screen"),
            new("/splash", "start a new conversation and show the splash screen"),
            new("/queue", "list and prune the messages queued while a reply runs"),
            new("/copy", "copy the last reply to the clipboard as markdown, or /copy <n> | all"),
            new("/draft", "write the next message in your editor: a temporary file, sent when it is saved and closed"),
            new("/loop", "repeat a message, each reply waited for: /loop <count> <message> | infinite <message> (ESC ends it)"),
        ],
        [
            new("/tts", "toggle speech output, or /tts on|off"),
            new("/stt", "toggle speech input, or /stt on|off"),
            new("/wake", "toggle the speech input wake word, or /wake on|off"),
            new("/interrupt", "toggle the speech input wake word interrupt, or /interrupt on|off"),
        ],
        [
            new("/memory", "list and prune memory items"),
            new("/remember", "add a memory: /remember <text>"),
            new("/forget", "forget all memory"),
            new("/memcopy", "copy this profile's memory into another: /memcopy <profile> [overwrite]"),
        ],
        [
            new("/cwd", "show or change the working directory, or /cwd <path> | ~"),
            new("/tree", "print a tree of the working directory's folders and files, or /tree <path>"),
            new("/explore", "open the working directory in your file browser, or /explore <path>"),
            new("/emptytrash", "empty the working directory's .trash for good (asks first)"),
            new("/git", "write the Git email and Git name settings into the working directory's repository: /git user [force]"),
        ],
        [
            new("/speak", "read a text file from the working directory aloud, as a reply: /speak <file> [n], or /speak to resume, or /speak <n> from sentence n"),
            new("/echo", "print a line as a reply and read it aloud when speech is on: /echo <text>"),
            new("/view", "show an image from the working directory in the transcript, as large as the window allows: /view <image>"),
            new("/window", "show the terminal window's width and height"),
        ],
        [
            new("/persona", "export and manage persona.md (the personality) in your editor, or /persona reset to go back to the default"),
            new("/operata", "export and manage operata.md (the operating rules) in your editor, or /operata reset to go back to the default"),
            new("/vocalia", "export and manage vocalia.md (the spoken-reply directive) in your editor, or /vocalia reset to go back to the default"),
        ],
        [
            new("/timer", "list timers, or /timer <duration> [name] (10m, 90s, 1h30m) | stop <name> | stop all"),
            new("/help", "show help"),
            new("/about", "show general information about the app and profile"),
            new("/exit", "exit/quit the application"),
        ],
    ];

    /// <summary>The groups flattened: every command with its summary, in the order <c>/help</c> lists them. Pinned by tests.</summary>
    public static readonly IReadOnlyList<HelpEntry> HelpEntries = HelpGroups.SelectMany(group => group).ToArray();

    /// <summary>
    /// The input line's command list (<see cref="UI.MentionCompleter.TryFindCommand"/>): every base
    /// command with its summary as the note, sorted by name — never an alias (the user's call,
    /// 2026-09-16; <c>///</c> and <c>////</c> stay out the same way, 2026-09-21). Pinned by tests.
    /// </summary>
    public static readonly IReadOnlyList<UI.CompletionItem> Completions =
        HelpEntries.Select(entry => new UI.CompletionItem(entry.Command, entry.Summary)).OrderBy(item => item.Text, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// <see cref="Completions"/> less <c>/exit</c>: the list under the setting <c>Hide /exit autocomplete</c>
    /// (2026-09-18, on by default), so a pick never ends the app by mistake; typed in full it exits as
    /// ever, and <c>/help</c> keeps the row. Pinned.
    /// </summary>
    public static readonly IReadOnlyList<UI.CompletionItem> CompletionsWithoutExit =
        Completions.Where(item => item.Text != "/exit").ToArray();

    /// <summary>The blank cells between the label column and the summary, on the pane and in <see cref="HelpText"/> alike.</summary>
    public const int HelpColumnGap = 2;

    /// <summary>
    /// The label column's width: the longest <see cref="HelpEntry.Label"/>, measured — never a literal — so a new
    /// command or alias re-fits the column. <see cref="HelpText"/> pads every label to it plus <see cref="HelpColumnGap"/>;
    /// the pane's grid measures its own first column the same way.
    /// </summary>
    public static readonly int LabelWidth = HelpEntries.Max(entry => entry.Label.Length);

    /// <summary>The key line of <see cref="HelpText"/>. Pinned by tests.</summary>
    public const string KeysLine = "Keys: Enter = send   ESC = stop the speech / clear the line / cancel the reply   Up/Down = history, or the draft's rows when it wraps   F4 = talk (push-to-talk key)";

    /// <summary>Printed by <c>/help</c> when there is no pane to open (a redirected console): the groups with a blank line between them. Pinned by tests.</summary>
    public static readonly string HelpText = BuildHelpText();

    private static string BuildHelpText()
    {
        var text = new System.Text.StringBuilder("Commands:\n");
        for (var i = 0; i < HelpGroups.Count; i++)
        {
            if (i > 0)
            {
                text.Append('\n');
            }

            foreach (var entry in HelpGroups[i])
            {
                text.Append("  ").Append(entry.Label.PadRight(LabelWidth + HelpColumnGap)).Append(entry.Summary).Append('\n');
            }
        }

        return text.Append(KeysLine).ToString();
    }

    /// <summary>Every command word, for help and completion.</summary>
    public static readonly string[] Words = { "/help", "/clear", "/new", "/splash", "/queue", "/session", "/compact", "/server", "/model", "/reasoning", "/settings", "//", "/tools", "///", "/mcp", "/tts", "/stt", "/wake", "/interrupt", "/speak", "/remember", "/memory", "/forget", "/memcopy", "/persona", "/operata", "/vocalia", "/sysprompt", "/usage", "/profile", "/timer", "/cwd", "/tree", "/explore", "/view", "/echo", "/emptytrash", "/git", "/copy", "/draft", "/loop", "/window", "/skills", "////", "/learn", "/about", "/exit" };

    /// <summary>The <c>/queue</c> word: what a double-click on the hint row's queued part sends through the mid-turn line hook, so the pane opens exactly as the typed command's does (2026-09-18). Pinned.</summary>
    public const string QueueWord = "/queue";

    /// <summary>Classifies <paramref name="line"/>; <c>Args</c> is the trimmed remainder — meaningful for the commands <see cref="TakesArgument"/> names, and carried by <see cref="SlashCommand.Overloaded"/> for the error line.</summary>
    public static (SlashCommand Command, string Args) Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string trimmed = line.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return (SlashCommand.None, "");
        }

        int split = trimmed.IndexOfAny(new[] { ' ', '\t' });
        string token = split < 0 ? trimmed : trimmed[..split];
        string args = split < 0 ? "" : trimmed[(split + 1)..].Trim();

        var command = token.ToLowerInvariant() switch
        {
            "/help" => SlashCommand.Help,
            "/clear" => SlashCommand.Clear,
            "/new" => SlashCommand.New,
            "/splash" => SlashCommand.Splash,
            "/compact" => SlashCommand.Compact,
            "/server" => SlashCommand.Server,
            "/model" => SlashCommand.Model,
            "/reasoning" => SlashCommand.Reasoning,
            "/settings" or "//" => SlashCommand.Settings,
            "/tools" or "///" => SlashCommand.Tools,
            "/mcp" => SlashCommand.Mcp,
            "/tts" => SlashCommand.Tts,
            "/stt" => SlashCommand.Voice,
            "/wake" => SlashCommand.Wake,
            "/interrupt" => SlashCommand.Interrupt,
            "/speak" => SlashCommand.Speak,
            "/remember" => SlashCommand.Remember,
            "/forget" => SlashCommand.Forget,
            "/memory" => SlashCommand.Memory,
            "/memcopy" => SlashCommand.MemCopy,
            "/persona" => SlashCommand.Persona,
            "/operata" => SlashCommand.Operata,
            "/vocalia" => SlashCommand.Vocalia,
            "/sysprompt" => SlashCommand.Sysprompt,
            "/usage" => SlashCommand.Usage,
            "/profile" => SlashCommand.Profile,
            "/timer" => SlashCommand.Timer,
            "/cwd" => SlashCommand.Cwd,
            "/tree" => SlashCommand.Tree,
            "/explore" => SlashCommand.Explore,
            "/view" => SlashCommand.View,
            "/echo" => SlashCommand.Echo,
            "/queue" => SlashCommand.Queue,
            "/session" => SlashCommand.Session,
            "/copy" => SlashCommand.Copy,
            "/draft" => SlashCommand.Draft,
            "/loop" => SlashCommand.Loop,
            "/emptytrash" => SlashCommand.EmptyTrash,
            "/git" => SlashCommand.Git,
            "/window" => SlashCommand.Window,
            "/about" => SlashCommand.About,
            "/skills" or "////" => SlashCommand.Skills,
            "/learn" => SlashCommand.Learn,
            "/exit" => SlashCommand.Exit,
            _ => SlashCommand.Unknown,
        };

        if (command is not SlashCommand.Unknown && !TakesArgument(command) && args.Length > 0)
        {
            command = SlashCommand.Overloaded;
        }

        return (command, args);
    }

    /// <summary>
    /// Whether the command reads what follows it. The one list (pinned): a command not here given an
    /// argument parses as <see cref="SlashCommand.Overloaded"/>. <see cref="SlashCommand.None"/>,
    /// <see cref="SlashCommand.Unknown"/> and <see cref="SlashCommand.Overloaded"/> take nothing.
    /// </summary>
    public static bool TakesArgument(SlashCommand command) => command is
        SlashCommand.Compact or SlashCommand.Server or SlashCommand.Model or SlashCommand.Reasoning
        or SlashCommand.Tts or SlashCommand.Voice or SlashCommand.Wake or SlashCommand.Interrupt or SlashCommand.Speak or SlashCommand.View or SlashCommand.Echo
        or SlashCommand.Learn
        or SlashCommand.Persona or SlashCommand.Operata or SlashCommand.Vocalia
        or SlashCommand.Remember or SlashCommand.MemCopy or SlashCommand.Profile or SlashCommand.Timer
        or SlashCommand.Cwd or SlashCommand.Tree or SlashCommand.Explore or SlashCommand.Copy or SlashCommand.Session or SlashCommand.Git
        or SlashCommand.Loop or SlashCommand.Skills;
}
