using System.Text.Json;
using NeonCompanion.Diagnostics;

namespace NeonCompanion.Sql;

/// <summary>One thing a load could not use: the file itself, or one connection in it. <see cref="Source"/> is the path, or the path and the connection's name.</summary>
public sealed record SqlConfigProblem(string Source, string Reason);

/// <summary>
/// What <see cref="SqlConfigFile.LoadCatalog"/> found: the usable connections (the profile's first, then the
/// home's the profile does not shadow, each in file order) and the problems, never a throw.
/// </summary>
public sealed record SqlCatalog(IReadOnlyList<SqlNamedConnection> Connections, IReadOnlyList<SqlConfigProblem> Problems)
{
    public static readonly SqlCatalog Empty = new([], []);

    /// <summary>
    /// The connection a call means: <paramref name="name"/> when given (case-insensitive), else
    /// <paramref name="defaultName"/> when that names one, else the first. Null when nothing matches.
    /// </summary>
    public SqlNamedConnection? Find(string? name, string? defaultName)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return Connections.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(defaultName)
            && Connections.FirstOrDefault(c => string.Equals(c.Name, defaultName.Trim(), StringComparison.OrdinalIgnoreCase)) is { } named)
        {
            return named;
        }

        return Connections.Count > 0 ? Connections[0] : null;
    }
}

/// <summary>
/// <c>sql.json</c> (2026-09-23): <c>{ "connections": { "&lt;name&gt;": { … } } }</c>, one in the profile's folder
/// and one in the home, the <c>mcp.json</c> pair (<see cref="Mcp.McpConfigFile"/>): the profile's wins by name.
/// A missing file is empty; a corrupt or unreadable one is a problem and one warning, never a crash. Read
/// afresh at every call (<see cref="SqlAccess"/>), so an edit in the editor counts on the next tool call with no
/// reload. The <c>/tools</c> SQL tab's edit rows create the file with <see cref="EmptyText"/> when it is missing.
/// </summary>
public sealed class SqlConfigFile
{
    /// <summary>The file's name in a profile's folder and in the home.</summary>
    public const string FileName = "sql.json";

    /// <summary>The log category of everything SQL.</summary>
    public const string Category = "Sql";

    /// <summary>What a fresh file holds: no connections, a comment on the shape. Pinned.</summary>
    public const string EmptyText =
        "{\n" +
        "  // One entry per connection; its name is what the model passes as \"connection\". For example:\n" +
        "  // \"adventureworks\": { \"server\": \"127.0.0.1,1433\", \"database\": \"AdventureWorks2022\",\n" +
        "  //   \"auth\": \"sql\", \"user\": \"reader\", \"password\": \"...\", \"encrypt\": \"mandatory\",\n" +
        "  //   \"trustServerCertificate\": true, \"description\": \"the sample sales database\" }\n" +
        "  // auth: sql (user + password) or windows; encrypt: strict, mandatory or optional.\n" +
        "  // The tools only read, but a read-only login is the real guard.\n" +
        "  \"connections\": {}\n" +
        "}\n";

    /// <summary>The connections by name, in file order (the deserializer keeps it).</summary>
    public Dictionary<string, SqlConnectionConfig?> Connections { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The profile's file: <c>&lt;profile&gt;\sql.json</c>.</summary>
    public static string ProfilePath(string profileDirectory) => Path.Combine(profileDirectory, FileName);

    /// <summary>The home's file: <c>&lt;home&gt;\sql.json</c>, every profile's.</summary>
    public static string GlobalPath(string home) => Path.Combine(home, FileName);

    /// <summary>
    /// Reads <paramref name="path"/>: the usable connections in file order and a problem per entry that cannot
    /// connect (<see cref="SqlConnectionConfig.Problem"/>) or has a blank name — for a file that is not JSON or
    /// cannot be read, one problem for the file and one <c>Sql</c> warning. A missing file is empty. Never throws.
    /// </summary>
    public static SqlCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return SqlCatalog.Empty;
        }

        SqlConfigFile? file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(path), SqlJsonContext.Default.SqlConfigFile);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string detail = LogText.Excerpt(ex.Message);
            DiagnosticLog.Warn(Category, SqlText.ConfigProblemLogLine(path, detail));
            return new SqlCatalog([], [new SqlConfigProblem(path, SqlText.UnreadableFile(detail))]);
        }

        if (file is null || file.Connections.Count == 0)
        {
            return SqlCatalog.Empty;
        }

        var connections = new List<SqlNamedConnection>(file.Connections.Count);
        var problems = new List<SqlConfigProblem>();
        foreach (var (name, config) in file.Connections)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                problems.Add(new SqlConfigProblem(path, SqlText.BlankName));
                continue;
            }

            string? reason = config is null ? SqlText.NoServer : config.Problem;
            if (reason is not null)
            {
                problems.Add(new SqlConfigProblem(SqlText.ConnectionSource(path, name), reason));
                continue;
            }

            connections.Add(new SqlNamedConnection(name.Trim(), config!, path));
        }

        return new SqlCatalog(connections, problems);
    }

    /// <summary>The profile's file over the home's (<paramref name="home"/> null = the profile's alone): a name in both is the profile's.</summary>
    public static SqlCatalog LoadCatalog(string profileDirectory, string? home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        var profile = Load(ProfilePath(profileDirectory));
        if (home is null || string.Equals(Path.GetFullPath(ProfilePath(profileDirectory)), Path.GetFullPath(GlobalPath(home)), StringComparison.OrdinalIgnoreCase))
        {
            return profile;
        }

        var global = Load(GlobalPath(home));
        var names = new HashSet<string>(profile.Connections.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        return new SqlCatalog(
            [.. profile.Connections, .. global.Connections.Where(c => !names.Contains(c.Name))],
            [.. profile.Problems, .. global.Problems]);
    }

    /// <summary>Writes <see cref="EmptyText"/> to <paramref name="path"/> when no file is there (the folder made first); true when it wrote. Throws on an IO failure — the caller's notice.</summary>
    public static bool EnsureExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, EmptyText);
        return true;
    }
}
