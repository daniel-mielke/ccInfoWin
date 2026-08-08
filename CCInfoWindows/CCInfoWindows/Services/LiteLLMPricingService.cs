using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Fetches model pricing from the LiteLLM GitHub JSON, caches it locally for 12 hours,
/// and falls back to a bundled resource when the network is unavailable.
///
/// Threading: <see cref="_loadLock"/> serialises the loaders, and every reader goes through an
/// immutable <see cref="PricingSnapshot"/> published by a single reference swap. There is
/// deliberately no lock on the read path — <see cref="GetPrice"/> runs per JSONL entry during
/// statistics aggregation on the UI thread and per file change on the watcher's debounce thread.
/// </summary>
public sealed class LiteLLMPricingService : IPricingService
{
    private const string PricingUrl =
        "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";

    private const string CacheFileName = "litellm-pricing-cache.json";
    private const int CacheValidHours = 12;
    private const string AnthropicProvider = "anthropic";
    private const string EmbeddedResourceName = "CCInfoWindows.Resources.fallback-prices.json";

    /// <summary>
    /// Hard ceiling on the pricing response, enforced against the raw UTF-8 byte count while it is
    /// still streaming. The real artifact is ~2 MB; the cap exists so upstream bloat, a compromised
    /// mirror or a proxy error page cannot be materialised into memory unbounded.
    /// </summary>
    private const int MaxPricingResponseBytes = 10 * 1024 * 1024;

    private const int DownloadChunkBytes = 64 * 1024;

    /// <summary>Regional and gateway aliases LiteLLM publishes for the same Anthropic models.</summary>
    private static readonly string[] ProviderPrefixes = ["anthropic/", "us.anthropic.", "eu.anthropic."];

    private const string LogSourceLiveFetch = "LiteLLMPricingService.LiveFetch";
    private const string LogSourceLocalCache = "LiteLLMPricingService.LocalCache";
    private const string LogSourceEmbedded = "LiteLLMPricingService.EmbeddedResource";
    private const string LogSourceCacheWrite = "LiteLLMPricingService.CacheWrite";
    private const string LogSourceMalformedEntries = "LiteLLMPricingService.MalformedEntries";

    /// <summary>
    /// How many recurrences of an already-reported failure pass before a single counted line is
    /// written. EnsurePricesLoadedAsync retries on every refresh tick, so a permanent failure would
    /// otherwise append one full stack trace per tick.
    /// </summary>
    private const int RepeatLogInterval = 100;

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly Func<Stream?> _openBundledPrices;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private readonly Lock _failureLogGate = new();
    private readonly Dictionary<string, int> _failureOccurrences = new(StringComparer.Ordinal);

    private PricingSnapshot _snapshot = PricingSnapshot.Empty;

    /// <summary>
    /// Price table, its provenance and its fetch timestamp as one immutable unit.
    ///
    /// Published by reference swap so a reader can never observe a half-rebuilt table (the previous
    /// plain <c>Dictionary</c> was <c>Clear()</c>ed and refilled on a thread-pool thread while
    /// unsynchronised readers enumerated it — either <c>InvalidOperationException</c> on the UI
    /// path or, worse, a silent cost of 0), nor a <see cref="PricingSource"/> that disagrees with
    /// the table it describes. Frozen rather than plain because the read path is hot and
    /// immutability makes "never mutate after publishing" a compile-time property.
    /// </summary>
    private sealed record PricingSnapshot(
        FrozenDictionary<string, ModelPricing> Prices,
        PricingSource Source,
        DateTimeOffset? LastFetch)
    {
        public static readonly PricingSnapshot Empty = new(
            FrozenDictionary<string, ModelPricing>.Empty, PricingSource.Unknown, null);
    }

    private PricingSnapshot Current => Volatile.Read(ref _snapshot);

    public PricingSource Source => Current.Source;
    public DateTimeOffset? LastFetch => Current.LastFetch;

    /// <summary>
    /// <paramref name="openBundledPrices"/> is a testability seam for the bundled table: the
    /// <see cref="PricingSource.Unknown"/> state is only reachable when no local source parses, and
    /// the embedded resource is always present in the real assembly. Callers own nothing — the
    /// returned stream is disposed here.
    /// </summary>
    public LiteLLMPricingService(
        HttpClient httpClient,
        string? cacheDirectory = null,
        Func<Stream?>? openBundledPrices = null)
    {
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory ?? AppPaths.DataDirectory;
        _openBundledPrices = openBundledPrices ?? OpenEmbeddedPricingResource;

        // Seed from disk cache / bundled resource up front so GetPrice is never empty.
        // EnsurePricesLoadedAsync runs fire-and-forget from MainViewModel.InitializeAsync while
        // RefreshSessionList already resolves context windows on the next line; without this seed
        // every model would briefly resolve to the 200K default on cold start. Both loaders are
        // local reads and report their own failures. LastFetch stays null, so the live fetch
        // still happens on the first EnsurePricesLoadedAsync.
        LoadFallback();
    }

    public async Task EnsurePricesLoadedAsync()
    {
        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Current.Prices.Count > 0 && !IsCacheExpired())
                return;

