using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NeonCompanion.Mcp;

namespace NeonCompanion.Tests.Fakes;

/// <summary>
/// The MCP transport seam for the tests (2026-09-20): one in-process <see cref="McpPipeServer"/> per configured
/// server name, handed to <c>McpSession</c> / <c>CompanionApp</c> as the factory — the real client runs over a
/// pipe and no child process starts. A name in <see cref="Hanging"/> gets a transport whose connect never
/// completes (the timeout's test); one in <see cref="Failing"/> a transport that throws (a command that is not there).
/// </summary>
public sealed class InProcessMcpServers : IAsyncDisposable
{
    private readonly Dictionary<string, McpPipeServer> _servers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<(string Name, string Description)>> _tools = new(StringComparer.Ordinal);

    /// <summary>The default tool list of a server not given its own: an echo and the fail tool.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> DefaultTools = [("echo", "Echoes the text back."), (McpPipeServer.FailToolName, "Always fails.")];

    /// <summary>Names whose connect never completes.</summary>
    public HashSet<string> Hanging { get; } = new(StringComparer.Ordinal);

    /// <summary>Names whose transport throws at once.</summary>
    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    /// <summary>Every transport the factory was asked for, in order: the name and the config it came with.</summary>
    public List<(string Name, McpServerConfig Config)> Requested { get; } = [];

    /// <summary>What a named server lists (set before the connect).</summary>
    public void Tools(string name, params (string Name, string Description)[] tools) => _tools[name] = tools;

    /// <summary>The server behind <paramref name="name"/> after a connect, for its <c>Calls</c>.</summary>
    public McpPipeServer Server(string name) => _servers[name];

    /// <summary>The factory for the seam.</summary>
    public IClientTransport Transport(McpServerConfig config, string name)
    {
        Requested.Add((name, config));
        if (Failing.Contains(name))
        {
            throw new InvalidOperationException("no such command: " + (config.Command ?? config.Url));
        }

        if (Hanging.Contains(name))
        {
            return new HangingTransport(name);
        }

        if (_servers.TryGetValue(name, out var old))
        {
            _ = old.DisposeAsync();
        }

        var server = McpPipeServer.Start(_tools.TryGetValue(name, out var tools) ? tools : DefaultTools, name);
        _servers[name] = server;
        return server.ClientTransport;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers.Values)
        {
            await server.DisposeAsync();
        }

        _servers.Clear();
    }

    private sealed class HangingTransport(string name) : IClientTransport
    {
        public string Name => name;

        public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
