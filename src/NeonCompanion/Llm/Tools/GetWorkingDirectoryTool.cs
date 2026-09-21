using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Files;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>get_working_directory()</c>: the sandbox's full path, created if it was missing. The
/// description carries every name the user has for it, so "where do my files go", "the cwd" and
/// "the current directory" all land here.
/// </summary>
public sealed class GetWorkingDirectoryTool : FileTool
{
    public const string ToolName = "get_working_directory";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {}
        }
        """);

    private readonly Func<bool> _isDefault;

    /// <param name="isDefault">Whether the root in force is the profile's own folder rather than a configured path.</param>
    public GetWorkingDirectoryTool(WorkingDirectory files, Func<bool> isDefault) : base(files)
    {
        _isDefault = isDefault ?? throw new ArgumentNullException(nameof(isDefault));
    }

    public override string Name => ToolName;

    public override string Description =>
        "The user's working directory: the folder they call the cwd, the current directory or the current working directory, where their files live and where you may read and write. " +
        "Call it before answering where files are or go.";

    public override JsonElement JsonSchema => Schema;

    /// <summary>The path (and whether it is the profile's default), pinned; a folder that cannot be created is an <c>Error:</c> sentence.</summary>
    public string Describe()
    {
        try
        {
            return FileText.Describe(Files.EnsureExists(), _isDefault());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return FileText.CouldNot("create", Files.Root, ex.Message);
        }
    }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Describe());
    }
}
