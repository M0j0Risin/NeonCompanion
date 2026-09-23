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
        VaultDeleteTool.ToolName,
    };

    /// <summary><see cref="All"/> less <c>vault_delete</c> (later on 2026-09-22): what a turn offers while <c>Obsidian allow delete</c> is off — the default — and what <c>Assistant.ObsidianRule</c> names.</summary>
    public static readonly string[] WithoutDelete = All.Where(name => name != VaultDeleteTool.ToolName).ToArray();
}
