using System.Text;

namespace NeonCompanion.Tests.Fakes;

/// <summary>
/// The <c>TestConsole</c>'s own <c>StringWriter</c> behind one lock (2026-09-22), installed as its
/// <c>Profile.Out</c> so every write the screen makes — the plain segment path and the ANSI one both
/// go through <c>Profile.Out.Writer</c> — is serialised with <see cref="Snapshot"/>. A script that
/// polls the output from <c>BeforeUpdate</c> while the screen's watcher redraws the pane on another
/// thread read <c>TestConsole.Output</c> (<c>StringBuilder.ToString()</c>, not thread-safe: it sizes
/// the result first and throws <c>ArgumentOutOfRangeException</c> when an append lands meanwhile);
/// the exception faulted the fake stream and the turn, and the scroll scripts failed in ~200 ms
/// with their flag never set — the Ctrl+End and PgUp flakes of full runs, and CI's release run.
/// </summary>
public sealed class LockedWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly object _gate = new();

    public LockedWriter(TextWriter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override Encoding Encoding => _inner.Encoding;

    /// <summary>Everything written so far, read under the same lock the writes take.</summary>
    public string Snapshot()
    {
        lock (_gate)
        {
            return _inner.ToString()!;
        }
    }

    public override void Write(char value)
    {
        lock (_gate)
        {
            _inner.Write(value);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (_gate)
        {
            _inner.Write(buffer, index, count);
        }
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        lock (_gate)
        {
            _inner.Write(buffer);
        }
    }

    public override void Write(string? value)
    {
        lock (_gate)
        {
            _inner.Write(value);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_gate)
        {
            _inner.WriteLine(value);
        }
    }

    public override void Flush()
    {
        lock (_gate)
        {
            _inner.Flush();
        }
    }
}
