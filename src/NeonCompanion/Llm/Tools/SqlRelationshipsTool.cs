using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Settings;
using NeonCompanion.Sql;

namespace NeonCompanion.Llm.Tools;

/// <summary><c>sql_relationships(connection?, database?, table?)</c>: the foreign-key join paths <c>from.col -&gt; to.col</c>, every one or those touching a table — so a JOIN is written from the keys, not guessed.</summary>
public sealed class SqlRelationshipsTool : SqlTool
{
    public const string ToolName = "sql_relationships";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        $$"""
        {
          "type": "object",
          "properties": {
            {{ConnectionProperty}},
            {{DatabaseProperty}},
            "table": { "type": "string", "description": "Only the keys into or out of this table (schema.name); leave it out for every one in the database." }
          }
        }
        """);

    public SqlRelationshipsTool(SqlAccess sql, Func<AppSettingsData> effective) : base(sql, effective)
    {
    }

    public override string Name => ToolName;

    public override string Description =>
        "Lists the foreign-key relationships of a SQL database as join paths, from_table.from_column -> to_table.to_column, " +
        "every one or only those touching one table. Use them to write JOINs without guessing column names.";

    public override JsonElement JsonSchema => Schema;

    public async Task<string> DescribeAsync(string? connection, string? database, string? table, CancellationToken cancellationToken)
    {
        object? id = null;
        string? label = null;
        if (table is not null)
        {
            var match = await FindTableAsync(connection, database, table, cancellationToken).ConfigureAwait(false);
            if (match.Error is { } error)
            {
                return error;
            }

            id = match.Id;
            label = match.Schema + "." + match.Name;
        }

        var run = await CatalogAsync(connection, database, SqlCatalogQueries.Relationships, [new SqlParameterValue("id", id)], cancellationToken).ConfigureAwait(false);
        return run.Outcome == SqlOutcome.Ok ? SqlText.Relationships(label, run, Files.WorkingDirectory.MaxReadChars) : SqlText.Error(run);
    }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return await DescribeAsync(Optional(arguments, ConnectionArgument), Optional(arguments, DatabaseArgument), Optional(arguments, TableArgument), cancellationToken).ConfigureAwait(false);
    }
}
