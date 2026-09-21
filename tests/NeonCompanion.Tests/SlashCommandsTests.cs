using NeonCompanion.App;

namespace NeonCompanion.Tests;

public class SlashCommandsTests
{
    [Theory]
    [InlineData("/help", SlashCommand.Help)]
    [InlineData("/HELP", SlashCommand.Help)]
    [InlineData("  /clear  ", SlashCommand.Clear)]
    [InlineData("/new", SlashCommand.New)]
    [InlineData("/NEW", SlashCommand.New)]
    [InlineData("  /new  ", SlashCommand.New)]
    [InlineData("/splash", SlashCommand.Splash)]   // 2026-09-19
    [InlineData("/SPLASH", SlashCommand.Splash)]
    [InlineData("/server", SlashCommand.Server)]
    [InlineData("/SERVER", SlashCommand.Server)]
    [InlineData("/model", SlashCommand.Model)]
    [InlineData("/reasoning", SlashCommand.Reasoning)]
    [InlineData("/REASONING", SlashCommand.Reasoning)]
    [InlineData("/settings", SlashCommand.Settings)]
    [InlineData("//", SlashCommand.Settings)]
    [InlineData("///", SlashCommand.Tools)]      // 2026-09-21
    [InlineData("////", SlashCommand.Skills)]    // 2026-09-21
    [InlineData("/loop", SlashCommand.Loop)]     // 2026-09-21
    [InlineData("/LOOP", SlashCommand.Loop)]
    [InlineData("/tts", SlashCommand.Tts)]
    [InlineData("/stt", SlashCommand.Voice)]
    [InlineData("/wake", SlashCommand.Wake)]
    [InlineData("/interrupt", SlashCommand.Interrupt)]
    [InlineData("/remember", SlashCommand.Remember)]
    [InlineData("/forget", SlashCommand.Forget)]
    [InlineData("/FORGET", SlashCommand.Forget)]
    [InlineData("/memory", SlashCommand.Memory)]
    [InlineData("/memcopy", SlashCommand.MemCopy)]
    [InlineData("/MemCopy", SlashCommand.MemCopy)]
    [InlineData("/persona", SlashCommand.Persona)]
    [InlineData("/operata", SlashCommand.Operata)]
    [InlineData("/OPERATA", SlashCommand.Operata)]
    [InlineData("/vocalia", SlashCommand.Vocalia)]
    [InlineData("/VOCALIA", SlashCommand.Vocalia)]
    [InlineData("/sys", SlashCommand.Sys)]   // the name since 2026-09-21; /sysprompt before
    [InlineData("/SYS", SlashCommand.Sys)]
    [InlineData("/window", SlashCommand.Window)]
    [InlineData("/windowsize", SlashCommand.Unknown)]   // the old word, later on 2026-09-19
    [InlineData("/usage", SlashCommand.Usage)]
    [InlineData("/about", SlashCommand.About)]
    [InlineData("/ABOUT", SlashCommand.About)]
    [InlineData("/skills", SlashCommand.Skills)]
    [InlineData("/tools", SlashCommand.Tools)]
    [InlineData("/TOOLS", SlashCommand.Tools)]
    [InlineData("/mcp", SlashCommand.Mcp)]
    [InlineData("/MCP", SlashCommand.Mcp)]
    [InlineData("/compact", SlashCommand.Compact)]
    [InlineData("/COMPACT", SlashCommand.Compact)]
    [InlineData("/Usage", SlashCommand.Usage)]
    [InlineData("/profile", SlashCommand.Profile)]
    [InlineData("/timer", SlashCommand.Timer)]
    [InlineData("/TIMER", SlashCommand.Timer)]
    [InlineData("/cwd", SlashCommand.Cwd)]
    [InlineData("/tree", SlashCommand.Tree)]
    [InlineData("/TREE", SlashCommand.Tree)]
    [InlineData("/explore", SlashCommand.Explore)]
    [InlineData("/EXPLORE", SlashCommand.Explore)]
    [InlineData("/speak", SlashCommand.Speak)]
    [InlineData("/SPEAK", SlashCommand.Speak)]
    [InlineData("/view", SlashCommand.View)]
    [InlineData("/VIEW", SlashCommand.View)]
    [InlineData("/echo", SlashCommand.Echo)]
    [InlineData("/ECHO", SlashCommand.Echo)]
    [InlineData("/CWD", SlashCommand.Cwd)]
    [InlineData("/copy", SlashCommand.Copy)]
    [InlineData("/COPY", SlashCommand.Copy)]
    [InlineData("/draft", SlashCommand.Draft)]
    [InlineData("/git", SlashCommand.Git)]
    [InlineData("/GIT", SlashCommand.Git)]
    [InlineData("/DRAFT", SlashCommand.Draft)]
    [InlineData("/PERSONA", SlashCommand.Persona)]
    [InlineData("/exit", SlashCommand.Exit)]
    public void Parse_KnownWords(string line, SlashCommand expected)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(expected, command);
        Assert.Equal("", args);
    }

    [Theory]
    [InlineData("/ask")]
    [InlineData("/ASK on")]
    [InlineData("/files")]
    [InlineData("/files off")]
    [InlineData("/web")]
    [InlineData("/web on")]
    public void Parse_TheRetiredToolSwitches_AreUnknown(string line)
    {
        // The three went later on 2026-09-18 (the user's call): the settings rows Ask user / File tools / Web tools are the one switch.
        Assert.Equal(SlashCommand.Unknown, SlashCommands.Parse(line).Command);
    }

    [Fact]
    public void Parse_SkillsTakesTheRestOfTheLine_TheHandlerJudgesIt()
    {
        // /skill <name> [message] loaded a skill from 2026-09-16 until later on 2026-09-18 (the user's
        // call: the #-mention covers it); a bare /skills is the pane (the plural since 2026-09-19, beside /tools;
        // /skill from later on 2026-09-18 until then, unknown now). Anything after it was Overloaded until
        // 2026-09-21, when /skills edit <name> came: the words are the handler's to judge (the usage line).
        Assert.Equal((SlashCommand.Skills, ""), SlashCommands.Parse("/skills"));
        Assert.Equal((SlashCommand.Skills, ""), SlashCommands.Parse("  /SKILLS  "));
        Assert.Equal((SlashCommand.Skills, ""), SlashCommands.Parse("////"));
        Assert.Equal((SlashCommand.Skills, "edit haiku"), SlashCommands.Parse("/skills edit haiku"));
        Assert.Equal((SlashCommand.Skills, "edit haiku"), SlashCommands.Parse("////  edit haiku "));
        Assert.Equal((SlashCommand.Skills, "haiku"), SlashCommands.Parse("/skills haiku"));
        Assert.Equal((SlashCommand.Skills, "haiku write one about rain"), SlashCommands.Parse("/SKILLS  haiku write one about rain "));
        Assert.Equal((SlashCommand.Skills, "list"), SlashCommands.Parse("/skills list"));   // /skill list was the pane for part of 2026-09-18
        Assert.Equal((SlashCommand.Loop, "3 hi there"), SlashCommands.Parse("/loop 3 hi there"));   // 2026-09-21: the count and the message are the handler's
        Assert.Equal((SlashCommand.Loop, ""), SlashCommands.Parse("/loop"));
        Assert.Equal((SlashCommand.Unknown, ""), SlashCommands.Parse("/skill"));
        Assert.Equal((SlashCommand.Unknown, "now"), SlashCommands.Parse("/skill now"));
    }

    [Fact]
    public void Parse_MemCopyTakesTheRestOfTheLine()
    {
        Assert.Equal((SlashCommand.MemCopy, "work"), SlashCommands.Parse("/memcopy work"));
        Assert.Equal((SlashCommand.MemCopy, "work overwrite"), SlashCommands.Parse("/memcopy  work overwrite "));
        Assert.Equal((SlashCommand.MemCopy, ""), SlashCommands.Parse("/memcopy"));
    }

    [Fact]
    public void Parse_ModelTakesAnArgument()
    {
        var (command, args) = SlashCommands.Parse("/model   qwen3-8b  ");
        Assert.Equal(SlashCommand.Model, command);
        Assert.Equal("qwen3-8b", args);
        Assert.Equal((SlashCommand.Model, "qwen3-8b"), SlashCommands.Parse("/model   qwen3-8b  "));
    }

    [Theory]
    [InlineData("/reasoning high", "high")]
    [InlineData("/REASONING  XHigh ", "XHigh")]
    [InlineData("/reasoning lots", "lots")]   // the menu decides whether it is a level
    [InlineData("/REASONING  low ", "low")]
    public void Parse_ReasoningTakesAnArgument(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Reasoning, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/compact", "")]
    [InlineData("/compact keep the file list", "keep the file list")]
    [InlineData("/Compact   what we decided about the icon  ", "what we decided about the icon")]
    public void Parse_CompactTakesAFocus(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Compact, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/server http://127.0.0.1:5000", "http://127.0.0.1:5000")]
    [InlineData("/Server   localhost:9  ", "localhost:9")]   // the screen decides whether it is a URL
    [InlineData("/SERVER  localhost:9 ", "localhost:9")]
    public void Parse_ServerTakesAnArgument(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Server, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/tts on", "on")]
    [InlineData("/TTS off", "off")]
    [InlineData("/tts maybe", "maybe")]   // the screen decides what the argument means
    public void Parse_TtsTakesAnArgument(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Tts, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/stt on", "on")]
    [InlineData("/STT off", "off")]
    [InlineData("/stt maybe", "maybe")]
    public void Parse_VoiceTakesAnArgument(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Voice, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/wake on", "on")]
    [InlineData("/WAKE off", "off")]
    [InlineData("/wake maybe", "maybe")]
    public void Parse_WakeTakesAnArgument(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Wake, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/interrupt on", "on")]
    [InlineData("/INTERRUPT off", "off")]
    [InlineData("/interrupt maybe", "maybe")]
    [InlineData("/interrupt off", "off")]
    [InlineData("/interrupt", "")]
    public void Parse_InterruptTakesAnArgument(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Interrupt, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/remember my name is Chris", "my name is Chris")]
    [InlineData("/REMEMBER   I live in Leeds  ", "I live in Leeds")]
    [InlineData("/remember what does /forget do?", "what does /forget do?")]
    public void Parse_RememberTakesTheRestOfTheLine(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Remember, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/profile", "")]
    [InlineData("/profile work", "work")]
    [InlineData("/PROFILE  add  work ", "add  work")]
    [InlineData("/profile delete work", "delete work")]
    public void Parse_ProfileTakesTheRestOfTheLine(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Profile, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/timer", "")]
    [InlineData("/timer 10m cooking", "10m cooking")]
    [InlineData("/TIMER  stop  all ", "stop  all")]
    [InlineData("/timer 1h 30m the big pot", "1h 30m the big pot")]
    public void Parse_TimerTakesTheRestOfTheLine(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Timer, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/cwd", "")]
    [InlineData("/cwd default", "default")]
    [InlineData("/cwd ~", "~")]
    [InlineData("/CWD  D:/my files/notes ", "D:/my files/notes")]
    public void Parse_CwdTakesTheRestOfTheLine(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Cwd, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/tree", "")]
    [InlineData("/tree docs", "docs")]
    [InlineData("/TREE  sub folder/deeper ", "sub folder/deeper")]
    [InlineData("/tree  docs/deep ", "docs/deep")]
    public void Parse_TreeTakesTheRestOfTheLine(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Tree, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/speak", "")]
    [InlineData("/speak notes.md", "notes.md")]
    [InlineData("/SPEAK  docs/a b.md ", "docs/a b.md")]
    public void Parse_SpeakTakesTheRestOfTheLine(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Speak, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/explore", "")]
    [InlineData("/explore docs", "docs")]
    [InlineData("/Explore  sub folder ", "sub folder")]
    [InlineData("/EXPLORE  sub folder ", "sub folder")]
    public void Parse_ExploreTakesTheRestOfTheLine(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Explore, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/copy", "")]
    [InlineData("/copy 2", "2")]
    [InlineData("/copy all", "all")]
    [InlineData("/COPY  -1 ", "-1")]
    [InlineData("/COPY  2 ", "2")]
    public void Parse_CopyTakesTheRestOfTheLine(string line, string expectedArgs)
    {
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(SlashCommand.Copy, command);
        Assert.Equal(expectedArgs, args);
    }

    [Theory]
    [InlineData("/cls everything")]
    [InlineData("/sysprompt tools")]   // renamed /sys on 2026-09-21
    [InlineData("/mem 2")]
    [InlineData("/win 80x24")]
    [InlineData("/windowsize")]   // /window took the word later on 2026-09-19
    [InlineData("/")]
    [InlineData("/bogus")]
    [InlineData("/config")]   // retired 2026-09-16
    [InlineData("/config x")]
    [InlineData("/use reset")]
    [InlineData("/ab x")]
    [InlineData("/////")]   // /// and //// are aliases since 2026-09-21; five is nothing
    public void Parse_AnUnknownWord_IsUnknown(string line)
    {
        Assert.Equal(SlashCommand.Unknown, SlashCommands.Parse(line).Command);
    }

    // A command we know, given an argument it does not take: its own case since 2026-09-17, the
    // argument kept, so the screen says the command takes nothing instead of calling it unknown.
    [Theory]
    [InlineData("/exit the program please", "the program please")]
    [InlineData("/clear everything", "everything")]
    [InlineData("/new everything", "everything")]
    [InlineData("/splash again", "again")]   // 2026-09-19
    [InlineData("/forget everything", "everything")]
    [InlineData("/forget 3", "3")]
    [InlineData("/memory 2", "2")]
    [InlineData("/memory list", "list")]
    [InlineData("/sys tools", "tools")]
    [InlineData("/usage reset", "reset")]
    [InlineData("/about x", "x")]
    [InlineData("/help me", "me")]
    [InlineData("/settings x", "x")]
    [InlineData("// x", "x")]
    [InlineData("/emptytrash now", "now")]
    [InlineData("/window 80", "80")]
    [InlineData("/draft notes.txt", "notes.txt")]   // 2026-09-19: the editor is the argument, never a file
    public void Parse_AKnownCommandWithAnArgumentItDoesNotTake_IsOverloaded(string line, string args)
    {
        Assert.Equal((SlashCommand.Overloaded, args), SlashCommands.Parse(line));
    }

    [Fact]
    public void TakesArgument_IsTheOneList()
    {
        SlashCommand[] withArgument =
        [
            SlashCommand.Compact, SlashCommand.Server, SlashCommand.Model, SlashCommand.Reasoning,
            SlashCommand.Tts, SlashCommand.Voice, SlashCommand.Wake, SlashCommand.Interrupt, SlashCommand.Speak, SlashCommand.View, SlashCommand.Echo,
            SlashCommand.Learn,
            SlashCommand.Persona, SlashCommand.Operata, SlashCommand.Vocalia,
            SlashCommand.Remember, SlashCommand.MemCopy, SlashCommand.Profile, SlashCommand.Timer,
            SlashCommand.Cwd, SlashCommand.Tree, SlashCommand.Explore, SlashCommand.Copy, SlashCommand.Session, SlashCommand.Git,
            SlashCommand.Loop, SlashCommand.Skills,   // 2026-09-21
        ];
        foreach (var command in Enum.GetValues<SlashCommand>())
        {
            Assert.Equal(withArgument.Contains(command), SlashCommands.TakesArgument(command));
        }
    }

    [Theory]
    [InlineData("/persona reset", SlashCommand.Persona, "reset")]
    [InlineData("/PERSONA  Reset ", SlashCommand.Persona, "Reset")]
    [InlineData("/persona pirate", SlashCommand.Persona, "pirate")]     // the handler judges the word (the usage line)
    [InlineData("/operata reset", SlashCommand.Operata, "reset")]
    [InlineData("/operata strict", SlashCommand.Operata, "strict")]
    [InlineData("/vocalia reset", SlashCommand.Vocalia, "reset")]
    [InlineData("/vocalia loud", SlashCommand.Vocalia, "loud")]
    public void Parse_PromptFilesTakeTheRestOfTheLine(string line, SlashCommand expected, string expectedArgs)
    {
        // Since 2026-09-16: `reset` removes the file; before, any argument made the line unknown.
        var (command, args) = SlashCommands.Parse(line);
        Assert.Equal(expected, command);
        Assert.Equal(expectedArgs, args);
    }

    [Fact]
    public void Completions_AreTheBaseCommandsSorted_WithTheirSummaries_NoAlias()
    {
        var items = SlashCommands.Completions;

        Assert.Equal(SlashCommands.HelpEntries.Count, items.Count);
        Assert.Equal(items.Select(i => i.Text).OrderBy(t => t, StringComparer.Ordinal), items.Select(i => i.Text));
        Assert.Equal("/about", items[0].Text);
        Assert.Equal("/window", items[^1].Text);
        Assert.DoesNotContain(items, i => i.Text is "//" or "///" or "////");   // the three aliases (2026-09-21) are never rows
        Assert.Contains(items, i => i.Text == "/loop");   // 2026-09-21
        Assert.All(SlashCommands.HelpEntries, e => Assert.Contains(new NeonCompanion.UI.CompletionItem(e.Command, e.Summary), items));
        Assert.Equal(["/server", "/session", "/settings", "/skills", "/speak", "/splash", "/stt", "/sys"], items.Where(i => i.Text.StartsWith("/s", StringComparison.Ordinal)).Select(i => i.Text));
        Assert.Equal(["/timer", "/tools", "/tree", "/tts"], items.Where(i => i.Text.StartsWith("/t", StringComparison.Ordinal)).Select(i => i.Text));   // /tools among them since 2026-09-19
    }

    [Fact]
    public void CompletionsWithoutExit_IsCompletionsLessExit_InTheSameOrder()
    {
        // The list under Hide /exit autocomplete (2026-09-18): /exit is a word to type in full, never a row to pick by mistake.
        var items = SlashCommands.CompletionsWithoutExit;

        Assert.Equal(SlashCommands.Completions.Count - 1, items.Count);
        Assert.Equal(SlashCommands.Completions.Where(i => i.Text != "/exit"), items);
        Assert.DoesNotContain(items, i => i.Text == "/exit");
        Assert.Contains(SlashCommands.Completions, i => i.Text == "/exit");
        Assert.Equal(SlashCommand.Exit, SlashCommands.Parse("/exit").Command);   // typed in full it still exits
    }

    [Theory]
    [InlineData("/?")]
    [InlineData("/cls")]
    [InlineData("/comp keep it")]
    [InlineData("/srv")]
    [InlineData("/mod")]
    [InlineData("/reason high")]
    [InlineData("/int on")]
    [InlineData("/rem x")]
    [InlineData("/mem")]
    [InlineData("/use")]
    [InlineData("/prof")]
    [InlineData("/tim 5m")]
    [InlineData("/cd ~")]
    [InlineData("/dir")]
    [InlineData("/ls docs")]
    [InlineData("/ex")]
    [InlineData("/cp all")]
    [InlineData("/win")]
    [InlineData("/ab")]
    [InlineData("/quit")]
    public void Parse_TheRetiredAliases_AreUnknown(string line)
    {
        // Every alias but // went on 2026-09-16 (the user's call): the completion list makes them redundant; /// and //// came on 2026-09-21.
        Assert.Equal(SlashCommand.Unknown, SlashCommands.Parse(line).Command);
    }

    [Theory]
    [InlineData("what does /clear do?")]
    [InlineData("quit")]
    [InlineData("")]
    [InlineData("  hello  ")]
    public void Parse_NotStartingWithSlash_IsAMessage(string line)
    {
        Assert.Equal(SlashCommand.None, SlashCommands.Parse(line).Command);
    }

    /// <summary>A line of <see cref="SlashCommands.HelpText"/> built the way the text builds it — through the measured width, never a literal run of spaces.</summary>
    private static string Row(string label, string summary) =>
        "  " + label.PadRight(SlashCommands.LabelWidth + SlashCommands.HelpColumnGap) + summary + "\n";

    [Fact]
    public void HelpText_NamesEveryCommandAndTheKeys()
    {
        foreach (var word in SlashCommands.Words)
        {
            Assert.Contains(word, SlashCommands.HelpText);
        }

        Assert.Contains("ESC", SlashCommands.HelpText);
        Assert.DoesNotContain("Ctrl+Q", SlashCommands.HelpText);
        Assert.DoesNotContain("(also", SlashCommands.HelpText);
        Assert.StartsWith("Commands:\n" + Row("/settings, //", "edit and save settings") + Row("/tools, ///", "switch the model's tools on or off and edit the Options, Ask, Files and Web settings on a pane"), SlashCommands.HelpText);   // /tools right under /settings since 2026-09-19; its alias 2026-09-21
        Assert.Contains(Row("/timer", "list timers, or /timer <duration> [name] (10m, 90s, 1h30m) | stop <name> | stop all") + Row("/help", "show help") + Row("/about", "show general information about the app and profile"), SlashCommands.HelpText);   // the bottom group since 2026-09-16, /timer under /help since later on 2026-09-19, /help under /timer later still that day
        Assert.Contains(Row("/settings, //", "edit and save settings"), SlashCommands.HelpText);
        Assert.Contains(Row("/profile", "switch profiles, or /profile <name> | add <name> | delete <name> | rename <name> <new-name> | reset [name] | edit | reload"), SlashCommands.HelpText);   // edit and reload 2026-09-21
        Assert.Contains(Row("/exit", "exit/quit the application"), SlashCommands.HelpText);
        Assert.Contains(Row("/server", "pick an LLM server found on the usual ports, or /server <url>"), SlashCommands.HelpText);
        Assert.Contains(Row("/model", "pick a model from the LLM server, or /model <id>"), SlashCommands.HelpText);
        Assert.Contains(Row("/reasoning", "pick the LLM reasoning effort, or /reasoning <level>"), SlashCommands.HelpText);
        Assert.Contains(Row("/sys", "show the system prompt and tools sent to the model"), SlashCommands.HelpText);
        Assert.Contains(Row("/usage", "show token usage and performance statistics"), SlashCommands.HelpText);
        Assert.Contains(Row("/compact", "shrink the current context, or /compact <focus> to steer the summary"), SlashCommands.HelpText);
        Assert.Contains(Row("/clear", "start a new conversation and clear the screen"), SlashCommands.HelpText);
        Assert.Contains(Row("/new", "start a new conversation but do not clear the screen"), SlashCommands.HelpText);
        Assert.Contains(Row("/splash", "start a new conversation and show the splash screen"), SlashCommands.HelpText);   // 2026-09-19
        Assert.Contains(Row("/copy", "copy the last reply to the clipboard as markdown, or /copy <n> | all"), SlashCommands.HelpText);
        Assert.Contains(Row("/tts", "toggle speech output, or /tts on|off"), SlashCommands.HelpText);
        Assert.Contains(Row("/stt", "toggle speech input, or /stt on|off"), SlashCommands.HelpText);
        Assert.Contains(Row("/wake", "toggle the speech input wake word, or /wake on|off"), SlashCommands.HelpText);
        Assert.Contains(Row("/interrupt", "toggle the speech input wake word interrupt, or /interrupt on|off"), SlashCommands.HelpText);
        Assert.Contains(Row("/remember", "add a memory: /remember <text>"), SlashCommands.HelpText);
        Assert.Contains(Row("/memory", "list and prune memory items"), SlashCommands.HelpText);
        Assert.Contains(Row("/forget", "forget all memory"), SlashCommands.HelpText);
        Assert.Contains(Row("/memcopy", "copy this profile's memory into another: /memcopy <profile> [overwrite]"), SlashCommands.HelpText);
        Assert.Contains(Row("/cwd", "show or change the working directory, or /cwd <path> | ~ | browse"), SlashCommands.HelpText);
        Assert.Contains(Row("/tree", "print a tree of the working directory's folders and files, or /tree <path>"), SlashCommands.HelpText);
        Assert.Contains(Row("/explore", "open the working directory in your file browser, or /explore <path>"), SlashCommands.HelpText);
        Assert.Contains(Row("/emptytrash", "empty the working directory's .trash for good (asks first)"), SlashCommands.HelpText);
        Assert.Contains(Row("/git", "write the Git native email and Git native name settings into the working directory's repository: /git user [force]"), SlashCommands.HelpText);   // 2026-09-21
        Assert.Contains(Row("/timer", "list timers, or /timer <duration> [name] (10m, 90s, 1h30m) | stop <name> | stop all"), SlashCommands.HelpText);
        Assert.Contains(Row("/window", "show the terminal window's width and height"), SlashCommands.HelpText);
        Assert.Contains(Row("/persona", "export and manage persona.md (the personality) in your editor, or /persona reset to go back to the default"), SlashCommands.HelpText);
        Assert.Contains(Row("/operata", "export and manage operata.md (the operating rules) in your editor, or /operata reset to go back to the default"), SlashCommands.HelpText);
        Assert.Contains(Row("/vocalia", "export and manage vocalia.md (the spoken-reply directive) in your editor, or /vocalia reset to go back to the default"), SlashCommands.HelpText);
        Assert.Contains(Row("/about", "show general information about the app and profile"), SlashCommands.HelpText);
        Assert.DoesNotContain("M5", SlashCommands.HelpText);
        Assert.DoesNotContain("/ask", SlashCommands.HelpText);   // the three tool switches went 2026-09-18
        Assert.DoesNotContain("/files", SlashCommands.HelpText);
        Assert.DoesNotContain("/web", SlashCommands.HelpText);
        Assert.EndsWith("F4 = talk (push-to-talk key)", SlashCommands.HelpText);

        // A blank line ahead of every group but the first.
        Assert.DoesNotContain("\n\n  /settings", SlashCommands.HelpText);
        foreach (var first in new[] { "/server", "/clear", "/tts", "/memory", "/cwd", "/speak", "/persona", "/timer" })   // nine groups since later on 2026-09-19; /timer leads the last since later still that day
        {
            Assert.Contains("\n\n  " + first + " ", SlashCommands.HelpText);
        }

        Assert.DoesNotContain("\n\n\n", SlashCommands.HelpText);
        Assert.DoesNotContain("\n\n" + SlashCommands.KeysLine, SlashCommands.HelpText);
    }

    [Fact]
    public void HelpEntry_Label_IsTheCommandAndItsAliases()
    {
        Assert.Equal("/tree, /dir, /ls", new SlashCommands.HelpEntry("/tree", "x", "/dir", "/ls").Label);   // the record still joins them; the table lists none but //
        Assert.Equal("/settings, //", new SlashCommands.HelpEntry("/settings", "x", "//").Label);
        Assert.Equal("/tools, ///", SlashCommands.HelpEntries.Single(e => e.Command == "/tools").Label);     // 2026-09-21
        Assert.Equal("/skills, ////", SlashCommands.HelpEntries.Single(e => e.Command == "/skills").Label);  // 2026-09-21
        Assert.Equal("/usage", new SlashCommands.HelpEntry("/usage", "x").Label);
    }

    [Fact]
    public void HelpEntries_AreTheCommandLines_InOrder()
    {
        // Nine groups, the user's order (/exit last beside /about, /compact under /reasoning, 2026-09-16; /speak + /view a
        // group of their own under /windowsize's, 2026-09-17; the three tool switches /ask /files /web — a group of their own
        // from 2026-09-15 — gone later on 2026-09-18, /session under /profile and /copy under /queue the same day; later still on
        // 2026-09-19 /skills + /learn under /session, /windowsize → /window under /view, /timer under /help); the flat list is
        // the groups end to end.
        Assert.Equal(new[] { 7, 6, 7, 4, 4, 5, 4, 3, 4 }, SlashCommands.HelpGroups.Select(g => g.Count));   // /loop under /draft since 2026-09-21   // /git under /emptytrash since 2026-09-21   // /mcp under /tools since 2026-09-20   // /learn under /skill since 2026-09-17; /skills folded into /skill 2026-09-18; /tools under /settings 2026-09-19; /draft under /copy later that day; /splash under /new later still
        Assert.Equal(44, SlashCommands.HelpEntries.Count);   // 44 with /loop, 43 with /git (2026-09-21)
        Assert.Equal(SlashCommands.HelpGroups.SelectMany(g => g), SlashCommands.HelpEntries);
        // /help moved to the bottom group above /about, and /memory heads its group (the user's call, 2026-09-16).
        Assert.Equal(["/settings", "/tools", "/mcp", "/profile", "/session", "/skills", "/learn"], SlashCommands.HelpGroups[0].Select(e => e.Command));   // /session under /profile since later on 2026-09-18; /tools under /settings since 2026-09-19; /skills + /learn under /session later that day
        Assert.Equal("/settings", SlashCommands.HelpEntries[0].Command);
        Assert.Equal("/tools", SlashCommands.HelpEntries[1].Command);
        Assert.Equal("switch the model's tools on or off and edit the Options, Ask, Files and Web settings on a pane", SlashCommands.HelpEntries[1].Summary);
        Assert.Equal("/mcp", SlashCommands.HelpEntries[2].Command);   // under /tools since 2026-09-20
        Assert.Equal("connect external MCP servers and switch their tools on or off on a pane", SlashCommands.HelpEntries[2].Summary);
        Assert.Equal("/profile", SlashCommands.HelpEntries[3].Command);
        Assert.Equal("/session", SlashCommands.HelpEntries[4].Command);
        Assert.Equal("list, restore and purge sessions: /session [<id> | purge <id> | purge older <age> | purge all | title <text>]", SlashCommands.HelpEntries[4].Summary);
        Assert.Equal("/skills", SlashCommands.HelpEntries[5].Command);
        Assert.Equal("list the skills, edit the skill settings and the project file on a pane, or /skills edit <name> to open its SKILL.md", SlashCommands.HelpEntries[5].Summary);   // edit 2026-09-21   // the /skill <name> [message] form went later on 2026-09-18; the Roots tab later on 2026-09-19
        Assert.Equal("/learn", SlashCommands.HelpEntries[6].Command);
        Assert.Equal("write or improve a skill from the last turn or the stored sessions, in the background: /learn [what to keep] | sessions [N | what to search]", SlashCommands.HelpEntries[6].Summary);   // the sessions form 2026-09-19
        Assert.Equal("/server", SlashCommands.HelpEntries[7].Command);
        Assert.Equal("/reasoning", SlashCommands.HelpEntries[9].Command);
        Assert.Equal("/compact", SlashCommands.HelpEntries[10].Command);   // under /reasoning since 2026-09-16
        Assert.Equal("/usage", SlashCommands.HelpEntries[12].Command);
        Assert.Equal("/clear", SlashCommands.HelpEntries[13].Command);
        Assert.Equal("/new", SlashCommands.HelpEntries[14].Command);   // its own row since 2026-09-16
        Assert.Equal("/splash", SlashCommands.HelpEntries[15].Command);   // under /new since later still on 2026-09-19
        Assert.Equal("start a new conversation and show the splash screen", SlashCommands.HelpEntries[15].Summary);
        Assert.Equal("/queue", SlashCommands.HelpEntries[16].Command);   // under /new since 2026-09-18, under /splash since 2026-09-19
        Assert.Equal(["/clear", "/new", "/splash", "/queue", "/copy", "/draft", "/loop"], SlashCommands.HelpGroups[2].Select(e => e.Command));   // /loop under /draft since 2026-09-21   // /copy under /queue since later on 2026-09-18; /draft under /copy since 2026-09-19; /splash under /new later still
        Assert.Equal("list and prune the messages queued while a reply runs", SlashCommands.HelpEntries[16].Summary);
        Assert.Equal("/copy", SlashCommands.HelpEntries[17].Command);
        Assert.Equal("copy the last reply to the clipboard as markdown, or /copy <n> | all", SlashCommands.HelpEntries[17].Summary);
        Assert.Equal("/draft", SlashCommands.HelpEntries[18].Command);
        Assert.Equal("write the next message in your editor: a temporary file, sent when it is saved and closed", SlashCommands.HelpEntries[18].Summary);
        Assert.Equal("/loop", SlashCommands.HelpEntries[19].Command);   // 2026-09-21
        Assert.Equal("repeat a message, each reply waited for: /loop <count> <message> | infinite <message> (ESC ends it)", SlashCommands.HelpEntries[19].Summary);
        Assert.Equal("/tts", SlashCommands.HelpEntries[20].Command);
        Assert.Equal(["/tts", "/stt", "/wake", "/interrupt"], SlashCommands.HelpGroups[3].Select(e => e.Command));
        Assert.Equal(["/memory", "/remember", "/forget", "/memcopy"], SlashCommands.HelpGroups[4].Select(e => e.Command));   // /memcopy last since 2026-09-17
        Assert.Equal("/memory", SlashCommands.HelpEntries[24].Command);
        Assert.Equal("add a memory: /remember <text>", SlashCommands.HelpEntries[25].Summary);
        Assert.Equal("forget all memory", SlashCommands.HelpEntries[26].Summary);
        Assert.Equal("copy this profile's memory into another: /memcopy <profile> [overwrite]", SlashCommands.HelpEntries[27].Summary);
        Assert.Equal("/cwd", SlashCommands.HelpEntries[28].Command);
        Assert.Equal(["/cwd", "/tree", "/explore", "/emptytrash", "/git"], SlashCommands.HelpGroups[5].Select(e => e.Command));   // /git last since 2026-09-21
        Assert.Equal("/emptytrash", SlashCommands.HelpEntries[31].Command);
        Assert.Equal("/git", SlashCommands.HelpEntries[32].Command);   // the working-directory group's last row (2026-09-21)
        Assert.Equal("write the Git native email and Git native name settings into the working directory's repository: /git user [force]", SlashCommands.HelpEntries[32].Summary);
        // /speak and /view: a group of their own (the user's call, 2026-09-17); /window under /view since later on 2026-09-19.
        Assert.Equal(["/speak", "/echo", "/view", "/window"], SlashCommands.HelpGroups[6].Select(e => e.Command));   // /echo between them, later on 2026-09-17
        Assert.Equal("/speak", SlashCommands.HelpEntries[33].Command);
        Assert.Equal("read a text file from the working directory aloud, as a reply: /speak <file> [n], or /speak to resume, or /speak <n> from sentence n", SlashCommands.HelpEntries[33].Summary);
        Assert.Equal("/echo", SlashCommands.HelpEntries[34].Command);
        Assert.Equal("print a line as a reply and read it aloud when speech is on: /echo <text>", SlashCommands.HelpEntries[34].Summary);
        Assert.Equal("/view", SlashCommands.HelpEntries[35].Command);
        Assert.Equal("show an image from the working directory in the transcript, as large as the window allows: /view <image>", SlashCommands.HelpEntries[35].Summary);
        Assert.Equal("/window", SlashCommands.HelpEntries[36].Command);
        Assert.Equal("show the terminal window's width and height", SlashCommands.HelpEntries[36].Summary);
        Assert.Equal("/persona", SlashCommands.HelpEntries[37].Command);
        Assert.Equal(["/persona", "/operata", "/vocalia"], SlashCommands.HelpGroups[7].Select(e => e.Command));
        Assert.Equal(["/timer", "/help", "/about", "/exit"], SlashCommands.HelpGroups[^1].Select(e => e.Command));   // /exit the very last row since 2026-09-16, /help above /about; /timer under /help since later on 2026-09-19, /help under /timer later still that day
        Assert.Equal("/timer", SlashCommands.HelpEntries[40].Command);
        Assert.Equal("list timers, or /timer <duration> [name] (10m, 90s, 1h30m) | stop <name> | stop all", SlashCommands.HelpEntries[40].Summary);
        Assert.Equal("/help", SlashCommands.HelpEntries[41].Command);
        Assert.Equal("/about", SlashCommands.HelpEntries[42].Command);
        Assert.Equal("/exit", SlashCommands.HelpEntries[^1].Command);
        Assert.DoesNotContain(SlashCommands.HelpEntries, e => e.Command == "/windowsize");
        Assert.DoesNotContain(SlashCommands.HelpEntries, e => e.Command is "/ask" or "/files" or "/web");

        // Every word is one entry's command or one of its aliases — never in a summary.
        var labels = SlashCommands.HelpEntries.SelectMany(e => e.Aliases.Prepend(e.Command)).ToList();
        Assert.Equal(labels, labels.Distinct());
        Assert.Equal(SlashCommands.Words.OrderBy(w => w), labels.OrderBy(w => w));
        Assert.All(SlashCommands.HelpEntries, e => Assert.DoesNotContain("(also", e.Summary));
        Assert.All(SlashCommands.HelpEntries, e => Assert.Equal(SlashCommands.Parse(e.Command).Command, SlashCommands.Parse(e.Label.Split(", ")[^1]).Command));

        // The column is measured, not written down: the widest label today.
        Assert.Equal(SlashCommands.HelpEntries.Max(e => e.Label.Length), SlashCommands.LabelWidth);
        Assert.Equal("/settings, //".Length, SlashCommands.LabelWidth);   // 13 since every other alias went (2026-09-16); 22 while it was /settings, /config, //; "/skills, ////" (2026-09-21) is 13 too

        // The plain text is the groups, one line each with a blank line between the groups, between the heading and the key line.
        string[] lines = SlashCommands.HelpText.Split('\n');
        Assert.Equal(1 + 44 + 8 + 1, lines.Length);   // 44 rows with /loop, 43 with /git (2026-09-21); 42 with /mcp (2026-09-20)   // nine groups since later on 2026-09-19; 41 rows with /draft and /splash
        Assert.Equal("Commands:", lines[0]);
        Assert.Equal(SlashCommands.KeysLine, lines[^1]);
        var line = 1;
        foreach (var group in SlashCommands.HelpGroups)
        {
            if (line > 1)
            {
                Assert.Equal("", lines[line++]);
            }

            foreach (var entry in group)
            {
                Assert.Equal(Row(entry.Label, entry.Summary).TrimEnd('\n'), lines[line++]);
            }
        }

        Assert.Equal(lines.Length - 1, line);
    }
}
