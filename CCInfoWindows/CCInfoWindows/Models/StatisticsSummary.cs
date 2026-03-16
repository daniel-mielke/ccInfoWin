namespace CCInfoWindows.Models;

/// <summary>
/// Aggregated token counts and cost data for a given time period.
/// </summary>
public record StatisticsSummary
{
    public static readonly StatisticsSummary Empty = new();

    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheCreationTokens { get; init; }
    public long CacheReadTokens { get; init; }

    /// <summary>Sum of all four token fields.</summary>
    public long TotalTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;

    public decimal TotalCostUsd { get; init; }

    /// <summary>True when at least one entry used estimated pricing (model not in pricing DB).</summary>
    public bool HasEstimatedCosts { get; init; }

    public IReadOnlyList<string> Models { get; init; } = [];
}
