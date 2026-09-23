using System.Text.Json;
using Microsoft.Extensions.AI;
using NeonCompanion.Files;
using NeonCompanion.Settings;

namespace NeonCompanion.Llm.Tools;

/// <summary>
/// <c>delete(path)</c>: moves a file or folder into the working directory's <c>.trash</c> while
/// <c>File safe edits</c> is on — nothing destroyed, <c>restore</c> brings it back — and removes it in
/// place, a folder with everything in it, while the setting is off (2026-09-20, the user's call). The
/// setting is read at every call, and the description says which one stands.
/// </summary>
public sealed class DeleteTool : FileTool
{
    public const string ToolName = "delete";

    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The file or folder to delete, relative to the working directory." }
          },
          "required": ["path"]
        }
        """);

    private readonly Func<AppSettingsData> _effective;

    /// <param name="effective">The settings <c>File safe edits</c> is read from at every call: into <c>.trash</c>, or gone for good.</param>
    public DeleteTool(WorkingDirectory files, Func<AppSettingsData> effective) : base(files)
    {
        _effective = effective ?? throw new ArgumentNullException(nameof(effective));
    }

    public override string Name => ToolName;

    public override string Description => DescribeTool(_effective().FileSafeEdits);

    /// <summary>
    /// The description under either setting (<c>/tools</c>' Offered tab shows the one that stands). The off-form
    /// names neither <c>restore</c> nor <c>.trash</c> (later still on 2026-09-20, the user's ask: the tool is not
    /// offered then, and the model never hears of a trash), nor the setting itself, nor that nothing brings a file
    /// back (2026-09-21, the user's ask: told so, the model answered about a restore it could not reach — it need
    /// not know of a switch it cannot use). Both end on <see cref="GitNote"/> (2026-09-23, the user's call). Pinned.
    /// </summary>
    public static string DescribeTool(bool safeEdits) =>
        (safeEdits
            ? "Deletes a file or folder under the working directory (the user's cwd / current directory) by moving it to the .trash folder there; " +
              "nothing is destroyed, and restore brings it back."
            : "Deletes a file or folder under the working directory (the user's cwd / current directory) for good; a folder goes with everything in it.") + GitNote;

    /// <summary>The sentence both descriptions end on: <c>.git</c> is refused, whatever the path (2026-09-23). Pinned.</summary>
    public const string GitNote = " A .git folder, anything in it, or a folder holding one is never deleted.";

    public override JsonElement JsonSchema => Schema;

    public string Describe(string path) => FileText.Trashed(Files.Delete(path, _effective().FileSafeEdits));

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new ValueTask<object?>(Describe(ReadPath(arguments)));
    }
}
