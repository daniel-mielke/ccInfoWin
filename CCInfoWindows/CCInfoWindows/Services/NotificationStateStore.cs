using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Persists notification state to %LOCALAPPDATA%\CCInfoWindows\notification-state.json.
///
/// Precedent for a dedicated store rather than AppSettings: UsageHistoryService, SessionNameStore.
/// Writes are synchronous and lock-protected; the payload is a handful of independent flags, so this
/// is the one store that opts out of <see cref="AtomicJsonFile"/>'s tmp+rename dance
/// (<c>viaTempFile: false</c>) — a torn file costs at most one re-fired toast, not a lost dataset.
/// Failures are swallowed the way SettingsService swallows them — losing a notification flag must
/// never take the dashboard down.
/// </summary>
public class NotificationStateStore : INotificationStateStore
{
    private const string FileName = "notification-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly Lock _gate = new();
    private NotificationState? _cached;

    private string FilePath => Path.Combine(_directory, FileName);

    public NotificationStateStore() : this(AppPaths.DataDirectory) { }

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

            // viaTempFile: false is the deliberate deviation from the other stores — see the class
            // remarks. The cache is assigned regardless of the write result: the in-memory value is
            // authoritative for this process, and a failed write must not re-arm a toast that fired.
            AtomicJsonFile.Write(
                FilePath, state, JsonOptions,
                $"{nameof(NotificationStateStore)}.{nameof(Save)}",
                "notification state not persisted -- toasts may re-fire after a restart",
                viaTempFile: false);
        }
    }

    private NotificationState LoadFromDisk() =>
        AtomicJsonFile.Read<NotificationState>(
            FilePath,
            JsonOptions,
            $"{nameof(NotificationStateStore)}.{nameof(LoadFromDisk)}",
            "notification state unreadable -- every threshold toast is re-armed")
        ?? new NotificationState();
}
