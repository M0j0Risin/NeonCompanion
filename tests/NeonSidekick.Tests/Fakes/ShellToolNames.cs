using NeonSidekick.Llm.Tools;

namespace NeonSidekick.Tests.Fakes;

/// <summary>The shell tools in the order <c>ChatScreen.ShellTools</c> offers them (2026-09-21): after the git tools on every turn. Pinned once, used by every tool-list assertion.</summary>
public static class ShellToolNames
{
    public static readonly string[] All =
    {
        RunCommandTool.ToolName,
        ProcessTool.ToolName,
        ExecuteCodeTool.ToolName,
    };
}
