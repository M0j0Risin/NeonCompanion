using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Files;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>unzip(path, to?, overwrite?)</c>: extracts a zip archive into a folder, all or nothing —
/// every entry is checked to land inside the folder (zip-slip) and, without <c>overwrite</c>, on
/// nothing that exists, before the first byte is written.
/// </summary>
public sealed class UnzipTool : FileTool
{
    public const string ToolName = "unzip";

    public const string ToArgument = "to";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The .zip archive, relative to the working directory." },
            "to": { "type": "string", "description": "The folder to extract into, relative to the working directory. Leave it out for a folder named after the archive, next to it." },
            "overwrite": { "type": "boolean", "description": "true to replace files already in the way. Without it nothing is extracted if any file would be replaced." }
          },
          "required": ["path"]
        }
        """);

    public UnzipTool(WorkingDirectory files) : base(files)
    {
    }

    public override string Name => ToolName;

    public override string Description =>
        "Extracts a .zip archive under the working directory (the user's cwd / current directory) into a folder (by default one named after the archive, next to it). " +
        "All or nothing: refuses if any entry would land on an existing file, unless overwrite is true.";

    public override JsonElement JsonSchema => Schema;

    public string Describe(string path, string? to, bool overwrite) => FileText.Unzipped(Files.Unzip(path, to, overwrite));

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool? overwrite = ReadOverwrite(arguments, out var raw);
        if (overwrite is null)
        {
            return new ValueTask<object?>(FileText.BadBoolean(OverwriteArgument, raw));
        }

        if (!RequirePath(arguments, PathArgument, out string path, out string error))
        {
            return new ValueTask<object?>(error);
        }

        return new ValueTask<object?>(Describe(path, ToolArguments.ReadString(arguments, ToArgument), overwrite.Value));
    }
}
