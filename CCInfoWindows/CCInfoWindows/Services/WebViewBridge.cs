using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
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
    private CoreWebView2? _coreWebView;
    private DispatcherQueue? _dispatcherQueue;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pending = new();

    public bool IsInitialized => _coreWebView is not null;

    /// <summary>
    /// Binds the bridge to a WebView2 instance that has already navigated to claude.ai.
    /// Must be called from UI thread after WebView2 initialization.
    /// </summary>
    public void Initialize(CoreWebView2 coreWebView, DispatcherQueue dispatcherQueue)
    {
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
            _coreWebView.WebMessageReceived -= OnWebMessageReceived;
        }
        _coreWebView = null;
        _dispatcherQueue = null;

        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
            {
                tcs.TrySetResult(null);
            }
        }
    }

    private const string AllowedUrlPrefix = "https://claude.ai";

    public async Task<string?> FetchJsonAsync(string url)
    {
        if (!url.StartsWith(AllowedUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"URL must start with {AllowedUrlPrefix}", nameof(url));
        }

        if (_coreWebView is null || _dispatcherQueue is null)
        {
            return null;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string?>();
        _pending[requestId] = tcs;

        // JS-safe escape: JsonSerializer.Serialize produces a quoted, escaped string literal
        var safeUrl = JsonSerializer.Serialize(url);
        var safeRequestId = JsonSerializer.Serialize(requestId);

        // Inject JS that does fetch() and posts result back via window.chrome.webview.postMessage.
        // Cannot use ExecuteScriptAsync with async/await JS — it returns the Promise object, not the resolved value.
        var script =
            "(function() {" +
            $"fetch({safeUrl}, {{ credentials: 'include' }})" +
            ".then(function(r) {" +
            "return r.text().then(function(body) {" +
            "window.chrome.webview.postMessage(JSON.stringify({" +
            $"id: {safeRequestId}," +
            "status: r.status," +
            "body: body" +
            "}));" +
            "});" +
            "})" +
            ".catch(function(e) {" +
            "window.chrome.webview.postMessage(JSON.stringify({" +
            $"id: {safeRequestId}," +
            "status: 0," +
            "body: e.message" +
            "}));" +
            "});" +
            "})();";


        var enqueued = _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var asyncOp = _coreWebView?.ExecuteScriptAsync(script);
                asyncOp?.AsTask().ContinueWith(t =>
                {
                    if (t.IsFaulted && _pending.TryRemove(requestId, out var faulted))
                    {
                        faulted.TrySetException(t.Exception!.InnerException!);
                    }
                }, TaskScheduler.Default);
            }
            catch (Exception)
            {
                if (_pending.TryRemove(requestId, out var removed))
                {
                    removed.TrySetResult(null);
                }
            }
        });

        if (!enqueued)
        {
            _pending.TryRemove(requestId, out _);
            throw new InvalidOperationException("WebView2 dispatcher queue is unavailable");
        }

        // Timeout after 30 seconds
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var reg = cts.Token.Register(() =>
        {
            if (_pending.TryRemove(requestId, out var removed))
            {
                removed.TrySetException(new TimeoutException("Request timed out after 30 seconds"));
            }
        });

        return await tcs.Task;
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.TryGetWebMessageAsString();
            if (json is null) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idProp)) return;
            var id = idProp.GetString();
            if (id is null || !_pending.TryRemove(id, out var tcs)) return;

            var status = root.GetProperty("status").GetInt32();
            var body = root.GetProperty("body").GetString();

            if (status == 401)
            {
                tcs.TrySetException(new UnauthorizedAccessException("Session expired (401)"));
                return;
            }

            if (status is < 200 or >= 300)
            {
                tcs.TrySetException(new HttpFetchException(status, body));
                return;
            }

            tcs.TrySetResult(body);
        }
        catch (Exception)
        {
            // Malformed message from page — ignore
        }
    }
}
