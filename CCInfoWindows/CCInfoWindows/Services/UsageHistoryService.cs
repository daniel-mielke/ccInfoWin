using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Reads/writes usage-history.json in %LOCALAPPDATA%\CCInfoWindows\.
/// Handles missing or corrupt files gracefully by returning empty defaults.
///
/// Writes are atomic: serialize to "&lt;file&gt;.tmp", then File.Move(overwrite) — the same invariant
/// SessionNameStore uses. File.WriteAllText truncates before writing, so an interruption used to
/// leave a half-written file that LoadHistory silently turned into an empty chart.
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
    private const string TempFileSuffix = ".tmp";

    /// <summary>
    /// Upper bound for the blocking waits in the synchronous members. They run on the UI thread
    /// during window teardown, where hanging is far worse than losing one flush — the next poll
    /// rewrites the file anyway.
    /// </summary>
    private static readonly TimeSpan SyncWriteLockTimeout = TimeSpan.FromSeconds(2);

    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyDirectory;
    private string HistoryFilePath => Path.Combine(_historyDirectory, FileName);
    private string TempFilePath => Path.Combine(_historyDirectory, FileName + TempFileSuffix);

    // D-05: SemaphoreSlim serializes sync and async writes -- never use lock keyword (cannot hold across await)
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // D-04: updated AFTER each successful write; null after ClearHistory
    private UsageHistory? _lastSavedSnapshot;

    public UsageHistoryService() : this(DefaultDirectory)
    {
    }

    public UsageHistoryService(string directoryOverride)
    {
        _historyDirectory = directoryOverride;
    }

    public UsageHistory LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryFilePath))
            {
                return new UsageHistory();
            }

            var json = File.ReadAllText(HistoryFilePath);
            return JsonSerializer.Deserialize<UsageHistory>(json, JsonOptions) ?? new UsageHistory();
        }
        catch (Exception ex)
        {
            AppLog.Write($"{nameof(UsageHistoryService)}.{nameof(LoadHistory)}", ex,
                "history unreadable, starting from an empty chart");
            return new UsageHistory();
        }
    }

    public void SaveHistory(UsageHistory history)
    {
        if (!TryAcquireForSyncCall(nameof(SaveHistory))) return;

        try
        {
            var json = PrepareWrite(history);
            File.WriteAllText(TempFilePath, json);
            CommitWrite(history);
        }
        catch (Exception ex)
        {
            AppLog.Write($"{nameof(UsageHistoryService)}.{nameof(SaveHistory)}", ex, "history save failed");
            DiscardTempFile();
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
            var json = PrepareWrite(history);
            await File.WriteAllTextAsync(TempFilePath, json).ConfigureAwait(false);
            CommitWrite(history);
        }
        catch (Exception ex)
        {
            AppLog.Write($"{nameof(UsageHistoryService)}.{nameof(SaveHistoryAsync)}", ex, "history save failed");
            DiscardTempFile();
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
            DiscardTempFile();
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

    // Shared by both writers so hardening applies to one copy: only the file-write call itself
    // differs between the sync and async paths.
    private string PrepareWrite(UsageHistory history)
    {
        Directory.CreateDirectory(_historyDirectory);
        return JsonSerializer.Serialize(history, JsonOptions);
    }

    // Atomic publish: a reader without the semaphore sees either the previous complete file or
    // this one, never a truncated prefix.
    private void CommitWrite(UsageHistory history)
    {
        File.Move(TempFilePath, HistoryFilePath, overwrite: true);
        _lastSavedSnapshot = history;   // AFTER successful write -- RESEARCH Pitfall 2
    }

    // A failure between the tmp write and the move would otherwise leave the fragment behind.
    private void DiscardTempFile()
    {
        try
        {
            if (File.Exists(TempFilePath)) File.Delete(TempFilePath);
        }
        catch (Exception ex)
        {
            AppLog.Write($"{nameof(UsageHistoryService)}.{nameof(DiscardTempFile)}", ex, "stale temp file left behind");
        }
    }
}
