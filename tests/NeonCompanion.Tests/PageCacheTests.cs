using NeonCompanion.Tests.Fakes;
using NeonCompanion.Web;

namespace NeonCompanion.Tests;

public class PageCacheTests
{
    private static FetchResult Page(string url, string text = "hello") =>
        new(FetchOutcome.Ok, url, url, 200, "text/html", new PageText("t", text), "", WebText.HttpEngine, "", false);

    [Fact]
    public void Put_ThenGet_WithinTheTtl()
    {
        var time = new ManualTimeProvider();
        var cache = new PageCache(time);
        cache.Put("https://a/", Page("https://a/"));

        Assert.Same(cache.TryGet("https://a/")!.Page, cache.TryGet("https://a/")!.Page);
        Assert.Null(cache.TryGet("https://b/"));
        Assert.Equal(1, cache.Count);
        time.Advance(PageCache.Ttl - TimeSpan.FromSeconds(1));
        Assert.NotNull(cache.TryGet("https://a/"));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(cache.TryGet("https://a/"));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void AFailedFetch_IsNeverKept()
    {
        var cache = new PageCache(new ManualTimeProvider());
        cache.Put("https://a/", FetchResult.Fail(FetchOutcome.Timeout, "https://a/", ""));

        Assert.Null(cache.TryGet("https://a/"));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void OverCapacity_TheOldestGoes()
    {
        var time = new ManualTimeProvider();
        var cache = new PageCache(time);
        for (int i = 0; i < PageCache.Capacity + 2; i++)
        {
            cache.Put("https://a/" + i, Page("https://a/" + i));
            time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(PageCache.Capacity, cache.Count);
        Assert.Null(cache.TryGet("https://a/0"));
        Assert.Null(cache.TryGet("https://a/1"));
        Assert.NotNull(cache.TryGet("https://a/2"));
        Assert.NotNull(cache.TryGet("https://a/" + (PageCache.Capacity + 1)));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var cache = new PageCache(new ManualTimeProvider());
        cache.Put("https://a/", Page("https://a/"));
        cache.Clear();

        Assert.Equal(0, cache.Count);
    }
}
