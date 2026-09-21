using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Llm.Tools;
using NeonCompanion.Tests.Fakes;

namespace NeonCompanion.Tests;

public class DaysBetweenToolTests
{
    private static readonly DateOnly Today = new(2026, 9, 11);
    private static readonly DateOnly Christmas = new(2026, 12, 25);

    private readonly DaysBetweenTool _tool = new(new ManualTimeProvider());

    private static AIFunctionArguments Args(object? from, object? to) =>
        new(new Dictionary<string, object?> { ["from"] = from, ["to"] = to });

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    [Fact]
    public void Schema_HasTwoRequiredStringProperties_MatchingTheKeysRead()
    {
        Assert.Equal("days_between", DaysBetweenTool.ToolName);
        Assert.Equal(DaysBetweenTool.ToolName, _tool.Name);
        Assert.Contains("negative when the second date is earlier", _tool.Description);

        var schema = _tool.JsonSchema;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        var properties = schema.GetProperty("properties");
        Assert.Equal(new[] { DaysBetweenTool.FromArgument, DaysBetweenTool.ToArgument }, properties.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("string", properties.GetProperty("from").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("to").GetProperty("type").GetString());
        Assert.Equal(new[] { "from", "to" }, schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void Describe_IsPinned()
    {
        Assert.Equal("105 days (15 weeks) from 2026-09-11 to 2026-12-25", DaysBetweenTool.Describe(Today, Christmas));
        Assert.Equal("-105 days (15 weeks) from 2026-12-25 to 2026-09-11", DaysBetweenTool.Describe(Christmas, Today));
        Assert.Equal("0 days from 2026-09-11 to 2026-09-11", DaysBetweenTool.Describe(Today, Today));
        Assert.Equal("1 day from 2026-09-11 to 2026-09-12", DaysBetweenTool.Describe(Today, Today.AddDays(1)));
        Assert.Equal("6 days from 2026-09-11 to 2026-09-17", DaysBetweenTool.Describe(Today, Today.AddDays(6)));
        Assert.Equal("7 days (1 week) from 2026-09-11 to 2026-09-18", DaysBetweenTool.Describe(Today, Today.AddDays(7)));
        Assert.Equal("17 days (2 weeks and 3 days) from 2026-09-11 to 2026-09-28", DaysBetweenTool.Describe(Today, Today.AddDays(17)));
        Assert.Equal("-8 days (1 week and 1 day) from 2026-09-11 to 2026-09-03", DaysBetweenTool.Describe(Today, Today.AddDays(-8)));
        Assert.Equal("366 days (52 weeks and 2 days) from 2027-09-11 to 2028-09-11", DaysBetweenTool.Describe(new DateOnly(2027, 9, 11), new DateOnly(2028, 9, 11)));
    }

    [Fact]
    public async Task Invoke_WithJsonArguments_CountsFromToday()
    {
        object? answer = await _tool.InvokeAsync(Args(Json("\"today\""), Json("\"2026-12-25\"")), CancellationToken.None);

        Assert.Equal("105 days (15 weeks) from 2026-09-11 to 2026-12-25", answer);
    }

    [Fact]
    public async Task Invoke_WithStringArguments_CountsBetweenTwoDates()
    {
        object? answer = await _tool.InvokeAsync(Args("2026-01-01", " today "), CancellationToken.None);

        Assert.Equal("253 days (36 weeks and 1 day) from 2026-01-01 to 2026-09-11", answer);
    }

    [Fact]
    public async Task Invoke_WithABadDate_NamesTheArgument()
    {
        Assert.Equal("Error: 'christmas' is not a date for 'to'; use today or yyyy-MM-dd", await _tool.InvokeAsync(Args("today", "christmas"), CancellationToken.None));
        Assert.Equal("Error: '' is not a date for 'from'; use today or yyyy-MM-dd", await _tool.InvokeAsync(Args(null, "today"), CancellationToken.None));
        Assert.Equal("Error: '' is not a date for 'from'; use today or yyyy-MM-dd", await _tool.InvokeAsync(new AIFunctionArguments(), CancellationToken.None));
    }
}
