using NeonCompanion.Llm;

namespace NeonCompanion.App;

/// <summary>
/// The words for <c>/compact</c>: one transcript notice per outcome. Pure statics, every string
/// pinned. The summary itself is never shown; the figures are the message counts either side and,
/// for a summary with a usage report, the summariser's own request — what it read → what it wrote
/// — not the new context in use, which is only measured at the next reply.
/// </summary>
public static class CompactionText
{
    /// <summary>
    /// What every line of <c>/compact</c> or the automatic compact opens with, inside its parentheses
    /// (2026-09-19, the user's pick; <c>ChatScreen.TrashGlyph</c>'s shape): the clamp U+1F5DC with
    /// its variation selector — bare it is text-presentation — and a space. A line where only tool
    /// results were pruned wears <see cref="Assistant.PruneGlyph"/> instead. Pinned.
    /// </summary>
    public const string CompactGlyph = "🗜️ ";

    public const string NothingToCompact = "(" + CompactGlyph + "nothing to compact)";
    public const string Cancelled = "(" + CompactGlyph + "compact cancelled)";

    /// <summary>The prefix of a failure's error line: <c>🗜️ Compact failed: </c> + <c>Assistant.Explain</c>.</summary>
    public const string FailedPrefix = CompactGlyph + "Compact failed: ";

    /// <summary>The spinner's label while the summariser runs.</summary>
    public const string CompactingLabel = "compacting the conversation";

    /// <summary>
    /// <c>(🗜️ compacted: 38 messages → 7 · 41.2k → 3.1k tokens)</c> for a summary with a usage report,
    /// <c>(🗜️ compacted: 38 messages → 7)</c> without one, <c>(✂️ compacted: 12 tool results pruned)</c> for
    /// a prune (the scissors: nothing was summarised); <c>auto-compacted at 83%</c> in place of
    /// <c>compacted</c> when the threshold fired, and a summary that also stubbed the kept turns'
    /// results (the automatic compact) ends <c> · 12 tool results pruned</c> under the clamp still.
    /// </summary>
    public static string Notice(ConversationCompactor.Result result, int? autoPercent)
    {
        ArgumentNullException.ThrowIfNull(result);
        string glyph = result.Summarised ? CompactGlyph : Assistant.PruneGlyph;
        string head = autoPercent is { } percent ? "auto-compacted at " + UsageText.PercentText(percent) : "compacted";
        string pruned = UsageText.Plural(result.Pruned, "tool result", "tool results") + " pruned";
        string body;
        if (!result.Summarised)
        {
            body = pruned;
        }
        else
        {
            body = result.MessagesBefore + " messages → " + result.MessagesAfter;
            if (result.Usage is { } usage)
            {
                body += " · " + UsageText.CompactNumber(usage.Input) + " → " + UsageText.CompactNumber(usage.Output) + " tokens";
            }

            if (result.Pruned > 0)
            {
                body += " · " + pruned;
            }
        }

        return "(" + glyph + head + ": " + body + ")";
    }
}
