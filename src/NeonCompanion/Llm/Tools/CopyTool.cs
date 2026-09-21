using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Files;
using NeonCompanion.Settings;

namespace NeonCompanion.Llm.Tools;

/// <summary><c>copy(from, to, overwrite?)</c>: copies a file or a folder (with everything in it) to a new path.</summary>
public sealed class CopyTool : FileTool
{
    public const string ToolName = "copy";

    public const string FromArgument = "from";
    public const string ToArgument = "to";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "description": "The file or folder to copy, relative to the working directory." },
            "to": { "type": "string", "description": "The path of the copy, relative to the working directory (the new name, not a parent folder)." },
            "overwrite": { "type": "boolean", "description": "true to replace files already at the new path. Without it the copy is refused." }
          },
          "required": ["from", "to"]
        }
        """);

    private readonly Func<AppSettingsData> _effective;

    /// <param name="effective">The settings <c>File safe edits</c> is read from at every call (2026-09-20: whether what is replaced is kept in <c>.trash</c>).</param>
    public CopyTool(WorkingDirectory files, Func<AppSettingsData> effective) : base(files)
    {
        _effective = effective ?? throw new ArgumentNullException(nameof(effective));
    }

    public override string Name => ToolName;

    public override string Description => DescribeTool(_effective().FileSafeEdits);

    /// <summary>
    /// The description under either setting, read at every call (later still on 2026-09-20, the user's ask): the
    /// off-form names no <c>.trash</c>, since nothing is kept then and the model never hears of a trash. Pinned.
    /// </summary>
    public static string DescribeTool(bool safeEdits) =>
        "Copies a file or a folder (with everything in it) under the working directory (the user's cwd / current directory) to a new path. " +
        "Refuses to replace something already at the new path unless overwrite is true; " +
        (safeEdits
            ? "what is replaced is kept in .trash while File safe edits is on, else a file is replaced in place and a folder in the way is refused (a folder copied over a folder merges into it)."
            : "a file is replaced in place and a folder in the way is refused (a folder copied over a folder merges into it).");

    public override JsonElement JsonSchema => Schema;

    public string Describe(string from, string to, bool overwrite) => FileText.Copied(Files.Copy(from, to, overwrite, _effective().FileSafeEdits));

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool? overwrite = ReadOverwrite(arguments, out var raw);
        if (overwrite is null)
        {
            return new ValueTask<object?>(FileText.BadBoolean(OverwriteArgument, raw));
        }

        if (!RequirePath(arguments, FromArgument, out string from, out string error) || !RequirePath(arguments, ToArgument, out string to, out error))
        {
            return new ValueTask<object?>(error);
        }

        return new ValueTask<object?>(Describe(from, to, overwrite.Value));
    }
}
