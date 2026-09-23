using System.Globalization;
using Microsoft.Extensions.AI;

namespace NeonSidekick.Llm;

/// <summary>
/// <c>/compact</c>: shrinks what the model re-reads on every request without forgetting it the way
/// <c>/clear</c> does. Two modes (<see cref="CompactMode"/>): the older turns become one summary
/// message written by the model itself, or their bulky tool results become one-line stubs. Either
/// way the most recent turns (the setting <c>LLM compact keep recent</c>) stay word for word.
///
/// <para>Pure statics over the message list plus one orchestration (<see cref="RunAsync"/>) that
/// calls the summariser and swaps the history; nothing here touches the console, and the history
/// is untouched until the new list is complete, so a cancelled or failed summary leaves the
/// conversation as it was.</para>
///
/// <para>The shape after a summary: <c>user(summary) → the opening call pairs → the recent turns</c>.
/// A user message, because every chat template accepts what follows one and because it keeps
/// <see cref="ConversationHistory.TurnCount"/> above zero, so the next turn does not seed the
/// opening calls again; the clock and working-directory pairs are carried over as they were, so
/// <c>/cwd</c>'s in-place edit (<see cref="ConversationHistory.TryReplaceToolResult"/>) still finds
/// its result. The summary turn then ages out at <see cref="ConversationHistory.MaxTurns"/> like any
/// other.</para>
/// </summary>
public static class ConversationCompactor
{
    /// <summary>A tool result longer than this (characters) is stubbed by <see cref="Prune"/>; shorter ones are left, a stub would not be smaller.</summary>
    public const int PruneThreshold = 200;

    /// <summary>The summariser's system prompt. Pinned.</summary>
    public const string SummaryInstruction =
        "You are compacting a conversation between a user and Neon, a terminal sidekick with tools. " +
        "Write a summary Neon can continue the conversation from as if it remembered everything: the user's goals and requests, " +
        "what was decided or answered, every concrete fact learned from tool results (file names, paths, values, names, dates — even ones the user has not asked about yet), " +
        "anything still open or promised, and the user's preferences and tone. Be specific and concise, in plain text without markdown. " +
        "Do not answer the user, do not add commentary, and do not mention that this is a summary.";

    /// <summary>The line that asks for the summary, closing the summariser's request. Pinned.</summary>
    public const string SummaryRequestLine = "Summarise the conversation above.";

    /// <summary>What goes before the summary in the message the history keeps. Pinned.</summary>
    public const string SummaryPreamble = "The conversation so far was compacted into this summary; continue as if you remembered it:\n\n";

    /// <summary>One <see cref="ChatMessage"/> list split for a compact: the older turns (the opening call pairs among them, in place), those pairs alone, and the recent turns kept verbatim.</summary>
    public sealed record Plan(IReadOnlyList<ChatMessage> Older, IReadOnlyList<ChatMessage> Opening, IReadOnlyList<ChatMessage> Recent)
    {
        /// <summary>Whether there is anything to compact: an older turn that is not just the opening pairs.</summary>
        public bool HasOlderTurns => Older.Count > Opening.Count;
    }

    /// <summary>
    /// What a compact did: the message counts either side, the results stubbed (a prune, or the
    /// recent turns' under the automatic compact), the summariser's usage when the server reported
    /// one, and whether the older turns became a summary at all (false for a prune, and for the
    /// automatic compact that found nothing older and stubbed the recent turns alone). Since
    /// 2026-09-21 (<c>LLM compact show summary</c>) it also carries what the transcript may show:
    /// the summary's text, one <see cref="PrunedEntry"/> per stubbed result and (later that day,
    /// the detail's closing lines) how many messages were protected at either end.
    /// </summary>
    public sealed record Result(int MessagesBefore, int MessagesAfter, int Pruned, TokenUsage? Usage, bool Summarised = false)
    {
        /// <summary>The summariser's text, trimmed, when <see cref="Summarised"/>; null otherwise.</summary>
        public string? Summary { get; init; }

