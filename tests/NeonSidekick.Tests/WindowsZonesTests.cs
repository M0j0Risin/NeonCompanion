using NeonSidekick.Llm.Tools;

namespace NeonSidekick.Tests;

public class WindowsZonesTests
{
    [Fact]
    public void Table_IsGenerated_AndLarge()
    {
        Assert.Equal("2021a", WindowsZones.Version);
        Assert.True(WindowsZones.Count > 500, $"{WindowsZones.Count} ids");
    }

    [Theory]
    [InlineData("Asia/Tokyo", "Tokyo Standard Time")]
    [InlineData("Europe/Paris", "Romance Standard Time")]
    [InlineData("America/New_York", "Eastern Standard Time")]
    [InlineData("Etc/UTC", "UTC")]
    [InlineData("Asia/Calcutta", "India Standard Time")]
    [InlineData("Asia/Kolkata", "India Standard Time")]
    [InlineData("Europe/Kiev", "FLE Standard Time")]
    [InlineData("Europe/Kyiv", "FLE Standard Time")]
    [InlineData("asia/tokyo", "Tokyo Standard Time")]
    public void TryGetWindowsId_MapsCurrentAndOldSpellings(string iana, string windows)
    {
        Assert.True(WindowsZones.TryGetWindowsId(iana, out var id));
        Assert.Equal(windows, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tokyo")]
    [InlineData("Tokyo Standard Time")]
    [InlineData("Mars/Olympus")]
    public void TryGetWindowsId_IsFalseForAnythingElse(string text)
    {
        Assert.False(WindowsZones.TryGetWindowsId(text, out _));
    }

    [Fact]
    public void EveryWindowsId_ExistsOnThisMachine()
    {
        // The table is only useful if the runtime resolves what it maps to.
        foreach (var iana in new[] { "Asia/Tokyo", "Europe/Paris", "America/New_York", "Australia/Sydney", "Asia/Kolkata", "Africa/Johannesburg" })
        {
            Assert.True(WindowsZones.TryGetWindowsId(iana, out var id));
            Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(id));
        }
    }
}