            if (!await TryLoadFromLiveApiAsync().ConfigureAwait(false))
                LoadFallback();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public ModelPricing? GetPrice(string modelName) => FindPricing(modelName);

    /// <summary>
    /// Returns false on any failure — including a well-formed response that yields no Anthropic
    /// entries, which is what an upstream schema change looks like. Publishing that as
    /// <see cref="PricingSource.Live"/> is the failure mode this method exists to prevent: the
    /// About tab would report live data while every entry priced at ~$0.00.
    /// </summary>
    private async Task<bool> TryLoadFromLiveApiAsync()
    {
        try
        {
            var utf8Json = await DownloadPricingJsonAsync().ConfigureAwait(false);
            var prices = ParseAnthropicEntries(utf8Json);

            if (prices.Count == 0)
            {
                LogFailure(LogSourceLiveFetch,
                    "Response parsed but contained no Anthropic entries; keeping the previous table.");
                return false;
            }

            Publish(prices, PricingSource.Live);
            SaveToLocalCache(utf8Json);
            return true;
        }
        catch (Exception ex)
        {
            LogFailure(LogSourceLiveFetch, ex);
            return false;
        }
    }

    /// <summary>
    /// Streams the response and rejects it as soon as it passes the byte cap, so the limit bounds
    /// the allocation instead of being checked after the whole body is already in memory.
    /// <c>MaxResponseContentBufferSize</c> is not an option here: the <see cref="HttpClient"/> is a
    /// DI singleton shared with UpdateService, and the property is instance-wide.
    /// </summary>
    private async Task<byte[]> DownloadPricingJsonAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PricingUrl);
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > MaxPricingResponseBytes)
        {
            throw new InvalidDataException(
                $"Pricing response declares {declaredLength} bytes, above the {MaxPricingResponseBytes} byte limit.");
        }

        await using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await ReadBoundedAsync(body).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[DownloadChunkBytes];

        while (true)
        {
            var read = await source.ReadAsync(chunk).ConfigureAwait(false);
            if (read == 0)
                break;

            if (buffer.Length + read > MaxPricingResponseBytes)
            {
                throw new InvalidDataException(
                    $"Pricing response exceeds the {MaxPricingResponseBytes} byte safety limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Falls back to the disk cache, then to the bundled table. When neither parses, an already
    /// published table is kept as-is — stale prices still price correctly, and its
    /// <see cref="PricingSource"/> still describes where it came from. Only a genuinely empty
    /// table is published, and then as <see cref="PricingSource.Unknown"/>: labelling it
    /// "Fallback" is what made a total pricing failure indistinguishable from a working offline
    /// mode while every entry rendered as an innocuous "~$0.00".
    /// </summary>
    private void LoadFallback()
    {
        var prices = TryReadLocalCache() ?? TryReadEmbeddedResource();
        if (prices is not null)
        {
            Publish(prices, PricingSource.Fallback);
            return;
        }

        if (Current.Prices.Count == 0)
            Publish(new Dictionary<string, ModelPricing>(), PricingSource.Unknown);
    }

    private Dictionary<string, ModelPricing>? TryReadLocalCache()
    {
        var cacheFile = new FileInfo(CacheFilePath());

        if (!cacheFile.Exists)
            return null;

        if (cacheFile.Length > MaxPricingResponseBytes)
        {
            LogFailure(LogSourceLocalCache,
                $"Cache file is {cacheFile.Length} bytes, above the {MaxPricingResponseBytes} byte limit; ignoring it.");
            return null;
        }

        try
        {
            var prices = ParseAnthropicEntries(File.ReadAllBytes(cacheFile.FullName));
            return prices.Count > 0 ? prices : null;
        }
        catch (Exception ex)
        {
            LogFailure(LogSourceLocalCache, ex);
            return null;
        }
    }

    private static Stream? OpenEmbeddedPricingResource() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName);

    private Dictionary<string, ModelPricing>? TryReadEmbeddedResource()
    {
        try
        {
            using var stream = _openBundledPrices();
            if (stream is null)
            {
                LogFailure(LogSourceEmbedded, $"Resource '{EmbeddedResourceName}' is not in the assembly.");
                return null;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            var prices = ParseAnthropicEntries(buffer.ToArray());
            return prices.Count > 0 ? prices : null;
        }
        catch (Exception ex)
        {
            LogFailure(LogSourceEmbedded, ex);
            return null;
        }
    }

    /// <summary>
    /// Keeps Anthropic entries and entries without a provider. The bedrock/vertex/azure clones are
    /// priced per gateway and would shadow the canonical ids in the stripped-suffix scan.
    /// Deserialising from UTF-8 bytes rather than a string is what lets the size cap be expressed
    /// in the unit it is named for.
    ///
    /// Each entry is deserialised on its own so one malformed entry costs one model instead of the
    /// whole catalogue: LiteLLM publishes a "sample_spec" documentation placeholder whose example
    /// values are strings where the schema declares numbers, and it sits at line 11 of a ~2 MB
    /// document. A single whole-dictionary Deserialize aborted there, so every real price was lost
    /// and the service never left the bundled fallback.
    /// </summary>
    private Dictionary<string, ModelPricing> ParseAnthropicEntries(byte[] utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);

        var prices = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return prices;

        var skippedEntries = 0;

        foreach (var entry in document.RootElement.EnumerateObject())
        {
            ModelPricing? pricing;
            try
            {
                pricing = entry.Value.Deserialize<ModelPricing>();
            }
            catch (JsonException)
            {
                skippedEntries++;
                continue;
            }

            if (pricing is null)
                continue;

            if (pricing.LitellmProvider is null
                || string.Equals(pricing.LitellmProvider, AnthropicProvider, StringComparison.OrdinalIgnoreCase))
            {
                prices[entry.Name] = pricing;
            }
        }

        if (skippedEntries > 0)
        {
            LogFailure(LogSourceMalformedEntries,
                $"Skipped {skippedEntries} pricing entries that did not match the expected schema.");
        }

        return prices;
    }

    /// <summary>
    /// Reports a handled failure at most once per distinct signature per process run, then a single
    /// counted line every <see cref="RepeatLogInterval"/> recurrences. app.log is capped at 1 MiB
    /// with one roll, so a permanent failure repeating on every refresh tick — 107 full stack traces
    /// in one day for the live fetch — evicts every other diagnostic before it can be read.
    /// </summary>
    private void LogFailure(string source, Exception ex)
    {
        if (ShouldReportInFull(source, $"{ex.GetType().FullName}: {ex.Message}"))
            AppLog.Write(source, ex);
    }

    private void LogFailure(string source, string message)
    {
        if (ShouldReportInFull(source, message))
            AppLog.Write(source, message);
    }

    /// <summary>
    /// True only for the first sighting of <paramref name="signature"/>. Recurrences are collapsed
    /// into one counted line per <see cref="RepeatLogInterval"/> sightings, so the caller's full
    /// entry is written once and the fact that it keeps happening is still recorded.
    /// </summary>
    private bool ShouldReportInFull(string source, string signature)
    {
        int occurrence;
        lock (_failureLogGate)
        {
            _failureOccurrences.TryGetValue(signature, out var previous);
            occurrence = previous + 1;
            _failureOccurrences[signature] = occurrence;
        }

        if (occurrence == 1)
            return true;

        if (occurrence % RepeatLogInterval == 0)
            AppLog.Write(source, $"Repeated {occurrence} times: {signature}");

        return false;
    }

    /// <summary>
    /// Swaps in a new snapshot. <see cref="PricingSnapshot.LastFetch"/> means "last successful live
    /// fetch", so a fallback publication carries the previous value forward rather than clearing it
    /// — that is also what keeps <see cref="IsCacheExpired"/> retrying after a failed fetch.
    /// </summary>
    private void Publish(Dictionary<string, ModelPricing> prices, PricingSource source)
    {
        var lastFetch = source == PricingSource.Live ? DateTimeOffset.UtcNow : Current.LastFetch;

        Volatile.Write(ref _snapshot, new PricingSnapshot(
            prices.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), source, lastFetch));
    }

    private void SaveToLocalCache(byte[] utf8Json)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                Directory.CreateDirectory(_cacheDirectory);

            File.WriteAllBytes(CacheFilePath(), utf8Json);
        }
        catch (Exception ex)
        {
            LogFailure(LogSourceCacheWrite, ex);
        }
    }

    private ModelPricing? FindPricing(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return null;

        // One snapshot for the whole lookup — a mid-lookup swap must not change the table
        // underneath the fallback scans.
        var prices = Current.Prices;

        if (prices.TryGetValue(modelName, out var exact))
            return exact;

        // Try with the date suffix stripped off the query (e.g. "claude-sonnet-4-5-20250929" -> "claude-sonnet-4-5")
        var queryStripped = ModelContextLimits.StripDateSuffix(modelName);
        if (!string.Equals(queryStripped, modelName, StringComparison.OrdinalIgnoreCase)
            && prices.TryGetValue(queryStripped, out var queryStrippedMatch))
        {
            return queryStrippedMatch;
        }

        // Try to find a map key that matches when its date suffix is stripped.
        // This handles: query is "claude-sonnet-4-5" but map has "claude-sonnet-4-5-20250929".
        foreach (var entry in prices)
        {
            var keyStripped = ModelContextLimits.StripDateSuffix(entry.Key);
            if (keyStripped.Equals(modelName, StringComparison.OrdinalIgnoreCase)
                || keyStripped.Equals(queryStripped, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        foreach (var prefix in ProviderPrefixes)
        {
            if (prices.TryGetValue(prefix + modelName, out var prefixed))
                return prefixed;
        }

        return null;
    }

    private bool IsCacheExpired()
    {
        var lastFetch = Current.LastFetch;

        return lastFetch is null
            || DateTimeOffset.UtcNow - lastFetch.Value > TimeSpan.FromHours(CacheValidHours);
    }

    private string CacheFilePath() => Path.Combine(_cacheDirectory, CacheFileName);
}
