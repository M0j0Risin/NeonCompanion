using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Files;
using NeonCompanion.Settings;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>move(from, to, overwrite?)</c>: renames or moves a file or a folder. Named <c>move</c>, not
/// <c>move_file</c>, because it takes either; there is no separate rename tool — the description
/// leads with the word, and the result says <c>renamed</c> when only the last segment changed.
/// </summary>
public sealed class MoveTool : FileTool
{
    public const string ToolName = "move";

    public const string FromArgument = "from";
    public const string ToArgument = "to";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "description": "The file or folder to rename or move, relative to the working directory." },
            "to": { "type": "string", "description": "Its new path, relative to the working directory: a new name in the same place is a rename; another folder is a move." },
            "overwrite": { "type": "boolean", "description": "true to replace whatever is already at the new path. Without it the move is refused." }
          },
          "required": ["from", "to"]
        }
        """);

    private readonly Func<AppSettingsData> _effective;

    /// <param name="effective">The settings <c>File safe edits</c> is read from at every call (2026-09-20: whether what is replaced is kept in <c>.trash</c>).</param>
    public MoveTool(WorkingDirectory files, Func<AppSettingsData> effective) : base(files)
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
        "Renames or moves a file or a folder under the working directory (the user's cwd / current directory): to is the new path " +
        "(a new name in the same place is a rename; a folder moves with everything in it). Refuses to replace something already at the new path unless overwrite is true; " +
        (safeEdits
            ? "what is replaced is kept in .trash while File safe edits is on, else a file is replaced in place and a folder in the way is refused."
            : "a file is replaced in place and a folder in the way is refused.");

    public override JsonElement JsonSchema => Schema;

    public string Describe(string from, string to, bool overwrite) => FileText.Moved(Files.Move(from, to, overwrite, _effective().FileSafeEdits));

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
