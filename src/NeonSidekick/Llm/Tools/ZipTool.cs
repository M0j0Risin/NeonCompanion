using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonSidekick.Files;

namespace NeonSidekick.Llm.Tools;

/// <summary><c>zip(path, to?, overwrite?)</c>: packs a file or folder into a zip archive next to it (or at <c>to</c>).</summary>
public sealed class ZipTool : FileTool
{
    public const string ToolName = "zip";

    public const string ToArgument = "to";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The file or folder to pack, relative to the working directory." },
            "to": { "type": "string", "description": "The archive to create, relative to the working directory. Leave it out for the same name with .zip, next to the original." },
            "overwrite": { "type": "boolean", "description": "true to replace an archive that already exists." }
          },
          "required": ["path"]
        }
        """);

    public ZipTool(WorkingDirectory files) : base(files)
    {
    }

    public override string Name => ToolName;

    public override string Description =>
        "Packs a file or folder under the working directory (the user's cwd / current directory) into a .zip archive; " +
        "by default the archive has the same name with .zip and sits next to the original.";

    public override JsonElement JsonSchema => Schema;

    public string Describe(string path, string? to, bool overwrite) => FileText.Zipped(Files.Zip(path, to, overwrite));

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool? overwrite = ReadOverwrite(arguments, out var raw);
        if (overwrite is null)
        {
            return new ValueTask<object?>(FileText.BadBoolean(OverwriteArgument, raw));
        }

        return new ValueTask<object?>(Describe(ReadPath(arguments), ToolArguments.ReadString(arguments, ToArgument), overwrite.Value));
    }
}
