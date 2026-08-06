using CCInfoWindows.Services;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Covers the two contracts of the bridge that can be exercised without a live WebView2:
/// the claude.ai egress allow-list (review finding 39) and the pending-request bookkeeping
/// that must never leave a caller awaiting a reply that will not come (review finding 16).
/// </summary>
public class WebViewBridgeTests
{
    private const string ValidApiUrl = "https://claude.ai/api/organizations";

    [Theory]
    [InlineData("https://claude.ai.evil.example/api/organizations")]
    [InlineData("https://claude.aievil.example/api/organizations")]
    [InlineData("https://notclaude.ai/api/organizations")]
    [InlineData("http://claude.ai/api/organizations")]
    [InlineData("https://user@evil.example/https://claude.ai")]
    [InlineData("/api/organizations")]
    [InlineData("not a url")]
    public async Task FetchJsonAsync_RejectsUrlOutsideTheAllowList(string url)
    {
        var bridge = new WebViewBridge();

        await Assert.ThrowsAsync<ArgumentException>(() => bridge.FetchJsonAsync(url));
    }

    [Theory]
    [InlineData(ValidApiUrl)]
    [InlineData("https://CLAUDE.AI/api/organizations")]
    [InlineData("https://claude.ai/api/organizations/abc/usage")]
    public async Task FetchJsonAsync_AcceptsClaudeAiHostAndReturnsNullWhenUnbound(string url)
    {
        var bridge = new WebViewBridge();

        // Passing the allow-list is proven by reaching the "no WebView2 bound yet" exit.
        Assert.Null(await bridge.FetchJsonAsync(url));
    }

    [Fact]
    public void IsInitialized_IsFalseBeforeBinding()
    {
        Assert.False(new WebViewBridge().IsInitialized);
    }

    [Fact]
    public async Task HandleReply_WithSuccessStatus_ReturnsBody()
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        pending.HandleReply("""{"id":"req-1","status":200,"body":"{\"ok\":true}"}""");

        Assert.True(reply.IsCompleted);
        Assert.Equal("{\"ok\":true}", await reply);
    }

    [Fact]
    public async Task HandleReply_WithoutBodyField_CompletesInsteadOfOrphaningTheCaller()
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        // JSON.stringify drops undefined properties, so a reply can legitimately lack "body".
        pending.HandleReply("""{"id":"req-1","status":200}""");

        Assert.True(reply.IsCompleted);
        Assert.Null(await reply);
    }

    [Fact]
    public async Task HandleReply_WithNonNumericStatus_FaultsTheCaller()
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        pending.HandleReply("""{"id":"req-1","status":"oops"}""");

        Assert.True(reply.IsCompleted);
        await Assert.ThrowsAsync<FormatException>(() => reply);
    }

    [Fact]
    public async Task HandleReply_WithUnauthorizedStatus_FaultsWithSessionExpired()
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        pending.HandleReply("""{"id":"req-1","status":401,"body":"unauthorized"}""");

        await Assert.ThrowsAsync<SessionExpiredException>(() => reply);
    }

    [Fact]
    public async Task HandleReply_WithServerError_FaultsWithHttpFetchException()
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        pending.HandleReply("""{"id":"req-1","status":503,"body":"down"}""");

        var error = await Assert.ThrowsAsync<HttpFetchException>(() => reply);
        Assert.Equal(503, error.StatusCode);
        Assert.Equal("down", error.ResponseBody);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"status":200,"body":"x"}""")]
    [InlineData("""{"id":42,"status":200,"body":"x"}""")]
    [InlineData("[]")]
    public async Task HandleReply_WithoutUsableRequestId_LeavesTheEntryForTheWatchdog(string replyJson)
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        pending.HandleReply(replyJson);

        // The entry must stay registered — the timeout can only rescue ids it still finds.
        Assert.False(reply.IsCompleted);

        pending.Fail("req-1", new TimeoutException());

        await Assert.ThrowsAsync<TimeoutException>(() => reply);
    }

    [Fact]
    public void HandleReply_ForUnknownRequestId_IsIgnored()
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        pending.HandleReply("""{"id":"other","status":200,"body":"x"}""");

        Assert.False(reply.IsCompleted);
    }

    [Fact]
    public async Task HandleReply_TwiceForTheSameRequestId_CompletesOnce()
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        pending.HandleReply("""{"id":"req-1","status":200,"body":"first"}""");
        pending.HandleReply("""{"id":"req-1","status":500,"body":"second"}""");

        Assert.Equal("first", await reply);
    }

    [Fact]
    public async Task DrainWithoutResult_CompletesEveryPendingRequestWithNull()
    {
        var pending = new WebViewBridge.PendingRequests();
        var first = pending.Register("req-1");
        var second = pending.Register("req-2");

        pending.DrainWithoutResult();

        Assert.Null(await first);
        Assert.Null(await second);
    }

    [Fact]
    public void Fail_AfterCompletion_IsIgnored()
    {
        var pending = new WebViewBridge.PendingRequests();
        var reply = pending.Register("req-1");

        pending.HandleReply("""{"id":"req-1","status":200,"body":"x"}""");
        pending.Fail("req-1", new TimeoutException());

        Assert.Equal(TaskStatus.RanToCompletion, reply.Status);
    }
}
