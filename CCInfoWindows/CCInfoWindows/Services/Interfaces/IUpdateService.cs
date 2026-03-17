namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Checks GitHub Releases for newer versions and fires an event when an update is available.
/// </summary>
public interface IUpdateService
{
    event Action<string, string>? UpdateAvailable;

    Task CheckForUpdateAsync();

    void StartPeriodicCheck();

    void StopPeriodicCheck();
}
