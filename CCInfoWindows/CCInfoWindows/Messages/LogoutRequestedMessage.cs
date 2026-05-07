namespace CCInfoWindows.Messages;

/// <summary>
/// Signal requesting that the application perform a full logout sequence.
/// Published by UI-layer ViewModels (Settings, taskbar, keyboard shortcuts).
/// Handled by MainViewModel as the single source of truth for logout — see
/// MainViewModel.Receive(LogoutRequestedMessage). The full sequence is
/// MainViewModel.Logout(): clear history (D-13), clear credentials, reset
/// WebView2 bridge, broadcast AuthStateChangedMessage(false), reset auth flags,
/// navigate to LoginView.
/// </summary>
public class LogoutRequestedMessage
{
}
