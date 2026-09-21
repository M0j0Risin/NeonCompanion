using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Timers;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>list_timers()</c>: every timer and how long each has left. The only way the model learns
/// what remains — nothing about timers is in the prompt, so a long turn cannot carry a stale
/// figure.
/// </summary>
public sealed class ListTimersTool : AIFunction
{
    public const string ToolName = "list_timers";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {}
        }
        """);

    private readonly TimerBoard _board;

    public ListTimersTool(TimerBoard board)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
    }

    public override string Name => ToolName;

    public override string Description =>
        "Every running timer and how long each has left. Call it to answer how much time remains on a timer, or which timers exist.";

    public override JsonElement JsonSchema => Schema;

    public string Describe() => TimerText.List(_board.Snapshot());

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Describe());
    }
}
