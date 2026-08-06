namespace CCInfoWindows.Services;

/// <summary>
/// Thrown by <see cref="WebViewBridge"/> when claude.ai answers HTTP 401. Catching this type is
/// the only signal that drives the app into the re-authentication flow.
/// Deliberately NOT derived from <see cref="UnauthorizedAccessException"/>: the filesystem raises
/// that type for ACL and read-only failures, so sharing it let a failed cache write force a logout.
/// </summary>
public sealed class SessionExpiredException : Exception
{
    private const string DefaultMessage = "Session expired (HTTP 401)";

    public SessionExpiredException()
        : base(DefaultMessage)
    {
    }

    public SessionExpiredException(string message)
        : base(message)
    {
    }

    public SessionExpiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
