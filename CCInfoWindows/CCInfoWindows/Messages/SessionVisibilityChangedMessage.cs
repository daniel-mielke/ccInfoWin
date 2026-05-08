using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Sent when SessionVisibilityWindowDays changes in Settings (DROPDOWN-04 / D-03).
/// MainViewModel receives this to re-apply the display-layer cutoff in RefreshSessionList.
/// G-1 compliant: receiver wraps body in _dispatcherQueue.TryEnqueue.
/// </summary>
public class SessionVisibilityChangedMessage : ValueChangedMessage<int>
{
    public SessionVisibilityChangedMessage(int newWindowDays) : base(newWindowDays) { }
}
