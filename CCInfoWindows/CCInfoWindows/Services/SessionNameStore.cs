using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Persists session custom names to %LOCALAPPDATA%\CCInfoWindows\session-names.json (RENAME-03, RENAME-07).
/// Mirrors the UsageHistoryService G-2 pattern: SemaphoreSlim write guard, sync + async writer,
/// atomic rename (tmp + File.Move) per PITFALLS A2-P1, _lastSavedSnapshot cache.
///
/// Truthfulness: SetCustomName/ClearCustomName mutate the in-memory map and raise NameChanged
/// BEFORE persistence, so the UI would otherwise keep showing a name that never reached disk.
/// A failed write therefore rolls the affected keys back to the persisted state and re-raises
/// NameChanged for them, so the UI corrects itself even when a caller ignores the bool.
///
/// Threading: SemaphoreSlim _writeLock serializes all I/O; the async path uses ConfigureAwait(false)
/// so the release continuation never lands on the dispatcher that Save() may be blocking. NameChanged
/// is raised WITHOUT holding the lock (a handler must not be able to re-enter a write and self-deadlock),
/// and handlers must marshal to the UI thread via IDispatcherQueue per G-1 (consumer's responsibility).
/// </summary>
public class SessionNameStore : ISessionNameStore
{
    private const string FileName = "session-names.json";

    /// <summary>Shared by the sync and async writers so both report the same degradation.</summary>
    private const string WriteFailureMessage = "session names not persisted";

    /// <summary>
    /// Upper bound for the blocking wait in Save(). It exists for termination paths that cannot
    /// await, so hanging the UI thread is worse than reporting a failed flush.
    /// </summary>
    private static readonly TimeSpan SyncWriteLockTimeout = TimeSpan.FromSeconds(2);

    // UnsafeRelaxedJsonEscaping: keep emoji/CJK readable in the file (PITFALLS A2-P2)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _directory;
    private string FilePath => Path.Combine(_directory, FileName);

    // G-2: SemaphoreSlim — never use lock keyword (cannot hold across await)
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // In-memory authoritative state. ConcurrentDictionary protects reads from background SaveAsync producers.
    private readonly ConcurrentDictionary<string, string> _names;

    // _lastSavedSnapshot for crash-safety / read-without-disk-hit (G-2 invariant) and as the
    // rollback target. Null means "nothing usable is on disk yet".
    private Dictionary<string, string>? _lastSavedSnapshot;

    // Bumped by every mutation. A failed write only rolls back when no newer edit arrived while it
    // was in flight — that edit owns the next save and must not be discarded here.
    private long _mutationVersion;

    public event EventHandler<SessionNameChangedEventArgs>? NameChanged;

    public SessionNameStore() : this(AppPaths.DataDirectory) { }

    public SessionNameStore(string directoryOverride)
    {
        _directory = directoryOverride;
        var persisted = LoadFromDisk();
        _names = new ConcurrentDictionary<string, string>(persisted ?? new Dictionary<string, string>());
        _lastSavedSnapshot = persisted;
    }

    /// <returns>The persisted map, or null when the file is missing or unreadable.</returns>
    private Dictionary<string, string>? LoadFromDisk() =>
        AtomicJsonFile.Read<Dictionary<string, string>>(
            FilePath,
            JsonOptions,
            $"{nameof(SessionNameStore)}.{nameof(LoadFromDisk)}",
            "session names unreadable, starting without custom names");

    public string? GetCustomName(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        return _names.TryGetValue(sessionId, out var v) && !string.IsNullOrEmpty(v) ? v : null;
    }

    public IReadOnlyCollection<string> GetKnownSessionIds() => [.. _names.Keys];

    public void SetCustomName(string sessionId, string customName)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        // D-07: belt-and-suspenders — strip control chars even if the caller already sanitized.
        var clean = SessionNameSanitizer.Strip(customName);

        if (string.IsNullOrEmpty(clean))
        {
            _names.TryRemove(sessionId, out _);
        }
        else
        {
            _names[sessionId] = clean;
        }

