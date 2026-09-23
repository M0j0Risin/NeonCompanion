using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonSidekick.App;
using NeonSidekick.Llm.Tools;
using NeonSidekick.Tests.Fakes;
using NeonSidekick.Timers;

namespace NeonSidekick.Tests;

/// <summary>The three timer tools over one board: they share the board, the clock and the sentences, so one class.</summary>
public class TimerToolsTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly TimerBoard _board;
    private readonly StartTimerTool _start;
    private readonly StopTimerTool _stop;
    private readonly ListTimersTool _list;

    public TimerToolsTests()
    {
        _board = new TimerBoard(_time, () => { });
        _start = new StartTimerTool(_board);
        _stop = new StopTimerTool(_board);
        _list = new ListTimersTool(_board);
    }

    public void Dispose() => _board.Dispose();

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
    public void Schemas_ArePinned()
    {
        Assert.Equal("start_timer", StartTimerTool.ToolName);
        Assert.Equal("stop_timer", StopTimerTool.ToolName);
        Assert.Equal("list_timers", ListTimersTool.ToolName);
        Assert.Equal(StartTimerTool.ToolName, _start.Name);
        Assert.Equal(StopTimerTool.ToolName, _stop.Name);
        Assert.Equal(ListTimersTool.ToolName, _list.Name);
        Assert.Contains("hours, minutes and/or seconds", _start.Description);
        Assert.Contains(ListTimersTool.ToolName, _stop.Description);
        Assert.Contains("how much time remains", _list.Description);

        var start = _start.JsonSchema.GetProperty("properties");
        Assert.Equal(
            new[] { StartTimerTool.NameArgument, StartTimerTool.HoursArgument, StartTimerTool.MinutesArgument, StartTimerTool.SecondsArgument },
            start.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("string", start.GetProperty("name").GetProperty("type").GetString());
        foreach (var amount in new[] { "hours", "minutes", "seconds" })
        {
            Assert.Equal("integer", start.GetProperty(amount).GetProperty("type").GetString());
        }

        Assert.False(_start.JsonSchema.TryGetProperty("required", out _));

        var stop = _stop.JsonSchema;
        Assert.Equal(new[] { "name" }, stop.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal("string", stop.GetProperty("properties").GetProperty("name").GetProperty("type").GetString());

        Assert.Equal("object", _list.JsonSchema.GetProperty("type").GetString());
        Assert.Empty(_list.JsonSchema.GetProperty("properties").EnumerateObject());
    }

    [Fact]
    public void QuietTools_IncludeAllThree()
    {
        Assert.Contains(StartTimerTool.ToolName, ChatScreen.QuietTools);
        Assert.Contains(StopTimerTool.ToolName, ChatScreen.QuietTools);
        Assert.Contains(ListTimersTool.ToolName, ChatScreen.QuietTools);
    }

    [Fact]
    public async Task Start_WithMinutesFromTheWire_StartsANamedTimer()
    {
        var result = await _start.InvokeAsync(Args(("name", Json("\"cooking\"")), ("minutes", Json("10"))), CancellationToken.None);

        Assert.Equal("started the cooking timer: 10 minutes, done at 14:15", result);
        Assert.True(_board.TryFind("cooking", out var timer));
        Assert.Equal(TimeSpan.FromMinutes(10), timer.Duration);
    }

    [Fact]
    public async Task Start_WithoutAName_NamesItAfterTheDuration()
    {
        var result = await _start.InvokeAsync(Args(("hours", 1), ("minutes", "30")), CancellationToken.None);

        Assert.Equal("started the 1 hour 30 minute timer: 1 hour 30 minutes, done at 15:35", result);
        Assert.True(_board.TryFind("1 hour 30 minute", out _));
    }

    [Fact]
    public async Task Start_AddsTheUnits_AndSecondsAlone()
    {
        Assert.Equal("started the 90 second timer: 1 minute 30 seconds, done at 14:07", await _start.InvokeAsync(Args(("name", "90 second"), ("seconds", 90)), CancellationToken.None));
        Assert.Equal("started the mixed timer: 1 hour 1 minute 1 second, done at 15:06", await _start.InvokeAsync(Args(("name", "mixed"), ("hours", 1), ("minutes", 1), ("seconds", 1)), CancellationToken.None));
    }

    [Fact]
    public async Task Start_ErrorSentences_ArePinned()
    {
        Assert.Equal(TimerText.NoDuration, await _start.InvokeAsync(Args(("name", "x")), CancellationToken.None));
        Assert.Equal(TimerText.NoDuration, await _start.InvokeAsync(new AIFunctionArguments(), CancellationToken.None));
        Assert.Equal(TimerText.NoDuration, await _start.InvokeAsync(Args(("minutes", Json("null")), ("seconds", "")), CancellationToken.None));
        Assert.Equal(TimerText.BadDuration, await _start.InvokeAsync(Args(("minutes", 0)), CancellationToken.None));
        Assert.Equal(TimerText.BadDuration, await _start.InvokeAsync(Args(("minutes", -5)), CancellationToken.None));
        Assert.Equal(TimerText.BadDuration, await _start.InvokeAsync(Args(("hours", 25)), CancellationToken.None));
        Assert.Equal(TimerText.BadDuration, await _start.InvokeAsync(Args(("hours", 24), ("seconds", 1)), CancellationToken.None));
        Assert.Equal("Error: 'ten' is not a whole number for 'minutes'", await _start.InvokeAsync(Args(("minutes", "ten")), CancellationToken.None));
        Assert.Equal("Error: '1.5' is not a whole number for 'hours'", await _start.InvokeAsync(Args(("hours", Json("1.5"))), CancellationToken.None));
        Assert.Equal(TimerText.BadName, await _start.InvokeAsync(Args(("name", "all"), ("minutes", 1)), CancellationToken.None));
        Assert.Equal(0, _board.Count);
    }

    [Fact]
    public async Task Start_ADuplicate_ReportsTheExistingTimer()
    {
        await _start.InvokeAsync(Args(("name", "cooking"), ("minutes", 10)), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(168));

        var result = await _start.InvokeAsync(Args(("name", "COOKING"), ("minutes", 1)), CancellationToken.None);

        Assert.Equal("Error: a timer called 'cooking' is already running (7 minutes 12 seconds left); stop it first or use another name", result);
        Assert.Equal(1, _board.Count);
    }

    [Fact]
    public async Task Start_TheTwentyFirst_IsFull()
    {
        for (int i = 0; i < TimerBoard.MaxTimers; i++)
        {
            _board.Start("t" + i, TimeSpan.FromMinutes(1));
        }

        Assert.Equal("Error: too many timers (20); stop one first", await _start.InvokeAsync(Args(("name", "x"), ("minutes", 1)), CancellationToken.None));
    }

    [Fact]
    public async Task Stop_ARunningTimer_SaysWhatWasLeft()
    {
        _board.Start("cooking", TimeSpan.FromMinutes(10));
        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal("stopped the cooking timer with 7 minutes left", await _stop.InvokeAsync(Args(("name", Json("\" Cooking \""))), CancellationToken.None));
        Assert.Equal(0, _board.Count);
    }

    [Fact]
    public async Task Stop_ARingingTimer_Silences()
    {
        _board.Start("cooking", TimeSpan.FromMinutes(1));
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal("silenced the cooking timer", await _stop.InvokeAsync(Args(("name", "cooking")), CancellationToken.None));
        Assert.False(_board.HasRinging);
    }

    [Fact]
    public async Task Stop_ErrorSentences_ArePinned()
    {
        _board.Start("tea", TimeSpan.FromMinutes(1));
        _board.Start("eggs", TimeSpan.FromMinutes(2));

        Assert.Equal("Error: no timer called 'cooking'; running: tea, eggs", await _stop.InvokeAsync(Args(("name", "cooking")), CancellationToken.None));
        Assert.Equal(TimerText.NoName, await _stop.InvokeAsync(Args(("name", "  ")), CancellationToken.None));
        Assert.Equal(TimerText.NoName, await _stop.InvokeAsync(new AIFunctionArguments(), CancellationToken.None));
        _board.StopAll();
        Assert.Equal("Error: no timer called 'cooking'; no timers are running", await _stop.InvokeAsync(Args(("name", "cooking")), CancellationToken.None));
    }

    [Fact]
    public async Task List_NamesEveryTimer_OrSaysNone()
    {
        Assert.Equal("no timers are running", await _list.InvokeAsync(new AIFunctionArguments(), CancellationToken.None));

        _board.Start("cooking", TimeSpan.FromMinutes(10));
        _board.Start("tea", TimeSpan.FromMinutes(1));
        _time.Advance(TimeSpan.FromSeconds(168));

        Assert.Equal(
            "cooking: 7 minutes 12 seconds left of 10 minutes (done at 14:15); tea: done, ringing",
            await _list.InvokeAsync(new AIFunctionArguments(), CancellationToken.None));
    }
}
