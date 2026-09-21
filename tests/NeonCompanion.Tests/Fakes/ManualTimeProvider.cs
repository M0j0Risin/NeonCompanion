namespace NeonCompanion.Tests.Fakes;

/// <summary>
/// A clock that only moves when a test says so. Timestamps are in ticks; the wall clock is
/// <see cref="UtcNow"/> in <see cref="LocalTimeZone"/>, fixed at a Friday afternoon in a
/// UTC-07:00 zone so every expected string is the same on every machine. Timers from
/// <see cref="CreateTimer"/> fire synchronously inside <see cref="Advance"/>, in due order, on the
/// caller's thread; a <c>Change</c> from inside a callback is honoured within the same advance.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>2026-09-11 21:05:30 UTC = Friday 11 September 2026, 14:05 in <see cref="DefaultZone"/>.</summary>
    public static readonly DateTimeOffset DefaultUtcNow = new(2026, 9, 11, 21, 5, 30, TimeSpan.Zero);

    /// <summary>A fixed UTC-07:00 zone with no daylight rules, named like the real thing.</summary>
    public static readonly TimeZoneInfo DefaultZone = TimeZoneInfo.CreateCustomTimeZone(
        "Test Pacific", TimeSpan.FromHours(-7), "(UTC-07:00) Test Pacific", "Pacific Daylight Time");

    private readonly List<ManualTimer> _timers = new();
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    /// <summary>Moves both clocks and fires every timer that came due, earliest first.</summary>
    public void Advance(TimeSpan by)
    {
        UtcNow += by;
        long target = _ticks + by.Ticks;
        while (true)
        {
            ManualTimer? next = null;
            foreach (var timer in _timers)
            {
                if (timer.Due is { } due && due <= target && (next is null || due < next.Due))
                {
                    next = timer;
                }
            }

            if (next is null)
            {
                break;
            }

            _ticks = Math.Max(_ticks, next.Due!.Value);
            next.Fire();
        }

        _ticks = target;
    }

    public DateTimeOffset UtcNow { get; set; } = DefaultUtcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public TimeZoneInfo Zone { get; set; } = DefaultZone;

    public override TimeZoneInfo LocalTimeZone => Zone;

    /// <summary>How many timers are alive (created and not disposed).</summary>
    public int TimerCount => _timers.Count;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        /// <summary>The timestamp it fires at, or null when parked.</summary>
        public long? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (!_owner._timers.Contains(this))
            {
                return false;
            }

            _period = period;
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._ticks + Math.Max(0, dueTime.Ticks);
            return true;
        }

        public void Fire()
        {
            Due = _period == Timeout.InfiniteTimeSpan ? null : Due + _period.Ticks;
            _callback(_state);
        }

        public void Dispose() => _owner._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
