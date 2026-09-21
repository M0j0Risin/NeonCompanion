namespace NeonCompanion.Web;

/// <summary>
/// The request headers a desktop Chrome sends for a top-level navigation, pinned as one set so
/// every web request looks the same. This is the reachable end of "look like a browser": headers
/// are ours to choose, the TLS and HTTP/2 fingerprints are the runtime's (SChannel on Windows)
/// and a fingerprint-reading gate tells them from Chrome's — that is what the headless-browser
/// leg of <see cref="WebFetcher"/> is for.
/// </summary>
public static class BrowserHeaders
{
    /// <summary>The Chrome major version the set claims; bump every field together.</summary>
    public const string ChromeVersion = "140";

    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" + ChromeVersion + ".0.0.0 Safari/537.36";

    public const string Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7";
    public const string AcceptLanguage = "en-US,en;q=0.9";
    public const string SecChUa = "\"Chromium\";v=\"" + ChromeVersion + "\", \"Google Chrome\";v=\"" + ChromeVersion + "\", \"Not?A_Brand\";v=\"24\"";
    public const string SecChUaMobile = "?0";
    public const string SecChUaPlatform = "\"Windows\"";

    /// <summary>
    /// Applies the set to <paramref name="request"/> as a top-level navigation: <c>Sec-Fetch-Site</c>
    /// is <c>none</c> for a typed address, <c>same-origin</c> when <paramref name="referer"/> is on the
    /// same host, else <c>cross-site</c>. Header values are added without validation, as sent.
    /// </summary>
    public static void Apply(HttpRequestMessage request, Uri? referer = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var headers = request.Headers;
        headers.TryAddWithoutValidation("User-Agent", UserAgent);
        headers.TryAddWithoutValidation("Accept", Accept);
        headers.TryAddWithoutValidation("Accept-Language", AcceptLanguage);
        headers.TryAddWithoutValidation("Sec-CH-UA", SecChUa);
        headers.TryAddWithoutValidation("Sec-CH-UA-Mobile", SecChUaMobile);
        headers.TryAddWithoutValidation("Sec-CH-UA-Platform", SecChUaPlatform);
        headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        if (referer is null)
        {
            headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            return;
        }

        headers.TryAddWithoutValidation("Referer", referer.AbsoluteUri);
        bool sameOrigin = request.RequestUri is { } target && string.Equals(target.Host, referer.Host, StringComparison.OrdinalIgnoreCase);
        headers.TryAddWithoutValidation("Sec-Fetch-Site", sameOrigin ? "same-origin" : "cross-site");
    }
}
