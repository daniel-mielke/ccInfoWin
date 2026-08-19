using System.Net;
using System.Text;
using Moq;
using Moq.Protected;

namespace CCInfoWindows.Tests.TestSupport;

/// <summary>
/// Shared fixture for the LiteLLMPricingService suites: a private cache directory, the two model keys
/// every assertion is phrased against, and the HttpMessageHandler stub. The <c>"SendAsync"</c> string
/// Moq.Protected needs was written out three times before this existed — a magic name with no
/// compiler check behind it, in a stub whose silent failure mode is a test that no longer stubs
/// anything.
/// </summary>
public abstract class PricingServiceTestBase : IDisposable
{
    /// <summary>
    /// Present in the bundled fallback table, so it proves both that the seed is in place and that a
    /// live fetch did not replace it.
    /// </summary>
    protected const string BundledModelKey = "claude-opus-4-5";

    /// <summary>Absent from the bundled table, so it can only come from a live or cached response.</summary>
    protected const string LiveOnlyModelKey = "claude-testmodel-9-9";

    /// <summary>
    /// Mirrors the service's private cache file name. Used only to SEED a cache file the service must
    /// then read back, so a rename upstream fails the seeding test loudly instead of retiring a
    /// negative assertion in silence — which is what the two hand-copied consts here used to do.
    /// </summary>
    private const string CacheFileName = "litellm-pricing-cache.json";

    private readonly TempDirectory _temp = new("ccinfo-pricing-");

    /// <summary>Cache directory handed to the service, in place of the real %LOCALAPPDATA%.</summary>
    protected string CacheDir => _temp.Path;

    /// <summary>Writes the cache file the service reads during construction.</summary>
    protected void SeedCacheFile(string json) => File.WriteAllText(Path.Combine(CacheDir, CacheFileName), json);

    /// <summary>
    /// Asserts a failed or oversized fetch wrote no cache at all. Deliberately phrased against the
    /// directory rather than the file name: a name-based Assert.False passes vacuously the moment the
    /// service renames its cache file, which silently retires the guard while showing green.
    /// </summary>
    protected void AssertNothingWasCached() => Assert.Empty(Directory.GetFileSystemEntries(CacheDir));

    /// <summary>
    /// A handler answering every request from <paramref name="respond"/>. Async-shaped so the tests that
    /// need to hold a fetch open on a gate use the same stub as everything else.
    /// </summary>
    protected static Mock<HttpMessageHandler> BuildHandler(Func<Task<HttpResponseMessage>> respond)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(respond);

        return handler;
    }

    /// <summary>A client answering every request with the given JSON body and status.</summary>
    protected static HttpClient BuildHttpClient(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpClient(BuildHandler(() => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        })).Object);
    }

    public void Dispose() => _temp.Dispose();
}
