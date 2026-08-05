using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Persistence for notification bookkeeping. Deliberately separate from ISettingsService:
/// AppSettings is user configuration, this is machine state that turns over on every window
/// rotation, and SettingsService.SaveSettings is read-modify-write without locking, so the poll
/// path and the settings UI would race and lose updates.
/// </summary>
public interface INotificationStateStore
{
    NotificationState Load();
    void Save(NotificationState state);
}