        Interlocked.Increment(ref _mutationVersion);
        RaiseNameChanged(sessionId);
    }

    public void ClearCustomName(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _names.TryRemove(sessionId, out _);
        Interlocked.Increment(ref _mutationVersion);
        RaiseNameChanged(sessionId);
    }

    // Save and SaveAsync are twins on purpose and must be edited as a pair: the body between the lock
    // acquire and the release — version before snapshot, roll back only when no newer edit arrived,
    // raise NameChanged outside the lock — cannot be factored out without either running the async
    // writer synchronously (deadlocks the dispatcher SaveAsync may be blocking) or splitting the
    // ordering rule across two calls, which is what it exists to keep together.
    public bool Save()
    {
        if (!_writeLock.Wait(SyncWriteLockTimeout))
        {
            AppLog.Write($"{nameof(SessionNameStore)}.{nameof(Save)}",
                "write lock still held after the timeout -- flush skipped to keep the calling thread free");
            return false;
        }

        List<string> reverted = [];
        bool written;
        try
        {
            var version = Volatile.Read(ref _mutationVersion);
            var snapshot = SnapshotNames();
            written = WriteToDisk(snapshot);
            if (!written) reverted = RollbackToPersisted(snapshot, version);
        }
        finally { _writeLock.Release(); }

        RaiseNameChanged(reverted);
        return written;
    }

    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);

        List<string> reverted = [];
        bool written;
        try
        {
            var version = Volatile.Read(ref _mutationVersion);
            var snapshot = SnapshotNames();
            written = await WriteToDiskAsync(snapshot, ct).ConfigureAwait(false);
            if (!written) reverted = RollbackToPersisted(snapshot, version);
        }
        finally { _writeLock.Release(); }

        RaiseNameChanged(reverted);
        return written;
    }

    private Dictionary<string, string> SnapshotNames() =>
        _names.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    // PITFALLS A2-P1: atomic rename — AtomicJsonFile writes to .tmp and publishes with
    // File.Move(overwrite:true), so the invariant lives in one place for every store. _lastSavedSnapshot
    // is only assigned on a true return, i.e. after the move: it is both the crash-safety cache and the
    // rollback target, so a snapshot that never reached disk must never land in it.
    private bool WriteToDisk(Dictionary<string, string> snapshot)
    {
        if (!AtomicJsonFile.Write(
                FilePath, snapshot, JsonOptions,
                $"{nameof(SessionNameStore)}.{nameof(Save)}", WriteFailureMessage))
        {
            return false;
        }

        _lastSavedSnapshot = snapshot;
        return true;
    }

    private async Task<bool> WriteToDiskAsync(Dictionary<string, string> snapshot, CancellationToken ct)
    {
        var written = await AtomicJsonFile.WriteAsync(
            FilePath, snapshot, JsonOptions,
            $"{nameof(SessionNameStore)}.{nameof(SaveAsync)}", WriteFailureMessage, ct).ConfigureAwait(false);

        if (!written) return false;

        _lastSavedSnapshot = snapshot;
        return true;
    }

    /// <summary>
    /// Restores the keys the failed write touched to the state that is actually on disk.
    /// </summary>
    /// <returns>Session ids whose in-memory value changed; empty when nothing was reverted.</returns>
    private List<string> RollbackToPersisted(Dictionary<string, string> failedSnapshot, long snapshotVersion)
    {
        if (Volatile.Read(ref _mutationVersion) != snapshotVersion) return [];

        var persisted = _lastSavedSnapshot ?? new Dictionary<string, string>();
        var reverted = new List<string>();

        foreach (var (sessionId, attempted) in failedSnapshot)
        {
            if (persisted.TryGetValue(sessionId, out var onDisk))
            {
                if (string.Equals(onDisk, attempted, StringComparison.Ordinal)) continue;
                _names[sessionId] = onDisk;
                reverted.Add(sessionId);
            }
            else if (_names.TryRemove(sessionId, out _))
            {
                reverted.Add(sessionId);
            }
        }

        // Keys on disk but missing from the snapshot: a removal that never reached the file.
        foreach (var (sessionId, onDisk) in persisted)
        {
            if (failedSnapshot.ContainsKey(sessionId)) continue;
            _names[sessionId] = onDisk;
            reverted.Add(sessionId);
        }

        return reverted;
    }

    private void RaiseNameChanged(string sessionId) =>
        NameChanged?.Invoke(this, new SessionNameChangedEventArgs { SessionId = sessionId });

    private void RaiseNameChanged(List<string> sessionIds)
    {
        foreach (var sessionId in sessionIds)
        {
            RaiseNameChanged(sessionId);
        }
    }

    // For test introspection (G-2 _lastSavedSnapshot invariant)
    internal IReadOnlyDictionary<string, string>? PeekLastSnapshot() => _lastSavedSnapshot;
}
