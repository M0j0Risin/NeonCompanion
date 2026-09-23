using System.Text;
using System.Text.Json;
using NeonSidekick.Diagnostics;

namespace NeonSidekick.Memory;

/// <summary>One remembered fact and when it was saved.</summary>
public sealed class MemoryEntry
{
    public string Text { get; set; } = "";

    public DateTimeOffset SavedAt { get; set; }
}

/// <summary>The on-disk shape of <c>memory.json</c>: a version and the entries, oldest first.</summary>
public sealed class MemoryFile
{
    /// <summary>Bumped when an entry changes meaning rather than merely gaining a field.</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<MemoryEntry> Entries { get; set; } = new();
}

/// <summary>What <see cref="MemoryStore.Add"/> did.</summary>
public enum MemoryAddOutcome
{
    Added,

    /// <summary>The same text (ignoring case and whitespace) is already stored; nothing changed.</summary>
    Duplicate,

    /// <summary>The store holds <see cref="MemoryStore.MaxEntries"/> already; nothing changed.</summary>
    Full,

    /// <summary>Nothing but whitespace was offered; nothing changed.</summary>
    Empty,

    /// <summary>The file could not be written; the store is as it was and the failure is in the log.</summary>
    Failed,
}

/// <summary>The outcome of an add and the text as it was (or would have been) stored.</summary>
public readonly record struct MemoryAddResult(MemoryAddOutcome Outcome, string Text);

/// <summary>
/// What <see cref="MemoryStore.Import"/> did: how many entries were written, how many were skipped
/// as already there (the target's, or an earlier one of the same batch), and how many did not fit
/// under <see cref="MemoryStore.MaxEntries"/>.
/// </summary>
public readonly record struct MemoryImportResult(int Added, int Duplicates, int Dropped);

/// <summary>
/// Long-term memory: a short list of facts about the user that outlives the conversation and the
/// process. One file next to <c>settings.json</c>, loaded once and written through on every
/// change, so what the store reports is what the disk holds.
///
/// <para>Three rules, each from the reference's conversation log. <b>One file</b>, so a clear is
/// one delete and "forgotten" is honest. <b>Stored text is flattened</b> (whitespace collapsed,
/// newlines gone, a hard length cap): recalled text goes into the system prompt and must not be
/// able to carry markdown structure or a second paragraph into it. <b>A clear that fails throws</b>:
/// reporting success for a delete that did not happen is the one failure a forget path must
/// never have. An add that fails, by contrast, reports <see cref="MemoryAddOutcome.Failed"/> and
/// leaves the store as it was.</para>
///
/// <para>Every write is synchronous and under one lock: the tool that saves a memory answers the
/// model "remembered" only once the file is on disk, and the shell's <c>/remember</c> shares the
/// same instance.</para>
/// </summary>
public sealed class MemoryStore
{
    public const string FileName = "memory.json";

    /// <summary>The ceiling on stored entries; the prompt grows with every one.</summary>
    public const int MaxEntries = 200;

    /// <summary>Characters kept per entry; longer text is cut with an ellipsis.</summary>
    public const int MaxTextLength = 300;

    private const string Category = "Memory";

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly TimeProvider _time;
    private List<MemoryEntry>? _entries;

    /// <param name="directory">The settings directory; the file is <see cref="FileName"/> under it.</param>
    /// <param name="time">The clock behind <see cref="MemoryEntry.SavedAt"/>; tests pass a fake.</param>
    public MemoryStore(string directory, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _filePath = Path.Combine(Path.GetFullPath(directory), FileName);
        _time = time ?? TimeProvider.System;
    }

    public string FilePath => _filePath;

