using NeonCompanion.Diagnostics;
using NeonCompanion.Settings;

namespace NeonCompanion.Web;

/// <summary>How <c>web_fetch</c> gets a page (<see cref="WebFetcher.FetchAsync"/>).</summary>
public enum FetchEngine
{
    /// <summary>The HTTP client first; the headless browser when the page came back blocked or as a script shell.</summary>
    Default,

    /// <summary>The HTTP client alone.</summary>
    HttpClient,

    /// <summary>The headless browser for every page.</summary>
    Chromium,
}

/// <summary>
/// The setting <c>Web browser mode</c>: the three words the operator picks from (<c>default</c>,
/// <c>httpclient</c>, <c>chromium</c>) and their mapping to <see cref="FetchEngine"/>, the
/// <see cref="Llm.LlmScanMode"/> shape. <see cref="Resolve"/> is the one place the saved string
/// becomes the enum: a hand-edited value that is none of them falls back to <see cref="Default"/>
/// with a warning.
/// </summary>
public static class BrowserMode
{
    /// <summary>The client, then the browser when needed. The compiled default, pinned by <c>AppSettingsTests</c>.</summary>
    public const string Default = "default";

    /// <summary>The modes in menu order.</summary>
    public static readonly string[] Names = { "default", "httpclient", "chromium" };

    private const string Category = "Web";

    /// <summary>Trims and ignores case; false (and <see cref="FetchEngine.Default"/>) for anything that is not one of <see cref="Names"/>.</summary>
    public static bool TryParse(string? text, out FetchEngine engine)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "default": engine = FetchEngine.Default; return true;
            case "httpclient": engine = FetchEngine.HttpClient; return true;
            case "chromium": engine = FetchEngine.Chromium; return true;
            default: engine = FetchEngine.Default; return false;
        }
    }

    /// <summary>The saved word for <paramref name="engine"/>.</summary>
    public static string Name(FetchEngine engine) => engine switch
    {
        FetchEngine.HttpClient => "httpclient",
        FetchEngine.Chromium => "chromium",
        _ => "default",
    };

    /// <summary>The menu hint next to a mode. Pinned.</summary>
    public static string Describe(string name) => name switch
    {
        "default" => "HttpClient, then a headless browser when a page is blocked or empty",
        "httpclient" => "HttpClient alone, with browser-like headers",
        "chromium" => "a headless Edge, Chrome or Brave for every page",
        _ => "",
    };

    /// <summary>The engine in force for <paramref name="effective"/>; an unknown saved value warns and uses <see cref="Default"/>.</summary>
    public static FetchEngine Resolve(AppSettingsData effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (TryParse(effective.WebBrowserMode, out var engine))
        {
            return engine;
        }

        DiagnosticLog.Warn(Category,
            $"{nameof(AppSettingsData.WebBrowserMode)}='{effective.WebBrowserMode}' is not one of {string.Join(", ", Names)}. Using {Default}.");
        TryParse(Default, out engine);
        return engine;
    }
}
