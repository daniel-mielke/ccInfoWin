using System.Net;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.TestSupport;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Defect B2: LiteLLM's model_prices_and_context_window.json opens with a "sample_spec"
/// documentation placeholder whose example values are strings where ModelPricing declares numbers.
/// The parser deserialised the whole document as one dictionary, so that single entry — on line 11
/// of a ~2 MB file — aborted the entire catalogue. Every live fetch failed, the About tab reported
/// "Never", and app.log collected 107 identical stack traces in one day.
///
/// These tests pin the per-entry parse: one malformed entry costs one model, never the catalogue,
/// while the provider filter and the "nothing usable is a failure" contract stay intact.
/// </summary>
public class LiteLLMPricingMalformedEntryTests : PricingServiceTestBase
{
    /// <summary>LiteLLM omits litellm_provider on some entries; those are kept as well.</summary>
    private const string ProviderlessModelKey = "claude-testmodel-9-8";

    private const string SampleSpecKey = "sample_spec";
    private const string BedrockCloneKey = "bedrock/claude-testmodel-9-9";
    private const string VertexCloneKey = "vertex_ai/claude-testmodel-9-9";

    /// <summary>
    /// The shape that broke the catalogue, copied from upstream: numeric fields carrying prose.
    /// </summary>
    private static string MalformedEntry(string key) =>
        $$"""
          "{{key}}": {
            "max_input_tokens": "the maximum input tokens, if the provider specifies it",
            "litellm_provider": "one of https://docs.litellm.ai/docs/providers"
          }
          """;

    private static string PricedEntry(string key, string? provider)
    {
        var providerField = provider is null ? string.Empty : $"\"litellm_provider\": \"{provider}\",";

        return $$"""
                 "{{key}}": {
                   "input_cost_per_token": 0.000003,
                   "output_cost_per_token": 0.000015,
                   {{providerField}}
                   "max_input_tokens": 200000
                 }
                 """;
    }

    private static string Document(params string[] entries) => "{" + string.Join(",", entries) + "}";

    [Fact]
    public async Task EnsurePricesLoadedAsync_DocumentLedByTheSampleSpecPlaceholder_KeepsEveryValidEntry()
    {
        // The placeholder comes first, exactly as upstream ships it: a whole-dictionary deserialize
        // aborts here and loses the models that follow.
        var json = Document(
            MalformedEntry(SampleSpecKey),
            PricedEntry(LiveOnlyModelKey, "anthropic"),
            PricedEntry(ProviderlessModelKey, provider: null));
        var service = new LiteLLMPricingService(BuildHttpClient(json), CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Live, service.Source);
        Assert.NotNull(service.LastFetch);
        Assert.NotNull(service.GetPrice(LiveOnlyModelKey));
        Assert.NotNull(service.GetPrice(ProviderlessModelKey));
        Assert.Null(service.GetPrice(SampleSpecKey));   // the unparsable entry is the only casualty
    }

    [Fact]
    public async Task EnsurePricesLoadedAsync_PerEntryParse_StillDropsTheGatewayClones()
    {
        // The clones are priced per gateway and would shadow the canonical ids in the
        // stripped-suffix scan, so the provider filter must survive the rewrite.
        var json = Document(
            MalformedEntry(SampleSpecKey),
            PricedEntry(LiveOnlyModelKey, "anthropic"),
            PricedEntry(BedrockCloneKey, "bedrock"),
            PricedEntry(VertexCloneKey, "vertex_ai"));
        var service = new LiteLLMPricingService(BuildHttpClient(json), CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.NotNull(service.GetPrice(LiveOnlyModelKey));
        Assert.Null(service.GetPrice(BedrockCloneKey));
        Assert.Null(service.GetPrice(VertexCloneKey));
    }

    [Fact]
    public async Task EnsurePricesLoadedAsync_DocumentWhereEveryEntryIsMalformed_IsStillAFailedFetch()
    {
        // Skipping an entry is correct; skipping all of them is an upstream schema change and must
        // not be published as Live with an empty table.
        var json = Document(
            MalformedEntry(SampleSpecKey),
            MalformedEntry("another_documentation_entry"));
        var service = new LiteLLMPricingService(BuildHttpClient(json), CacheDir);

        await service.EnsurePricesLoadedAsync();

        Assert.Equal(PricingSource.Fallback, service.Source);
        Assert.Null(service.LastFetch);
        Assert.NotNull(service.GetPrice(BundledModelKey));   // the seeded table survived
        AssertNothingWasCached();
    }

    [Fact]
    public void Constructor_CacheFileLedByTheSampleSpecPlaceholder_SeedsTheValidEntries()
    {
        // Same defect on the cold-start path: the cached copy of the upstream document is parsed by
        // the same method, so a poisoned first entry used to discard the whole cache file.
        var json = Document(
            MalformedEntry(SampleSpecKey),
            PricedEntry(LiveOnlyModelKey, "anthropic"));
        SeedCacheFile(json);

        var service = new LiteLLMPricingService(
            BuildHttpClient("", HttpStatusCode.ServiceUnavailable), CacheDir);

        Assert.Equal(PricingSource.Fallback, service.Source);
        Assert.NotNull(service.GetPrice(LiveOnlyModelKey));
    }
}
