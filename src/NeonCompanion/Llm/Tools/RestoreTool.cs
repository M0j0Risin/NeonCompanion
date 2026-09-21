using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Files;
using NeonCompanion.Settings;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>restore(path, overwrite?)</c>: puts back the most recently deleted (or, under <c>File safe edits</c>,
/// edited-over) copy of that path from <c>.trash</c>; over something that is there again only with
/// <c>overwrite</c>, which trashes the live entry first — how an edit is undone (2026-09-17).
/// </summary>
public sealed class RestoreTool : FileTool
{
    public const string ToolName = "restore";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The path the file or folder had before it was deleted or changed, relative to the working directory." },
            "overwrite": { "type": "boolean", "description": "true puts the trashed copy over what is at the path now (that goes to .trash itself). This undoes an edit. Default false: refuse when something is there." }
          },
          "required": ["path"]
        }
        """);

    private readonly Func<AppSettingsData> _effective;

    /// <param name="effective">The settings <c>File safe edits</c> is read from at every call (2026-09-20: whether what is replaced is kept in <c>.trash</c>).</param>
    public RestoreTool(WorkingDirectory files, Func<AppSettingsData> effective) : base(files)
    {
        _effective = effective ?? throw new ArgumentNullException(nameof(effective));
    }

    public override string Name => ToolName;

    public override string Description =>
        "Puts back the most recent copy of a file or folder from the working directory's .trash, at the path it had: what delete moved there, " +
        "or the previous version a patch_file or a replacing or appending write_file kept. Refuses if something is at that path again unless overwrite is true — " +
        "restore with overwrite true undoes the last edit of a file: the live file is kept in .trash while File safe edits is on, else replaced in place (a folder in the way is refused).";

    public override JsonElement JsonSchema => Schema;

    public string Describe(string path, bool overwrite = false) => FileText.Restored(Files.Restore(path, overwrite, _effective().FileSafeEdits));

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

        return new ValueTask<object?>(Describe(path, overwrite.Value));
    }
}
