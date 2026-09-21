using NeonCompanion.Tests.Fakes;
using NeonCompanion.UI;

namespace NeonCompanion.Tests;

public class DoubleClickTests
{
    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void TheIntervalIsHalfASecond()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), DoubleClick.Interval);
    }

    [Fact]
    public void ASecondClickOnTheSameRow_WithinTheInterval_IsThePair_AndSpendsIt()
    {
        var clicks = new DoubleClick(_time);

        Assert.False(clicks.Second(3));
        _time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.True(clicks.Second(3));

        // Spent: a third click is a first again, the fourth its pair.
        Assert.False(clicks.Second(3));
        Assert.True(clicks.Second(3));
    }

    [Fact]
    public void ASecondClick_AtTheInterval_StillPairs_PastIt_IsAFirstAgain()
    {
        var clicks = new DoubleClick(_time);

        Assert.False(clicks.Second(1));
        _time.Advance(DoubleClick.Interval);
        Assert.True(clicks.Second(1));

        Assert.False(clicks.Second(1));
        _time.Advance(DoubleClick.Interval + TimeSpan.FromMilliseconds(1));
        Assert.False(clicks.Second(1));            // too late: this one is the new first
        Assert.True(clicks.Second(1));             // and this its pair, straight away
    }

    [Fact]
    public void AClickOnAnotherRow_StartsOver_FromThatRow()
    {
        var clicks = new DoubleClick(_time);

        Assert.False(clicks.Second(0));
        Assert.False(clicks.Second(2));            // another row: a first, measured from here
        _time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(clicks.Second(0));            // back on the old row: a first again, not the pair of the very first
        Assert.True(clicks.Second(0));
    }

    [Fact]
    public void Reset_ForgetsTheFirstClick()
    {
        var clicks = new DoubleClick(_time);

        Assert.False(clicks.Second(5));
        clicks.Reset();
        Assert.False(clicks.Second(5));
        Assert.True(clicks.Second(5));
    }

    [Fact]
    public void NeedsAClock()
    {
        Assert.Throws<ArgumentNullException>(() => new DoubleClick(null!));
    }
}
