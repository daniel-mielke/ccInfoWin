using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CCInfoWindows.Messages;

/// <summary>
/// Notification that the color theme changed. Value: "dark" or "light".
/// </summary>
public class ThemeChangedMessage : ValueChangedMessage<string>
{
    public ThemeChangedMessage(string colorMode) : base(colorMode)
    {
    }
}
