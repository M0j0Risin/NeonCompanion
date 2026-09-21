using NeonCompanion.Tests.Fakes;
using NeonCompanion.Timers;

namespace NeonCompanion.Tests;

public class TimerBoardTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly List<int> _signals = new();
    private readonly TimerBoard _board;

    public TimerBoardTests()
    {
        _board = new TimerBoard(_time, () => _signals.Add(_signals.Count));
    }

    public void Dispose() => _board.Dispose();

    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    [Fact]
    public void Start_ReturnsTheSnapshot_WithTheDueClock()
    {
        var result = _board.Start(" cooking ", TenMinutes);

        Assert.Equal(TimerStartOutcome.Started, result.Outcome);
        Assert.Equal("cooking", result.Timer.Name);
        Assert.Equal(TenMinutes, result.Timer.Duration);
        Assert.Equal(TenMinutes, result.Timer.Remaining);
        Assert.False(result.Timer.Ringing);
        // 14:05:30 local + 10 min.
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 14, 15, 30, TimeSpan.FromHours(-7)), result.Timer.DueLocal);
        Assert.Equal(1, _board.Count);
        Assert.Empty(_signals);
    }

    [Fact]
    public void Start_RefusesADuplicateName_IgnoringCase_AndReportsTheExistingOne()
    {
        _board.Start("Cooking", TenMinutes);
        _time.Advance(TimeSpan.FromSeconds(30));

        var result = _board.Start("cooking", TimeSpan.FromMinutes(1));

        Assert.Equal(TimerStartOutcome.Duplicate, result.Outcome);
        Assert.Equal("Cooking", result.Timer.Name);
        Assert.Equal(TimeSpan.FromSeconds(570), result.Timer.Remaining);
        Assert.Equal(1, _board.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData("a name that is far too long to be a timer name at all")]
    public void Start_RefusesABadName(string name)
    {
        Assert.Equal(TimerStartOutcome.BadName, _board.Start(name, TenMinutes).Outcome);
        Assert.Equal(0, _board.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(-5)]
    [InlineData(24 * 3600 + 1)]
    public void Start_RefusesABadDuration(double seconds)
    {
        Assert.Equal(TimerStartOutcome.BadDuration, _board.Start("x", TimeSpan.FromSeconds(seconds)).Outcome);
        Assert.Equal(0, _board.Count);
    }

    [Fact]
    public void Start_RefusesTheTwentyFirst()
    {
        for (int i = 0; i < TimerBoard.MaxTimers; i++)
        {
            Assert.Equal(TimerStartOutcome.Started, _board.Start("t" + i, TenMinutes).Outcome);
        }

        Assert.Equal(TimerStartOutcome.Full, _board.Start("one more", TenMinutes).Outcome);
        Assert.Equal(TimerBoard.MaxTimers, _board.Count);
    }

    [Fact]
    public void Remaining_FollowsTheClock_RoundedUp()
    {
        _board.Start("cooking", TenMinutes);
        _time.Advance(TimeSpan.FromSeconds(30.2));

        Assert.True(_board.TryFind("COOKING", out var timer));
        Assert.Equal(TimeSpan.FromSeconds(570), timer.Remaining);
        Assert.False(_board.TryFind("tea", out _));
    }

    [Fact]
    public void Expiry_QueuesOneAlert_AndSignals_ThenRepeatsEveryMinute()
    {
        _board.Start("cooking", TenMinutes);
        _time.Advance(TimeSpan.FromMinutes(9));
        Assert.False(_board.HasAlerts);
        Assert.Empty(_signals);

        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Single(_signals);
        Assert.True(_board.HasRinging);
        Assert.True(_board.TryTakeAlert(out var first));
        Assert.Equal(new TimerAlert("cooking", TenMinutes, TimeSpan.Zero, 0), first);
        Assert.False(_board.TryTakeAlert(out _));
        Assert.True(_board.TryFind("cooking", out var ringing));
        Assert.True(ringing.Ringing);
        Assert.Equal(TimeSpan.Zero, ringing.Remaining);

        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(_board.HasAlerts);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, _signals.Count);
        Assert.True(_board.TryTakeAlert(out var second));
        Assert.Equal(new TimerAlert("cooking", TenMinutes, TimeSpan.FromMinutes(1), 1), second);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(_board.TryTakeAlert(out var third));
        Assert.Equal(2, third.Repeat);
        Assert.Equal(TimeSpan.FromMinutes(2), third.Overdue);
    }

    [Fact]
    public void Acknowledge_RemovesTheRingingTimers_AndStopsTheRepeats()
    {
        _board.Start("cooking", TenMinutes);
        _board.Start("tea", TimeSpan.FromMinutes(20));
        _time.Advance(TenMinutes);
        _board.TryTakeAlert(out _);

        Assert.Equal(1, _board.Acknowledge());
        Assert.False(_board.HasRinging);
        Assert.Equal(1, _board.Count);
        Assert.Equal(0, _board.Acknowledge());

        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.False(_board.HasAlerts);
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(_board.TryTakeAlert(out var tea));
        Assert.Equal("tea", tea.Name);
    }

    [Fact]
    public void Stop_RemovesARunningTimer_WithWhatWasLeft()
    {
        _board.Start("cooking", TenMinutes);
        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.True(_board.Stop("cooking", out var removed));
        Assert.Equal(TimeSpan.FromMinutes(7), removed.Remaining);
        Assert.False(removed.Ringing);
        Assert.Equal(0, _board.Count);
        Assert.False(_board.Stop("cooking", out _));

        _time.Advance(TenMinutes);
        Assert.False(_board.HasAlerts);
    }

    [Fact]
    public void Stop_SilencesARingingTimer()
    {
        _board.Start("cooking", TenMinutes);
        _time.Advance(TenMinutes);
        _board.TryTakeAlert(out _);

        Assert.True(_board.Stop("cooking", out var removed));
        Assert.True(removed.Ringing);
        Assert.False(_board.HasRinging);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(_board.HasAlerts);
    }

    [Fact]
    public void StopAll_ClearsEverything()
    {
        _board.Start("a", TenMinutes);
        _board.Start("b", TenMinutes);
        _time.Advance(TenMinutes);

        Assert.Equal(2, _board.StopAll());
        Assert.Equal(0, _board.Count);
        Assert.Equal(0, _board.StopAll());
    }

    [Fact]
    public void Snapshot_RunningByRemaining_ThenRinging()
    {
        _board.Start("long", TimeSpan.FromMinutes(30));
        _board.Start("done", TimeSpan.FromMinutes(1));
        _board.Start("short", TimeSpan.FromMinutes(5));
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(new[] { "short", "long", "done" }, _board.Snapshot().Select(t => t.Name));
        Assert.DoesNotContain(_board.Snapshot(), t => t.Name != "done" && t.Ringing);
    }

    [Fact]
    public void Dispose_StopsTheClock()
    {
        _board.Start("cooking", TenMinutes);
        _board.Dispose();
        _board.Dispose();

        Assert.Equal(0, _time.TimerCount);
        _time.Advance(TenMinutes);
        Assert.False(_board.HasAlerts);
        // Still answers; nothing schedules.
        Assert.Equal(TimerStartOutcome.Started, _board.Start("tea", TenMinutes).Outcome);
    }

    [Fact]
    public void ManualTimeProvider_FiresTimersInDueOrder_AndHonoursAChangeFromACallback()
    {
        var log = new List<string>();
        using var late = _time.CreateTimer(_ => log.Add("late"), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        ITimer? early = null;
        early = _time.CreateTimer(
            _ =>
            {
                log.Add("early");
                early!.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            },
            null,
            TimeSpan.FromSeconds(1),
            Timeout.InfiniteTimeSpan);

        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "early", "early", "late", "early", "early", "early" }, log);
        int before = _time.TimerCount;
        early.Dispose();
        Assert.Equal(before - 1, _time.TimerCount);
    }
    [Fact]
    public void StartRingStop_EachLogOneLine()
    {
        var lines = new List<NeonCompanion.Diagnostics.DiagnosticEvent>();
        Action<NeonCompanion.Diagnostics.DiagnosticEvent> capture = e => { if (e.Category == TimerBoard.Category) lines.Add(e); };
        NeonCompanion.Diagnostics.DiagnosticLog.Emitted += capture;
        try
        {
            _board.Start("tea", TimeSpan.FromMinutes(5));
            _board.Start("eggs", TimeSpan.FromMinutes(10));
            _time.Advance(TimeSpan.FromMinutes(5));
            _board.Acknowledge();
            _board.Stop("eggs", out _);
            _board.Start("x", TimeSpan.FromMinutes(1));
            _board.StopAll();
        }
        finally
        {
            NeonCompanion.Diagnostics.DiagnosticLog.Emitted -= capture;
        }

        Assert.Equal(
            ["Timer \"tea\" started for 5 minutes", "Timer \"eggs\" started for 10 minutes", "Timer \"tea\" rang", "1 ringing timer acknowledged", "Timer \"eggs\" stopped", "Timer \"x\" started for 1 minute", "All timers stopped (1)"],
            lines.Select(e => e.Message));
        Assert.Equal(NeonCompanion.Diagnostics.DiagnosticLevel.Debug, lines.Single(e => e.Message.EndsWith("acknowledged", StringComparison.Ordinal)).Level);
        Assert.All(lines.Where(e => !e.Message.EndsWith("acknowledged", StringComparison.Ordinal)), e => Assert.Equal(NeonCompanion.Diagnostics.DiagnosticLevel.Info, e.Level));
        Assert.Equal("Timer \"tea\" stopped (ringing)", TimerBoard.StoppedLogLine("tea", true));
        Assert.Equal("Timers", TimerBoard.Category);
    }
}
