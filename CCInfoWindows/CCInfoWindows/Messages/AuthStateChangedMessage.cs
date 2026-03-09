using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Notification that authentication state changed. Value: true = logged in, false = logged out.
/// </summary>
public class AuthStateChangedMessage : ValueChangedMessage<bool>
{
    public AuthStateChangedMessage(bool value) : base(value)
    {
    }
}
