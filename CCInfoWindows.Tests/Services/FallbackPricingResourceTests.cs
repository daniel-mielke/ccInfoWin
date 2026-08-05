using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Guards the bundled offline price table (Resources/fallback-prices.json).
///
/// The file is the anthropic-only subset of upstream ccInfo's claude-pricing-fallback.json.
/// Reducing it is behaviour-preserving: LiteLLMPricingService.ParseAndStore keeps only
/// entries whose litellm_provider is "anthropic" (or absent), and the upstream file has no
/// provider-less entries — the other 248 keys were bedrock/vertex/azure variants that were
/// parsed and thrown away on every cold start.
///
/// These tests read the actual embedded resource, so a bad re-import fails here rather than
/// silently degrading offline cost estimates and context-window sizing.
/// </summary>
public class FallbackPricingResourceTests
{
    private const string EmbeddedResourceName = "CCInfoWindows.Resources.fallback-prices.json";

    private static Dictionary<string, ModelPricing> LoadEmbedded()
    {
        var assembly = typeof(ModelContextLimits).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        Assert.True(stream is not null, $"Embedded resource '{EmbeddedResourceName}' not found.");

        using var reader = new StreamReader(stream!);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, ModelPricing>>(reader.ReadToEnd());
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public void EmbeddedFallback_ContainsOnlyAnthropicEntries()
    {
        var foreign = LoadEmbedded()
            .Where(kv => !string.Equals(kv.Value.LitellmProvider, "anthropic", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        Assert.True(foreign.Count == 0,
            $"Non-anthropic entries would be dropped at load time anyway: [{string.Join(", ", foreign)}]");
    }

    [Fact]
    public void EmbeddedFallback_EveryEntryHasPositiveInputAndOutputCost()
    {
        var broken = LoadEmbedded()
            .Where(kv => kv.Value.InputCostPerToken <= 0 || kv.Value.OutputCostPerToken <= 0)
            .Select(kv => kv.Key)
            .ToList();

        Assert.True(broken.Count == 0, $"Entries with non-positive cost: [{string.Join(", ", broken)}]");
    }

    [Theory]
    // Native large window, no surcharge tier -> full window
    [InlineData("claude-sonnet-5", 1_000_000)]
    [InlineData("claude-sonnet-4-6", 1_000_000)]
    [InlineData("claude-opus-5", 1_000_000)]
    [InlineData("claude-opus-4-7", 1_000_000)]
    [InlineData("claude-opus-4-8", 1_000_000)]
    [InlineData("claude-fable-5", 1_000_000)]
    // Opt-in large window (above-200k surcharge) -> effective 200K without the beta header
    [InlineData("claude-sonnet-4-20250514", 200_000)]
    // Plain 200K models — the direction the old `Opus => 1M` map got wrong
    [InlineData("claude-opus-4-5", 200_000)]
    [InlineData("claude-opus-4-1", 200_000)]
    [InlineData("claude-sonnet-4-5", 200_000)]
    [InlineData("claude-haiku-4-5", 200_000)]
    public void EmbeddedFallback_ResolvesExpectedContextWindow(string modelKey, long expected)
    {
        var table = LoadEmbedded();
        Assert.True(table.ContainsKey(modelKey), $"Bundled price table is missing '{modelKey}'.");

        var result = ModelContextLimits.GetMaxContextTokens(
            modelKey, name => table.GetValueOrDefault(name));

        Assert.Equal(expected, result);
    }
}
