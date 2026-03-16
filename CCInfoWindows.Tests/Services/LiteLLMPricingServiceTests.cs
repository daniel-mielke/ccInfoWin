using System.Net;
using System.Text;
using System.Text.Json;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using Moq;
using Moq.Protected;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Unit tests for LiteLLMPricingService covering live fetch, fallback, and model name lookup.
/// </summary>
public class LiteLLMPricingServiceTests : IDisposable
{
    private readonly string _cacheDir;

    public LiteLLMPricingServiceTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    private static HttpClient BuildHttpClient(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        return new HttpClient(handler.Object);
    }

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
    public async Task EnsurePricesLoadedAsync_SuccessfulFetch_SetsSourceToLive()
    {
        var json = BuildPricingJson("claude-sonnet-4-6-20260205");
        var client = BuildHttpClient(json);
        var service = new LiteLLMPricingService(client, _cacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Live, service.Source);
        Assert.NotNull(service.LastFetch);
    }

    [Fact]
    public async Task EnsurePricesLoadedAsync_FailedFetch_SetsSourceToFallback()
    {
        var client = BuildHttpClient("", HttpStatusCode.ServiceUnavailable);
        var service = new LiteLLMPricingService(client, _cacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Fallback, service.Source);
    }

    [Fact]
    public async Task GetPrice_ExactModelMatch_ReturnsPricing()
    {
        const string ModelKey = "claude-haiku-4-5-20251001";
        var json = BuildPricingJson(ModelKey, inputCost: 0.0000008, outputCost: 0.000004);
        var client = BuildHttpClient(json);
        var service = new LiteLLMPricingService(client, _cacheDir);
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
        var service = new LiteLLMPricingService(client, _cacheDir);
        await service.EnsurePricesLoadedAsync();

        // Query with stripped name — service should fall back to matching the full key
        var pricingExact = service.GetPrice(ModelKeyWithDate);
        var pricingStripped = service.GetPrice(ModelKeyStripped);

        Assert.NotNull(pricingExact);
        // Either the stripped name returns the same entry, or is null (no explicit stripped key in map).
        // The behavior we want: date-suffix stripping in FindPricing also checks the stripped model
        // against keys in the map that contain the stripped name as a prefix.
        // For this test, both exact and stripped should return the entry.
        Assert.NotNull(pricingStripped);
    }

    [Fact]
    public async Task GetPrice_UnknownModel_ReturnsNull()
    {
        var json = BuildPricingJson("claude-sonnet-4-6-20260205");
        var client = BuildHttpClient(json);
        var service = new LiteLLMPricingService(client, _cacheDir);
        await service.EnsurePricesLoadedAsync();

        var pricing = service.GetPrice("gpt-4o");

        Assert.Null(pricing);
    }
}
