using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Fetches model pricing from the LiteLLM GitHub JSON, caches it locally for 12 hours,
/// and falls back to a bundled resource when the network is unavailable.
/// </summary>
public sealed class LiteLLMPricingService : IPricingService
{
    private const string PricingUrl =
        "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";

    private const string CacheFileName = "litellm-pricing-cache.json";
    private const int CacheValidHours = 12;
    private const string AnthropicProvider = "anthropic";
    private const string EmbeddedResourceName = "CCInfoWindows.Resources.fallback-prices.json";
    private const int MaxPricingJsonBytes = 10 * 1024 * 1024; // 10 MB safety limit

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly Dictionary<string, ModelPricing> _pricingMap =
        new(StringComparer.OrdinalIgnoreCase);

    private PricingSource _source = PricingSource.Unknown;
    private DateTimeOffset? _lastFetch;

    public PricingSource Source => _source;
    public DateTimeOffset? LastFetch => _lastFetch;

    public LiteLLMPricingService(HttpClient httpClient, string? cacheDirectory = null)
    {
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCInfoWindows");
    }

    public async Task EnsurePricesLoadedAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            if (_pricingMap.Count > 0 && !IsCacheExpired())
                return;

            await TryLoadFromLiveApi();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public ModelPricing? GetPrice(string modelName) => FindPricing(modelName);

    private async Task TryLoadFromLiveApi()
    {
        try
        {
            var json = await _httpClient.GetStringAsync(PricingUrl);
            if (json.Length > MaxPricingJsonBytes)
                throw new InvalidDataException($"Pricing JSON exceeds {MaxPricingJsonBytes} bytes safety limit");

            ParseAndStore(json);
            SaveToLocalCache(json);
            _source = PricingSource.Live;
            _lastFetch = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LiteLLMPricingService] Live fetch failed: {ex.Message}");
            LoadFallback();
        }
    }

    private void LoadFallback()
    {
        if (TryLoadFromLocalCache())
        {
            _source = PricingSource.Fallback;
            return;
        }

        if (TryLoadFromEmbeddedResource())
        {
            _source = PricingSource.Fallback;
            return;
        }

        _source = PricingSource.Fallback;
    }

    private bool TryLoadFromLocalCache()
    {
        var cacheFile = CacheFilePath();

        if (!File.Exists(cacheFile))
            return false;

        try
        {
            var json = File.ReadAllText(cacheFile);
            ParseAndStore(json);
            return _pricingMap.Count > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LiteLLMPricingService] Local cache load failed: {ex.Message}");
            return false;
        }
    }

    private bool TryLoadFromEmbeddedResource()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null)
                return false;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            ParseAndStore(json);
            return _pricingMap.Count > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LiteLLMPricingService] Embedded resource load failed: {ex.Message}");
            return false;
        }
    }

    private void ParseAndStore(string json)
    {
        var allEntries = JsonSerializer.Deserialize<Dictionary<string, ModelPricing>>(json);
        if (allEntries is null)
            return;

        _pricingMap.Clear();

        foreach (var (key, pricing) in allEntries)
        {
            if (pricing is null)
                continue;

            // Only store anthropic entries or entries without a provider prefix
            if (pricing.LitellmProvider is null
                || string.Equals(pricing.LitellmProvider, AnthropicProvider, StringComparison.OrdinalIgnoreCase))
            {
                _pricingMap[key] = pricing;
            }
        }
    }

    private void SaveToLocalCache(string json)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                Directory.CreateDirectory(_cacheDirectory);

            File.WriteAllText(CacheFilePath(), json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LiteLLMPricingService] Cache save failed: {ex.Message}");
        }
    }

    private ModelPricing? FindPricing(string modelName)
    {
        if (_pricingMap.TryGetValue(modelName, out var exact))
            return exact;

        // Try with the date suffix stripped off the query (e.g. "claude-sonnet-4-5-20250929" -> "claude-sonnet-4-5")
        var queryStripped = StripDateSuffix(modelName);
        if (!string.Equals(queryStripped, modelName, StringComparison.OrdinalIgnoreCase)
            && _pricingMap.TryGetValue(queryStripped, out var queryStrippedMatch))
        {
            return queryStrippedMatch;
        }

        // Try to find a map key that matches when its date suffix is stripped.
        // This handles: query is "claude-sonnet-4-5" but map has "claude-sonnet-4-5-20250929".
        foreach (var key in _pricingMap.Keys)
        {
            var keyStripped = StripDateSuffix(key);
            if (keyStripped.Equals(modelName, StringComparison.OrdinalIgnoreCase)
                || keyStripped.Equals(queryStripped, StringComparison.OrdinalIgnoreCase))
            {
                return _pricingMap[key];
            }
        }

        foreach (var prefix in new[] { "anthropic/", "us.anthropic.", "eu.anthropic." })
        {
            if (_pricingMap.TryGetValue(prefix + modelName, out var prefixed))
                return prefixed;
        }

        return null;
    }

    private static string StripDateSuffix(string modelName)
    {
        var parts = modelName.Split('-');
        if (parts.Length > 0 && parts[^1].Length == 8 && long.TryParse(parts[^1], out _))
            return string.Join('-', parts[..^1]);

        return modelName;
    }

    private bool IsCacheExpired() =>
        _lastFetch is null
        || DateTimeOffset.UtcNow - _lastFetch.Value > TimeSpan.FromHours(CacheValidHours);

    private string CacheFilePath() => Path.Combine(_cacheDirectory, CacheFileName);
}
