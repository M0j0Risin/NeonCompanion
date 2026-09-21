namespace NeonCompanion.UI;

/// <summary>
/// Pairs two left clicks on the same row into a double-click: the second within
/// <see cref="Interval"/> of the first is the pair's, and the menu pane treats it as Enter.
/// Keyed by the list's row, not the cell, so a few columns of drift between the two presses still
/// pair (Windows' own tolerance is a rectangle). The console reader hands the pane two plain clicks
/// — under Windows Terminal the Win32 <c>DOUBLE_CLICK</c> flag never arrives — so the pairing is
/// by the clock alone, the pane's own (<see cref="ScreenPane.Time"/>).
/// </summary>
public sealed class DoubleClick
{
    /// <summary>How close the second click has to follow the first. A constant, not a setting.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time;
    private int _row = -1;
    private long _at;

    public DoubleClick(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Registers a click on <paramref name="target"/> (a list row, a hint-row zone — any number the
    /// caller keys its parts by): true when it is the second on that target within
    /// <see cref="Interval"/> (the pair is spent — a third click starts over); false when it is a
    /// first, on another target or too late, and then it becomes the first the next one is measured against.
    /// </summary>
    public bool Second(int target)
    {
        long now = _time.GetTimestamp();
        if (_row == target && _time.GetElapsedTime(_at, now) <= Interval)
        {
            _row = -1;
            return true;
        }

        _row = target;
        _at = now;
        return false;
    }

    /// <summary>Forgets the first click: the next one is a first again.</summary>
    public void Reset() => _row = -1;
}
