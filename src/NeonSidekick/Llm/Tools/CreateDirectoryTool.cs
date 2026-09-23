using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonSidekick.Files;

namespace NeonSidekick.Llm.Tools;

/// <summary><c>create_directory(path)</c>: a folder (and any missing parents) under the working directory.</summary>
public sealed class CreateDirectoryTool : FileTool
{
    public const string ToolName = "create_directory";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The folder to create, relative to the working directory. Missing folders on the way are created too." }
          },
          "required": ["path"]
        }
        """);

    public CreateDirectoryTool(WorkingDirectory files) : base(files)
    {
    }

    public override string Name => ToolName;

    public override string Description =>
        "Creates a folder (and any missing parents) under the working directory (the user's cwd / current directory). " +
        "write_file creates folders on its own, so this is for an empty folder.";

    public override JsonElement JsonSchema => Schema;

    public string Describe(string path) => FileText.Created(Files.CreateDirectory(path));

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!RequirePath(arguments, PathArgument, out string path, out string error))
        {
            return new ValueTask<object?>(error);
        }

        return new ValueTask<object?>(Describe(path));
    }
}
