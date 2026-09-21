using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Files;

namespace NeonCompanion.Llm.Tools;

/// <summary><c>file_info(path)</c>: size, modified time and line/word count of a file; counts and total size of a folder.</summary>
public sealed class FileInfoTool : FileTool
{
    public const string ToolName = "file_info";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "A file or folder, relative to the working directory." }
          },
          "required": ["path"]
        }
        """);

    public FileInfoTool(WorkingDirectory files) : base(files)
    {
    }

    public override string Name => ToolName;

    public override string Description =>
        "Size, modified time, line and word count, line ending (CRLF or LF) and BOM of a file under the working directory (the user's cwd / current directory); " +
        "file and folder counts and total size of a folder. Also the way to check whether something exists.";

    public override JsonElement JsonSchema => Schema;

    public string Describe(string path) => FileText.Info(Files.Info(path));

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Describe(ReadPath(arguments)));
    }
}
