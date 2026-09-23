using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonSidekick.Llm.Tools;
using NeonSidekick.Tests.Fakes;

namespace NeonSidekick.Tests;

public class GetCurrentTimeToolTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly GetCurrentTimeTool _tool;

    public GetCurrentTimeToolTests()
    {
        _tool = new GetCurrentTimeTool(_time);
    }

    private static AIFunctionArguments Args(object? zone) =>
        new(new Dictionary<string, object?> { ["zone"] = zone });

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    [Fact]
    public void Schema_HasOneOptionalStringProperty_MatchingTheKeyRead()
    {
        Assert.Equal("get_current_time", GetCurrentTimeTool.ToolName);
        Assert.Equal(GetCurrentTimeTool.ToolName, _tool.Name);
        Assert.Equal("The current date, time, weekday and time zone. Call it whenever a question depends on today's date or the time now.", _tool.Description);

        var schema = _tool.JsonSchema;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        var properties = schema.GetProperty("properties");
        Assert.Single(properties.EnumerateObject());
        Assert.Equal("string", properties.GetProperty(GetCurrentTimeTool.ZoneArgument).GetProperty("type").GetString());
        Assert.Contains("IANA", properties.GetProperty("zone").GetProperty("description").GetString());
        Assert.False(schema.TryGetProperty("required", out _));
    }

    [Fact]
    public async Task Invoke_WithNoArguments_IsTheLocalMoment()
    {
        object? answer = await _tool.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        Assert.Equal("Friday 11 September 2026, 14:05 (Pacific Daylight Time, UTC-07:00)", answer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invoke_WithABlankZone_IsTheLocalMoment(string? zone)
    {
        object? answer = await _tool.InvokeAsync(Args(zone), CancellationToken.None);

        Assert.Equal("Friday 11 September 2026, 14:05 (Pacific Daylight Time, UTC-07:00)", answer);
    }

    [Fact]
    public async Task Invoke_WithAJsonNullZone_IsTheLocalMoment()
    {
        object? answer = await _tool.InvokeAsync(Args(Json("null")), CancellationToken.None);

        Assert.Equal("Friday 11 September 2026, 14:05 (Pacific Daylight Time, UTC-07:00)", answer);
    }

    [Fact]
    public async Task Invoke_WithAJsonZone_IsTheMomentThere_AndTheLocalOne()
    {
        object? answer = await _tool.InvokeAsync(Args(Json("\"Asia/Tokyo\"")), CancellationToken.None);

        Assert.Equal("Saturday 12 September 2026, 06:05 in Asia/Tokyo (UTC+09:00); local time is Friday 11 September 2026, 14:05", answer);
    }

    [Fact]
    public async Task Invoke_WithAStringZone_TrimsIt_AndEchoesTheNameAsGiven()
    {
        object? answer = await _tool.InvokeAsync(Args("  Europe/London "), CancellationToken.None);

        // BST in September.
        Assert.Equal("Friday 11 September 2026, 22:05 in Europe/London (UTC+01:00); local time is Friday 11 September 2026, 14:05", answer);
    }

    [Fact]
    public async Task Invoke_WithAnUnknownZone_IsAnErrorSentence()
    {
        object? answer = await _tool.InvokeAsync(Args("Mars/Olympus"), CancellationToken.None);

        Assert.Equal("Error: unknown time zone 'Mars/Olympus'; give an IANA name such as Europe/Paris", answer);
    }

    [Fact]
    public void Describe_FollowsTheClock()
    {
        _time.UtcNow = new DateTimeOffset(2026, 12, 25, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal("Friday 25 December 2026, 01:00 (Pacific Daylight Time, UTC-07:00)", _tool.Describe(""));
        Assert.Equal("Friday 25 December 2026, 08:00 in UTC (UTC+00:00); local time is Friday 25 December 2026, 01:00", _tool.Describe("UTC"));
    }
}
