using NeonSidekick.Diagnostics;
using NeonSidekick.Settings;
using NeonSidekick.Web;

namespace NeonSidekick.Tests;

public class NetworkModeTests
{
    [Fact]
    public void Names_ArePinned_InMenuOrder()
    {
        Assert.Equal(new[] { "internet", "local_area_network", "both" }, NetworkMode.Names);
        Assert.Equal("internet", NetworkMode.Default);
        Assert.Equal(NetworkMode.Default, new AppSettingsData().WebBrowserNetworkMode);
    }

    [Theory]
    [InlineData("internet", NetworkReach.Internet)]
    [InlineData("local_area_network", NetworkReach.LocalAreaNetwork)]
    [InlineData("both", NetworkReach.Both)]
    [InlineData("  Both ", NetworkReach.Both)]
    public void TryParse_TrimsAndIgnoresCase(string text, NetworkReach expected)
    {
        Assert.True(NetworkMode.TryParse(text, out var reach));
        Assert.Equal(expected, reach);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lan")]
    [InlineData("on")]
    public void TryParse_RejectsAnythingElse_AsInternet(string? text)
    {
        Assert.False(NetworkMode.TryParse(text, out var reach));
        Assert.Equal(NetworkReach.Internet, reach);
    }

    [Fact]
    public void Name_RoundTripsEveryMode_AndEveryModeHasAHint()
    {
        foreach (var name in NetworkMode.Names)
        {
            Assert.True(NetworkMode.TryParse(name, out var reach));
            Assert.Equal(name, NetworkMode.Name(reach));
            Assert.NotEqual("", NetworkMode.Describe(name));
        }

        Assert.Equal("public addresses alone; this machine and the local network refused", NetworkMode.Describe("internet"));
        Assert.Equal("this machine and the local network alone; the internet refused", NetworkMode.Describe("local_area_network"));
        Assert.Equal("every address", NetworkMode.Describe("both"));
        Assert.Equal("", NetworkMode.Describe("lan"));
    }

    [Fact]
    public void Resolve_MapsTheSavedMode()
    {
        Assert.Equal(NetworkReach.Both, NetworkMode.Resolve(new AppSettingsData { WebBrowserNetworkMode = "both" }));
        Assert.Equal(NetworkReach.LocalAreaNetwork, NetworkMode.Resolve(new AppSettingsData { WebBrowserNetworkMode = "Local_Area_Network" }));
        Assert.Equal(NetworkReach.Internet, NetworkMode.Resolve(new AppSettingsData()));
    }

    [Fact]
    public void Resolve_UnknownSavedValue_WarnsAndUsesTheDefault()
    {
        var warnings = new List<DiagnosticEvent>();
        Action<DiagnosticEvent> capture = e => { if (e.Category == "Web" && e.Level == DiagnosticLevel.Warning) warnings.Add(e); };
        DiagnosticLog.Emitted += capture;
        try
        {
            Assert.Equal(NetworkReach.Internet, NetworkMode.Resolve(new AppSettingsData { WebBrowserNetworkMode = "on" }));
        }
        finally
        {
            DiagnosticLog.Emitted -= capture;
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("WebBrowserNetworkMode='on' is not one of internet, local_area_network, both. Using internet.", warning.Message);
    }
}
