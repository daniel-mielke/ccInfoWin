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
///
/// Failure contract: the mutators change the in-memory map and raise NameChanged immediately, so a
/// write failure would leave the UI showing an unpersisted name. Save/SaveAsync therefore return
/// false AND roll the affected keys back to the persisted state, re-raising NameChanged for each.
/// Callers should still check the flag to surface a generic "could not be saved" message; the
/// technical detail is already in AppLog.
/// </summary>
public interface ISessionNameStore
{
    /// <summary>Returns the custom name for the given session id, or null if none set / cleared.</summary>
    string? GetCustomName(string sessionId);

    /// <summary>
    /// Snapshot of every session id that currently carries a custom name, including ids whose
    /// session no longer exists on disk (orphans). The store owns the file, so consumers must ask
    /// it rather than rebuilding the session-names.json path themselves.
    /// </summary>
    IReadOnlyCollection<string> GetKnownSessionIds();

    /// <summary>
    /// Sets a custom name. Empty string is treated as "cleared" (subsequent GetCustomName returns null).
    /// Value is sanitized via SessionNameSanitizer.Strip (belt-and-suspenders per D-07).
    /// Raises NameChanged after the in-memory map is updated. Caller is responsible for SaveAsync().
    /// </summary>
    void SetCustomName(string sessionId, string customName);

    /// <summary>Removes any custom name for the given session id. Raises NameChanged. Caller is responsible for SaveAsync().</summary>
    void ClearCustomName(string sessionId);

    /// <summary>
    /// Persists the current in-memory map synchronously, for termination paths that cannot await
    /// (RENAME-07). Blocks only for a bounded time and returns false rather than hanging the caller.
    /// No production caller today — the rename commands await SaveAsync immediately, so nothing is
    /// left pending at shutdown.
    /// </summary>
    /// <returns>True when the map reached disk; false after a logged failure and rollback.</returns>
    bool Save();

    /// <summary>Persists the current in-memory map asynchronously. Used by UI commit paths.</summary>
    /// <returns>True when the map reached disk; false after a logged failure and rollback.</returns>
    Task<bool> SaveAsync(CancellationToken ct = default);

    /// <summary>
    /// Raised after SetCustomName / ClearCustomName mutate the in-memory map (NOT after persistence
    /// completes), and again for every key a failed save rolled back.
    /// </summary>
    event EventHandler<SessionNameChangedEventArgs>? NameChanged;
}
