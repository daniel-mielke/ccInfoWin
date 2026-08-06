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

    /// <summary>
    /// Purges the cached usage snapshot from memory and from disk. Must run on logout so the next
    /// account cannot see the previous one's utilization, quota and cost figures. Best-effort on
    /// the file — a delete failure is logged, never thrown.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// ORGID-01 (D-OG-01): Returns the list of organizations the current session has access to,
    /// fetched from /api/organizations. Empty list when unauthenticated, network failure, or
    /// JSON parse error. <see cref="SessionExpiredException"/> broadcasts AuthStateChangedMessage(false)
    /// to trigger the existing auto-reauth flow before returning empty.
    /// </summary>
    Task<IReadOnlyList<OrganizationInfo>> ListAvailableOrganizationsAsync(CancellationToken ct = default);
}
