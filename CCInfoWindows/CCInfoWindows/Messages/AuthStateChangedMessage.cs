using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Notification that authentication state changed. Value: true = logged in, false = logged out.
///
/// In practice only `false` is broadcast (ClaudeApiService on a 401, and the two logout commands).
/// `true` had no reachable recipient: LoginViewModel signalled success before navigating to MainView,
/// so the MainViewModel that would have received it did not exist yet, and the one built a moment
/// later starts with default flags and polls from InitializeAsync anyway. MainViewModel still guards
/// against a `true` arriving so it can never be mistaken for a 401.
/// </summary>
public class AuthStateChangedMessage : ValueChangedMessage<bool>
{
    public AuthStateChangedMessage(bool value) : base(value)
    {
    }
}
