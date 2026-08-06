using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Services.Interfaces;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace CCInfoWindows.Services;

/// <summary>
/// Routes HTTP requests through WebView2's Chromium engine to bypass Cloudflare bot protection.
/// Uses WebMessageReceived callback pattern because ExecuteScriptAsync cannot await JS Promises.
/// </summary>
public class WebViewBridge : IWebViewBridge
{
    private const string IdField = "id";
    private const string StatusField = "status";
    private const string BodyField = "body";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly PendingRequests _pending = new();

    private CoreWebView2? _coreWebView;
    private DispatcherQueue? _dispatcherQueue;

    public bool IsInitialized => _coreWebView is not null;

    /// <summary>
    /// Binds the bridge to a WebView2 instance that has already navigated to claude.ai.
    /// Must be called from UI thread after WebView2 initialization.
    /// </summary>
    public void Initialize(CoreWebView2 coreWebView, DispatcherQueue dispatcherQueue)
    {
        // Re-binding (e.g. MainView taking over after login) must detach the previous
        // WebView first, otherwise the handler leaks onto a dead CoreWebView2.
        if (!ReferenceEquals(_coreWebView, coreWebView))
        {
            Reset();
        }

        _coreWebView = coreWebView;
        _dispatcherQueue = dispatcherQueue;
        _coreWebView.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>
    /// Clears the WebView2 reference (e.g., on logout).
    /// </summary>
    public void Reset()
    {
        if (_coreWebView is not null)
        {
            // The previous CoreWebView2 may already be disposed (navigated-away View),
            // in which case detaching throws — we only care that we stop listening.
            try
            {
                _coreWebView.WebMessageReceived -= OnWebMessageReceived;
            }
            catch (Exception ex)
            {
                AppLog.Write("WebViewBridge.Reset", ex, "detaching the previous CoreWebView2 failed");
            }
        }

        _coreWebView = null;
        _dispatcherQueue = null;
        _pending.DrainWithoutResult();
    }

    public async Task<string?> FetchJsonAsync(string url)
    {
        if (!ClaudeAiUrlPolicy.IsAllowed(url))
        {
            throw new ArgumentException(
                $"URL must be an absolute https URL on {ClaudeAiUrlPolicy.AllowedHost}", nameof(url));
        }

        var dispatcherQueue = _dispatcherQueue;
        if (_coreWebView is null || dispatcherQueue is null)
        {
            return null;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var reply = _pending.Register(requestId);
        var script = BuildFetchScript(url, requestId);

        if (!dispatcherQueue.TryEnqueue(() => ExecuteFetchScript(requestId, script)))
        {
            _pending.Discard(requestId);
            throw new InvalidOperationException("WebView2 dispatcher queue is unavailable");
        }

        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var registration = timeout.Token.Register(() => _pending.Fail(
            requestId,
            new TimeoutException($"Request timed out after {RequestTimeout.TotalSeconds:0} seconds")));

        return await reply;
    }

    /// <summary>
    /// Injects the fetch script on the UI thread. Every failure path completes the pending
    /// request, so the caller never waits for the watchdog when the injection itself fails.
    /// </summary>
    private void ExecuteFetchScript(string requestId, string script)
    {
        var coreWebView = _coreWebView;
        if (coreWebView is null)
        {
            // Reset() ran between enqueue and execution.
            _pending.CompleteWithoutResult(requestId);
            return;
        }

        try
        {
            coreWebView.ExecuteScriptAsync(script).AsTask().ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        _pending.Fail(requestId, task.Exception!.InnerException ?? task.Exception!);
                    }
                },
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            AppLog.Write("WebViewBridge.ExecuteFetchScript", ex, "fetch script injection failed");
            _pending.CompleteWithoutResult(requestId);
        }
    }

