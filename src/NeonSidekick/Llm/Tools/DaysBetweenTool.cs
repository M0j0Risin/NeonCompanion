using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NeonSidekick.Llm.Tools;

/// <summary>
/// <c>days_between(from, to)</c>: the signed day count between two dates, with weeks alongside
/// once there are any. "How many days until Christmas" is <c>from: today, to: 2026-12-25</c>.
/// </summary>
public sealed class DaysBetweenTool : AIFunction
{
    public const string ToolName = "days_between";

    public const string FromArgument = "from";
    public const string ToArgument = "to";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "from": {
              "type": "string",
              "description": "The first date: today, or a date as yyyy-MM-dd."
            },
            "to": {
              "type": "string",
              "description": "The second date: today, or a date as yyyy-MM-dd."
            }
          },
          "required": ["from", "to"]
        }
        """);

    private readonly TimeProvider _time;

    public DaysBetweenTool(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public override string Name => ToolName;

    public override string Description =>
        "Counts the days from one date to another (negative when the second date is earlier). " +
        "Use it for \"how many days until\" and \"how long since\" questions instead of counting yourself.";

    public override JsonElement JsonSchema => Schema;

    /// <summary>
    /// <c>105 days (15 weeks) from 2026-09-11 to 2026-12-25</c>; <c>(2 weeks and 3 days)</c> with a
    /// remainder; no parenthesis under a week; <c>-3 days from … to …</c> backwards. Pinned.
    /// </summary>
    public static string Describe(DateOnly from, DateOnly to)
    {
        int days = to.DayNumber - from.DayNumber;
        string text = ClockText.Count(days, "day");
        int magnitude = Math.Abs(days);
        if (magnitude >= 7)
        {
            int weeks = magnitude / 7;
            int rest = magnitude % 7;
            text += " (" + ClockText.Count(weeks, "week") + (rest == 0 ? "" : " and " + ClockText.Count(rest, "day")) + ")";
        }

        return text + " from " + ClockText.Iso(from) + " to " + ClockText.Iso(to);
    }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Run(arguments));
    }

    private string Run(AIFunctionArguments arguments)
    {
        var today = ClockText.LocalDate(_time);
        string fromText = ToolArguments.ReadString(arguments, FromArgument);
        if (!ClockText.TryParseDate(fromText, today, out var from))
        {
            return ClockText.BadDate(FromArgument, fromText);
        }

        string toText = ToolArguments.ReadString(arguments, ToArgument);
        if (!ClockText.TryParseDate(toText, today, out var to))
        {
            return ClockText.BadDate(ToArgument, toText);
        }

        return Describe(from, to);
    }
}
