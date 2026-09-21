using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Timers;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>start_timer(name?, hours?, minutes?, seconds?)</c>: a named countdown on the
/// <see cref="TimerBoard"/>. Integers per unit rather than a duration string, because a small
/// model fills three integer slots more reliably than it formats "1h30m"; a blank name becomes
/// the duration (<see cref="TimerText.DefaultName"/>) so "start a 5 minute timer" needs nothing
/// more. The result is a sentence (<see cref="TimerText"/>) the transcript shows verbatim.
/// </summary>
public sealed class StartTimerTool : AIFunction
{
    public const string ToolName = "start_timer";

    public const string NameArgument = "name";
    public const string HoursArgument = "hours";
    public const string MinutesArgument = "minutes";
    public const string SecondsArgument = "seconds";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "What the timer is for, a word or two: cooking, tea, laundry. Leave it out to name it after its duration." },
            "hours": { "type": "integer", "description": "Hours to count down." },
            "minutes": { "type": "integer", "description": "Minutes to count down." },
            "seconds": { "type": "integer", "description": "Seconds to count down." }
          }
        }
        """);

    private readonly TimerBoard _board;

    public StartTimerTool(TimerBoard board)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
    }

    public override string Name => ToolName;

    public override string Description =>
        "Starts a named countdown timer; the user is alerted when it ends. Give the duration as hours, minutes and/or seconds. " +
        "Several timers can run at once, each with its own name.";

    public override JsonElement JsonSchema => Schema;

    /// <summary>The board's answer as the sentence the model reads. Pinned.</summary>
    public static string Describe(TimerStartResult result) => result.Outcome switch
    {
        TimerStartOutcome.Started => TimerText.Started(result.Timer),
        TimerStartOutcome.Duplicate => TimerText.Duplicate(result.Timer),
        TimerStartOutcome.Full => TimerText.Full(TimerBoard.MaxTimers),
        TimerStartOutcome.BadName => TimerText.BadName,
        _ => TimerText.BadDuration,
    };

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Run(arguments));
    }

    private string Run(AIFunctionArguments arguments)
    {
        if (!ToolArguments.TryReadInt32(arguments, HoursArgument, out var hours, out var raw))
        {
            return ClockText.BadInteger(HoursArgument, raw);
        }

        if (!ToolArguments.TryReadInt32(arguments, MinutesArgument, out var minutes, out raw))
        {
            return ClockText.BadInteger(MinutesArgument, raw);
        }

        if (!ToolArguments.TryReadInt32(arguments, SecondsArgument, out var seconds, out raw))
        {
            return ClockText.BadInteger(SecondsArgument, raw);
        }

        if (hours is null && minutes is null && seconds is null)
        {
            return TimerText.NoDuration;
        }

        long total = (hours ?? 0) * 3600L + (minutes ?? 0) * 60L + (seconds ?? 0);
        if (total <= 0 || total > TimerBoard.MaxDuration.TotalSeconds)
        {
            return TimerText.BadDuration;
        }

        var duration = TimeSpan.FromSeconds(total);
        string name = TimerText.NormalizeName(ToolArguments.ReadString(arguments, NameArgument));
        if (name.Length == 0)
        {
            name = TimerText.DefaultName(duration);
        }

        return Describe(_board.Start(name, duration));
    }
}
