using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Notification that the polling refresh interval changed. Value: interval in seconds.
/// Sent by SettingsViewModel, received by MainViewModel to update the poll timer.
/// </summary>
public class RefreshIntervalChangedMessage : ValueChangedMessage<int>
{
    public RefreshIntervalChangedMessage(int intervalSeconds) : base(intervalSeconds)
    {
    }
}
