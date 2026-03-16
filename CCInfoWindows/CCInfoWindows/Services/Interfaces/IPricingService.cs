using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Source of the currently loaded pricing data.
/// </summary>
public enum PricingSource { Live, Fallback, Unknown }

/// <summary>
/// Contract for fetching and querying model pricing from the LiteLLM pricing database.
/// </summary>
public interface IPricingService
{
    /// <summary>Returns pricing for the given model name, or null if not found.</summary>
    ModelPricing? GetPrice(string modelName);

    /// <summary>Indicates whether prices were loaded from the live API or a fallback source.</summary>
    PricingSource Source { get; }

    /// <summary>Time of the last successful live fetch, or null if never fetched.</summary>
    DateTimeOffset? LastFetch { get; }

    /// <summary>Ensures prices are loaded, fetching from the live API if the cache is stale.</summary>
    Task EnsurePricesLoadedAsync();
}
