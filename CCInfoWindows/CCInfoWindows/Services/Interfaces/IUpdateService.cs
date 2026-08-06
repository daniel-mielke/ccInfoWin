namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Checks GitHub Releases for newer versions and fires an event when an update is available.
/// </summary>
public interface IUpdateService
{
    event Action<string, string>? UpdateAvailable;

    /// <summary>
    /// One stateless check. Owns no timer, so any caller may run it — the dashboard does on load.
    /// </summary>
    Task CheckForUpdateAsync();

    /// <summary>
    /// Starts the hourly schedule. Called once per process by App.StartBackgroundServices: restarting
    /// it from a transient ViewModel reset the interval on every Settings round-trip, so a user who
    /// visited Settings more often than hourly never completed a check (finding 29).
    /// </summary>
    void StartPeriodicCheck();

    /// <summary>Stops the hourly schedule. Called by MainWindow.OnClosing.</summary>
    void StopPeriodicCheck();
}
