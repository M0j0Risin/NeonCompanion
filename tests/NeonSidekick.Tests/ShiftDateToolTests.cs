using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonSidekick.Llm.Tools;
using NeonSidekick.Tests.Fakes;

namespace NeonSidekick.Tests;

public class ShiftDateToolTests
{
    private static readonly DateOnly Today = new(2026, 9, 11);

    private readonly ShiftDateTool _tool = new(new ManualTimeProvider());

    private static AIFunctionArguments Args(params (string Name, object? Value)[] values)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var (name, value) in values)
        {
            dictionary[name] = value;
        }

        return new AIFunctionArguments(dictionary);
    }

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    [Fact]
    public void Schema_HasTheDateRequired_AndFourOptionalIntegers()
    {
        Assert.Equal("shift_date", ShiftDateTool.ToolName);
        Assert.Equal(ShiftDateTool.ToolName, _tool.Name);
        Assert.Contains("weekday", _tool.Description);
        Assert.Contains("instead of counting", _tool.Description);

        var schema = _tool.JsonSchema;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        var properties = schema.GetProperty("properties");
        Assert.Equal(
            new[] { ShiftDateTool.DateArgument, ShiftDateTool.DaysArgument, ShiftDateTool.WeeksArgument, ShiftDateTool.MonthsArgument, ShiftDateTool.YearsArgument },
            properties.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("string", properties.GetProperty("date").GetProperty("type").GetString());
        foreach (var amount in new[] { "days", "weeks", "months", "years" })
        {
            Assert.Equal("integer", properties.GetProperty(amount).GetProperty("type").GetString());
            Assert.Contains("negative", properties.GetProperty(amount).GetProperty("description").GetString());
        }

        Assert.Contains("clamps", properties.GetProperty("months").GetProperty("description").GetString());
        Assert.Equal(new[] { "date" }, schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void Describe_IsPinned()
    {
        Assert.Equal("2026-10-23 is a Friday, 6 weeks after 2026-09-11", ShiftDateTool.Describe(Today, 0, 0, 6, 0));
        Assert.Equal("2026-10-23 is a Friday, 42 days after 2026-09-11", ShiftDateTool.Describe(Today, 0, 0, 0, 42));
        Assert.Equal("2026-09-11 is a Friday", ShiftDateTool.Describe(Today, 0, 0, 0, 0));
        Assert.Equal("2026-09-10 is a Thursday, 1 day before 2026-09-11", ShiftDateTool.Describe(Today, 0, 0, 0, -1));
        Assert.Equal("2026-07-08 is a Wednesday, 2 months and 3 days before 2026-09-11", ShiftDateTool.Describe(Today, 0, -2, 0, -3));
        Assert.Equal("2027-11-14 is a Sunday, 1 year, 2 months and 3 days after 2026-09-11", ShiftDateTool.Describe(Today, 1, 2, 0, 3));
        Assert.Equal("2026-08-31 is a Monday, -2 weeks and 3 days from 2026-09-11", ShiftDateTool.Describe(Today, 0, 0, -2, 3));
    }

    [Fact]
    public void Describe_ClampsAMonthEnd_AndALeapDay()
    {
        Assert.Equal("2026-02-28 is a Saturday, 1 month after 2026-01-31", ShiftDateTool.Describe(new DateOnly(2026, 1, 31), 0, 1, 0, 0));
        Assert.Equal("2028-02-29 is a Tuesday, 1 month after 2028-01-31", ShiftDateTool.Describe(new DateOnly(2028, 1, 31), 0, 1, 0, 0));
        Assert.Equal("2029-02-28 is a Wednesday, 1 year after 2028-02-29", ShiftDateTool.Describe(new DateOnly(2028, 2, 29), 1, 0, 0, 0));
    }

    [Fact]
    public void Describe_OutOfRange_IsAnErrorSentence()
    {
        Assert.Equal("Error: that date is out of range", ShiftDateTool.Describe(Today, 8000, 0, 0, 0));
        Assert.Equal("Error: that date is out of range", ShiftDateTool.Describe(Today, 0, 0, int.MaxValue, 0));
        Assert.Equal("Error: that date is out of range", ShiftDateTool.Describe(Today, -3000, 0, 0, 0));
    }

    [Fact]
    public async Task Invoke_WithJsonArguments_ShiftsFromToday()
    {
        object? answer = await _tool.InvokeAsync(Args(("date", Json("\"today\"")), ("weeks", Json("6"))), CancellationToken.None);

        Assert.Equal("2026-10-23 is a Friday, 6 weeks after 2026-09-11", answer);
    }

    [Fact]
    public async Task Invoke_WithStringArguments_ShiftsAnIsoDate()
    {
        object? answer = await _tool.InvokeAsync(Args(("date", "2026-12-25"), ("days", "-7"), ("months", 1)), CancellationToken.None);

        Assert.Equal("2027-01-18 is a Monday, 1 month and -7 days from 2026-12-25", answer);
    }

    [Fact]
    public async Task Invoke_WithNoAmounts_NamesTheWeekday()
    {
        object? answer = await _tool.InvokeAsync(Args(("date", "2026-12-25")), CancellationToken.None);

        Assert.Equal("2026-12-25 is a Friday", answer);
    }

    [Fact]
    public async Task Invoke_WithEmptyAndNullAmounts_TreatsThemAsZero()
    {
        object? answer = await _tool.InvokeAsync(Args(("date", "today"), ("days", ""), ("weeks", Json("null")), ("months", null), ("years", Json("1"))), CancellationToken.None);

        Assert.Equal("2027-09-11 is a Saturday, 1 year after 2026-09-11", answer);
    }

    [Fact]
    public async Task Invoke_WithABadDate_IsAnErrorSentence()
    {
        Assert.Equal("Error: 'next friday' is not a date for 'date'; use today or yyyy-MM-dd", await _tool.InvokeAsync(Args(("date", "next friday")), CancellationToken.None));
        Assert.Equal("Error: '' is not a date for 'date'; use today or yyyy-MM-dd", await _tool.InvokeAsync(new AIFunctionArguments(), CancellationToken.None));
    }

    [Fact]
    public async Task Invoke_WithANonIntegerAmount_IsAnErrorSentence()
    {
        Assert.Equal("Error: 'many' is not a whole number for 'days'", await _tool.InvokeAsync(Args(("date", "today"), ("days", "many")), CancellationToken.None));
        Assert.Equal("Error: '1.5' is not a whole number for 'weeks'", await _tool.InvokeAsync(Args(("date", "today"), ("weeks", Json("1.5"))), CancellationToken.None));
        Assert.Equal("Error: 'true' is not a whole number for 'months'", await _tool.InvokeAsync(Args(("date", "today"), ("months", Json("true"))), CancellationToken.None));
    }
}
