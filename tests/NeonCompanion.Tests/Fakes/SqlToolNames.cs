using NeonCompanion.Llm.Tools;

namespace NeonCompanion.Tests.Fakes;

/// <summary>The SQL tools in the order <c>ChatScreen.SqlTools</c> offers them (2026-09-23). Pinned once, used by every tool-list assertion.</summary>
public static class SqlToolNames
{
    public static readonly string[] All =
    {
        SqlConnectionsTool.ToolName,
        SqlDatabasesTool.ToolName,
        SqlTablesTool.ToolName,
        SqlColumnsTool.ToolName,
        SqlDescribeTool.ToolName,
        SqlRelationshipsTool.ToolName,
        SqlIndexesTool.ToolName,
        SqlQueryTool.ToolName,
    };
}
