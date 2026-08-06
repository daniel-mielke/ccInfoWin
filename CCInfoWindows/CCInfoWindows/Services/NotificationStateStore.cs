using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Persists notification state to %LOCALAPPDATA%\CCInfoWindows\notification-state.json.
///
/// Precedent for a dedicated store rather than AppSettings: UsageHistoryService, SessionNameStore.
/// Writes are synchronous and lock-protected; the payload is a handful of fields, so the atomic
/// tmp+rename dance SessionNameStore needs for its larger map would be overhead here.
/// Failures are swallowed the way SettingsService swallows them — losing a notification flag must
/// never take the dashboard down.
/// </summary>
public class NotificationStateStore : INotificationStateStore
{
    private const string FileName = "notification-state.json";

    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly Lock _gate = new();
    private NotificationState? _cached;

    private string FilePath => Path.Combine(_directory, FileName);

    public NotificationStateStore() : this(DefaultDirectory) { }

    public NotificationStateStore(string directoryOverride)
    {
        _directory = directoryOverride;
    }

    public NotificationState Load()
    {
        lock (_gate)
        {
            _cached ??= LoadFromDisk();
            return _cached;
        }
    }

    public void Save(NotificationState state)
    {
        lock (_gate)
        {
            _cached = state;
            try
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(state, JsonOptions));
            }
            catch (Exception ex)
            {
                AppLog.Write($"{nameof(NotificationStateStore)}.{nameof(Save)}", ex,
                    "notification state not persisted -- toasts may re-fire after a restart");
            }
        }
    }

    private NotificationState LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath)) return new NotificationState();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<NotificationState>(json, JsonOptions) ?? new NotificationState();
        }
        catch (Exception ex)
        {
            AppLog.Write($"{nameof(NotificationStateStore)}.{nameof(LoadFromDisk)}", ex,
                "notification state unreadable -- every threshold toast is re-armed");
            return new NotificationState();
        }
    }
}
