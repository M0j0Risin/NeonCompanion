using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeonSidekick.Mcp;

/// <summary>
/// One server of <c>mcp.json</c> (2026-09-20), in the ecosystem's own spelling so a block pasted from
/// Claude Desktop or Claude Code loads as it is: a stdio server names a <see cref="Command"/> (with
/// <see cref="Args"/>, <see cref="Env"/> and <see cref="Cwd"/> optional), a streamable-HTTP server a
/// <see cref="Url"/> (with <see cref="Headers"/> optional); <see cref="Type"/> is read and ignored — the
/// two keys decide. Neither or both is a problem the catalog reports (<see cref="McpCatalog"/>), never a
/// throw. Read through <see cref="McpJsonContext"/> alone (camel-case keys, comments and a trailing comma
/// tolerated).
/// </summary>
public sealed class McpServerConfig
{
    /// <summary>The transport word other clients write (<c>stdio</c>, <c>http</c>, <c>sse</c>); kept for the round trip, never consulted.</summary>
    public string? Type { get; set; }

    /// <summary>The stdio server's executable, resolved by the OS as a process start would (<c>docker</c>, <c>npx</c>, a full path).</summary>
    public string? Command { get; set; }

    /// <summary>The stdio server's arguments, one per element.</summary>
    public List<string>? Args { get; set; }

    /// <summary>Variables set for the stdio server on top of the app's own environment.</summary>
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>The stdio server's working directory; the app's own when absent.</summary>
    public string? Cwd { get; set; }

    /// <summary>The streamable-HTTP server's endpoint (<c>http://localhost:8811/mcp</c>).</summary>
    public string? Url { get; set; }

    /// <summary>Headers sent with every request to the HTTP server (<c>Authorization</c> and the like).</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>Whether this is a stdio server: a non-blank <see cref="Command"/>.</summary>
    [JsonIgnore]
    public bool IsStdio => !string.IsNullOrWhiteSpace(Command);

    /// <summary>Whether this is an HTTP server: a non-blank <see cref="Url"/>.</summary>
    [JsonIgnore]
    public bool IsHttp => !string.IsNullOrWhiteSpace(Url);

    /// <summary>
    /// The reason this entry cannot be started, or null: neither key (<see cref="McpText.NeitherCommandNorUrl"/>),
    /// both (<see cref="McpText.BothCommandAndUrl"/>), or a <see cref="Url"/> that is not an absolute http(s)
    /// address (<see cref="McpText.BadUrl"/>). Pure.
    /// </summary>
    [JsonIgnore]
    public string? Problem
    {
        get
        {
            if (IsStdio && IsHttp)
            {
                return McpText.BothCommandAndUrl;
            }

            if (!IsStdio && !IsHttp)
            {
                return McpText.NeitherCommandNorUrl;
            }

            if (IsHttp && (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                return McpText.BadUrl(Url!);
            }

            return null;
        }
    }

    /// <summary>
    /// The entry as canonical JSON, so a reload can tell a changed server from an untouched one
    /// (the file's own spacing and key order never count). Through the source-generated context.
    /// </summary>
    public string Fingerprint() => JsonSerializer.Serialize(this, McpJsonContext.Default.McpServerConfig);

    /// <summary>What the pane says of the transport: <c>stdio: docker mcp gateway run</c> or <c>http: http://…</c>. Pinned through <see cref="McpText.Transport"/>.</summary>
    public string Describe() => McpText.Transport(this);
}
