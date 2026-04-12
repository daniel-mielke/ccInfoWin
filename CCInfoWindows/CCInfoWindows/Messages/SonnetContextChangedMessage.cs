using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Notification that the Sonnet context window size changed. Value: new size in tokens.
/// Sent by SettingsViewModel, received by MainViewModel to refresh context display.
/// </summary>
public class SonnetContextChangedMessage : ValueChangedMessage<long>
{
    public SonnetContextChangedMessage(long contextSize) : base(contextSize)
    {
    }
}