        /// <summary>One entry per stubbed result — the older turns' first, then the recent turns' under the automatic compact — in message order; <see cref="Pruned"/> is their count (a carrier's pictures counted each). Empty when none.</summary>
        public IReadOnlyList<PrunedEntry> Entries { get; init; } = [];

        /// <summary>How many messages at the start were protected: the opening call pairs (<see cref="Plan.Opening"/>), carried across a summary in place and never stubbed by a prune.</summary>
        public int OpeningKept { get; init; }

        /// <summary>How many messages at the end were protected: the recent turns (<see cref="Plan.Recent"/>) kept in place — verbatim by hand, their older results stubbed under the automatic compact.</summary>
        public int RecentKept { get; init; }
    }

    /// <summary>
    /// One stubbed result (2026-09-21): the tool that produced it (<see cref="UnknownTool"/> when no
    /// call in the history carries its id) and its length in characters, or — for a carrier's
    /// pictures — <c>view_image</c> with <paramref name="Pictures"/> above zero and no length.
    /// </summary>
    public sealed record PrunedEntry(string Tool, int Characters, int Pictures = 0);

    /// <summary>The tool name an entry carries when the result's call is not in the history (a hand-built list). Pinned.</summary>
    public const string UnknownTool = "tool";

    /// <summary>The summariser's closing user message: <see cref="SummaryRequestLine"/>, the focus appended when given. Pinned.</summary>
    public static string SummaryRequest(string? focus) =>
        string.IsNullOrWhiteSpace(focus) ? SummaryRequestLine : SummaryRequestLine + " Pay particular attention to: " + focus.Trim();

    /// <summary>The stub a pruned result becomes: <c>(a 4,312-character result, pruned by /compact)</c>. Pinned.</summary>
    public static string PrunedStub(int length) =>
        "(a " + length.ToString("N0", CultureInfo.InvariantCulture) + "-character result, pruned by /compact)";

    /// <summary>What a carrier's pictures become: <c>(a picture from view_image, pruned by /compact)</c>, <c>(2 pictures …)</c>. Pinned.</summary>
    public static string PrunedImageStub(int pictures) =>
        (pictures == 1 ? "(a picture" : "(" + pictures.ToString(CultureInfo.InvariantCulture) + " pictures") + " from " + Tools.ViewImageTool.ToolName + ", pruned by /compact)";

