using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Notification that the user selected a different session. Value: session ID, or null to clear selection.
/// </summary>
public class SessionSelectedMessage : ValueChangedMessage<string?>
{
    public SessionSelectedMessage(string? sessionId) : base(sessionId)
    {
    }
}