    /// <summary>How many memories are stored.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return Entries().Count;
            }
        }
    }

    /// <summary>The stored texts, oldest first, as a copy.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return Entries().Select(e => e.Text).ToArray();
        }
    }

    /// <summary>The stored entries with their dates, oldest first, as copies; the <c>/memory</c> menu's view.</summary>
    public IReadOnlyList<MemoryEntry> EntriesSnapshot()
    {
        lock (_gate)
        {
            return Entries().Select(e => new MemoryEntry { Text = e.Text, SavedAt = e.SavedAt }).ToArray();
        }
    }

    /// <summary>
    /// Forgets one memory, matched the way <see cref="Add"/> matches a duplicate (normalised,
    /// ignoring case). False when nothing matched and nothing was written. A rewrite that fails
    /// puts the entry back and throws (<see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/>), like <see cref="Clear"/>: a removal that did not
    /// happen is never reported as done.
    /// </summary>
    public bool Remove(string? text)
    {
        string normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            var entries = Entries();
            int index = entries.FindIndex(e => string.Equals(e.Text, normalized, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            var entry = entries[index];
            entries.RemoveAt(index);
            try
            {
                Save(entries);
            }
            catch
            {
                entries.Insert(index, entry);
                throw;
            }

            DiagnosticLog.Info(Category, $"Removed: {entry.Text}");
            return true;
        }
    }

    /// <summary>
    /// The text as it is stored: trimmed, every run of whitespace (newlines included) one space,
    /// cut to <see cref="MaxTextLength"/> with an ellipsis. Empty when nothing is left.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        if (sb.Length > MaxTextLength)
        {
            sb.Length = MaxTextLength - 1;
            sb.Append('…');
        }

        return sb.ToString();
    }

    /// <summary>Saves one memory. Never throws; the result says what happened and carries the normalised text.</summary>
    public MemoryAddResult Add(string? text)
    {
        string normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return new(MemoryAddOutcome.Empty, normalized);
        }

        lock (_gate)
        {
            var entries = Entries();
            var existing = entries.FirstOrDefault(e => string.Equals(e.Text, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return new(MemoryAddOutcome.Duplicate, existing.Text);
            }

            if (entries.Count >= MaxEntries)
            {
                return new(MemoryAddOutcome.Full, normalized);
            }

            var entry = new MemoryEntry { Text = normalized, SavedAt = _time.GetUtcNow() };
            entries.Add(entry);
            if (!TrySave(entries))
            {
                entries.Remove(entry);
                return new(MemoryAddOutcome.Failed, normalized);
            }

            DiagnosticLog.Info(Category, $"Remembered: {normalized}");
            return new(MemoryAddOutcome.Added, normalized);
        }
    }

    /// <summary>
    /// Another store's entries into this one (<c>/memory copy</c>, 2026-09-17 as <c>/memcopy</c>): appended after what is
    /// here, or — <paramref name="overwrite"/> — in place of it. Each text is normalised like an
    /// <see cref="Add"/>; one that is already in the list (this store's, or an earlier entry of the
    /// batch, ignoring case) is a duplicate and skipped; past <see cref="MaxEntries"/> the rest are
    /// dropped. The source's dates are kept. One write at the end: a write that fails puts the old
    /// list back and throws (<see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>),
    /// so the caller never reports a copy that did not happen. An overwrite with nothing to write
    /// still writes: the target ends empty, as asked.
    /// </summary>
    public MemoryImportResult Import(IReadOnlyList<MemoryEntry> entries, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_gate)
        {
            var previous = Entries();
            var merged = overwrite ? new List<MemoryEntry>() : new List<MemoryEntry>(previous);
            int added = 0, duplicates = 0, dropped = 0;
            foreach (var entry in entries)
            {
                string normalized = Normalize(entry.Text);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (merged.Any(e => string.Equals(e.Text, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    duplicates++;
                    continue;
                }

                if (merged.Count >= MaxEntries)
                {
                    dropped++;
                    continue;
                }

                merged.Add(new MemoryEntry { Text = normalized, SavedAt = entry.SavedAt });
                added++;
            }

            Save(merged);   // throws; _entries still holds the previous list then
            _entries = merged;
            DiagnosticLog.Info(Category, $"Imported {added} memories ({duplicates} already there, {dropped} dropped){(overwrite ? ", replacing the previous list" : "")}.");
            return new MemoryImportResult(added, duplicates, dropped);
        }
    }

    /// <summary>
    /// Forgets everything: deletes the file and returns how many entries it held. A delete that
    /// fails throws (<see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>), and the
    /// in-memory list is kept, so the caller never reports a forget that did not happen.
    /// </summary>
    public int Clear()
    {
        lock (_gate)
        {
            int count = Entries().Count;
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);   // throws when it cannot; a missing file (or directory) is not an error
            }

            _entries = new List<MemoryEntry>();
            if (count > 0)
            {
                DiagnosticLog.Info(Category, $"Forgot {count} memories.");
            }

            return count;
        }
    }

    /// <summary>The list, loaded on first use. Caller holds the lock.</summary>
    private List<MemoryEntry> Entries()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        _entries = Load();
        return _entries;
    }

    /// <summary>
    /// Reads the file. Missing means empty; corrupt is logged once and also treated as empty,
    /// which means the next add overwrites it — a memory file nobody can read is not worth more
    /// than that.
    /// </summary>
    private List<MemoryEntry> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<MemoryEntry>();
        }

        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(_filePath), MemoryJsonContext.Default.MemoryFile);
            var entries = file?.Entries ?? new List<MemoryEntry>();
            entries.RemoveAll(e => string.IsNullOrWhiteSpace(e.Text));
            foreach (var entry in entries)
            {
                entry.Text = Normalize(entry.Text);
            }

            return entries;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Warn(Category, $"Could not read {FileName}; starting with no memories: {ex.Message}", ex);
            return new List<MemoryEntry>();
        }
    }

    /// <summary><see cref="Save"/> for the paths that must not throw: the failure is logged and false comes back. Caller holds the lock.</summary>
    private bool TrySave(List<MemoryEntry> entries)
    {
        try
        {
            Save(entries);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Error(Category, $"Could not write {FileName}: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>Temp file, then a move over the old one: a reader never sees half a file. Throws on failure, temp file removed. Caller holds the lock.</summary>
    private void Save(List<MemoryEntry> entries)
    {
        string tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var file = new MemoryFile { Entries = entries };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, MemoryJsonContext.Default.MemoryFile));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }
}
