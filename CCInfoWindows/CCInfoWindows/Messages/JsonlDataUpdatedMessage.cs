using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Notification that JSONL data for a session was updated. Value: session ID.
/// </summary>
public class JsonlDataUpdatedMessage : ValueChangedMessage<string>
{
    public JsonlDataUpdatedMessage(string sessionId) : base(sessionId)
    {
    }
}
