using NeonCompanion.Web;

namespace NeonCompanion.Tests;

public class HeadlessBrowserTests
{
    private static readonly IReadOnlyList<string> Windows = HeadlessBrowser.WindowsCandidates(@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\me\AppData\Local");

    [Fact]
    public void WindowsCandidates_AreEdgeChromeBrave_UnderTheThreeRoots()
    {
        Assert.Equal(
            new[]
            {
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Users\me\AppData\Local\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                @"C:\Users\me\AppData\Local\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
                @"C:\Program Files (x86)\BraveSoftware\Brave-Browser\Application\brave.exe",
                @"C:\Users\me\AppData\Local\BraveSoftware\Brave-Browser\Application\brave.exe",
            },
            Windows);
    }

    [Fact]
    public void WindowsCandidates_SkipAnEmptyOrRepeatedRoot()
    {
        var list = HeadlessBrowser.WindowsCandidates(@"C:\Program Files", @"C:\Program Files", "");

        Assert.Equal(3, list.Count);
        Assert.All(list, p => Assert.StartsWith(@"C:\Program Files\", p));
    }

    [Fact]
    public void Locate_TakesTheConfiguredPath_WhenItExists()
    {
        Assert.Equal(@"D:\tools\chrome.exe", HeadlessBrowser.Locate(@" D:\tools\chrome.exe ", Windows, p => p == @"D:\tools\chrome.exe"));
        Assert.Null(HeadlessBrowser.Locate(@"D:\tools\chrome.exe", Windows, _ => false));
        // A configured path that is not there never falls back to the candidates: the row says what the user asked for.
        Assert.Null(HeadlessBrowser.Locate(@"D:\tools\chrome.exe", Windows, p => p == Windows[0]));
    }

    [Fact]
    public void Locate_FindsTheFirstCandidate_EdgeBeforeChromeBeforeBrave()
    {
        Assert.Equal(Windows[1], HeadlessBrowser.Locate("", Windows, p => p == Windows[1] || p == Windows[3]));
        Assert.Equal(Windows[3], HeadlessBrowser.Locate("", Windows, p => p == Windows[3] || p == Windows[6]));
        Assert.Null(HeadlessBrowser.Locate("", Windows, _ => false));
    }

    [Fact]
    public void Candidates_OnThisMachine_AreNonEmpty()
    {
        Assert.NotEmpty(HeadlessBrowser.Candidates());
        Assert.EndsWith(Path.Combine("NeonCompanion", "browser"), HeadlessBrowser.UserDataDirectory);
    }

    [Fact]
    public void Arguments_ArePinned()
    {
        Assert.Equal(
            new[]
            {
                "--headless=new", "--dump-dom", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                "--disable-background-networking", "--mute-audio", "--blink-settings=imagesEnabled=false", "--virtual-time-budget=5000",
                @"--user-data-dir=C:\Temp\NeonCompanion\browser", "https://example.com/a?b=c",
            },
            HeadlessBrowser.Arguments(new Uri("https://example.com/a?b=c"), @"C:\Temp\NeonCompanion\browser"));
        Assert.Equal(TimeSpan.FromSeconds(30), HeadlessBrowser.Timeout);
    }

    [Fact]
    public void LastLine_IsTheLastNonEmptyLine()
    {
        Assert.Equal("[1234:5678:0915/120000.123:ERROR:x.cc(1)] boom", HeadlessBrowser.LastLine("first\r\n\r\n[1234:5678:0915/120000.123:ERROR:x.cc(1)] boom\r\n\r\n"));
        Assert.Equal("", HeadlessBrowser.LastLine("\n\n"));
    }
}
