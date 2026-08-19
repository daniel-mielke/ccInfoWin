using System.Net;
using System.Text;
using System.Text.Json;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.TestSupport;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Unit tests for LiteLLMPricingService covering live fetch, fallback, model name lookup, the
/// download size cap, and the source reporting MainViewModel keys its pricing banner off.
/// </summary>
public class LiteLLMPricingServiceTests : PricingServiceTestBase
{
    private static string BuildPricingJson(string modelKey, double inputCost = 0.000003, double outputCost = 0.000015)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [modelKey] = new
            {
                input_cost_per_token = inputCost,
                output_cost_per_token = outputCost,
                litellm_provider = "anthropic",
                max_input_tokens = 200000
            }
        });
    }

    [Fact]
    public void Constructor_SeedsBundledFallback_SoGetPriceIsNeverEmpty()
    {
        // MainViewModel.InitializeAsync kicks off EnsurePricesLoadedAsync in a fire-and-forget
        // Task.Run and calls RefreshSessionList on the next line. Context-window resolution reads
        // GetPrice synchronously there, so an empty map on cold start would size every model to
        // the 200K default.
        var service = new LiteLLMPricingService(BuildHttpClient(""), CacheDir);

        Assert.NotNull(service.GetPrice(BundledModelKey));
        Assert.Equal(PricingSource.Fallback, service.Source);
        Assert.Null(service.LastFetch);   // seeding must not suppress the live fetch
    }

    [Fact]
    public async Task EnsurePricesLoadedAsync_SuccessfulFetch_SetsSourceToLive()
    {
        var json = BuildPricingJson("claude-sonnet-4-6-20260205");
        var client = BuildHttpClient(json);
        var service = new LiteLLMPricingService(client, CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Live, service.Source);
        Assert.NotNull(service.LastFetch);
    }

    [Fact]
    public async Task EnsurePricesLoadedAsync_FailedFetch_SetsSourceToFallback()
    {
        var client = BuildHttpClient("", HttpStatusCode.ServiceUnavailable);
        var service = new LiteLLMPricingService(client, CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Fallback, service.Source);
        Assert.Null(service.LastFetch);
    }

    [Fact]
    public async Task GetPrice_ExactModelMatch_ReturnsPricing()
    {
        const string ModelKey = "claude-haiku-4-5-20251001";
        var json = BuildPricingJson(ModelKey, inputCost: 0.0000008, outputCost: 0.000004);
        var client = BuildHttpClient(json);
        var service = new LiteLLMPricingService(client, CacheDir);
        await service.EnsurePricesLoadedAsync();

        var pricing = service.GetPrice(ModelKey);

        Assert.NotNull(pricing);
        Assert.Equal(0.0000008, pricing.InputCostPerToken);
    }

    [Fact]
    public async Task GetPrice_DateSuffixStripped_ReturnsPricing()
    {
        const string ModelKeyWithDate = "claude-sonnet-4-5-20250929";
        const string ModelKeyStripped = "claude-sonnet-4-5";
        var json = BuildPricingJson(ModelKeyWithDate);
        var client = BuildHttpClient(json);
        var service = new LiteLLMPricingService(client, CacheDir);
        await service.EnsurePricesLoadedAsync();

        // Both the dated id and its stripped form must resolve to the same entry: the map holds the
        // dated key, and the stripped-suffix scan is what bridges the two.
        Assert.NotNull(service.GetPrice(ModelKeyWithDate));
        Assert.NotNull(service.GetPrice(ModelKeyStripped));
    }

    [Fact]
    public async Task GetPrice_UnknownModel_ReturnsNull()
    {
        var json = BuildPricingJson("claude-sonnet-4-6-20260205");
        var client = BuildHttpClient(json);
        var service = new LiteLLMPricingService(client, CacheDir);
        await service.EnsurePricesLoadedAsync();

        var pricing = service.GetPrice("gpt-4o");

        Assert.Null(pricing);
    }

    [Fact]
    public void GetPrice_EmptyModelName_ReturnsNullInsteadOfThrowing()
    {
        var service = new LiteLLMPricingService(BuildHttpClient(""), CacheDir);

        Assert.Null(service.GetPrice(""));
    }

    // -------------------------------------------------------------------------
    // Publication: the table is swapped whole, never cleared in place
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnsurePricesLoadedAsync_LiveFetch_ReplacesTheSeededTableWholesale()
    {
        var client = BuildHttpClient(BuildPricingJson(LiveOnlyModelKey));
        var service = new LiteLLMPricingService(client, CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.NotNull(service.GetPrice(LiveOnlyModelKey));
        Assert.Null(service.GetPrice(BundledModelKey));   // replaced, not merged
    }

    [Fact]
    public async Task GetPrice_WhileALiveFetchIsInFlight_StillAnswersFromTheSeededTable()
    {
        // The concurrency defect this replaces: ParseAndStore called _pricingMap.Clear() and
        // refilled in place on a thread-pool thread, so an unsynchronised reader could land in the
        // empty window and silently price the entry at 0. The table is now immutable and published
        // by one reference swap, so there is no window to land in. The gate keeps the fetch
        // provably suspended while the reader runs — no sleeps, no timing assumptions.
        var gate = new TaskCompletionSource();
        var handler = BuildHandler(async () =>
        {
            await gate.Task;
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    BuildPricingJson(LiveOnlyModelKey), Encoding.UTF8, "application/json")
            };
        });

        var service = new LiteLLMPricingService(new HttpClient(handler.Object), CacheDir);
        var load = service.EnsurePricesLoadedAsync();

        Assert.NotNull(service.GetPrice(BundledModelKey));
        Assert.Equal(PricingSource.Fallback, service.Source);

        gate.SetResult();
        await load;

        Assert.NotNull(service.GetPrice(LiveOnlyModelKey));
        Assert.Equal(PricingSource.Live, service.Source);
    }

    [Fact]
    public async Task EnsurePricesLoadedAsync_CachesTheResponse_SoTheNextColdStartSeesIt()
    {
        var client = BuildHttpClient(BuildPricingJson(LiveOnlyModelKey));
        await new LiteLLMPricingService(client, CacheDir).EnsurePricesLoadedAsync();

        // Fresh instance, same cache directory, no network: it must seed from the cache file the
        // first instance wrote (the cache is now written as raw UTF-8 bytes, not a re-encoded string).
        var coldStart = new LiteLLMPricingService(
            BuildHttpClient("", HttpStatusCode.ServiceUnavailable), CacheDir);

        Assert.NotNull(coldStart.GetPrice(LiveOnlyModelKey));
        Assert.Equal(PricingSource.Fallback, coldStart.Source);
    }

    // -------------------------------------------------------------------------
    // Never publish an empty table as Live (finding 34)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("{}")]                                                          // upstream emptied the file
    [InlineData("{\"gpt-4o\":{\"litellm_provider\":\"openai\"}}")]               // schema still parses, no Anthropic entries
    public async Task EnsurePricesLoadedAsync_ResponseWithoutAnthropicEntries_DoesNotClaimLive(string json)
    {
        // A well-formed response that yields nothing usable is what an upstream schema change looks
        // like. ParseAndStore used to Clear() first and never check the count, so the About tab
        // reported "Live" while every entry priced at ~$0.00.
        var service = new LiteLLMPricingService(BuildHttpClient(json), CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Fallback, service.Source);
        Assert.Null(service.LastFetch);
        Assert.NotNull(service.GetPrice(BundledModelKey));   // the seeded table survived
    }

    [Fact]
    public async Task Source_WhenNoLocalTableParses_IsUnknownRatherThanFallback()
    {
        // The state MainViewModel must surface as a pricing error: no prices at all. Reporting
        // "Fallback" here asserted "Fallback (gebündelt)" in Settings while every cost was zero.
        var service = new LiteLLMPricingService(
            BuildHttpClient("", HttpStatusCode.ServiceUnavailable),
            CacheDir,
            openBundledPrices: () => null);

        Assert.Equal(PricingSource.Unknown, service.Source);
        Assert.Null(service.GetPrice(BundledModelKey));

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Unknown, service.Source);
    }

    [Fact]
    public void Source_WhenTheBundledTableIsCorrupt_IsUnknown()
    {
        var service = new LiteLLMPricingService(
            BuildHttpClient(""),
            CacheDir,
            openBundledPrices: () => new MemoryStream("not json at all"u8.ToArray()));

        Assert.Equal(PricingSource.Unknown, service.Source);
    }

    // -------------------------------------------------------------------------
    // Download size cap (finding 41)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnsurePricesLoadedAsync_ResponseDeclaringMoreThanTheCap_IsRejectedBeforeReading()
    {
        var handler = BuildHandler(() =>
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            content.Headers.ContentLength = 64L * 1024 * 1024;   // declared, never delivered
            return Task.FromResult(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = content });
        });
        var service = new LiteLLMPricingService(new HttpClient(handler.Object), CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Fallback, service.Source);
        Assert.Null(service.LastFetch);
        Assert.NotNull(service.GetPrice(BundledModelKey));
    }

    [Fact]
    public async Task EnsurePricesLoadedAsync_ResponseWithoutContentLengthThatExceedsTheCap_IsAborted()
    {
        // No Content-Length (chunked, or a lying proxy), so only the streaming guard can stop it.
        // The old code awaited GetStringAsync first and checked the char count afterwards, which
        // could not prevent the allocation it existed to prevent.
        var handler = BuildHandler(() => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StreamContent(new NeverEndingStream())
        }));
        var service = new LiteLLMPricingService(new HttpClient(handler.Object), CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Fallback, service.Source);
        Assert.Null(service.LastFetch);
        AssertNothingWasCached();
    }

    /// <summary>
    /// Unseekable, endless source of filler bytes: reports no length, so the response has no
    /// Content-Length and the byte cap has to be enforced while reading.
    /// </summary>
    private sealed class NeverEndingStream : Stream
    {
        private const byte Filler = (byte)'a';

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill(Filler);
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
