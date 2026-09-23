using NeonCompanion.Llm.Tools;

namespace NeonCompanion.Tests.Fakes;

/// <summary>The vault tools in the order <c>ChatScreen.ObsidianTools</c> offers them (2026-09-22). Pinned once, used by every tool-list assertion.</summary>
public static class ObsidianToolNames
{
    public static readonly string[] All =
    {
        VaultSearchTool.ToolName,
        VaultListTool.ToolName,
        VaultReadTool.ToolName,
        VaultLinksTool.ToolName,
        VaultDailyTool.ToolName,
        VaultWriteTool.ToolName,
        VaultPropertiesTool.ToolName,
        VaultMoveTool.ToolName,
    };
}
