using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace NeonCompanion.Sql;

/// <summary>
/// One named connection of <c>sql.json</c> (2026-09-23, the user's ask: "tools for connecting to and querying
/// MSSQL server", after the Python <c>mcp-mssql-read</c> server — its <c>MSSQL_*</c> variables are these keys):
/// the server (<c>host</c>, <c>host,port</c> or <c>host\instance</c>), the database a call opens when it names
/// none, the login (<see cref="Auth"/> <c>sql</c> with <see cref="User"/>/<see cref="Password"/>, or
/// <c>windows</c> — integrated, as the app's own Windows identity), the TLS pair, and a free-text
/// <see cref="Description"/> the model sees in <c>sql_connections</c> and the rules. The password sits in the
/// file as plain text, the way <c>LlmApiKey</c> sits in <c>profile.json</c> (the user's call, 2026-09-23); it is
/// never shown, logged or sent to the model (<see cref="SqlText.ConnectionLine"/> leaves it out). Entra
/// sign-in is not offered: it would pull in Azure.Identity. Read through <see cref="SqlJsonContext"/> alone.
/// </summary>
public sealed class SqlConnectionConfig
{
    /// <summary>The <see cref="Auth"/> word for a SQL login (the default).</summary>
    public const string SqlAuth = "sql";

    /// <summary>The <see cref="Auth"/> word for integrated Windows sign-in.</summary>
    public const string WindowsAuth = "windows";

    /// <summary>The <see cref="Encrypt"/> words, the <see cref="SqlConnectionEncryptOption"/> names in lower case.</summary>
    public static readonly IReadOnlyList<string> EncryptWords = ["strict", "mandatory", "optional"];

    /// <summary>The connect timeout when the entry names none.</summary>
    public const int DefaultConnectTimeoutSeconds = 15;

    /// <summary>The <see cref="ConnectTimeoutSeconds"/> ceiling.</summary>
    public const int MaxConnectTimeoutSeconds = 120;

    /// <summary>The server: <c>host</c>, <c>host,port</c>, <c>host\instance</c> (SqlClient's <c>Data Source</c>).</summary>
    public string? Server { get; set; }

    /// <summary>The database a call opens when it names none; the login's default database when blank.</summary>
    public string? Database { get; set; }

    /// <summary><c>sql</c> (the default) or <c>windows</c>.</summary>
    public string? Auth { get; set; }

    /// <summary>The SQL login, for <c>sql</c>.</summary>
    public string? User { get; set; }

    /// <summary>The SQL login's password, for <c>sql</c>. Plain text; never shown.</summary>
    public string? Password { get; set; }

    /// <summary><c>strict</c>, <c>mandatory</c> (the default) or <c>optional</c>.</summary>
    public string? Encrypt { get; set; }

    /// <summary>Whether a certificate no authority vouches for is accepted — a dev container's self-signed one.</summary>
    public bool TrustServerCertificate { get; set; }

    /// <summary>Seconds a connect may take, 1 to <see cref="MaxConnectTimeoutSeconds"/>; <see cref="DefaultConnectTimeoutSeconds"/> when absent.</summary>
    public int? ConnectTimeoutSeconds { get; set; }

    /// <summary>What the database holds, in the user's words; the model reads it to pick a connection.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the entry signs in as the app's Windows identity.</summary>
    [JsonIgnore]
    public bool IsWindows => string.Equals(Auth?.Trim(), WindowsAuth, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The reason this entry cannot connect, or null: no server, an <see cref="Auth"/> or <see cref="Encrypt"/>
    /// word outside the lists, a SQL login without a user, a connect timeout out of range. Pure.
    /// </summary>
    [JsonIgnore]
    public string? Problem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Server))
            {
                return SqlText.NoServer;
            }

            string auth = Auth?.Trim() ?? "";
            if (auth.Length > 0 && !auth.Equals(SqlAuth, StringComparison.OrdinalIgnoreCase) && !auth.Equals(WindowsAuth, StringComparison.OrdinalIgnoreCase))
            {
                return SqlText.BadAuth(auth);
            }

            if (!IsWindows && string.IsNullOrWhiteSpace(User))
            {
                return SqlText.NoUser;
            }

            string encrypt = Encrypt?.Trim() ?? "";
            if (encrypt.Length > 0 && !EncryptWords.Contains(encrypt.ToLowerInvariant()))
            {
                return SqlText.BadEncrypt(encrypt);
            }

            if (ConnectTimeoutSeconds is { } seconds && (seconds < 1 || seconds > MaxConnectTimeoutSeconds))
            {
                return SqlText.BadConnectTimeout(seconds, MaxConnectTimeoutSeconds);
            }

            return null;
        }
    }

    /// <summary>
    /// The SqlClient connection string for this entry, <paramref name="database"/> (when not blank) in place of
    /// <see cref="Database"/>. Always read-only intent (a replica takes it; a primary ignores it), the app's name
    /// in <c>sys.dm_exec_sessions</c>, pooling on. Call only on an entry without a <see cref="Problem"/>.
    /// </summary>
    public SqlConnectionStringBuilder Builder(string? database = null)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server!.Trim(),
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ApplicationName = "NeonCompanion",
            ConnectTimeout = ConnectTimeoutSeconds ?? DefaultConnectTimeoutSeconds,
            TrustServerCertificate = TrustServerCertificate,
            Encrypt = (Encrypt?.Trim().ToLowerInvariant() ?? "") switch
            {
                "strict" => SqlConnectionEncryptOption.Strict,
                "optional" => SqlConnectionEncryptOption.Optional,
                _ => SqlConnectionEncryptOption.Mandatory,
            },
        };

        string catalog = !string.IsNullOrWhiteSpace(database) ? database.Trim() : Database?.Trim() ?? "";
        if (catalog.Length > 0)
        {
            builder.InitialCatalog = catalog;
        }

        if (IsWindows)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = User!.Trim();
            builder.Password = Password ?? "";
        }

        return builder;
    }
}

/// <summary>A connection by its name, and the file it came from.</summary>
public sealed record SqlNamedConnection(string Name, SqlConnectionConfig Config, string Source);
