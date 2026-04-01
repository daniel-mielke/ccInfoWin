namespace CCInfoWindows.Services;

/// <summary>
/// Thrown by WebViewBridge when the HTTP response has a non-success status code.
/// Carries the raw status and response body so callers can surface actionable error messages.
/// </summary>
public class HttpFetchException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public HttpFetchException(int statusCode, string? responseBody)
        : base(BuildMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    private static string BuildMessage(int statusCode, string? responseBody)
    {
        var description = statusCode switch
        {
            0 => "Network error (no connection or DNS failure)",
            400 => "Bad Request (400)",
            403 => "Forbidden (403) — Cloudflare may be blocking the request",
            404 => "Not Found (404) — API endpoint does not exist",
            429 => "Too Many Requests (429) — rate limit exceeded",
            500 => "Internal Server Error (500)",
            502 => "Bad Gateway (502) — upstream server error",
            503 => "Service Unavailable (503) — claude.ai may be down",
            _ => $"HTTP {statusCode}"
        };

        return string.IsNullOrWhiteSpace(responseBody) || responseBody.Length > 200
            ? description
            : $"{description}: {responseBody}";
    }
}
