using System.Text;
using NeonSidekick.Files;

namespace NeonSidekick.Obsidian;

/// <summary>
/// One Markdown note of the vault as the index last read it: its vault-relative path (<c>/</c>-separated,
/// with <c>.md</c>), its name (the file name without <c>.md</c>), when and how big it was, its
/// properties, their tags and aliases, and what <see cref="NoteScanner"/> found in its body.
/// </summary>
public sealed record NoteEntry(string Relative, string Full, DateTime ModifiedUtc, long Length, NoteFrontmatter Frontmatter, NoteScan Scan)
{
    public string Name { get; } = VaultPaths.NameOf(Relative);

    public string Folder { get; } = VaultPaths.FolderOf(Relative);

    public IReadOnlyList<string> Aliases { get; } = NoteProperties.Aliases(Frontmatter);

    /// <summary>Every tag the note carries, inline and in its properties, first spelling kept, no duplicates.</summary>
    public IReadOnlyList<string> Tags { get; } = NoteProperties.Tags(Frontmatter).Concat(Scan.Tags.Select(t => t.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Whether the note carries <paramref name="tag"/> or a tag nested under it (<c>project</c> matches <c>project/alpha</c>).</summary>
    public bool HasTag(string tag)
    {
        string wanted = tag.Trim().TrimStart('#');
        return Tags.Any(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase) || t.StartsWith(wanted + "/", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>How a link or a <c>note</c> argument found its note: by its path, by its name, by an alias.</summary>
public enum ResolveBy
{
    None,
    Path,
    RelativePath,
    Name,
    Alias,
}

/// <summary>
/// What a link or a <c>note</c> argument names: the note (or, for an attachment, the file's relative
/// path), how it was found, and the other notes of the same name a short link could also have meant.
/// </summary>
public sealed record Resolution(NoteEntry? Note, string? Attachment, ResolveBy By, IReadOnlyList<string> Others)
{
    public static Resolution Unresolved { get; } = new(null, null, ResolveBy.None, []);

    public bool Found => Note is not null || Attachment is not null;

    /// <summary>The vault-relative path of what was found, or null.</summary>
    public string? Relative => Note?.Relative ?? Attachment;
}

/// <summary>The vault's path spelling (2026-09-22): <c>/</c>-separated and relative, as Obsidian's links and its own UI write them.</summary>
public static class VaultPaths
{
    public const string NoteExtension = ".md";

    /// <summary>A typed path as the vault spells it: <c>\</c> as <c>/</c>, no leading <c>./</c> or <c>/</c>, no trailing <c>/</c>.</summary>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string p = path.Trim().Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        return p.Trim('/');
    }

    public static bool IsNote(string path) => path.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the name ends in a file extension: a dot, then one to six letters and digits starting with a
    /// letter (<c>.png</c>, <c>.mp4</c>, <c>.canvas</c>) — so a dotted note name (<c>2026.09.22</c>,
    /// <c>v1.2 plan</c>) is a note's name, not a file type.
    /// </summary>
    public static bool HasFileExtension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int slash = path.LastIndexOf('/');
        int dot = path.LastIndexOf('.');
        if (dot <= slash + 1 || dot == path.Length - 1)
        {
            return false;
        }

        var ext = path.AsSpan(dot + 1);
        if (ext.Length > 6 || !char.IsAsciiLetter(ext[0]))
        {
            return false;
        }

        foreach (char c in ext)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Obsidian's rule for a file name: none of <c>* " \ / &lt; &gt; : | ?</c> (a <c>:</c> would also be an NTFS stream), no control character, not ending in a dot or space.</summary>
    public static bool IsValidName(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.Length == 0 || segment.EndsWith('.') || segment.EndsWith(' '))
        {
            return false;
        }

        foreach (char c in segment)
        {
            if (c is '*' or '"' or '\\' or '/' or '<' or '>' or ':' or '|' or '?' || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>Projects/Plan.md</c> as <c>Plan</c>; an attachment keeps its extension (<c>diagram.png</c>).</summary>
    public static string NameOf(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        int slash = relative.LastIndexOf('/');
        string name = slash < 0 ? relative : relative[(slash + 1)..];
        return IsNote(name) ? name[..^NoteExtension.Length] : name;
    }

    /// <summary><c>Projects/Plan.md</c> as <c>Projects</c>; empty at the root.</summary>
    public static string FolderOf(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        int slash = relative.LastIndexOf('/');
        return slash < 0 ? "" : relative[..slash];
    }

    public static string Join(string folder, string name) => folder.Length == 0 ? name : folder + "/" + name;

    /// <summary><c>../x/y.md</c> against <paramref name="folder"/>, <c>.</c> and <c>..</c> folded; null when it climbs above the vault.</summary>
    public static string? Combine(string folder, string relative)
    {
        var parts = new List<string>(folder.Length == 0 ? [] : folder.Split('/'));
        foreach (var part in relative.Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    return null;
                }

                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    /// <summary>The path from <paramref name="fromFolder"/> to <paramref name="target"/>: <c>../Archive/Plan.md</c>.</summary>
    public static string RelativeFrom(string fromFolder, string target)
    {
        var from = fromFolder.Length == 0 ? Array.Empty<string>() : fromFolder.Split('/');
        var to = target.Split('/');
        int common = 0;
        while (common < from.Length && common < to.Length - 1 && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        var sb = new StringBuilder();
        for (int i = common; i < from.Length; i++)
        {
            sb.Append("../");
        }

        sb.Append(string.Join('/', to.Skip(common)));
        return sb.ToString();
    }

    /// <summary>Whether a vault-relative path touches a dot-folder or dot-file (<c>.obsidian</c>, <c>.trash</c>, <c>.git</c>): Obsidian's own, never a note.</summary>
    public static bool IsHidden(string relative) =>
        relative.Split('/').Any(part => part.StartsWith('.') && part != "." && part != "..");
}

/// <summary>
/// The vault as the tools see it (2026-09-22): every Markdown note and every other file, walked from the
/// root with the dot-folders (<c>.obsidian</c>, <c>.trash</c>, <c>.git</c>) and hidden, system and
/// reparse-point entries skipped. <see cref="Refresh"/> re-walks on each call and re-reads a note only when
/// its size or time changed, so a vault edited in Obsidian between two calls is current without a watcher.
///
/// <para>Resolution is Obsidian's (<see cref="Resolve"/>): an exact vault path first (with or without
/// <c>.md</c>), then a path relative to the linking note's folder (a Markdown link's usual form), then a
/// path suffix (<c>Projects/Plan</c> for <c>Work/Projects/Plan.md</c>), then the name, then an alias;
/// among several of one name the linking note's own folder wins, then the shortest path, then the first
/// in order — and the result names the others. Not thread-safe: <see cref="ObsidianVault"/> holds the lock.</para>
/// </summary>
public sealed class NoteIndex
{
    /// <summary>The most notes read into the index; a bigger vault is cut there and says so.</summary>
    public const int MaxNotes = 20_000;

    /// <summary>The most attachment paths kept for resolution.</summary>
    public const int MaxAttachments = 50_000;

    private readonly Dictionary<string, NoteEntry> _notes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _attachments = [];
    private Dictionary<string, List<NoteEntry>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<NoteEntry>> _byAlias = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _attachmentsByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _attachmentsByPath = new(StringComparer.OrdinalIgnoreCase);
    private string _root = "";

    /// <summary>The notes in path order.</summary>
    public IReadOnlyList<NoteEntry> Notes { get; private set; } = [];

    public IReadOnlyList<string> Attachments => _attachments;

    /// <summary>Whether the last walk stopped at <see cref="MaxNotes"/>.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Re-walks <paramref name="root"/>: new and changed notes read, gone ones dropped, the name tables rebuilt.</summary>
    public void Refresh(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!string.Equals(root, _root, StringComparison.OrdinalIgnoreCase))
        {
            _notes.Clear();
            _root = root;
        }

        _attachments.Clear();
        Truncated = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var full in Walk(root))
        {
            string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (!VaultPaths.IsNote(relative))
            {
                if (_attachments.Count < MaxAttachments)
                {
                    _attachments.Add(relative);
                }

                continue;
            }

            if (seen.Count >= MaxNotes)
            {
                Truncated = true;
                continue;
            }

            seen.Add(relative);
            FileInfo info;
            try
            {
                info = new FileInfo(full);
                _ = info.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (_notes.TryGetValue(relative, out var known) && known.Length == info.Length && known.ModifiedUtc == info.LastWriteTimeUtc)
            {
                continue;
            }

            _notes[relative] = Read(relative, full, info);
        }

        foreach (var gone in _notes.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _notes.Remove(gone);
        }

        Rebuild();
    }

    /// <summary>Re-reads one note now (after a tool wrote it), or drops it when it is gone.</summary>
    public void Update(string relative, string full)
    {
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                _notes.Remove(relative);
            }
            else
            {
                _notes[relative] = Read(relative, full, info);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notes.Remove(relative);
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Notes = _notes.Values.OrderBy(n => n.Relative, StringComparer.OrdinalIgnoreCase).ToList();
        _byName = Group(Notes, n => [n.Name]);
        _byAlias = Group(Notes, n => n.Aliases);
        _attachmentsByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _attachmentsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in _attachments)
        {
            _attachmentsByPath[a] = a;
            string name = VaultPaths.NameOf(a);
            if (!_attachmentsByName.TryGetValue(name, out var list))
            {
                _attachmentsByName[name] = list = [];
            }

            list.Add(a);
        }

        static Dictionary<string, List<NoteEntry>> Group(IEnumerable<NoteEntry> notes, Func<NoteEntry, IEnumerable<string>> keys)
        {
            var map = new Dictionary<string, List<NoteEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in notes)
            {
                foreach (var key in keys(note))
                {
                    if (!map.TryGetValue(key, out var list))
                    {
                        map[key] = list = [];
                    }

                    if (!list.Contains(note))
                    {
                        list.Add(note);
                    }
                }
            }

            return map;
        }
    }

    public NoteEntry? Get(string relative) => _notes.GetValueOrDefault(relative);

    private static NoteEntry Read(string relative, string full, FileInfo info)
    {
        var lines = info.Length <= WorkingDirectory.MaxTextFileBytes ? ReadLines(full) : null;
        if (lines is null)
        {
            return new NoteEntry(relative, full, info.LastWriteTimeUtc, info.Length, NoteFrontmatter.None, NoteScan.Empty);
        }

        return new NoteEntry(relative, full, info.LastWriteTimeUtc, info.Length, NoteProperties.Parse(lines), NoteScanner.Scan(lines));
    }

    /// <summary>A note's lines (BOM dropped), or null when it cannot be read or is not text.</summary>
    public static List<string>? ReadLines(string full)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(full);
            return WorkingDirectory.LooksBinary(bytes) ? null : WorkingDirectory.SplitLines(WorkingDirectory.Decode(bytes, out _));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Walk(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> folders;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", options).ToList();
                folders = Directory.EnumerateDirectories(folder, "*", options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!Path.GetFileName(file).StartsWith('.'))
                {
                    yield return file;
                }
            }

            foreach (var sub in folders.OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!Path.GetFileName(sub).StartsWith('.'))
                {
                    pending.Push(sub);
                }
            }
        }
    }

    /// <summary>
    /// What <paramref name="reference"/> names, seen from the note in <paramref name="fromFolder"/> (null
    /// for a tool's own argument, which has no linking note). The reference is a link's target as
    /// written — no <c>#</c> part, no alias — or a <c>note</c> argument.
    /// </summary>
    public Resolution Resolve(string reference, string? fromFolder = null, bool markdown = false)
    {
        ArgumentNullException.ThrowIfNull(reference);
        string target = VaultPaths.Normalize(reference);
        if (target.Length == 0)
        {
            return Resolution.Unresolved;
        }

        bool hasExtension = VaultPaths.HasFileExtension(target) && !VaultPaths.IsNote(target);
        string asNote = VaultPaths.IsNote(target) ? target : target + VaultPaths.NoteExtension;

        // A Markdown link is relative to its note first (Obsidian writes them so under "relative path to file").
        if (markdown && fromFolder is not null && VaultPaths.Combine(fromFolder, target) is { } near)
        {
            if (Found(near, asNoteToo: true) is { } hit)
            {
                return hit with { By = ResolveBy.RelativePath };
            }
        }

        if (Found(target, asNoteToo: true) is { } exact)
        {
            return exact;
        }

        if (target.Contains('/', StringComparison.Ordinal))
        {
            string suffix = "/" + asNote;
            var byNoteSuffix = Notes.Where(n => n.Relative.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byNoteSuffix.Count > 0)
            {
                return Pick(byNoteSuffix, fromFolder, ResolveBy.Path);
            }

            string attachmentSuffix = "/" + target;
            var byAttachmentSuffix = _attachments.Where(a => a.EndsWith(attachmentSuffix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byAttachmentSuffix.Count > 0)
            {
                return new Resolution(null, byAttachmentSuffix.OrderBy(a => a.Length).First(), ResolveBy.Path, byAttachmentSuffix.OrderBy(a => a.Length).Skip(1).ToList());
            }

            return Resolution.Unresolved;
        }

        if (hasExtension && _attachmentsByName.TryGetValue(target, out var typed))
        {
            var byType = typed.OrderBy(a => a.Length).ThenBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            return new Resolution(null, byType[0], ResolveBy.Name, byType.Skip(1).ToList());
        }

        {
            string name = VaultPaths.NameOf(asNote);
            if (_byName.TryGetValue(name, out var named))
            {
                return Pick(named, fromFolder, ResolveBy.Name);
            }

            if (_byAlias.TryGetValue(target, out var aliased))
            {
                return Pick(aliased, fromFolder, ResolveBy.Alias);
            }
        }

        if (_attachmentsByName.TryGetValue(target, out var files))
        {
            var ordered = files.OrderBy(a => a.Length).ThenBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            return new Resolution(null, ordered[0], ResolveBy.Name, ordered.Skip(1).ToList());
        }

        return Resolution.Unresolved;

        Resolution? Found(string path, bool asNoteToo)
        {
            if (_notes.TryGetValue(path, out var note) || (asNoteToo && _notes.TryGetValue(path + VaultPaths.NoteExtension, out note)))
            {
                return new Resolution(note, null, ResolveBy.Path, []);
            }

            return _attachmentsByPath.TryGetValue(path, out var attachment) ? new Resolution(null, attachment, ResolveBy.Path, []) : null;
        }
    }

    /// <summary>One of several candidates: the linking note's folder first, then the shortest path, then path order; the rest named.</summary>
    private static Resolution Pick(List<NoteEntry> candidates, string? fromFolder, ResolveBy by)
    {
        var ordered = candidates
            .OrderBy(n => fromFolder is not null && string.Equals(n.Folder, fromFolder, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n.Relative.Count(c => c == '/'))
            .ThenBy(n => n.Relative.Length)
            .ThenBy(n => n.Relative, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new Resolution(ordered[0], null, by, ordered.Skip(1).Select(n => n.Relative).ToList());
    }

    /// <summary>Every link in the vault that resolves to <paramref name="note"/>, with the note it is in.</summary>
    public IReadOnlyList<(NoteEntry Source, NoteLink Link)> Backlinks(NoteEntry note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var found = new List<(NoteEntry, NoteLink)>();
        foreach (var source in Notes)
        {
            foreach (var link in source.Scan.Links)
            {
                if (ResolveLink(source, link).Note is { } target && ReferenceEquals(target, note))
                {
                    found.Add((source, link));
                }
            }
        }

        return found;
    }

    /// <summary>A link as its note sees it (an empty target is the note itself: <c>[[#Heading]]</c>).</summary>
    public Resolution ResolveLink(NoteEntry source, NoteLink link)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(link);
        return link.Target.Length == 0
            ? new Resolution(source, null, ResolveBy.Path, [])
            : Resolve(link.Target, source.Folder, markdown: !link.IsWiki);
    }
}