    /// <summary>
    /// Builds the fetch script. ExecuteScriptAsync cannot await a JS Promise, so the result is
    /// posted back through window.chrome.webview.postMessage and matched up by request id.
    /// </summary>
    private static string BuildFetchScript(string url, string requestId)
    {
        // JS-safe escape: JsonSerializer.Serialize produces a quoted, escaped string literal
        var safeUrl = JsonSerializer.Serialize(url);
        var safeRequestId = JsonSerializer.Serialize(requestId);

        return
            "(function() {" +
            $"fetch({safeUrl}, {{ credentials: 'include' }})" +
            ".then(function(r) {" +
            "return r.text().then(function(body) {" +
            "window.chrome.webview.postMessage(JSON.stringify({" +
            $"{IdField}: {safeRequestId}," +
            $"{StatusField}: r.status," +
            $"{BodyField}: body" +
            "}));" +
            "});" +
            "})" +
            ".catch(function(e) {" +
            "window.chrome.webview.postMessage(JSON.stringify({" +
            $"{IdField}: {safeRequestId}," +
            $"{StatusField}: 0," +
            $"{BodyField}: e.message" +
            "}));" +
            "});" +
            "})();";
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string? reply;
        try
        {
            reply = args.TryGetWebMessageAsString();
        }
        catch (Exception ex)
        {
            // Any non-string postMessage from the page throws here — it is not one of our replies.
            AppLog.Write("WebViewBridge.OnWebMessageReceived", ex, "web message was not a string");
            return;
        }

        if (reply is null) return;

        _pending.HandleReply(reply);
    }

    /// <summary>
    /// Tracks in-flight bridge requests and completes at most one of them per reply.
    /// Invariant: an entry is only removed together with a completion. Removing without
    /// completing strands the caller forever, because the watchdog can only rescue request ids
    /// that are still registered.
    /// </summary>
    internal sealed class PendingRequests
    {
        private const int UnauthorizedStatus = 401;
        private const int SuccessStatusMin = 200;
        private const int SuccessStatusExclusiveMax = 300;

        private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _entries = new();

        /// <summary>
        /// Registers a request and returns the task its reply completes.
        /// RunContinuationsAsynchronously: replies arrive on the UI thread inside the
        /// WebMessageReceived handler, where inline caller continuations must not run.
        /// </summary>
        internal Task<string?> Register(string requestId)
        {
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _entries[requestId] = completion;
            return completion.Task;
        }

        /// <summary>
        /// Drops an entry whose caller is about to fail synchronously and therefore never awaits.
        /// </summary>
        internal void Discard(string requestId) => _entries.TryRemove(requestId, out _);

        internal void CompleteWithoutResult(string requestId)
        {
            if (_entries.TryRemove(requestId, out var completion))
            {
                completion.TrySetResult(null);
            }
        }

        internal void Fail(string requestId, Exception error)
        {
            if (_entries.TryRemove(requestId, out var completion))
            {
                completion.TrySetException(error);
            }
        }

        internal void DrainWithoutResult()
        {
            foreach (var requestId in _entries.Keys)
            {
                CompleteWithoutResult(requestId);
            }
        }

        /// <summary>
        /// Resolves the request named in a reply. The reply comes from page script, so every
        /// parse outcome — including an unparsable payload — must end in a completion.
        /// </summary>
        internal void HandleReply(string replyJson)
        {
            string? requestId = null;

            try
            {
                using var document = JsonDocument.Parse(replyJson);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object) return;
                if (!root.TryGetProperty(IdField, out var idProperty)) return;
                if (idProperty.ValueKind != JsonValueKind.String) return;

                requestId = idProperty.GetString();
                if (requestId is null) return;

                // The ValueKind check is required: TryGetInt32 throws on a non-number element.
                if (!root.TryGetProperty(StatusField, out var statusProperty) ||
                    statusProperty.ValueKind != JsonValueKind.Number ||
                    !statusProperty.TryGetInt32(out var status))
                {
                    Fail(requestId, new FormatException("Bridge reply carried no numeric status."));
                    return;
                }

                var body = root.TryGetProperty(BodyField, out var bodyProperty) &&
                           bodyProperty.ValueKind == JsonValueKind.String
                    ? bodyProperty.GetString()
                    : null;

                Complete(requestId, status, body);
            }
            catch (Exception ex)
            {
                AppLog.Write("WebViewBridge.HandleReply", ex, "bridge reply could not be parsed");

                if (requestId is not null)
                {
                    Fail(requestId, new FormatException("Bridge reply could not be parsed.", ex));
                }
            }
        }

        private void Complete(string requestId, int status, string? body)
        {
            if (!_entries.TryRemove(requestId, out var completion)) return;

            if (status == UnauthorizedStatus)
            {
                completion.TrySetException(new SessionExpiredException());
                return;
            }

            if (status is < SuccessStatusMin or >= SuccessStatusExclusiveMax)
            {
                completion.TrySetException(new HttpFetchException(status, body));
                return;
            }

            completion.TrySetResult(body);
        }
    }
}
