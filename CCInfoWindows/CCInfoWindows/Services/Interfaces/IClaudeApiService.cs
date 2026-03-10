using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Service contract for fetching and caching Claude API usage data.
/// </summary>
public interface IClaudeApiService
{
    Task<UsageResponse?> FetchUsageAsync(CancellationToken ct = default);

    UsageResponse? GetCachedUsage();

    Task SaveCacheAsync(UsageResponse data);

    Task<UsageResponse?> LoadCacheAsync();
}
