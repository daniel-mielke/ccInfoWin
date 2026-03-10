using System.Net;
using System.Text.Json;
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using Moq;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Unit tests for ClaudeApiService HTTP client with retry, caching, and error handling.
/// Uses a custom HttpMessageHandler to intercept HTTP calls without real network access.
/// </summary>
public class ClaudeApiServiceTests : IDisposable
{
    private readonly Mock<ICredentialService> _credentialMock;
    private readonly MockHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    private readonly string _cacheFile;

    public ClaudeApiServiceTests()
    {
        _credentialMock = new Mock<ICredentialService>();
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler);
        _cacheDir = Path.Combine(Path.GetTempPath(), $"ccinfo_test_{Guid.NewGuid():N}");
        _cacheFile = Path.Combine(_cacheDir, "usage_cache.json");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    private ClaudeApiService CreateService()
    {
        return new ClaudeApiService(_httpClient, _credentialMock.Object, _cacheDir);
    }

    [Fact]
    public async Task FetchUsageAsync_ConstructsUrlWithPercentEncodedOrgId()
    {
        // Arrange
        var orgId = "org-123/test space";
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns(orgId);

        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(CreateUsageJson(0.5))
        });

        var service = CreateService();

        // Act
        await service.FetchUsageAsync();

        // Assert
        var requestUrl = _handler.LastRequestUri?.ToString() ?? "";
        Assert.Contains(Uri.EscapeDataString(orgId), requestUrl);
        Assert.Contains("/api/organizations/", requestUrl);
        Assert.Contains("/usage", requestUrl);
    }

    [Fact]
    public async Task FetchUsageAsync_IncludesRequiredHeaders()
    {
        // Arrange
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("my-session-key");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(CreateUsageJson(0.3))
        });

        var service = CreateService();

        // Act
        await service.FetchUsageAsync();

        // Assert
        var request = _handler.LastRequest!;
        Assert.Contains("sessionKey=my-session-key", request.Headers.GetValues("Cookie").First());
        Assert.Equal("web_claude_ai", request.Headers.GetValues("anthropic-client-platform").First());
    }

    [Fact]
    public async Task FetchUsageAsync_On401_SendsAuthStateChangedAndReturnsNull()
    {
        // Arrange
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("expired-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        bool authMessageReceived = false;
        bool authState = true;
        WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this, (_, msg) =>
        {
            authMessageReceived = true;
            authState = msg.Value;
        });

        var service = CreateService();

        // Act
        var result = await service.FetchUsageAsync();

        // Assert
        Assert.Null(result);
        Assert.True(authMessageReceived);
        Assert.False(authState);
        Assert.Equal(1, _handler.RequestCount); // No retry on 401

        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    [Fact]
    public async Task FetchUsageAsync_OnTransientError_RetriesAndSucceeds()
    {
        // Arrange
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        _handler.SetResponses(new[]
        {
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateUsageJson(0.75))
            }
        });

        var service = CreateService();

        // Act
        var result = await service.FetchUsageAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0.75, result!.FiveHour!.Utilization);
        Assert.Equal(3, _handler.RequestCount);
    }

    [Fact]
    public async Task FetchUsageAsync_OnPersistentError_ReturnsNullAfterRetries()
    {
        // Arrange
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        _handler.SetResponses(new[]
        {
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
        });

        var service = CreateService();

        // Act
        var result = await service.FetchUsageAsync();

        // Assert
        Assert.Null(result);
        Assert.Equal(3, _handler.RequestCount);
    }

    [Fact]
    public async Task CacheRoundTrip_SaveAndLoadReturnsEquivalentData()
    {
        // Arrange
        var service = CreateService();
        var data = new UsageResponse
        {
            FiveHour = new UsageWindow { Utilization = 0.42 },
            SevenDay = new UsageWindow { Utilization = 0.88 }
        };

        // Act
        await service.SaveCacheAsync(data);
        var loaded = await service.LoadCacheAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(0.42, loaded!.FiveHour!.Utilization);
        Assert.Equal(0.88, loaded!.SevenDay!.Utilization);
    }

    [Fact]
    public async Task LoadCacheAsync_MissingFile_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var loaded = await service.LoadCacheAsync();

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadCacheAsync_CorruptFile_ReturnsNull()
    {
        // Arrange
        Directory.CreateDirectory(_cacheDir);
        await File.WriteAllTextAsync(_cacheFile, "not valid json{{{");
        var service = CreateService();

        // Act
        var loaded = await service.LoadCacheAsync();

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetCachedUsage_ReturnsLastFetchedData()
    {
        // Arrange
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns("org-123");

        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(CreateUsageJson(0.6))
        });

        var service = CreateService();

        // Act
        Assert.Null(service.GetCachedUsage());
        await service.FetchUsageAsync();
        var cached = service.GetCachedUsage();

        // Assert
        Assert.NotNull(cached);
        Assert.Equal(0.6, cached!.FiveHour!.Utilization);
    }

    [Fact]
    public async Task FetchUsageAsync_NullSessionToken_ReturnsNull()
    {
        // Arrange
        _credentialMock.Setup(x => x.GetSessionToken()).Returns((string?)null);

        var service = CreateService();

        // Act
        var result = await service.FetchUsageAsync();

        // Assert
        Assert.Null(result);
        Assert.Equal(0, _handler.RequestCount);
    }

    [Fact]
    public async Task FetchUsageAsync_NullOrgId_AttemptsOrgMigration()
    {
        // Arrange
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.SetupSequence(x => x.GetOrganizationId())
            .Returns((string?)null)  // First call: no org ID
            .Returns("migrated-org"); // After migration

        // First request: /api/organizations (migration)
        // Second request: /api/organizations/{id}/usage (actual fetch)
        _handler.SetResponses(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"uuid\":\"migrated-org\",\"name\":\"My Org\"}]")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateUsageJson(0.33))
            }
        });

        var service = CreateService();

        // Act
        var result = await service.FetchUsageAsync();

        // Assert
        Assert.NotNull(result);
        _credentialMock.Verify(x => x.SaveOrganizationId("migrated-org"), Times.Once);
    }

    [Fact]
    public async Task FetchUsageAsync_OrgMigrationFails_ReturnsNull()
    {
        // Arrange
        _credentialMock.Setup(x => x.GetSessionToken()).Returns("test-token");
        _credentialMock.Setup(x => x.GetOrganizationId()).Returns((string?)null);

        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService();

        // Act
        var result = await service.FetchUsageAsync();

        // Assert
        Assert.Null(result);
    }

    private static string CreateUsageJson(double utilization)
    {
        return JsonSerializer.Serialize(new
        {
            five_hour = new { utilization, resets_at = DateTimeOffset.UtcNow.AddHours(1).ToString("o") },
            seven_day = new { utilization = utilization * 0.5, resets_at = DateTimeOffset.UtcNow.AddDays(3).ToString("o") }
        });
    }

    /// <summary>
    /// Test double for HttpMessageHandler that captures requests and returns configured responses.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        private HttpResponseMessage? _defaultResponse;

        public HttpRequestMessage? LastRequest { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public int RequestCount { get; private set; }

        public void SetResponse(HttpResponseMessage response)
        {
            _defaultResponse = response;
        }

        public void SetResponses(IEnumerable<HttpResponseMessage> responses)
        {
            _responses.Clear();
            foreach (var r in responses)
            {
                _responses.Enqueue(r);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestUri = request.RequestUri;
            RequestCount++;

            if (_responses.Count > 0)
            {
                return Task.FromResult(_responses.Dequeue());
            }

            return Task.FromResult(_defaultResponse ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
