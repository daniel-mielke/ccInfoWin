using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Reads/writes usage-history.json in %LOCALAPPDATA%\CCInfoWindows\.
/// Handles missing or corrupt files gracefully by returning empty defaults.
///
/// Writes are atomic: serialize to "&lt;file&gt;.tmp", then File.Move(overwrite) — done by
/// <see cref="AtomicJsonFile"/>, which every store here shares. File.WriteAllText truncates before
/// writing, so an interruption used to leave a half-written file that LoadHistory silently turned
/// into an empty chart.
///
/// Threading: SemaphoreSlim serializes the sync and async writers (G-2 — never the lock keyword,
/// which cannot be held across an await). The async path awaits with ConfigureAwait(false)
/// throughout because SaveHistory/ClearHistory are called ON the UI thread (MainWindow.OnClosing):
/// a continuation captured onto the dispatcher can never run while that same thread is blocked in
/// Wait(), which self-deadlocks the app half-closed. The blocking waits are additionally bounded,
/// so even an unforeseen contention source degrades to a skipped flush instead of a hang.
/// </summary>
public class UsageHistoryService : IUsageHistoryService
{
    private const string FileName = "usage-history.json";

    /// <summary>Shared by the sync and async writers so both report the same degradation.</summary>
    private const string WriteFailureMessage = "history save failed";

    /// <summary>
    /// Upper bound for the blocking waits in the synchronous members. They run on the UI thread
    /// during window teardown, where hanging is far worse than losing one flush — the next poll
    /// rewrites the file anyway.
    /// </summary>
    private static readonly TimeSpan SyncWriteLockTimeout = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyDirectory;
    private string HistoryFilePath => Path.Combine(_historyDirectory, FileName);

    // D-05: SemaphoreSlim serializes sync and async writes -- never use lock keyword (cannot hold across await)
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // D-04: updated AFTER each successful write; null after ClearHistory
    private UsageHistory? _lastSavedSnapshot;

    public UsageHistoryService() : this(AppPaths.DataDirectory)
    {
    }

    public UsageHistoryService(string directoryOverride)
    {
        _historyDirectory = directoryOverride;
    }

    public UsageHistory LoadHistory() =>
        AtomicJsonFile.Read<UsageHistory>(
            HistoryFilePath,
            JsonOptions,
            $"{nameof(UsageHistoryService)}.{nameof(LoadHistory)}",
            "history unreadable, starting from an empty chart")
        ?? new UsageHistory();

    public void SaveHistory(UsageHistory history)
    {
        if (!TryAcquireForSyncCall(nameof(SaveHistory))) return;

        try
        {
            // D-04 / RESEARCH Pitfall 2: the snapshot cache is assigned only after a successful
            // publish, so a torn or failed write can never be mistaken for what is on disk.
            if (AtomicJsonFile.Write(
                    HistoryFilePath, history, JsonOptions,
                    $"{nameof(UsageHistoryService)}.{nameof(SaveHistory)}", WriteFailureMessage))
            {
                _lastSavedSnapshot = history;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveHistoryAsync(UsageHistory history)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var written = await AtomicJsonFile.WriteAsync(
                HistoryFilePath, history, JsonOptions,
                $"{nameof(UsageHistoryService)}.{nameof(SaveHistoryAsync)}", WriteFailureMessage)
                .ConfigureAwait(false);

            if (written) _lastSavedSnapshot = history;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void ClearHistory()
    {
        if (!TryAcquireForSyncCall(nameof(ClearHistory))) return;

        try
        {
            _lastSavedSnapshot = null;                                   // 1. Invalidate cache FIRST (D-13)
            if (File.Exists(HistoryFilePath)) File.Delete(HistoryFilePath);   // 2. Then delete on disk
            AtomicJsonFile.DiscardTemp(HistoryFilePath, $"{nameof(UsageHistoryService)}.{nameof(ClearHistory)}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"{nameof(UsageHistoryService)}.{nameof(ClearHistory)}", ex, "history file could not be deleted");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // D-04: atomic reference read; producer side is locked via _writeLock
    public UsageHistory? PeekLastSnapshot() => _lastSavedSnapshot;

    /// <summary>
    /// Bounded acquire for the UI-thread callers. Returns false when the lock could not be taken,
    /// in which case the caller must NOT release it.
    /// </summary>
    private bool TryAcquireForSyncCall(string member)
    {
        if (_writeLock.Wait(SyncWriteLockTimeout)) return true;

        AppLog.Write($"{nameof(UsageHistoryService)}.{member}",
            "write lock still held after the timeout -- skipping this flush to keep the UI thread free");
        return false;
    }
}
