using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Persists user-supplied custom display names for sessions to %LOCALAPPDATA%\CCInfoWindows\session-names.json.
/// Mirrors the G-2 convention established by IUsageHistoryService (SemaphoreSlim write guard, sync+async,
/// atomic-rename via tmp+File.Move, _lastSavedSnapshot cache). Storage key is encoded projectDirName
/// (= SessionInfo.Id) per D-02. Empty string sets clear the entry (D-02 empty-value semantics).
///
/// Cross-VM propagation: NameChanged is a standard .NET event, NOT a WeakReferenceMessenger broadcast
/// (D-13 lesson honored — singleton-published events survive AddTransient consumers).
/// </summary>
public interface ISessionNameStore
{
    /// <summary>Returns the custom name for the given session id, or null if none set / cleared.</summary>
    string? GetCustomName(string sessionId);

    /// <summary>
    /// Sets a custom name. Empty string is treated as "cleared" (subsequent GetCustomName returns null).
    /// Value is sanitized via SessionNameSanitizer.Strip (belt-and-suspenders per D-07).
    /// Raises NameChanged after the in-memory map is updated. Caller is responsible for SaveAsync().
    /// </summary>
    void SetCustomName(string sessionId, string customName);

    /// <summary>Removes any custom name for the given session id. Raises NameChanged. Caller is responsible for SaveAsync().</summary>
    void ClearCustomName(string sessionId);

    /// <summary>Persists the current in-memory map synchronously. Used by termination flush paths.</summary>
    bool Save();

    /// <summary>Persists the current in-memory map asynchronously. Used by UI commit paths.</summary>
    Task<bool> SaveAsync(CancellationToken ct = default);

    /// <summary>Raised after SetCustomName / ClearCustomName mutate the in-memory map (NOT after persistence completes).</summary>
    event EventHandler<SessionNameChangedEventArgs>? NameChanged;
}
