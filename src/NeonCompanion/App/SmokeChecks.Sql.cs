using System.Globalization;
using Microsoft.Data.SqlClient;
using NeonCompanion.Sql;

namespace NeonCompanion.App;

public static partial class SmokeChecks
{
    /// <summary>
    /// <c>sql:parse-and-sni</c> (2026-09-23): the SQL tools' two libraries on the published binary, with no server.
    /// ScriptDom parses a SELECT the gate lets through and refuses the <c>mcp-mssql-read</c> bypass
    /// <c>SELECT 1 DELETE FROM t</c>; then SqlClient opens a named pipe nothing listens on, with a one-second
    /// timeout — a <see cref="SqlException"/> naming the pipe provider is the proof that the native SNI layer
    /// (<see cref="SqlAccess.NativeLibraryFileName"/>) loaded and the globalization SqlClient demands is there
    /// (the exe under <c>InvariantGlobalization</c> threw <c>NotSupportedException</c> from the constructor in the
    /// spike); a <see cref="DllNotFoundException"/>, a type-initializer or a not-supported failure is the break.
    /// SqlClient declares no AOT support: the JIT proves nothing, this line does.
    /// </summary>
    public static SmokeCheck ProbeSql()
    {
        const string name = "sql:parse-and-sni";
        if (SqlReadOnlyGate.Check("WITH c AS (SELECT 1 AS x) SELECT x FROM c") is { } refused)
        {
            return new SmokeCheck(name, false, "the gate refused a plain CTE: " + refused);
        }

        if (SqlReadOnlyGate.Check("SELECT 1 DELETE FROM t") is null)
        {
            return new SmokeCheck(name, false, "the gate let SELECT 1 DELETE FROM t through");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = @"np:\\.\pipe\neoncompanion-smoke-" + Guid.NewGuid().ToString("N") + @"\sql\query",
            IntegratedSecurity = true,
            ConnectTimeout = 1,
            Encrypt = SqlConnectionEncryptOption.Optional,
            Pooling = false,
        };
        try
        {
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            return new SmokeCheck(name, false, "a pipe nobody serves accepted a connection");
        }
        catch (SqlException ex)
        {
            return new SmokeCheck(name, true, $"ScriptDom gate ok; SqlClient {typeof(SqlConnection).Assembly.GetName().Version} answered error {ex.Number.ToString(CultureInfo.InvariantCulture)} through the native SNI");
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or NotSupportedException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new SmokeCheck(name, false, ex.GetType().Name + ": " + Diagnostics.LogText.Excerpt(ex.Message));
        }
    }

    /// <summary>
    /// <c>culture:invariant</c> (2026-09-23): <see cref="CulturePin"/> held — the current culture is the invariant one,
    /// and a number and a date print as they did under <c>InvariantGlobalization</c>. The flag is off since SqlClient
    /// refuses it; this is the line that says the process still formats the way it always did.
    /// </summary>
    public static SmokeCheck ProbeCulture()
    {
        const string name = "culture:invariant";
        string sample = string.Format(CultureInfo.CurrentCulture, "{0} {1:d}", 1234.5, new DateTime(2009, 1, 7));
        bool ok = CulturePin.Holds && sample == "1234.5 01/07/2009";
        return new SmokeCheck(name, ok, ok ? "current culture invariant; " + sample : $"current culture '{CultureInfo.CurrentCulture.Name}', sample '{sample}'");
    }
}
