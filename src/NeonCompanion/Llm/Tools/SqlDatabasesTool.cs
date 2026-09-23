using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Settings;
using NeonCompanion.Sql;

namespace NeonCompanion.Llm.Tools;

/// <summary><c>sql_databases(connection?)</c>: the databases on the connection's server that its login may open (<c>HAS_DBACCESS</c>), with state, compatibility level and collation.</summary>
public sealed class SqlDatabasesTool : SqlTool
{
    public const string ToolName = "sql_databases";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        $$"""
        {
          "type": "object",
          "properties": {
            {{ConnectionProperty}}
          }
        }
        """);

    public SqlDatabasesTool(SqlAccess sql, Func<AppSettingsData> effective) : base(sql, effective)
    {
    }

    public override string Name => ToolName;

    public override string Description =>
        "Lists the databases on a SQL connection's server that its login may open, with each one's state, compatibility level and collation. " +
        "Pass one as \"database\" to the other sql_ tools to work in it.";

    public override JsonElement JsonSchema => Schema;

    public async Task<string> DescribeAsync(string? connection, CancellationToken cancellationToken)
    {
        var run = await CatalogAsync(connection, null, SqlCatalogQueries.Databases, [], cancellationToken).ConfigureAwait(false);
        return run.Outcome == SqlOutcome.Ok ? SqlText.Listing("database", "databases", run, Files.WorkingDirectory.MaxReadChars) : SqlText.Error(run);
    }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return await DescribeAsync(Optional(arguments, ConnectionArgument), cancellationToken).ConfigureAwait(false);
    }
}
