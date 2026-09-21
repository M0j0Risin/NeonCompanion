using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Files;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>open(path?)</c>: hands a file to the user's own editor or viewer, or a folder to Explorer,
/// through the injected opener (<c>PersonaFile.OpenInEditor</c>: shell execute, never waits).
/// No path is the working directory itself.
/// </summary>
public sealed class OpenTool : FileTool
{
    public const string ToolName = "open";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The file or folder to open, relative to the working directory. Leave it out to open the working directory itself." }
          }
        }
        """);

    private readonly Action<string> _opener;

    public OpenTool(WorkingDirectory files, Action<string> opener) : base(files)
    {
        _opener = opener ?? throw new ArgumentNullException(nameof(opener));
    }

    public override string Name => ToolName;

    public override string Description =>
        "Opens a file in the user's own editor or viewer, or a folder in Explorer, on their screen; no path opens the working directory (the user's cwd / current directory) itself. " +
        "Use it when they ask to open, show or see something rather than to have it read out.";

    public override JsonElement JsonSchema => Schema;

    public string Describe(string path) => FileText.Opened(Files.Open(path, _opener));

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Describe(ReadPath(arguments)));
    }
}
