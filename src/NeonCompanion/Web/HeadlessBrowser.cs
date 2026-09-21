using System.Diagnostics;
using System.Globalization;
using System.Text;
using NeonCompanion.Diagnostics;

namespace NeonCompanion.Web;

/// <summary>What a headless run produced: the rendered DOM, or null with the reason.</summary>
public sealed record BrowserDump(string? Html, string Detail);

/// <summary>The headless-browser seam: where the browser is, and a page through it. <see cref="HeadlessBrowser"/> in the app, a fake in tests.</summary>
public interface IHeadlessBrowser
{
    /// <summary>The executable to run: <paramref name="configuredPath"/> when it is a file, else the first of the known browsers installed; null for none.</summary>
    string? Locate(string configuredPath);

    /// <summary>The DOM of <paramref name="url"/> after the page ran its scripts, through <paramref name="executable"/>.</summary>
    Task<BrowserDump> DumpDomAsync(string executable, Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// A real browser for the pages the HTTP client cannot read — a script shell that renders
/// client-side, a gate that reads the TLS fingerprint — run as Chromium's <c>--headless=new
/// --dump-dom</c>: Edge (always on Windows 11), Chrome or Brave, found in their standard install
/// folders or named by the setting <c>Web browser path</c>. The rendered DOM comes back on stdout;
/// nothing else of the browser is used. <b>The second <c>Process.Start</c> site in the app</b> (the
/// editor opener is the first): no shell, no window, a throwaway profile folder under the temp
/// directory — without one a running Edge takes the URL over and this process exits with nothing —
/// one run at a time, killed with its tree at <see cref="Timeout"/>.
/// </summary>
public sealed class HeadlessBrowser : IHeadlessBrowser
{
    /// <summary>How long one page may take, scripts included, before the process is killed.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Virtual time the page's scripts get to settle before the DOM is dumped.</summary>
    public const int VirtualTimeBudgetMs = 5000;

    /// <summary>The profile folder under the temp directory, so the user's own browser profile is never touched.</summary>
    public static string UserDataDirectory => Path.Combine(Path.GetTempPath(), "NeonCompanion", "browser");

    private const string Category = "Web";
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>The known executables in the order they are tried, under the standard install folders of this machine.</summary>
    public static IReadOnlyList<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return WindowsCandidates(programFiles, programFilesX86, localAppData);
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser",
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
            ];
        }

        var linux = new List<string>();
        foreach (var dir in new[] { "/usr/bin", "/usr/local/bin", "/snap/bin", "/opt/homebrew/bin" })
        {
            foreach (var name in new[] { "microsoft-edge", "microsoft-edge-stable", "google-chrome", "google-chrome-stable", "brave-browser", "chromium", "chromium-browser" })
            {
                linux.Add(Path.Combine(dir, name));
            }
        }

        return linux;
    }

    /// <summary>The Windows install paths, Edge → Chrome → Brave, each under Program Files, Program Files (x86) and the per-user folder. Pure; pinned.</summary>
    public static IReadOnlyList<string> WindowsCandidates(string programFiles, string programFilesX86, string localAppData)
    {
        ArgumentNullException.ThrowIfNull(programFiles);
        ArgumentNullException.ThrowIfNull(programFilesX86);
        ArgumentNullException.ThrowIfNull(localAppData);
        var roots = new[] { programFiles, programFilesX86, localAppData }.Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var list = new List<string>();
        foreach (var relative in new[]
                 {
                     Path.Combine("Microsoft", "Edge", "Application", "msedge.exe"),
                     Path.Combine("Google", "Chrome", "Application", "chrome.exe"),
                     Path.Combine("BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                 })
        {
            foreach (var root in roots)
            {
                list.Add(Path.Combine(root, relative));
            }
        }

        return list;
    }

    /// <summary>The pure form of <see cref="Locate(string)"/>: the configured path when <paramref name="exists"/> says it is there, else the first candidate that is.</summary>
    public static string? Locate(string configuredPath, IReadOnlyList<string> candidates, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(configuredPath);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(exists);
        string configured = configuredPath.Trim();
        if (configured.Length > 0)
        {
            return exists(configured) ? configured : null;
        }

        foreach (var candidate in candidates)
        {
            if (exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string? Locate(string configuredPath) => Locate(configuredPath, Candidates(), File.Exists);

    /// <summary>The command line after the executable. Pinned.</summary>
    public static IReadOnlyList<string> Arguments(Uri url, string userDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(userDataDirectory);
        return
        [
            "--headless=new",
            "--dump-dom",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-extensions",
            "--disable-background-networking",
            "--mute-audio",
            "--blink-settings=imagesEnabled=false",
            "--virtual-time-budget=" + VirtualTimeBudgetMs.ToString(CultureInfo.InvariantCulture),
            "--user-data-dir=" + userDataDirectory,
            url.AbsoluteUri,
        ];
    }

    public async Task<BrowserDump> DumpDomAsync(string executable, Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(url);
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunAsync(executable, url, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private static async Task<BrowserDump> RunAsync(string executable, Uri url, CancellationToken cancellationToken)
    {
        string userData = UserDataDirectory;
        try
        {
            Directory.CreateDirectory(userData);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BrowserDump(null, $"could not create the profile folder {userData} ({ex.Message})");
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in Arguments(url, userData))
        {
            start.ArgumentList.Add(argument);
        }

        var watch = Stopwatch.StartNew();
        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new BrowserDump(null, $"could not start {Path.GetFileName(executable)} ({ex.Message})");
        }

        if (process is null)
        {
            return new BrowserDump(null, $"could not start {Path.GetFileName(executable)}");
        }

        using (process)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(Timeout);
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            bool timedOut = false;
            try
            {
                await process.WaitForExitAsync(budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                TryKill(process);
                if (!timedOut)
                {
                    throw;
                }
            }

            string html = await stdout.ConfigureAwait(false);
            string errors = await stderr.ConfigureAwait(false);
            DiagnosticLog.Debug(Category, $"{Path.GetFileName(executable)} {url}: exit {process.ExitCode.ToString(CultureInfo.InvariantCulture)} after {watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s, {html.Length.ToString(CultureInfo.InvariantCulture)} chars" + (errors.Length > 0 ? $"; stderr: {LastLine(errors)}" : ""));
            if (timedOut)
            {
                return new BrowserDump(null, $"no page within {Llm.LlmTimeouts.Format(Timeout)}");
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                string reason = process.ExitCode != 0 ? $"exit code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}" : "it printed nothing";
                string tail = LastLine(errors);
                return new BrowserDump(null, tail.Length > 0 ? $"{reason}: {tail}" : reason);
            }

            return new BrowserDump(html, "");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already gone, or not ours to kill: the wait is over either way.
        }
    }

    /// <summary>The last non-empty line of a browser's stderr, for a diagnostic. Pinned.</summary>
    public static string LastLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "" : lines[^1];
    }
}