    /// <summary>
    /// Splits <paramref name="messages"/> at a user-message boundary: the last <paramref name="keepRecent"/>
    /// user turns are <see cref="Plan.Recent"/> (every message from the first of them to the end),
    /// the rest <see cref="Plan.Older"/>; zero keeps nothing verbatim, and a history with fewer
    /// turns than that has nothing older. The opening call pairs (<see cref="Assistant.IsOpeningCallId"/>)
    /// found among the older messages are listed again as <see cref="Plan.Opening"/>. Copies, never views.
    /// </summary>
    public static Plan Split(IReadOnlyList<ChatMessage> messages, int keepRecent)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentOutOfRangeException.ThrowIfNegative(keepRecent);
        var users = new List<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            if (ConversationHistory.IsTurnStart(messages[i]))
            {
                users.Add(i);
            }
        }

        int cut = keepRecent == 0 ? messages.Count : users.Count < keepRecent ? 0 : users[^keepRecent];
        var older = new List<ChatMessage>(cut);
        var opening = new List<ChatMessage>(4);
        for (int i = 0; i < cut; i++)
        {
            older.Add(messages[i]);
            if (OpeningCallId(messages[i]) is { } callId && i + 1 < cut && HasResult(messages[i + 1], callId))
            {
                opening.Add(messages[i]);
                opening.Add(messages[i + 1]);
                older.Add(messages[++i]);
            }
        }

        var recent = new List<ChatMessage>(messages.Count - cut);
        for (int i = cut; i < messages.Count; i++)
        {
            recent.Add(messages[i]);
        }

        return new Plan(older, opening, recent);
    }

    /// <summary>
    /// The list with every tool result in the older turns longer than <see cref="PruneThreshold"/>
    /// replaced by <see cref="PrunedStub"/> — new messages and contents, the held ones untouched
    /// (<see cref="ConversationHistory.TryReplaceToolResult"/> stays the one in-place edit) — and the
    /// recent turns as they are. The opening pairs' results are never stubbed. A carrier's pictures
    /// (<see cref="ConversationHistory.IsImageCarrier"/>) go the same way — a new tagged message with
    /// <see cref="PrunedImageStub"/> for its text and no image parts, each picture counted as one
    /// result — since a picture is the bulkiest result there is. The count is how many were.
    /// <paramref name="entries"/>, when given, receives one <see cref="PrunedEntry"/> per stub (2026-09-21).
    /// </summary>
    public static (List<ChatMessage> Messages, int Pruned) Prune(Plan plan, bool protectSkills = true, List<PrunedEntry>? entries = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var messages = new List<ChatMessage>(plan.Older.Count + plan.Recent.Count);
        int pruned = Stub(plan.Older, 0, plan.Older.Count, messages, protectSkills, entries, entries is null ? null : CallNames(plan.Older));
        messages.AddRange(plan.Recent);
        return (messages, pruned);
    }

    /// <summary>
    /// The list with the last turn's older tool results stubbed the way <see cref="Prune"/> stubs
    /// the older turns': every tool result and carrier from the last user message up to — not
    /// including — the last <see cref="ChatRole.Tool"/> message and what follows it, which are the
    /// last iteration's, the ones the model has not read yet. Everything before the last turn is
    /// copied as it is. The tool loop's mid-turn guard (<see cref="Assistant.ContextGuard"/>) and,
    /// over the recent turns it keeps, the automatic compact (<see cref="RunAsync"/>) both use this
    /// rule. Nothing to stub (no turn, no earlier iteration, no result long enough) is a copy and zero.
    /// <paramref name="entries"/>, when given, receives one <see cref="PrunedEntry"/> per stub (2026-09-21).
    /// </summary>
    public static (List<ChatMessage> Messages, int Pruned) PruneRecent(IReadOnlyList<ChatMessage> messages, bool protectSkills = true, List<PrunedEntry>? entries = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        int start = -1;
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (ConversationHistory.IsTurnStart(messages[i]))
            {
                start = i;
                break;
            }
        }

        var result = new List<ChatMessage>(messages.Count);
        if (start < 0)
        {
            result.AddRange(messages);
            return (result, 0);
        }

        for (int i = 0; i < start; i++)
        {
            result.Add(messages[i]);
        }

        int pruned = StubBeforeLastIteration(messages, start, result, protectSkills, entries, entries is null ? null : CallNames(messages));
        return (result, pruned);
    }

    /// <summary>
    /// Appends <paramref name="messages"/>[<paramref name="from"/>..] to <paramref name="into"/> with
    /// the tool results and carriers before the last iteration stubbed: the last <see cref="ChatRole.Tool"/>
    /// message at or after <paramref name="from"/> and what follows it are copied. Returns the count stubbed.
    /// </summary>
    private static int StubBeforeLastIteration(IReadOnlyList<ChatMessage> messages, int from, List<ChatMessage> into, bool protectSkills, List<PrunedEntry>? entries = null, IReadOnlyDictionary<string, string>? names = null)
    {
        int keep = from;
        for (int i = messages.Count - 1; i >= from; i--)
        {
            if (messages[i].Role == ChatRole.Tool)
            {
                keep = i;
                break;
            }
        }

        int pruned = Stub(messages, from, keep, into, protectSkills, entries, names);
        for (int i = keep; i < messages.Count; i++)
        {
            into.Add(messages[i]);
        }

        return pruned;
    }

    /// <summary>
    /// Appends <paramref name="messages"/>[<paramref name="from"/>..<paramref name="to"/>) to <paramref name="into"/>,
    /// the tool results over the threshold and the carriers' pictures stubbed; returns the count stubbed.
    /// The opening pairs' results never are; a loaded skill's (<see cref="ConversationHistory.IsSkillResult"/>)
    /// is not while <paramref name="protectSkills"/> — the <c>Skill compact mode</c> setting.
    /// With <paramref name="entries"/> each stub is logged there, its tool looked up in <paramref name="names"/> (<see cref="CallNames"/>).
    /// </summary>
    private static int Stub(IReadOnlyList<ChatMessage> messages, int from, int to, List<ChatMessage> into, bool protectSkills, List<PrunedEntry>? entries, IReadOnlyDictionary<string, string>? names)
    {
        int pruned = 0;
        for (int index = from; index < to; index++)
        {
            var message = messages[index];
            if (ConversationHistory.IsImageCarrier(message))
            {
                int pictures = message.Contents.Count(c => c is DataContent);
                if (pictures == 0)
                {
                    into.Add(message);
                    continue;
                }

                into.Add(new ChatMessage(ChatRole.User, PrunedImageStub(pictures))
                {
                    AdditionalProperties = new AdditionalPropertiesDictionary { [ConversationHistory.CarrierKey] = true },
                });
                pruned += pictures;
                entries?.Add(new PrunedEntry(Tools.ViewImageTool.ToolName, 0, pictures));
                continue;
            }

            if (message.Role != ChatRole.Tool)
            {
                into.Add(message);
                continue;
            }

            List<AIContent>? rebuilt = null;
            for (int i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is FunctionResultContent { Result: string text } result
                    && text.Length > PruneThreshold && !Assistant.IsOpeningCallId(result.CallId)
                    && !(protectSkills && ConversationHistory.IsSkillResult(result)))
                {
                    rebuilt ??= new List<AIContent>(message.Contents);
                    rebuilt[i] = new FunctionResultContent(result.CallId, PrunedStub(text.Length));
                    pruned++;
                    entries?.Add(new PrunedEntry(names is not null && names.TryGetValue(result.CallId, out string? tool) ? tool : UnknownTool, text.Length));
                }
            }

            into.Add(rebuilt is null ? message : new ChatMessage(ChatRole.Tool, rebuilt));
        }

        return pruned;
    }

    /// <summary>
    /// Every tool call's id → the tool's name, over the assistant messages of <paramref name="messages"/>
    /// (2026-09-21): what a stubbed result is named by in its <see cref="PrunedEntry"/>, since a result
    /// carries only the call's id. A repeated id keeps the first.
    /// </summary>
    public static Dictionary<string, string> CallNames(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            if (message.Role != ChatRole.Assistant)
            {
                continue;
            }

            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent call && !string.IsNullOrEmpty(call.CallId))
                {
                    names.TryAdd(call.CallId, call.Name);
                }
            }
        }

        return names;
    }

    /// <summary>The list after a summary: <see cref="SummaryPreamble"/> + <paramref name="summary"/> as one user message, then the opening pairs, then the recent turns.</summary>
    public static List<ChatMessage> Summarised(string summary, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(plan);
        var messages = new List<ChatMessage>(1 + plan.Opening.Count + plan.Recent.Count)
        {
            new(ChatRole.User, SummaryPreamble + summary.Trim()),
        };
        messages.AddRange(plan.Opening);
        messages.AddRange(plan.Recent);
        return messages;
    }

    /// <summary>
    /// Whether the next turn should compact first: <paramref name="percent"/> above zero, a known
    /// window, and the last request's context (<see cref="TokenTally.LastRequest"/>) at or past
    /// that share of it. A zeroed last request — nothing sent yet, or just compacted — never fires.
    /// </summary>
    public static bool ShouldAutoCompact(TokenUsage lastRequest, ContextLength? window, int percent) =>
        percent > 0 && window is { Tokens: > 0 } w && lastRequest.Total > 0 && lastRequest.Total * 100L >= (long)w.Tokens * percent;

    /// <summary>
    /// One compact of <paramref name="assistant"/>'s history: null when there is nothing to do (no
    /// older turn; for <see cref="CompactMode.Prune"/>, no result long enough), otherwise the new
    /// list is swapped in and <see cref="TokenTally.AddCompaction"/> told. The summariser's
    /// exceptions (a failed or cancelled request, an empty summary) propagate with the history untouched.
    ///
    /// <para>With <paramref name="pruneRecent"/> (the automatic compact, never <c>/compact</c> by
    /// hand) the recent turns it keeps lose their older tool results too, by <see cref="PruneRecent"/>'s
    /// rule — the last turn's last iteration stays — so a turn that walked the context to the share
    /// by itself shrinks even though it is kept; with nothing older that alone is the compact, no
    /// summariser request (one over that history is what just failed), and the count is the result's.</para>
    /// </summary>
    public static async Task<Result?> RunAsync(Assistant assistant, TokenTally tally, CompactMode mode, int keepRecent, string? focus, CancellationToken cancellationToken, bool pruneRecent = false, bool protectSkills = true)
    {
        ArgumentNullException.ThrowIfNull(assistant);
        ArgumentNullException.ThrowIfNull(tally);
        var history = assistant.History;
        var plan = Split(history.Messages, keepRecent);
        int before = history.Messages.Count;
        int recentPruned = 0;
        var recentEntries = new List<PrunedEntry>();
        if (pruneRecent && plan.Recent.Count > 0)
        {
            var kept = new List<ChatMessage>(plan.Recent.Count);
            recentPruned = StubBeforeLastIteration(plan.Recent, 0, kept, protectSkills, recentEntries, CallNames(plan.Recent));
            if (recentPruned > 0)
            {
                plan = plan with { Recent = kept };
            }
        }

        if (!plan.HasOlderTurns)
        {
            if (recentPruned == 0)
            {
                return null;
            }

            var shrunk = new List<ChatMessage>(plan.Older.Count + plan.Recent.Count);
            shrunk.AddRange(plan.Older);
            shrunk.AddRange(plan.Recent);
            history.Replace(shrunk);
            tally.AddCompaction(null);
            return new Result(before, shrunk.Count, recentPruned, null) { Entries = recentEntries, OpeningKept = plan.Opening.Count, RecentKept = plan.Recent.Count };
        }

        if (mode == CompactMode.Prune)
        {
            var olderEntries = new List<PrunedEntry>();
            var (pruned, count) = Prune(plan, protectSkills, olderEntries);
            count += recentPruned;
            if (count == 0)
            {
                return null;
            }

            history.Replace(pruned);
            tally.AddCompaction(null);
            return new Result(before, pruned.Count, count, null) { Entries = [.. olderEntries, .. recentEntries], OpeningKept = plan.Opening.Count, RecentKept = plan.Recent.Count };
        }

        var (summary, usage) = await assistant.SummarizeAsync(plan.Older, focus, cancellationToken).ConfigureAwait(false);
        var messages = Summarised(summary, plan);
        history.Replace(messages);
        tally.AddCompaction(usage);
        return new Result(before, messages.Count, recentPruned, usage, Summarised: true) { Summary = summary.Trim(), Entries = recentEntries, OpeningKept = plan.Opening.Count, RecentKept = plan.Recent.Count };
    }

    /// <summary>The opening call id an assistant message carries, or null.</summary>
    private static string? OpeningCallId(ChatMessage message)
    {
        if (message.Role != ChatRole.Assistant)
        {
            return null;
        }

        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent call && Assistant.IsOpeningCallId(call.CallId))
            {
                return call.CallId;
            }
        }

        return null;
    }

    private static bool HasResult(ChatMessage message, string callId) =>
        message.Role == ChatRole.Tool && message.Contents.Any(c => c is FunctionResultContent r && string.Equals(r.CallId, callId, StringComparison.Ordinal));
}
