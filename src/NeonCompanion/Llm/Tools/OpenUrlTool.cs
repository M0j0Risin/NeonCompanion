using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Web;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>open_url(url?, urls?)</c>: hands a link — or up to <see cref="MaxUrls"/> of them — to the
/// user's default browser through <see cref="WebAccess.OpenUrl"/> (<see cref="PersonaFile.OpenInBrowser"/>
/// in the app: a shell execute, never waited for). Nothing is fetched and the LAN rule does not
/// apply: it is the user's own browser opening the page, not the app reading it. One link answers
/// <c>Opened … in your browser</c>; several a header and a line each, a bad one its <c>Error:</c>
/// line in place. Offered under the setting <c>Web tools</c> with the other two.
/// </summary>
public sealed class OpenUrlTool : AIFunction
{
    public const string ToolName = "open_url";
    public const string UrlArgument = "url";
    public const string UrlsArgument = "urls";

    /// <summary>The most links one call opens: enough for "open these", too few for a tab storm.</summary>
    public const int MaxUrls = 5;

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "url": { "type": "string", "description": "The link to open (http or https)." },
            "urls": { "type": "array", "items": { "type": "string" }, "description": "Several links to open at once, at most 5. Either url or urls." }
          }
        }
        """);

    private readonly WebAccess _web;

    public OpenUrlTool(WebAccess web)
    {
        _web = web ?? throw new ArgumentNullException(nameof(web));
    }

    public override string Name => ToolName;

    public override string Description =>
        "Opens a web page in the user's own browser, on their screen. Use it when they ask to open, show or see a link rather than to have it read out; " +
        "one link in url, or up to " + MaxUrls.ToString(System.Globalization.CultureInfo.InvariantCulture) + " in urls.";

    public override JsonElement JsonSchema => Schema;

    /// <summary>Opens each link and describes what happened. The argument reading is <see cref="InvokeCoreAsync"/>'s.</summary>
    public string Open(IReadOnlyList<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        if (urls.Count == 0)
        {
            return WebText.NoUrl;
        }

        if (urls.Count > MaxUrls)
        {
            return WebText.TooManyLinks;
        }

        var lines = new List<string>(urls.Count);
        int opened = 0;
        string last = "";
        foreach (var raw in urls)
        {
            var parsed = WebFetcher.ParseUrl(raw);
            if (parsed is null)
            {
                lines.Add(WebText.NotHttp(raw.Trim()));
                continue;
            }

            try
            {
                _web.OpenUrl(parsed.AbsoluteUri);
                NeonCompanion.Diagnostics.DiagnosticLog.Info(WebFetcher.Category, "Opened in the browser: " + parsed.AbsoluteUri);
                opened++;
                last = parsed.AbsoluteUri;
                lines.Add(WebText.OpenedLine(parsed.AbsoluteUri));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or PlatformNotSupportedException)
            {
                lines.Add(WebText.OpenFailed(parsed.AbsoluteUri, ex.Message));
            }
        }

        if (urls.Count == 1)
        {
            return opened == 1 ? WebText.OpenedOne(last) : lines[0];
        }

        var sb = new StringBuilder(opened == 0 ? WebText.NoneOpened(urls.Count) : WebText.OpenedHeader(opened));
        foreach (var line in lines)
        {
            sb.Append('\n').Append(line);
        }

        return sb.ToString();
    }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!ToolArguments.TryReadStringList(arguments, UrlsArgument, out var several, out string raw))
        {
            return new ValueTask<object?>(WebText.BadUrlList(UrlsArgument, raw));
        }

        var urls = new List<string>(several.Count + 1);
        string one = ToolArguments.ReadString(arguments, UrlArgument);
        if (!string.IsNullOrWhiteSpace(one))
        {
            urls.Add(one);
        }

        urls.AddRange(several);
        return new ValueTask<object?>(Open(urls));
    }
}
