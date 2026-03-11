using System.Text.Json;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;

namespace CCInfoWindows.Services;

/// <summary>
/// Fetches Claude API usage data via WebView2 bridge with retry logic, disk caching, and auth error handling.
/// Routes requests through Chromium's fetch() to bypass Cloudflare bot protection.
/// </summary>
public class ClaudeApiService : IClaudeApiService
{
    private const string BaseUrl = "https://claude.ai";
    private const int MaxAttempts = 3;

    private readonly IWebViewBridge _bridge;
    private readonly ICredentialService _credentialService;
    private readonly string _cacheFilePath;

    private UsageResponse? _cachedUsage;

    /// <param name="bridge">WebView2 bridge for Cloudflare-safe HTTP requests.</param>
    /// <param name="credentialService">Credential store for session token and org ID.</param>
    /// <param name="cacheDirectory">
    /// Override cache directory for testing. Defaults to %LOCALAPPDATA%\CCInfoWindows.
    /// </param>
    public ClaudeApiService(
        IWebViewBridge bridge,
        ICredentialService credentialService,
        string? cacheDirectory = null)
    {
        _bridge = bridge;
        _credentialService = credentialService;

        var dir = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCInfoWindows");
        _cacheFilePath = Path.Combine(dir, "usage_cache.json");
    }

    public async Task<UsageResponse?> FetchUsageAsync(CancellationToken ct = default)
    {
        if (!_bridge.IsInitialized)
        {
            return null;
        }

        var orgId = _credentialService.GetOrganizationId();
        if (orgId is null)
        {
            orgId = await TryMigrateOrgIdAsync(ct);
            if (orgId is null)
            {
                return null;
            }
        }

        var encodedOrgId = Uri.EscapeDataString(orgId);
        var url = $"{BaseUrl}/api/organizations/{encodedOrgId}/usage";

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var responseBody = await _bridge.FetchJsonAsync(url);

                if (responseBody is null)
                {
                    // Non-success status or network error — retry
                    if (attempt < MaxAttempts)
                    {
                        await Task.Delay(attempt * 1000, ct);
                        continue;
                    }
                    return null;
                }

                var usage = JsonSerializer.Deserialize<UsageResponse>(responseBody);

                if (usage is not null)
                {
                    _cachedUsage = usage;
                    await SaveCacheAsync(usage);
                }

                return usage;
            }
            catch (UnauthorizedAccessException)
            {
                WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
                return null;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(attempt * 1000, ct);
                    continue;
                }
                return null;
            }
            catch (Exception) when (attempt < MaxAttempts)
            {
                await Task.Delay(attempt * 1000, ct);
            }
        }

        return null;
    }

    public UsageResponse? GetCachedUsage() => _cachedUsage;

    public async Task SaveCacheAsync(UsageResponse data)
    {
        var dir = Path.GetDirectoryName(_cacheFilePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        await File.WriteAllTextAsync(_cacheFilePath, json);
    }

    public async Task<UsageResponse?> LoadCacheAsync()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_cacheFilePath);
            var usage = JsonSerializer.Deserialize<UsageResponse>(json);
            _cachedUsage = usage;
            return usage;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches org ID from /api/organizations when lastActiveOrg cookie was not captured.
    /// </summary>
    private async Task<string?> TryMigrateOrgIdAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var responseBody = await _bridge.FetchJsonAsync($"{BaseUrl}/api/organizations");
            if (responseBody is null)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var orgs = doc.RootElement;

            if (orgs.GetArrayLength() > 0)
            {
                var uuid = orgs[0].GetProperty("uuid").GetString();
                if (!string.IsNullOrEmpty(uuid))
                {
                    _credentialService.SaveOrganizationId(uuid);
                    return uuid;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
        }
        catch (Exception)
        {
            // Migration failed — user needs to re-login
        }

        return null;
    }
}
