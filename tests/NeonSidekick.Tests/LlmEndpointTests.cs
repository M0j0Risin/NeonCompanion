using NeonSidekick.Llm;

namespace NeonSidekick.Tests;

public class LlmEndpointTests
{
    [Theory]
    [InlineData("http://localhost:1234", "http://localhost:1234/v1")]
    [InlineData("http://localhost:1234/", "http://localhost:1234/v1")]
    [InlineData("http://localhost:1234/v1", "http://localhost:1234/v1")]
    [InlineData("http://localhost:1234/v1/", "http://localhost:1234/v1")]   // not /v1/v1
    [InlineData("http://localhost:1234///", "http://localhost:1234/v1")]
    [InlineData("http://localhost:1234/V1", "http://localhost:1234/V1")]    // case kept, not doubled
    [InlineData("  http://host:8000/api  ", "http://host:8000/api/v1")]
    [InlineData("https://example.com", "https://example.com/v1")]
    public void NormalizeBaseUrl_EndsInExactlyOneV1(string raw, string expected)
    {
        Assert.Equal(expected, LlmEndpoint.NormalizeBaseUrl(raw).AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost:8080")]      // UriBuilder would take "localhost" as the scheme
    [InlineData("ftp://host/v1")]
    [InlineData("not a url")]
    public void NormalizeBaseUrl_RejectsAnythingButAbsoluteHttp(string raw)
    {
        Assert.Throws<ArgumentException>(() => LlmEndpoint.NormalizeBaseUrl(raw));
    }

    [Fact]
    public void ModelsUrl_AppendsWithAnExplicitSlash()
    {
        var v1 = LlmEndpoint.NormalizeBaseUrl("http://127.0.0.1:1234");
        Assert.Equal("http://127.0.0.1:1234/v1/models", LlmEndpoint.ModelsUrl(v1).AbsoluteUri);
    }

    [Fact]
    public void Constants_ArePinned()
    {
        Assert.Equal("local-model", LlmEndpoint.FallbackModelId);
        Assert.Equal("empty", LlmEndpoint.DefaultApiKey);
    }
}
