using System.Text.Json;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using Moq;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Unit tests for ClaudeApiService with retry, caching, and auth error handling.
/// Uses Mock of IWebViewBridge to simulate API responses without network access.
/// </summary>
[Collection("WeakReferenceMessenger")]
public class ClaudeApiServiceTests : IDisposable
{
    private readonly Mock<ICredentialService> _credentialMock;
    private readonly Mock<IWebViewBridge> _bridgeMock;
    private readonly string _cacheDir;
    private readonly string _cacheFile;

    public ClaudeApiServiceTests()
    {
        _credentialMock = new Mock<ICredentialService>();
        _bridgeMock = new Mock<IWebViewBridge>();
        _bridgeMock.Setup(b => b.IsInitialized).Returns(true);
        _cacheDir = Path.Combine(Path.GetTempPath(), $"ccinfo_test_{Guid.NewGuid():N}");
        _cacheFile = Path.Combine(_cacheDir, "usage_cache.json");
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    private ClaudeApiService CreateService()
    {
        return new ClaudeApiService(_bridgeMock.Object, _credentialMock.Object, _cacheDir);
    }

    [Fact]
    public async Task FetchUsageAsync_ConstructsUrlWithPercentEncodedOrgId()
    {
        var orgId = "org 123+test";
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns(orgId);

        string? capturedUrl = null;
        _bridgeMock
            .Setup(b => b.FetchJsonAsync(It.IsAny<string>()))
            .Callback<string>(url => capturedUrl = url)
            .ReturnsAsync(CreateUsageJson(0.5));

        var service = CreateService();

        await service.FetchUsageAsync();

        Assert.NotNull(capturedUrl);
        Assert.Contains("/api/organizations/", capturedUrl);
        Assert.Contains("/usage", capturedUrl);
        Assert.DoesNotContain(" ", capturedUrl);
        Assert.Contains("%2B", capturedUrl);
    }

    [Fact]
    public async Task FetchUsageAsync_On401_SendsAuthStateChangedAndReturnsNull()
    {
        // The bridge signals HTTP 401 with SessionExpiredException. It used to raise
        // UnauthorizedAccessException, which the filesystem also raises for ACL failures —
        // that ambiguity let a failed cache write force a logout (review finding 5).
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("expired-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        _bridgeMock
            .Setup(b => b.FetchJsonAsync(It.IsAny<string>()))
            .ThrowsAsync(new SessionExpiredException());

        bool authMessageReceived = false;
        bool authState = true;
        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (_, msg) =>
        {
            authMessageReceived = true;
            authState = msg.Value;
        });

        var service = CreateService();

        var result = await service.FetchUsageAsync();

        Assert.Null(result);
        Assert.True(authMessageReceived);
        Assert.False(authState);

        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    [Fact]
    public async Task FetchUsageAsync_WhenCacheWriteFails_ReturnsDataAndKeepsSessionAlive()
    {
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");
        _bridgeMock
            .Setup(b => b.FetchJsonAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateUsageJson(0.5));

        // A directory where the cache file belongs makes File.WriteAllTextAsync raise
        // UnauthorizedAccessException — deterministic, no ACL manipulation needed.
        Directory.CreateDirectory(_cacheFile);

        var authMessages = 0;
        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (_, _) => authMessages++);

        var service = CreateService();

        var result = await service.FetchUsageAsync();

        Assert.NotNull(result);
        Assert.Equal(0.5, result!.FiveHour!.Utilization);
        Assert.Equal(0, authMessages);
        _bridgeMock.Verify(b => b.FetchJsonAsync(It.IsAny<string>()), Times.Once);

        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    [Fact]
    public async Task ClearCache_RemovesFileAndInMemorySnapshot()
    {
        var service = CreateService();
        await service.SaveCacheAsync(new UsageResponse
        {
            FiveHour = new UsageWindow { Utilization = 0.7 }
        });
        await service.LoadCacheAsync();

        Assert.NotNull(service.GetCachedUsage());

        service.ClearCache();

        Assert.Null(service.GetCachedUsage());
        Assert.False(File.Exists(_cacheFile));
    }

    [Fact]
    public void ClearCache_WithoutCacheFile_DoesNotThrow()
    {
        var service = CreateService();

        service.ClearCache();

        Assert.Null(service.GetCachedUsage());
    }

    [Fact]
    public async Task FetchUsageAsync_OnNullResponseBody_ReturnsNullWithoutRetrying()
    {
        // Contract per ClaudeApiService.cs `if (responseBody is null) return null;` (commit 5999719):
        // a null body means the bridge fetch produced nothing. That is not treated as a transient
        // fault — the caller renders stale/no data and the 30s poll loop retries on its own.
        // The retry loop is reserved for thrown exceptions (timeouts, 5xx).
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        _bridgeMock
            .Setup(b => b.FetchJsonAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var service = CreateService();

        var result = await service.FetchUsageAsync();

        Assert.Null(result);
        _bridgeMock.Verify(b => b.FetchJsonAsync(It.IsAny<string>()), Times.Once);
    }


    [Fact]
    public async Task CacheRoundTrip_SaveAndLoadReturnsEquivalentData()
    {
        var service = CreateService();
        var data = new UsageResponse
        {
            FiveHour = new UsageWindow { Utilization = 0.42 },
            SevenDay = new UsageWindow { Utilization = 0.88 }
        };

        await service.SaveCacheAsync(data);
        var loaded = await service.LoadCacheAsync();

        Assert.NotNull(loaded);
        Assert.Equal(0.42, loaded!.FiveHour!.Utilization);
        Assert.Equal(0.88, loaded!.SevenDay!.Utilization);
    }

    [Fact]
    public async Task LoadCacheAsync_MissingFile_ReturnsNull()
    {
        var service = CreateService();

        var loaded = await service.LoadCacheAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadCacheAsync_CorruptFile_ReturnsNull()
    {
        Directory.CreateDirectory(_cacheDir);
        await File.WriteAllTextAsync(_cacheFile, "not valid json{{{");
        var service = CreateService();

        var loaded = await service.LoadCacheAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetCachedUsage_ReturnsLastFetchedData()
    {
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        _bridgeMock
            .Setup(b => b.FetchJsonAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateUsageJson(0.6));

        var service = CreateService();

        Assert.Null(service.GetCachedUsage());
        await service.FetchUsageAsync();
        var cached = service.GetCachedUsage();

        Assert.NotNull(cached);
        Assert.Equal(0.6, cached!.FiveHour!.Utilization);
    }

    [Fact]
    public async Task FetchUsageAsync_BridgeNotInitialized_ThrowsInvalidOperation()
    {
        _bridgeMock.Setup(b => b.IsInitialized).Returns(false);
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchUsageAsync());
        _bridgeMock.Verify(b => b.FetchJsonAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task FetchUsageAsync_NullOrgId_AttemptsOrgMigration()
    {
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.SetupSequence(x => x.GetOrganizationId())
            .Returns((string?)null)
            .Returns("migrated-org");

        var callCount = 0;
        _bridgeMock
            .Setup(b => b.FetchJsonAsync(It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? "[{\"uuid\":\"migrated-org\",\"name\":\"My Org\"}]"
                    : CreateUsageJson(0.33);
            });

        var service = CreateService();

        var result = await service.FetchUsageAsync();

        Assert.NotNull(result);
        _credentialMock.Verify(x => x.SaveOrganizationId("migrated-org"), Times.Once);
    }

    [Fact]
    public async Task FetchUsageAsync_OrgMigrationFails_ThrowsInvalidOperation()
    {
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns((string?)null);

        _bridgeMock
            .Setup(b => b.FetchJsonAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchUsageAsync());
    }

    private static string CreateUsageJson(double utilization)
    {
        return JsonSerializer.Serialize(new
        {
            five_hour = new { utilization, resets_at = DateTimeOffset.UtcNow.AddHours(1).ToString("o") },
            seven_day = new { utilization = utilization * 0.5, resets_at = DateTimeOffset.UtcNow.AddDays(3).ToString("o") }
        });
    }
}
