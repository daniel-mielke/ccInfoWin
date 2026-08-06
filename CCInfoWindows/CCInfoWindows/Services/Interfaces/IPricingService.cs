using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Source of the currently loaded pricing data.
/// </summary>
public enum PricingSource
{
    /// <summary>Freshly downloaded from the LiteLLM database, and non-empty.</summary>
    Live,

    /// <summary>Loaded from the local cache or the bundled table. Prices are usable but may be stale.</summary>
    Fallback,

    /// <summary>
    /// No usable pricing data is loaded — every <see cref="IPricingService.GetPrice"/> returns null
    /// and every cost estimate degrades to zero. This is the pricing error state: the only way a
    /// caller can distinguish a total pricing failure from working offline operation, since none of
    /// the loaders throw.
    /// </summary>
    Unknown
}

/// <summary>
/// Contract for fetching and querying model pricing from the LiteLLM pricing database.
/// </summary>
public interface IPricingService
{
    /// <summary>Returns pricing for the given model name, or null if not found.</summary>
    ModelPricing? GetPrice(string modelName);

    /// <summary>
    /// Where the currently loaded prices came from. Callers that need to surface a pricing failure
    /// must test for <see cref="PricingSource.Unknown"/> — <see cref="EnsurePricesLoadedAsync"/>
    /// handles every failure internally and does not throw.
    /// </summary>
    PricingSource Source { get; }

    /// <summary>Time of the last successful live fetch, or null if never fetched.</summary>
    DateTimeOffset? LastFetch { get; }

    /// <summary>
    /// Ensures prices are loaded, fetching from the live API if the cache is stale. Never throws:
    /// a failed fetch degrades to the cache, then to the bundled table, and only reports itself
    /// through <see cref="Source"/>.
    /// </summary>
    Task EnsurePricesLoadedAsync();
}
