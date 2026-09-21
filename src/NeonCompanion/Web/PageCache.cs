namespace NeonCompanion.Web;

/// <summary>
/// The pages fetched lately, keyed by the URL the model asked for, so a second <c>web_fetch</c>
/// with an <c>offset</c> pages through the text without a second download. Entries live
/// <see cref="Ttl"/>, at most <see cref="Capacity"/> of them (the oldest goes first); a failed
/// fetch is never kept. One per app, shared by the screen and headless.
/// </summary>
public sealed class PageCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    public const int Capacity = 20;

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, (FetchResult Page, DateTimeOffset At)> _pages = new(StringComparer.Ordinal);

    public PageCache(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _pages.Count;
            }
        }
    }

    /// <summary>The page for <paramref name="url"/> fetched within <see cref="Ttl"/>, or null.</summary>
    public FetchResult? TryGet(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        lock (_gate)
        {
            if (_pages.TryGetValue(url, out var entry))
            {
                if (_time.GetUtcNow() - entry.At < Ttl)
                {
                    return entry.Page;
                }

                _pages.Remove(url);
            }

            return null;
        }
    }

    /// <summary>Keeps a successful fetch; anything else is ignored.</summary>
    public void Put(string url, FetchResult page)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(page);
        if (!page.Ok)
        {
            return;
        }

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            _pages[url] = (page, now);
            while (_pages.Count > Capacity)
            {
                string oldest = _pages.MinBy(p => p.Value.At).Key;
                _pages.Remove(oldest);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pages.Clear();
        }
    }
}
