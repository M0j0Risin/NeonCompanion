using System.Net;
using NeonCompanion.Tests.Fakes;
using NeonCompanion.Web;

namespace NeonCompanion.Tests;

public class SearxngSearchTests
{
    private const string Json = """
        {"query": "rust async traits", "number_of_results": 2, "results": [
          {"url": "https://blog.rust-lang.org/2023/12/21/async-fn-rfc.html", "title": "Announcing `async fn` and return-position `impl Trait` in traits", "content": "  Stabilised in\n 1.75. ", "engine": "duckduckgo", "score": 3.0},
          {"url": "https://docs.rs/async-trait", "title": "async_trait", "engine": "google"},
          {"title": "no url", "content": "skipped"},
          "junk"
        ], "suggestions": []}
        """;

    private readonly StubHttpMessageHandler _http = new();
    private static readonly Uri Base = new("http://localhost:8080");

    private SearxngSearch Engine() => new(new HttpClient(_http), Base);

    private static FetchOptions Options => new(FetchEngine.Default, "", NetworkReach.Internet);

    [Fact]
    public void QueryUrl_IsTheJsonApi()
    {
        Assert.Equal("http://localhost:8080/search?q=rust%20async%20traits&format=json&language=en", SearxngSearch.QueryUrl(Base, " rust async traits ").AbsoluteUri);
        Assert.Equal("http://box:8080/searx/search?q=x&format=json&language=en", SearxngSearch.QueryUrl(new Uri("http://box:8080/searx/"), "x").AbsoluteUri);
        Assert.Equal("http://box:8080/search?q=x&format=json&language=en", SearxngSearch.QueryUrl(new Uri("http://box:8080/search?q=old"), "x").AbsoluteUri);
        Assert.Equal(WebText.SearxngName, Engine().Name);
    }

    [Fact]
    public void Parse_ReadsTitleUrlContent_AndSkipsWhatHasNoUrl()
    {
        var results = SearxngSearch.Parse(Json);

        Assert.Equal(2, results.Count);
        Assert.Equal(new SearchResult("Announcing `async fn` and return-position `impl Trait` in traits", "https://blog.rust-lang.org/2023/12/21/async-fn-rfc.html", "Stabilised in 1.75."), results[0]);
        Assert.Equal(new SearchResult("async_trait", "https://docs.rs/async-trait", ""), results[1]);
        Assert.Empty(SearxngSearch.Parse("not json"));
        Assert.Empty(SearxngSearch.Parse("[]"));
        Assert.Empty(SearxngSearch.Parse("{\"results\": 3}"));
    }

    [Fact]
    public async Task Search_AsksForJson_ExemptFromTheLanRule_AndCapsTheCount()
    {
        HttpRequestMessage? seen = null;
        _http.Map("http://localhost:8080/search", (r, _) => { seen = r; return Task.FromResult(StubHttpMessageHandler.Json(HttpStatusCode.OK, Json)); });

        var outcome = await Engine().SearchAsync("rust async traits", 1, Options, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Single(outcome.Results);
        Assert.Equal("http://localhost:8080/search?q=rust%20async%20traits&format=json&language=en", seen!.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json", seen.Headers.GetValues("Accept").Single());
        Assert.True(seen.Options.TryGetValue(LanPolicy.ExemptKey, out bool exempt) && exempt);
    }

    [Fact]
    public async Task Search_A403_NamesTheFormatSetting()
    {
        _http.Map("http://localhost:8080/search", HttpStatusCode.Forbidden, "{\"message\":\"forbidden\"}");

        var outcome = await Engine().SearchAsync("x", 8, Options, CancellationToken.None);

        Assert.Equal("Error: SearXNG at http://localhost:8080 refused the JSON format (HTTP 403); add json to search.formats in its settings.yml", outcome.Error);
    }

    [Fact]
    public async Task Search_OtherStatuses_AndNoAnswer()
    {
        _http.Map("http://localhost:8080/search", HttpStatusCode.BadGateway, "");
        Assert.Equal("Error: SearXNG could not be searched (HTTP 502 from http://localhost:8080)", (await Engine().SearchAsync("x", 8, Options, CancellationToken.None)).Error);

        var down = new SearxngSearch(new HttpClient(new StubHttpMessageHandler()), Base);
        Assert.StartsWith("Error: SearXNG at http://localhost:8080 did not answer (No connection could be made", (await down.SearchAsync("x", 8, Options, CancellationToken.None)).Error);
    }
}
