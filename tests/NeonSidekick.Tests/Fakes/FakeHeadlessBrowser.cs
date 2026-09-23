using NeonSidekick.Web;

namespace NeonSidekick.Tests.Fakes;

/// <summary>
/// The headless-browser seam without a browser: <see cref="Executable"/> is what <see cref="Locate"/>
/// finds (null = none installed), <see cref="Html"/> what a run dumps (null = the run fails with
/// <see cref="Failure"/>), and every run is recorded.
/// </summary>
public sealed class FakeHeadlessBrowser : IHeadlessBrowser
{
    public string? Executable { get; set; } = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

    public string? Html { get; set; }

    public string Failure { get; set; } = "it printed nothing";

    public List<Uri> Runs { get; } = new();

    public List<string> LocateCalls { get; } = new();

    public string? Locate(string configuredPath)
    {
        LocateCalls.Add(configuredPath);
        return configuredPath.Length > 0 ? configuredPath : Executable;
    }

    public Task<BrowserDump> DumpDomAsync(string executable, Uri url, CancellationToken cancellationToken)
    {
        Runs.Add(url);
        return Task.FromResult(Html is null ? new BrowserDump(null, Failure) : new BrowserDump(Html, ""));
    }
}
