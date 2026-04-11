using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Notification that the chart data changed and the Win2D canvas needs a redraw.
/// Sent by MainViewModel, received by MainView.
/// </summary>
public class ChartInvalidateMessage : ValueChangedMessage<bool>
{
    public ChartInvalidateMessage() : base(true)
    {
    }
}
