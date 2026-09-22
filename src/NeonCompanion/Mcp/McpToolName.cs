using System.Globalization;
using System.Text;

namespace NeonCompanion.Mcp;

/// <summary>
/// How an MCP tool is named for the model (2026-09-20, the user's call): always
/// <c>&lt;server&gt;__&lt;tool&gt;</c>, never the server's bare name — so a gateway's <c>get_current_time</c>
/// never shadows the app's, and the transcript's <c>🛠️</c> line says which server answered. Both halves
/// are sanitised to <c>[A-Za-z0-9_-]</c> (some servers use dotted tool names) and the whole is cut at
/// <see cref="MaxLength"/>, OpenAI's rule on a function name; two tools that collide after the cut are
/// told apart with <c>_2</c>, <c>_3</c>… (<see cref="Unique"/>), since the turn loop matches the first
/// exact name. Pure.
/// </summary>
public static class McpToolName
{
    /// <summary>Between the server's name and the tool's.</summary>
    public const string Separator = "__";

    /// <summary>The longest name an OpenAI-compatible server accepts for a function.</summary>
    public const int MaxLength = 64;

    /// <summary>Every character outside <c>[A-Za-z0-9_-]</c> becomes <c>_</c>; an empty text becomes <c>_</c>.</summary>
    public static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return "_";
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' ? c : '_');
        }

        return sb.ToString();
    }

    /// <summary>The prefixed name: <c>docker__fetch</c>, cut at <see cref="MaxLength"/>.</summary>
    public static string Prefixed(string server, string tool)
    {
        string name = Sanitize(server) + Separator + Sanitize(tool);
        return name.Length <= MaxLength ? name : name[..MaxLength];
    }

    /// <summary>
    /// The names made distinct in order: a repeat gets <c>_2</c>, then <c>_3</c>…, the base cut so the
    /// suffix still fits <see cref="MaxLength"/>. Ordinal.
    /// </summary>
    public static IReadOnlyList<string> Unique(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var result = new List<string>(names.Count);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            string candidate = name;
            for (int n = 2; !taken.Add(candidate); n++)
            {
                string suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
                int keep = Math.Min(name.Length, MaxLength - suffix.Length);
                candidate = name[..keep] + suffix;
            }

            result.Add(candidate);
        }

        return result;
    }
}
