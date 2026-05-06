using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Sent when SessionActivityThresholdMinutes changes in Settings.
/// MainViewModel receives this to recompute tooltips (POLISH-06, D-08).
/// </summary>
public class SessionTimeoutChangedMessage : ValueChangedMessage<int>
{
    public SessionTimeoutChangedMessage(int thresholdMinutes) : base(thresholdMinutes) { }
}
