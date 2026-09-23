using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NeonSidekick.Llm.Tools;

/// <summary>
/// <c>shift_date(date, days?, weeks?, months?, years?)</c>: calendar arithmetic the model would
/// otherwise do by counting. Years, then months, then weeks and days, so a month-end clamps the
/// way a calendar does (31 January + 1 month = 28 February). The answer names the weekday, which
/// is usually the second half of the question.
/// </summary>
public sealed class ShiftDateTool : AIFunction
{
    public const string ToolName = "shift_date";

    public const string DateArgument = "date";
    public const string DaysArgument = "days";
    public const string WeeksArgument = "weeks";
    public const string MonthsArgument = "months";
    public const string YearsArgument = "years";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "date": {
              "type": "string",
              "description": "The starting date: today, or a date as yyyy-MM-dd."
            },
            "days": { "type": "integer", "description": "Days to add; negative to go back." },
            "weeks": { "type": "integer", "description": "Weeks to add; negative to go back." },
            "months": { "type": "integer", "description": "Months to add; negative to go back. A month-end clamps: 31 January plus 1 month is 28 February." },
            "years": { "type": "integer", "description": "Years to add; negative to go back." }
          },
          "required": ["date"]
        }
        """);

    private readonly TimeProvider _time;

    public ShiftDateTool(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public override string Name => ToolName;

    public override string Description =>
        "Adds or subtracts days, weeks, months or years to a date and returns the resulting date with its weekday. " +
        "Use it instead of counting days yourself.";

    public override JsonElement JsonSchema => Schema;

    /// <summary>
    /// <c>2026-10-23 is a Friday, 6 weeks after 2026-09-11</c>; <c>… before …</c> when every
    /// amount is negative, <c>… from …</c> with signed amounts when they disagree; with nothing
    /// to add, <c>2026-09-11 is a Friday</c>. Pinned.
    /// </summary>
    public static string Describe(DateOnly start, int years, int months, int weeks, int days)
    {
        DateOnly result;
        try
        {
            result = start.AddYears(years).AddMonths(months).AddDays(checked(weeks * 7 + days));
        }
        catch (ArgumentOutOfRangeException)
        {
            return ClockText.OutOfRange;
        }
        catch (OverflowException)
        {
            return ClockText.OutOfRange;
        }

        string head = ClockText.Iso(result) + " is a " + ClockText.Weekday(result);
        if (years == 0 && months == 0 && weeks == 0 && days == 0)
        {
            return head;
        }

        bool allForward = years >= 0 && months >= 0 && weeks >= 0 && days >= 0;
        bool allBack = years <= 0 && months <= 0 && weeks <= 0 && days <= 0;
        string relation = allForward ? "after" : allBack ? "before" : "from";
        string amounts = allForward || allBack
            ? ClockText.Amounts(Math.Abs(years), Math.Abs(months), Math.Abs(weeks), Math.Abs(days))
            : ClockText.Amounts(years, months, weeks, days);
        return head + ", " + amounts + " " + relation + " " + ClockText.Iso(start);
    }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Run(arguments));
    }

    private string Run(AIFunctionArguments arguments)
    {
        var today = ClockText.LocalDate(_time);
        string dateText = ToolArguments.ReadString(arguments, DateArgument);
        if (!ClockText.TryParseDate(dateText, today, out var start))
        {
            return ClockText.BadDate(DateArgument, dateText);
        }

        if (!ToolArguments.TryReadInt32(arguments, YearsArgument, out var years, out var raw))
        {
            return ClockText.BadInteger(YearsArgument, raw);
        }

        if (!ToolArguments.TryReadInt32(arguments, MonthsArgument, out var months, out raw))
        {
            return ClockText.BadInteger(MonthsArgument, raw);
        }

        if (!ToolArguments.TryReadInt32(arguments, WeeksArgument, out var weeks, out raw))
        {
            return ClockText.BadInteger(WeeksArgument, raw);
        }

        if (!ToolArguments.TryReadInt32(arguments, DaysArgument, out var days, out raw))
        {
            return ClockText.BadInteger(DaysArgument, raw);
        }

        return Describe(start, years ?? 0, months ?? 0, weeks ?? 0, days ?? 0);
    }
}
