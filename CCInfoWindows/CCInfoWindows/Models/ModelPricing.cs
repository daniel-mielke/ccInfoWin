using System.Text.Json.Serialization;

namespace CCInfoWindows.Models;

/// <summary>
/// Pricing data for a single model as returned by the LiteLLM pricing JSON.
/// All per-token costs are in USD per single token.
/// </summary>
public record ModelPricing
{
    [JsonPropertyName("input_cost_per_token")]
    public double InputCostPerToken { get; init; }

    [JsonPropertyName("output_cost_per_token")]
    public double OutputCostPerToken { get; init; }

    [JsonPropertyName("cache_creation_input_token_cost")]
    public double? CacheCreationCost { get; init; }

    [JsonPropertyName("cache_read_input_token_cost")]
    public double? CacheReadCost { get; init; }

    [JsonPropertyName("input_cost_per_token_above_200k_tokens")]
    public double? InputCostAbove200k { get; init; }

    [JsonPropertyName("output_cost_per_token_above_200k_tokens")]
    public double? OutputCostAbove200k { get; init; }

    [JsonPropertyName("cache_creation_input_token_cost_above_200k_tokens")]
    public double? CacheCreationCostAbove200k { get; init; }

    [JsonPropertyName("cache_read_input_token_cost_above_200k_tokens")]
    public double? CacheReadCostAbove200k { get; init; }

    [JsonPropertyName("litellm_provider")]
    public string? LitellmProvider { get; init; }

    [JsonPropertyName("max_input_tokens")]
    public long? MaxInputTokens { get; init; }
}
