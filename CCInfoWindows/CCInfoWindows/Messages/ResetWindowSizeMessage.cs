using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Requests the main window to resize itself to the default dimensions.
/// </summary>
public class ResetWindowSizeMessage : ValueChangedMessage<bool>
{
    public ResetWindowSizeMessage() : base(true)
    {
    }
}
